using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

internal sealed class DownloadFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "GhostSlacking.DownloadTests." + Guid.NewGuid().ToString("N"));
    public byte[] Bytes { get; }
    public RangeDownloadHandler Handler { get; }
    public FixtureUpdateSource Source { get; }
    public HttpClient Client { get; }
    public RecordingDownloadInstaller Installer { get; } = new();
    public UpdateDownloadOptions Options { get; set; } = new()
    {
        ParallelThreshold = 128 * 1024, SegmentSize = 128 * 1024, MaxParallelism = 4,
        ProgressInterval = TimeSpan.FromMilliseconds(1), CheckpointInterval = TimeSpan.FromMilliseconds(5),
        RetryDelay = TimeSpan.FromMilliseconds(1)
    };

    public DownloadFixture(int size = 1024 * 1024)
    {
        Directory.CreateDirectory(Root);
        Bytes = new byte[size];
        new Random(7821).NextBytes(Bytes);
        Handler = new RangeDownloadHandler(Bytes);
        Client = new HttpClient(Handler);
        Source = new FixtureUpdateSource(Bytes);
    }

    public ApplicationUpdateManager Manager() => new(NullLogger.Instance, Client,
        new UpdateStateStore(Path.Combine(Root, "update-state.json")), Installer,
        updatesDirectory: Path.Combine(Root, "updates"), currentVersion: "1.0.0", installationDetector: () => true,
        releasePublicKey: TestReleaseSigning.PublicKey, source: Source, downloadOptions: Options);

    public string PackageDirectory => Path.Combine(Root, "updates", Source.Release.VersionText);

    public static async Task WaitAsync(ApplicationUpdateManager manager, Func<ApplicationUpdateSnapshot, bool> predicate)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs args) { if (predicate(manager.Snapshot)) completion.TrySetResult(); }
        manager.Changed += Changed;
        try
        {
            Changed(null, EventArgs.Empty);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        finally { manager.Changed -= Changed; }
    }

    public void Dispose()
    {
        Client.Dispose();
        var expected = Path.GetFullPath(Path.GetTempPath()) + "GhostSlacking.DownloadTests.";
        if (!Path.GetFullPath(Root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid fixture cleanup path.");
        Directory.Delete(Root, recursive: true);
    }
}

internal sealed class RecordingDownloadInstaller : IUpdateInstallerLauncher
{
    public int Calls;
    public bool Result { get; set; } = true;
    public Action? OnLaunch { get; set; }
    public bool Launch(UpdateInstallRequest request) { Interlocked.Increment(ref Calls); OnLaunch?.Invoke(); return Result; }
}

internal sealed class FixtureUpdateSource : IUpdateSource
{
    public string Id => "alternate-test-source";
    public Uri RepositoryUri => new("https://downloads.example.test/project");
    public Uri ReleaseListUri => new("https://downloads.example.test/releases");
    public UpdateRelease Release { get; set; }
    public int Checks;
    public int Checksums;
    public bool AllowCdn { get; set; }
    public bool BlockCheck { get; set; }
    public TaskCompletionSource CheckEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public UpdateChannel LastChannel;
    public FixtureUpdateSource(byte[] bytes)
    {
        var version = ReleaseVersion.Parse("2.0.0");
        var name = SignedReleaseManifest.InstallerName(version.Text);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            formatVersion = 2, product = "GhostSlacking", version = version.Text, channel = "stable", prerelease = false,
            architecture = "win-x64", installer = new { file = name, bytes = bytes.Length, sha256 = hash, kind = "nsis", scope = "currentUser" }
        }));
        UpdateAsset Asset(string file, long size) => new(file, size, new Uri("https://downloads.example.test/2.0.0/" + file), null);
        Release = new(version, version.Text, new Uri("https://downloads.example.test/2.0.0"), Asset(name, bytes.Length),
            Asset(name + ".sha256", 120), Asset("release.json", manifest.Length), hash, [],
            ManifestBytes: manifest, SignatureText: TestReleaseSigning.Sign(Encoding.UTF8.GetString(manifest)));
    }
    public async Task<UpdateRelease?> GetLatestReleaseAsync(UpdateChannel channel, ReleaseVersion currentVersion, CancellationToken cancellationToken)
    {
        Checks++;
        LastChannel = channel;
        CheckEntered.TrySetResult();
        if (BlockCheck) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Release;
    }
    public void ValidateRelease(UpdateRelease release)
    {
        UpdateReleaseValidation.Validate(release, TestReleaseSigning.PublicKey);
        if (!IsAllowedAssetUri(release.Installer.DownloadUrl)) throw new InvalidDataException("Untrusted test source URL.");
    }
    public bool IsAllowedAssetUri(Uri uri) => uri.Scheme == "https" && (uri.Host == "downloads.example.test" || AllowCdn && uri.Host == "cdn.example.test");
    public Task<string> ReadChecksumAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        Checksums++;
        return Task.FromResult(release.ExpectedSha256 + "  " + release.Installer.Name);
    }
}

internal sealed record DownloadRequest(long Start, long End, string? IfRange, bool Probe, string Host);

internal sealed class RangeDownloadHandler(byte[] bytes) : HttpMessageHandler
{
    public ConcurrentQueue<DownloadRequest> Requests { get; } = new();
    public bool IgnoreRange { get; set; }
    public bool WrongRange { get; set; }
    public string ETag { get; set; } = "\"download-v1\"";
    public int FailTransfers;
    public HttpStatusCode? Status { get; set; }
    public int TransientResponses;
    public TimeSpan? RetryAfter { get; set; }
    public bool HangHeaders { get; set; }
    public bool HangBody { get; set; }
    public bool RejectedRedirect { get; set; }
    public bool RedirectToCdn { get; set; }
    public int ExpiredCdnResponses;
    public TimeSpan ReadDelay { get; set; } = TimeSpan.FromMilliseconds(4);
    public long NetworkBytes;
    public int ActiveTransfers;
    public int MaxTransfers;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var range = request.Headers.Range?.Ranges.Single();
        var start = range?.From ?? 0;
        var end = range?.To ?? bytes.Length - 1;
        var probe = start == 0 && end == 0;
        Requests.Enqueue(new(start, end, request.Headers.IfRange?.ToString(), probe, request.RequestUri!.Host));
        if (HangHeaders) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (RejectedRedirect)
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://untrusted.example.test/setup.exe");
            return redirect;
        }
        if (RedirectToCdn && request.RequestUri.Host == "downloads.example.test")
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://cdn.example.test/installer");
            return redirect;
        }
        if (!probe && request.RequestUri.Host == "cdn.example.test" && Interlocked.Decrement(ref ExpiredCdnResponses) >= 0)
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        if (Status is not null || Interlocked.Decrement(ref TransientResponses) >= 0)
        {
            var response = new HttpResponseMessage(Status ?? HttpStatusCode.ServiceUnavailable);
            if (RetryAfter is { } wait) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
            return response;
        }
        var supports = !IgnoreRange && request.Headers.Range is not null;
        if (!supports) { start = 0; end = bytes.Length - 1; }
        var fail = !probe && Interlocked.Decrement(ref FailTransfers) >= 0;
        var content = new StreamContent(new ThrottledDownloadStream(this, bytes, (int)start, checked((int)(end - start + 1)), probe, fail));
        content.Headers.ContentLength = end - start + 1;
        if (supports) content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, WrongRange ? bytes.Length + 1 : bytes.Length);
        var result = new HttpResponseMessage(supports ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content, RequestMessage = request };
        result.Headers.ETag = new EntityTagHeaderValue(ETag);
        if (supports) result.Headers.AcceptRanges.Add("bytes");
        return result;
    }

    private sealed class ThrottledDownloadStream : Stream
    {
        private readonly RangeDownloadHandler _owner;
        private readonly byte[] _bytes;
        private readonly int _offset;
        private readonly int _length;
        private readonly bool _probe;
        private readonly bool _fail;
        private int _position;
        private bool _disposed;
        public ThrottledDownloadStream(RangeDownloadHandler owner, byte[] bytes, int offset, int length, bool probe, bool fail)
        {
            _owner = owner; _bytes = bytes; _offset = offset; _length = length; _probe = probe; _fail = fail;
            if (!probe)
            {
                var active = Interlocked.Increment(ref owner.ActiveTransfers);
                int previous;
                do { previous = owner.MaxTransfers; } while (active > previous && Interlocked.CompareExchange(ref owner.MaxTransfers, active, previous) != previous);
            }
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_owner.HangBody) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (_fail && _position >= 16384) throw new IOException("Simulated connection reset.");
            if (_position == _length) return 0;
            await Task.Delay(_owner.ReadDelay, cancellationToken);
            var count = Math.Min(16384, Math.Min(buffer.Length, _length - _position));
            _bytes.AsMemory(_offset + _position, count).CopyTo(buffer);
            _position += count;
            Interlocked.Add(ref _owner.NetworkBytes, count);
            return count;
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && !_probe) Interlocked.Decrement(ref _owner.ActiveTransfers);
            _disposed = true;
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
