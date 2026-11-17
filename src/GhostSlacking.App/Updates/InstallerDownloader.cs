using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace GhostSlacking.App;

internal sealed class InstallerDownloader(HttpClient client, UpdateDownloadOptions? options = null)
{
    private readonly UpdateDownloadOptions _options = options ?? new();

    public async Task<string> DownloadAsync(UpdateDownloadCache cache, IUpdateSource source,
        Action<UpdateDownloadProgress> progress, Action<ApplicationUpdateStatus> phase, CancellationToken cancellationToken)
    {
        var release = cache.State.Release;
        source.ValidateRelease(release);
        if (cache.State.ChecksumText is not { } checksum)
        {
            for (var retry = 0; ; retry++)
            {
                try { checksum = await source.ReadChecksumAsync(release, cancellationToken).ConfigureAwait(false); break; }
                catch (Exception exception) when (IsTransient(exception) && retry < _options.MaxRetries && !cancellationToken.IsCancellationRequested)
                { await RetryAsync(exception, retry, cancellationToken).ConfigureAwait(false); }
            }
            cache.SetChecksum(checksum);
        }
        UpdateReleaseValidation.ValidateChecksum(checksum, release);
        cache.Touch();
        if (File.Exists(cache.InstallerPath))
        {
            phase(ApplicationUpdateStatus.Verifying);
            if (await MatchesAsync(cache.InstallerPath, release, cancellationToken).ConfigureAwait(false))
            {
                await SaveVerificationAssetsAsync(cache, checksum, cancellationToken).ConfigureAwait(false);
                cache.MarkComplete();
                progress(new(release.Installer.Size, release.Installer.Size, SupportsResume: true));
                return cache.InstallerPath;
            }
            cache.ResetPayload();
        }

        for (var restart = 0; ; restart++)
        {
            try
            {
                phase(ApplicationUpdateStatus.Downloading);
                await TransferAsync(cache, source, progress, cancellationToken, restart > 0).ConfigureAwait(false);
                break;
            }
            catch (DownloadRestartException) when (restart == 0)
            {
                cache.ResetPayload();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        phase(ApplicationUpdateStatus.Verifying);
        await using (var destination = new FileStream(cache.AssemblyPath, FileMode.Create, FileAccess.Write, FileShare.None,
            131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            for (var index = 0; index < cache.State.Segments.Count; index++)
            {
                await using var part = File.OpenRead(cache.SegmentPath(index));
                await part.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }
        if (!await MatchesAsync(cache.AssemblyPath, release, cancellationToken).ConfigureAwait(false))
        {
            cache.ResetPayload();
            throw new InvalidDataException("The downloaded installer checksum is invalid.");
        }
        // Keep the existing Updater path and signed-manifest protocol.
        await SaveVerificationAssetsAsync(cache, checksum, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(cache.AssemblyPath, cache.InstallerPath, overwrite: true);
        cache.MarkComplete();
        return cache.InstallerPath;
    }

    private static async Task SaveVerificationAssetsAsync(UpdateDownloadCache cache, string checksum, CancellationToken cancellationToken)
    {
        var release = cache.State.Release;
        var directory = Path.GetDirectoryName(cache.InstallerPath)!;
        await File.WriteAllBytesAsync(Path.Combine(directory, "release.json"), release.ManifestBytes!, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "release.json.sig"), release.SignatureText!, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, release.Checksum.Name), checksum, cancellationToken).ConfigureAwait(false);
    }

    private async Task TransferAsync(UpdateDownloadCache cache, IUpdateSource source,
        Action<UpdateDownloadProgress> progress, CancellationToken cancellationToken, bool forceSingle = false)
    {
        var release = cache.State.Release;
        await cache.ValidateSegmentsAsync(cancellationToken).ConfigureAwait(false);
        var state = cache.State;
        var location = new DownloadLocation(release.Installer.DownloadUrl);
        var saved = state.Segments.Sum(segment => segment.SavedBytes);
        if (state.Segments.Count > 0 && saved == release.Installer.Size)
        {
            progress(new(saved, release.Installer.Size, SupportsResume: state.SupportsRanges));
            return;
        }
        var large = !forceSingle && release.Installer.Size >= _options.ParallelThreshold;
        if (large || saved > 0)
        {
            using var probe = await SendWithRetryAsync(source, release.Installer.DownloadUrl,
                new RangeHeaderValue(0, 0), null, cancellationToken).ConfigureAwait(false);
            location.Update(probe.RequestMessage?.RequestUri);
            var ranges = probe.StatusCode == HttpStatusCode.PartialContent;
            if (ranges) ValidateRange(probe, 0, 0, release.Installer.Size);
            else if (probe.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Unsupported download response.");
            if (!ranges && saved > 0) throw new ResumeUnavailableException();
            var etag = StrongETag(probe);
            if (state.ETag is not null && state.ETag != etag)
            {
                cache.ResetPayload();
                saved = 0;
            }
            cache.Configure(ranges, etag, ranges && large ? _options.SegmentSize : checked((int)release.Installer.Size));
        }
        else if (state.Segments.Count == 0)
        {
            cache.Configure(false, null, checked((int)release.Installer.Size));
        }
        state = cache.State;
        var counts = state.Segments.Select(segment => segment.SavedBytes).ToArray();
        var reporter = new DownloadReporter(counts, release.Installer.Size, progress, _options.ProgressInterval,
            () => cache.State.SupportsRanges);
        reporter.Report(force: true);
        using var group = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var next = -1;
        Exception? failure = null;
        async Task Worker()
        {
            try
            {
                while (Interlocked.Increment(ref next) is var index && index < counts.Length)
                {
                    await TransferSegmentAsync(cache, source, location, index, counts, reporter, group.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                if (exception is not OperationCanceledException || !group.IsCancellationRequested)
                    Interlocked.CompareExchange(ref failure, exception, null);
                group.Cancel();
                throw;
            }
        }
        var reporting = reporter.RunAsync(group.Token);
        var workers = Enumerable.Range(0, Math.Min(state.SupportsRanges == true ? _options.MaxParallelism : 1, counts.Length))
            .Select(_ => Worker()).ToArray();
        try { await Task.WhenAll(workers).ConfigureAwait(false); }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        finally
        {
            group.Cancel();
            await reporting.ConfigureAwait(false);
        }
        reporter.Report(force: true);
    }

    private async Task TransferSegmentAsync(UpdateDownloadCache cache, IUpdateSource source, DownloadLocation location, int index,
        long[] counts, DownloadReporter reporter, CancellationToken cancellationToken)
    {
        var release = cache.State.Release;
        var segment = cache.State.Segments[index];
        if (segment.SavedBytes == segment.Length) return;
        var path = cache.SegmentPath(index);
        var refreshLocation = false;
        for (var retry = 0; ; retry++)
        {
            segment = cache.State.Segments[index];
            var count = segment.SavedBytes;
            var changed = false;
            var requestUri = refreshLocation ? release.Installer.DownloadUrl : location.Uri;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
                131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            file.SetLength(count);
            var buffer = new byte[131072];
            while (file.Position < count)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count - file.Position)), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new IOException("A cached download segment was truncated.");
                hash.AppendData(buffer, 0, read);
            }
            void Checkpoint()
            {
                if (!changed) return;
                file.Flush(flushToDisk: true);
                cache.Checkpoint(index, count, Convert.ToHexString(hash.GetCurrentHash()));
                changed = false;
            }
            try
            {
                var start = segment.Offset + count;
                var end = segment.Offset + segment.Length - 1;
                using var response = await UpdateHttpTransport.SendAsync(client, requestUri, source.IsAllowedAssetUri,
                    cancellationToken, _options.HeaderTimeout, new RangeHeaderValue(start, end), cache.State.ETag).ConfigureAwait(false);
                location.Update(response.RequestMessage?.RequestUri);
                refreshLocation = false;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    ValidateRange(response, start, end, release.Installer.Size);
                    var etag = StrongETag(response);
                    if (cache.State.ETag is { } expected && expected != etag) throw new DownloadRestartException();
                    cache.Configure(true, etag, checked((int)release.Installer.Size));
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (count != 0 || segment.Offset != 0 || segment.Length != release.Installer.Size)
                        throw new DownloadRestartException();
                    cache.Configure(false, StrongETag(response), checked((int)release.Installer.Size));
                    if (response.Content.Headers.ContentLength is { } length && length != release.Installer.Size)
                        throw new InvalidDataException("The installer length differs from its signed manifest.");
                }
                else throw new InvalidDataException("Unsupported download response.");
                if (response.Content.Headers.ContentEncoding.Count != 0) throw new DownloadRestartException();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var checkpoint = Stopwatch.StartNew();
                while (count < segment.Length)
                {
                    var read = await UpdateHttpTransport.ReadAsync(stream, buffer.AsMemory(0, (int)Math.Min(buffer.Length, segment.Length - count)),
                        _options.IdleTimeout, cancellationToken).ConfigureAwait(false);
                    if (read == 0) throw new IOException("The download ended before the expected range was received.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    count += read;
                    changed = true;
                    Interlocked.Exchange(ref counts[index], count);
                    reporter.Report();
                    if (checkpoint.Elapsed >= _options.CheckpointInterval)
                    {
                        Checkpoint();
                        checkpoint.Restart();
                    }
                }
                if (await UpdateHttpTransport.ReadAsync(stream, buffer.AsMemory(0, 1), _options.IdleTimeout, cancellationToken).ConfigureAwait(false) != 0)
                    throw new DownloadRestartException();
                Checkpoint();
                return;
            }
            catch (UpdateHttpException e) when (e.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                throw new DownloadRestartException();
            }
            catch (UpdateHttpException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized &&
                requestUri != release.Installer.DownloadUrl && retry < _options.MaxRetries)
            {
                // CDN grants expire. Resolve the canonical asset again without persisting signed redirect URLs.
                refreshLocation = true;
            }
            catch (Exception e) when (IsTransient(e) && retry < _options.MaxRetries && !cancellationToken.IsCancellationRequested)
            {
                Checkpoint();
                if (cache.State.SupportsRanges != true)
                {
                    file.SetLength(0);
                    cache.Checkpoint(index, 0, string.Empty);
                    Interlocked.Exchange(ref counts[index], 0);
                }
                await RetryAsync(e, retry, cancellationToken).ConfigureAwait(false);
            }
            finally { Checkpoint(); }
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(IUpdateSource source, Uri uri, RangeHeaderValue range,
        string? etag, CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            try { return await UpdateHttpTransport.SendAsync(client, uri, source.IsAllowedAssetUri, cancellationToken,
                _options.HeaderTimeout, range, etag).ConfigureAwait(false); }
            catch (UpdateHttpException e) when (e.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { throw new DownloadRestartException(); }
            catch (Exception e) when (IsTransient(e) && retry < _options.MaxRetries && !cancellationToken.IsCancellationRequested)
            { await RetryAsync(e, retry, cancellationToken).ConfigureAwait(false); }
        }
    }

    private Task RetryAsync(Exception exception, int retry, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(_options.RetryDelay.TotalMilliseconds * Math.Pow(2, retry));
        if (exception is UpdateHttpException { RetryAfter: { } requested } && requested > delay) delay = requested;
        return Task.Delay(delay, cancellationToken);
    }

    private static bool IsTransient(Exception exception) => exception is IOException && exception is not DownloadRestartException &&
        exception is not ResumeUnavailableException || exception is HttpRequestException http &&
        (http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500);

    private static string? StrongETag(HttpResponseMessage response) => response.Headers.ETag is { IsWeak: false } etag ? etag.ToString() : null;

    private static void ValidateRange(HttpResponseMessage response, long start, long end, long total)
    {
        var range = response.Content.Headers.ContentRange;
        if (range is null || range.Unit != "bytes" || range.From != start || range.To != end || range.Length != total ||
            response.Content.Headers.ContentLength is { } length && length != end - start + 1)
            throw new DownloadRestartException();
    }

    private static async Task<bool> MatchesAsync(string path, UpdateRelease release, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length != release.Installer.Size) return false;
        await using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        return hash.Equals(release.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DownloadLocation(Uri uri)
    {
        private Uri _uri = uri;
        public Uri Uri => Volatile.Read(ref _uri);
        public void Update(Uri? resolved) { if (resolved is not null) Volatile.Write(ref _uri, resolved); }
    }

    private sealed class DownloadReporter(long[] counts, long total, Action<UpdateDownloadProgress> report,
        TimeSpan interval, Func<bool?> supportsResume)
    {
        private readonly object _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Queue<(double Time, long Bytes)> _samples = new();
        private double _last = double.NegativeInfinity;
        private readonly long _initialBytes = counts.Sum();
        private bool _firstBytesReported;
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(interval);
            try { while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) Report(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        public void Report(bool force = false)
        {
            lock (_gate)
            {
                var time = _clock.Elapsed.TotalSeconds;
                long downloaded = 0;
                for (var index = 0; index < counts.Length; index++) downloaded += Interlocked.Read(ref counts[index]);
                if (!_firstBytesReported && downloaded > _initialBytes) { force = true; _firstBytesReported = true; }
                if (!force && time - _last < interval.TotalSeconds) return;
                _samples.Enqueue((time, downloaded));
                while (_samples.Count > 1 && time - _samples.Peek().Time > 5) _samples.Dequeue();
                var first = _samples.Peek();
                var speed = time - first.Time > 0.2 ? Math.Max(0, (downloaded - first.Bytes) / (time - first.Time)) : 0;
                _last = time;
                report(new(downloaded, total, speed, speed > 0 ? TimeSpan.FromSeconds((total - downloaded) / speed) : null, supportsResume()));
            }
        }
    }
}

internal sealed class DownloadRestartException : IOException;
internal sealed class ResumeUnavailableException : IOException;
