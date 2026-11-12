using System.Security.Cryptography;
using System.Text.Json;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed record DownloadSegment(long Offset, long Length, long SavedBytes = 0, string Digest = "");
internal sealed record DownloadCacheState(int Format, string SourceId, UpdateRelease Release,
    DateTimeOffset LastUsedUtc, IReadOnlyList<DownloadSegment> Segments,
    string? ChecksumText = null, bool? SupportsRanges = null, string? ETag = null, bool Complete = false);

internal sealed class UpdateDownloadCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private readonly object _gate = new();
    private DownloadCacheState _state;
    private readonly string _directory;
    public DownloadCacheState State { get { lock (_gate) return _state; } }
    public string InstallerPath => Path.Combine(_directory, _state.Release.Installer.Name);
    public string AssemblyPath => InstallerPath + ".partial";
    private string IndexPath => Path.Combine(_directory, "download.json");

    public UpdateDownloadCache(string root, string sourceId, UpdateRelease release)
    {
        if (!ReleaseVersion.TryParse(release.VersionText, out _) || release.Installer.Name != SignedReleaseManifest.InstallerName(release.VersionText))
            throw new InvalidDataException("Invalid download cache identity.");
        _directory = Path.Combine(Path.GetFullPath(root), release.VersionText);
        EnsureDirectory(root);
        EnsureDirectory(_directory);
        var loaded = Read(_directory);
        _state = loaded is not null && loaded.SourceId == sourceId && SamePackage(loaded.Release, release)
            ? loaded with { Release = release }
            : new(1, sourceId, release, DateTimeOffset.UtcNow, []);
        if (_state.ChecksumText is { } checksum)
        {
            try { UpdateReleaseValidation.ValidateChecksum(checksum, release); }
            catch (InvalidDataException) { _state = _state with { ChecksumText = null }; }
        }
        if (loaded is not null && !SamePackage(loaded.Release, release)) DeletePayload();
    }

    public static bool SamePackage(UpdateRelease left, UpdateRelease right) => left.VersionText == right.VersionText &&
        left.Installer?.Name == right.Installer.Name && left.Installer.Size == right.Installer.Size &&
        string.Equals(left.ExpectedSha256, right.ExpectedSha256, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<DownloadCacheState> ReadAll(string root)
    {
        if (!Directory.Exists(root) || IsReparse(root)) return [];
        var states = new List<DownloadCacheState>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (ReleaseVersion.TryParse(Path.GetFileName(directory), out _) && !IsReparse(directory) && Read(directory) is { } state &&
                Path.GetFileName(directory) == state.Release.VersionText) states.Add(state);
        }
        return states;
    }

    private static DownloadCacheState? Read(string directory)
    {
        var path = Path.Combine(directory, "download.json");
        try
        {
            if (!File.Exists(path) || IsReparse(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return null;
            var state = JsonSerializer.Deserialize<DownloadCacheState>(File.ReadAllBytes(path), JsonOptions);
            if (state is null || state.Format != 1 || state.Release is null || state.Release.Installer is null ||
                state.Segments is null || state.Segments.Count > 4096) return null;
            long offset = 0;
            foreach (var segment in state.Segments)
            {
                if (segment is null || segment.Offset != offset || segment.Length <= 0 || segment.SavedBytes < 0 ||
                    segment.SavedBytes > segment.Length || segment.Length > state.Release.Installer.Size ||
                    segment.SavedBytes > 0 && (segment.Digest is null || segment.Digest.Length != 64)) return null;
                offset = checked(offset + segment.Length);
            }
            return state.Segments.Count == 0 || offset == state.Release.Installer.Size ? state : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    public void SetChecksum(string checksum)
    {
        UpdateReleaseValidation.ValidateChecksum(checksum, _state.Release);
        lock (_gate) { _state = _state with { ChecksumText = checksum }; Save(); }
    }

    public void Configure(bool ranges, string? etag, int segmentSize)
    {
        lock (_gate)
        {
            if (_state.Segments.Count == 0)
            {
                var segments = new List<DownloadSegment>();
                for (long offset = 0; offset < _state.Release.Installer.Size; offset += segmentSize)
                    segments.Add(new(offset, Math.Min(segmentSize, _state.Release.Installer.Size - offset)));
                _state = _state with { Segments = segments };
            }
            _state = _state with { SupportsRanges = ranges, ETag = etag };
            Save();
        }
    }

    public void Checkpoint(int index, long bytes, string digest)
    {
        lock (_gate)
        {
            var segments = _state.Segments.ToArray();
            segments[index] = segments[index] with { SavedBytes = bytes, Digest = digest };
            _state = _state with { Segments = segments };
            Save();
        }
    }

    public void Touch() { lock (_gate) Save(); }

    public string SegmentPath(int index) => InstallerPath + $".segment-{index:D4}.partial";

    public async Task ValidateSegmentsAsync(CancellationToken cancellationToken)
    {
        var segments = State.Segments;
        for (var index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = segments[index];
            var path = SegmentPath(index);
            var valid = false;
            if (File.Exists(path) && !IsReparse(path) && new FileInfo(path).Length >= segment.SavedBytes)
            {
                await using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                file.SetLength(segment.SavedBytes);
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
                valid = segment.SavedBytes == 0 || digest.Equals(segment.Digest, StringComparison.OrdinalIgnoreCase);
            }
            if (!valid)
            {
                File.Delete(path);
                Checkpoint(index, 0, string.Empty);
            }
        }
    }

    public void ResetPayload()
    {
        lock (_gate)
        {
            DeletePayload();
            _state = _state with { Segments = [], SupportsRanges = null, ETag = null, Complete = false };
            Save();
        }
    }

    public void MarkComplete()
    {
        lock (_gate) { _state = _state with { Complete = true }; Save(); }
        foreach (var file in Directory.EnumerateFiles(_directory, _state.Release.Installer.Name + ".segment-*.partial")) File.Delete(file);
    }

    public void Delete()
    {
        lock (_gate)
        {
            // Only delete known cache files. Updater staging, backups and unrelated files are never traversed.
            DeletePayload();
            foreach (var name in new[] { "download.json", "download.json.tmp", "release.json", "release.json.sig", _state.Release.Checksum.Name })
            {
                if (name != Path.GetFileName(name)) throw new InvalidDataException("Invalid cache file name.");
                File.Delete(Path.Combine(_directory, name));
            }
        }
    }

    private void DeletePayload()
    {
        File.Delete(InstallerPath);
        File.Delete(AssemblyPath);
        foreach (var path in Directory.EnumerateFiles(_directory, _state.Release.Installer.Name + ".segment-*.partial")) File.Delete(path);
    }

    private void Save()
    {
        if (File.Exists(IndexPath + ".tmp") && IsReparse(IndexPath + ".tmp"))
            throw new IOException("The download checkpoint cannot use a redirected file.");
        _state = _state with { LastUsedUtc = DateTimeOffset.UtcNow };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_state, JsonOptions);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("The download checkpoint exceeds its size limit.");
        using (var file = new FileStream(IndexPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(IndexPath + ".tmp", IndexPath, overwrite: true);
    }

    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (IsReparse(path)) throw new IOException("The download cache cannot use a redirected directory.");
    }
}
