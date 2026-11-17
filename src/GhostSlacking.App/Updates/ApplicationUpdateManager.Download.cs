using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed partial class ApplicationUpdateManager
{
    public Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default) => StartTransfer(false, cancellationToken);
    public async Task StartDownloadAndInstallAsync() => await StartTransfer(true, CancellationToken.None).ConfigureAwait(false);

    private Task<string?> StartTransfer(bool install, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task<string?> task;
        ApplicationUpdateStatus previous;
        lock (_lifecycle)
        {
            if (_shutdown || _installing || !CanInstallUpdates || Snapshot.Status is ApplicationUpdateStatus.Checking or ApplicationUpdateStatus.Pausing or ApplicationUpdateStatus.Stopping ||
                Snapshot.Release is not { } release || release.Version <= _currentVersion) return Task.FromResult<string?>(null);
            if (_downloadTask is { IsCompleted: false }) return _downloadTask;
            _autoInstall = install;
            _hasTask = true;
            var generation = ++_generation;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _downloadCancellation = cancellation;
            previous = Snapshot.Status;
            Volatile.Write(ref _snapshot, CreateSnapshot(ApplicationUpdateStatus.Downloading, release));
            task = _downloadTask = Task.Run(() => RunDownloadAsync(release, generation, cancellation));
        }
        LogPhase(previous, Snapshot.Status);
        Changed?.Invoke(this, EventArgs.Empty);
        return task;
    }

    private async Task<string?> RunDownloadAsync(UpdateRelease release, long generation, CancellationTokenSource cancellation)
    {
        var acquired = false;
        try
        {
            await _operationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            acquired = true;
            cancellation.Token.ThrowIfCancellationRequested();
            _source.ValidateRelease(release);
            UpdateReleaseValidation.Validate(release, _releasePublicKey);
            _cache = new UpdateDownloadCache(_updatesDirectory, _source.Id, release);
            var cache = _cache;
            var phase = ApplicationUpdateStatus.Downloading;
            var path = await _downloader.DownloadAsync(cache, _source,
                progress => PublishTransfer(generation, phase, progress),
                status => { phase = status; PublishTransfer(generation, status, _progress); }, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            PublishTransfer(generation, ApplicationUpdateStatus.Ready, new(release.Installer.Size, release.Installer.Size, SupportsResume: true));
            bool install;
            lock (_lifecycle) install = !_shutdown && _generation == generation && _autoInstall;
            if (install) BeginInstall(path, generation);
            return path;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            lock (_lifecycle) { if (_generation == generation) _autoInstall = false; }
            PublishTransfer(generation, ApplicationUpdateStatus.Paused, CachedProgress(release));
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or
            UnauthorizedAccessException or TaskCanceledException)
        {
            _logger.Log(LogLevel.Warning, "Update download or verification failed; valid checkpoints are retained.", exception);
            if (exception is InvalidDataException or DownloadRestartException)
            {
                try { _cache?.ResetPayload(); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                { _logger.Log(LogLevel.Warning, "Invalid download data could not be fully removed.", cleanup); }
            }
            lock (_lifecycle) { if (_generation == generation) _autoInstall = false; }
            PublishTransfer(generation, ApplicationUpdateStatus.Error, CachedProgress(release),
                exception is ResumeUnavailableException ? UpdateDownloadError.ResumeUnsupported : UpdateDownloadError.Failed);
            return null;
        }
        finally
        {
            if (acquired) _operationGate.Release();
            lock (_lifecycle) { if (ReferenceEquals(_downloadCancellation, cancellation)) _downloadCancellation = null; }
            cancellation.Dispose();
        }
    }

    private void PublishTransfer(long generation, ApplicationUpdateStatus status, UpdateDownloadProgress? progress,
        UpdateDownloadError error = UpdateDownloadError.None)
    {
        ApplicationUpdateStatus previous;
        lock (_lifecycle)
        {
            if (_generation != generation || _shutdown) return;
            previous = Snapshot.Status;
            _progress = progress;
            Volatile.Write(ref _snapshot, CreateSnapshot(status, Snapshot.Release, error));
        }
        LogPhase(previous, status);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task PauseDownloadAsync() => RequestControl(clear: false);
    public Task StopDownloadAsync() => RequestControl(clear: true);

    private Task RequestControl(bool clear)
    {
        lock (_lifecycle)
        {
            if (_controlTask is { IsCompleted: false }) return _controlTask;
            return _controlTask = ControlDownloadAsync(clear);
        }
    }

    private async Task ControlDownloadAsync(bool clear)
    {
        Task<string?>? task;
        CancellationTokenSource? cancellation;
        UpdateRelease? release;
        long generation;
        ApplicationUpdateStatus previous;
        lock (_lifecycle)
        {
            if (_shutdown || _installing || !_hasTask || Snapshot.Status is ApplicationUpdateStatus.Pausing or ApplicationUpdateStatus.Stopping) return;
            _autoInstall = false;
            generation = ++_generation;
            task = _downloadTask;
            cancellation = _downloadCancellation;
            release = Snapshot.Release;
            previous = Snapshot.Status;
            Volatile.Write(ref _snapshot, CreateSnapshot(clear ? ApplicationUpdateStatus.Stopping : ApplicationUpdateStatus.Pausing, release));
        }
        Cancel(cancellation);
        LogPhase(previous, Snapshot.Status);
        Changed?.Invoke(this, EventArgs.Empty);
        if (task is not null) await task.ConfigureAwait(false);
        try
        {
            if (clear) _cache?.Delete();
            lock (_lifecycle)
            {
                if (_generation != generation || _shutdown) return;
                if (clear) { _cache = null; _hasTask = false; _progress = null; }
                else _progress = release is null ? null : CachedProgress(release);
                previous = Snapshot.Status;
                Volatile.Write(ref _snapshot, CreateSnapshot(clear ? ApplicationUpdateStatus.Available : ApplicationUpdateStatus.Paused, release));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "The download cache could not be cleared.", exception);
            PublishTransfer(generation, ApplicationUpdateStatus.Error, _progress, UpdateDownloadError.Failed);
            return;
        }
        LogPhase(previous, Snapshot.Status);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task PrepareForExitAsync()
    {
        Task<string?>? task;
        Task? control;
        CancellationTokenSource? cancellation;
        lock (_lifecycle)
        {
            _shutdown = true;
            _autoInstall = false;
            ++_generation;
            task = _downloadTask;
            control = _controlTask;
            cancellation = _installing ? null : _downloadCancellation;
        }
        Cancel(_lifetime);
        Cancel(cancellation);
        if (task is not null) await task.ConfigureAwait(false);
        if (control is not null) await control.ConfigureAwait(false);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        _operationGate.Release();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private UpdateDownloadProgress CachedProgress(UpdateRelease release)
    {
        var state = _cache?.State;
        return new(state?.Complete == true ? release.Installer.Size : state?.Segments.Sum(segment => segment.SavedBytes) ?? 0,
            release.Installer.Size, SupportsResume: state?.SupportsRanges);
    }

    private void RestoreCache()
    {
        try
        {
            var valid = new List<DownloadCacheState>();
            foreach (var state in UpdateDownloadCache.ReadAll(_updatesDirectory))
            {
                try
                {
                    if (state.SourceId != _source.Id) continue;
                    UpdateReleaseValidation.Validate(state.Release, _releasePublicKey);
                    _source.ValidateRelease(state.Release);
                    valid.Add(state);
                }
                catch (InvalidDataException) { _logger.Log(LogLevel.Debug, "An invalid download checkpoint was ignored."); }
            }
            var future = valid.Where(entry => entry.Release.Version > _currentVersion && entry.LastUsedUtc >= _utcNow().AddDays(-30));
            var active = future.Where(entry => !entry.Complete).MaxBy(entry => entry.LastUsedUtc);
            var complete = future.Where(entry => entry.Complete).MaxBy(entry => entry.LastUsedUtc);
            foreach (var obsolete in valid.Where(entry => !ReferenceEquals(entry, active) && !ReferenceEquals(entry, complete)))
            {
                var directory = Path.Combine(_updatesDirectory, obsolete.Release.VersionText);
                if (Directory.EnumerateDirectories(directory, "updater-*").Any()) continue;
                try { new UpdateDownloadCache(_updatesDirectory, _source.Id, obsolete.Release).Delete(); }
                catch (IOException) { _logger.Log(LogLevel.Debug, "A locked download cache was retained."); }
            }
            var restored = active ?? complete;
            if (restored is null) return;
            _cache = new UpdateDownloadCache(_updatesDirectory, _source.Id, restored.Release);
            _progress = CachedProgress(restored.Release);
            _hasTask = CanInstallUpdates;
            _snapshot = CreateSnapshot(CanInstallUpdates ? ApplicationUpdateStatus.Paused : ApplicationUpdateStatus.Available, restored.Release);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Log(LogLevel.Warning, "The download cache could not be restored.", exception);
        }
    }
}
