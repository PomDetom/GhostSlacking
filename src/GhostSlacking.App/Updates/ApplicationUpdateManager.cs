using System.Diagnostics;
using System.Net.Http.Headers;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed partial class ApplicationUpdateManager : IApplicationUpdateManager
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IUpdateSource _source;
    private readonly InstallerDownloader _downloader;
    private readonly UpdateStateStore _stateStore;
    private readonly IUpdateInstallerLauncher _installerLauncher;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _updatesDirectory;
    private readonly ReleaseVersion _currentVersion;
    private readonly string? _releasePublicKey;
    private readonly object _lifecycle = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private long _phaseStartedAt = Stopwatch.GetTimestamp();
    private UpdateState _state;
    private UpdateChannel _channel;
    private ApplicationUpdateSnapshot _snapshot;
    private bool _disposed;
    private bool _shutdown;
    private bool _installing;
    private bool _hasTask;
    private bool _autoInstall;
    private long _generation;
    private UpdateDownloadCache? _cache;
    private UpdateDownloadProgress? _progress;
    private CancellationTokenSource? _downloadCancellation;
    private Task<string?>? _downloadTask;
    private Task? _controlTask;

    public ApplicationUpdateManager(ILogger logger, HttpClient? httpClient = null, UpdateStateStore? stateStore = null,
        IUpdateInstallerLauncher? installerLauncher = null, IExternalLinkLauncher? linkLauncher = null,
        Func<DateTimeOffset>? utcNow = null, string? updatesDirectory = null, string? currentVersion = null,
        Func<bool>? installationDetector = null, UpdateChannel channel = UpdateChannel.Stable,
        string? releasePublicKey = null, IUpdateSource? source = null, UpdateDownloadOptions? downloadOptions = null)
    {
        _logger = logger;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 4, PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        }) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GhostSlacking", currentVersion ?? ApplicationVersionInfo.Current()));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _source = source ?? new GitHubUpdateSource(_httpClient, logger, releasePublicKey);
        _downloader = new InstallerDownloader(_httpClient, downloadOptions);
        _stateStore = stateStore ?? new UpdateStateStore(logger: logger);
        _installerLauncher = installerLauncher ?? new AutomatedUpdateLauncher(logger);
        _linkLauncher = linkLauncher ?? new ShellLauncher();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _updatesDirectory = updatesDirectory ?? Path.Combine(GhostSlackingDataPaths.RootDirectory, "updates");
        _currentVersion = ReleaseVersion.TryParse(currentVersion ?? ApplicationVersionInfo.Current(), out var parsed) ? parsed : new(0, 0, 0);
        _releasePublicKey = releasePublicKey;
        _channel = channel;
        _state = _stateStore.Load();
        CanInstallUpdates = (installationDetector ?? InstalledLocationMatchesCurrentApplication)();
        _snapshot = new(ApplicationUpdateStatus.Idle, _currentVersion.Text, LastSuccessfulCheckUtc: _state.LastSuccessfulCheckUtc);
        RestoreCache();
    }

    public ApplicationUpdateSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public bool CanInstallUpdates { get; }
    public UpdateChannel Channel => _channel;
    public event EventHandler? Changed;
    public event EventHandler? InstallHandoffStarted;
    public void SetChannel(UpdateChannel channel) => _channel = channel is UpdateChannel.Stable or UpdateChannel.Test ? channel : UpdateChannel.Stable;

    public async Task<ApplicationUpdateSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lifecycle) { if (_hasTask || _shutdown) return Snapshot; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        try { await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_shutdown && cancellationToken.IsCancellationRequested) { return Snapshot; }
        try
        {
            lock (_lifecycle) { if (_hasTask || _shutdown) return Snapshot; }
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Checking, Snapshot.Release));
            try
            {
                var release = await _source.GetLatestReleaseAsync(_channel, _currentVersion, cancellationToken).ConfigureAwait(false);
                if (release is not null) { _source.ValidateRelease(release); UpdateReleaseValidation.Validate(release, _releasePublicKey); }
                _state = _state with { LastSuccessfulCheckUtc = _utcNow() };
                _stateStore.Save(_state);
                var status = release is null ? ApplicationUpdateStatus.NoCompatibleRelease : release.Version <= _currentVersion
                    ? ApplicationUpdateStatus.UpToDate : _state.SkippedVersion == release.VersionText
                    ? ApplicationUpdateStatus.Skipped : ApplicationUpdateStatus.Available;
                if (release is not null && ReleaseVersion.TryParse(_state.SkippedVersion, out var skipped) && release.Version > skipped)
                {
                    _state = _state with { SkippedVersion = null };
                    _stateStore.Save(_state);
                }
                SetSnapshot(CreateSnapshot(status, release));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (_shutdown) return Snapshot;
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Idle, Snapshot.Release));
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or System.Text.Json.JsonException or InvalidDataException or TaskCanceledException)
            {
                _logger.Log(LogLevel.Warning, "Update check failed.", exception);
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, Snapshot.Release));
            }
            return Snapshot;
        }
        finally { _operationGate.Release(); }
    }

    public bool BeginAutomaticInstall(string installerPath) => BeginInstall(installerPath, null);

    private bool BeginInstall(string installerPath, long? generation)
    {
        UpdateRelease release;
        lock (_lifecycle)
        {
            if (_shutdown || _installing || !CanInstallUpdates || Snapshot.Status != ApplicationUpdateStatus.Ready ||
                Snapshot.Release is not { } candidate || generation is { } expected && (_generation != expected || !_autoInstall)) return false;
            release = candidate;
            _installing = true;
        }
        SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Preparing, release));
        try
        {
            var expectedPath = Path.GetFullPath(Path.Combine(_updatesDirectory, release.VersionText, release.Installer.Name));
            var application = Environment.ProcessPath;
            if (!string.Equals(expectedPath, Path.GetFullPath(installerPath), StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(application))
                throw new InvalidDataException("The installer is outside the verified version cache.");
            var signed = UpdateReleaseValidation.Validate(release, _releasePublicKey);
            using var installer = signed.OpenVerifiedInstaller(expectedPath);
            using var process = Process.GetCurrentProcess();
            var request = new UpdateInstallRequest(expectedPath, release.VersionText, Path.GetFullPath(application),
                Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks);
            if (_installerLauncher.Launch(request))
            {
                InstallHandoffStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Log(LogLevel.Warning, "The automatic update handoff could not be started.", exception);
        }
        lock (_lifecycle) { _installing = false; _autoInstall = false; }
        SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, release, UpdateDownloadError.Failed));
        return false;
    }

    public void SkipCurrentRelease()
    {
        lock (_lifecycle) { if (_hasTask || _shutdown) return; }
        var release = Snapshot.Release;
        if (release is null || release.Version <= _currentVersion) return;
        _state = _state with { SkippedVersion = release.VersionText };
        _stateStore.Save(_state);
        SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Skipped, release));
    }

    public void ResumeCurrentRelease()
    {
        lock (_lifecycle) { if (_hasTask || _shutdown) return; }
        _state = _state with { SkippedVersion = null };
        _stateStore.Save(_state);
        var release = Snapshot.Release;
        SetSnapshot(CreateSnapshot(release is not null && release.Version > _currentVersion ? ApplicationUpdateStatus.Available : ApplicationUpdateStatus.Idle, release));
    }

    public bool OpenSourceRepository() => OpenLink(_source.RepositoryUri);
    public bool OpenReleasePage() => OpenLink(Snapshot.Release?.ReleasePageUrl ?? _source.ReleaseListUri);
    private bool OpenLink(Uri uri)
    {
        try { return _linkLauncher.Open(uri); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Log(LogLevel.Warning, "Unable to open the update source page.", exception);
            return false;
        }
    }

    private ApplicationUpdateSnapshot CreateSnapshot(ApplicationUpdateStatus status, UpdateRelease? release,
        UpdateDownloadError error = UpdateDownloadError.None) => new(status, _currentVersion.Text, release,
        _progress is null ? null : (int)Math.Clamp(_progress.DownloadedBytes * 100 / _progress.TotalBytes, 0, 100),
        _state.LastSuccessfulCheckUtc, _progress, error, _hasTask);

    private void SetSnapshot(ApplicationUpdateSnapshot snapshot)
    {
        ApplicationUpdateStatus previous;
        lock (_lifecycle) { previous = _snapshot.Status; Volatile.Write(ref _snapshot, snapshot); }
        LogPhase(previous, snapshot.Status);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LogPhase(ApplicationUpdateStatus previous, ApplicationUpdateStatus current)
    {
        if (previous == current) return;
        var started = Interlocked.Exchange(ref _phaseStartedAt, Stopwatch.GetTimestamp());
        _logger.Log(LogLevel.Info, $"UpdatePhase {previous} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} next={current}");
    }

    private static bool InstalledLocationMatchesCurrentApplication()
    {
        try { return UserInstallation.Matches(AppContext.BaseDirectory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        PrepareForExitAsync().GetAwaiter().GetResult();
        _disposed = true;
        _lifetime.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
