using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GhostSlacking.Core;
using Microsoft.Win32;

namespace GhostSlacking.App;

internal enum ApplicationUpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Skipped,
    Downloading,
    Verifying,
    Ready,
    Error
}

internal sealed record UpdateAsset(string Name, long Size, Uri DownloadUrl, string? Digest);

internal sealed record UpdateRelease(
    Version Version,
    string VersionText,
    Uri ReleasePageUrl,
    UpdateAsset Installer,
    UpdateAsset Checksum,
    UpdateAsset Manifest,
    string ExpectedSha256);

internal sealed record ApplicationUpdateSnapshot(
    ApplicationUpdateStatus Status,
    string CurrentVersion,
    UpdateRelease? Release = null,
    int? ProgressPercent = null,
    DateTimeOffset? LastSuccessfulCheckUtc = null);

internal interface IApplicationUpdateManager : IDisposable
{
    ApplicationUpdateSnapshot Snapshot { get; }
    bool CanInstallUpdates { get; }
    event EventHandler? Changed;
    Task<ApplicationUpdateSnapshot> CheckAsync(CancellationToken cancellationToken = default);
    Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default);
    bool LaunchInstaller(string installerPath);
    void SkipCurrentRelease();
    void ResumeCurrentRelease();
    bool OpenSourceRepository();
    bool OpenReleasePage();
}

internal sealed record UpdateState(DateTimeOffset? LastSuccessfulCheckUtc = null, string? SkippedVersion = null);

internal sealed class UpdateStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public UpdateStateStore(string? path = null, ILogger? logger = null)
    {
        _path = path ?? Path.Combine(GhostSlackingDataPaths.RootDirectory, "update-state.json");
        _logger = logger ?? NullLogger.Instance;
    }

    public UpdateState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UpdateState();
            }

            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_path), JsonOptions) ?? new UpdateState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Log(LogLevel.Warning, "Update state is invalid; using defaults.", exception);
            return new UpdateState();
        }
    }

    public bool Save(UpdateState state)
    {
        var temporaryPath = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "Unable to save update state.", exception);
            TryDelete(temporaryPath);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}

internal interface IUpdateInstallerLauncher
{
    bool Launch(string installerPath);
}

internal interface IExternalLinkLauncher
{
    bool Open(Uri uri);
}

internal sealed class ShellLauncher : IUpdateInstallerLauncher, IExternalLinkLauncher
{
    public bool Launch(string installerPath) => Start(installerPath);

    public bool Open(Uri uri) => Start(uri.AbsoluteUri);

    private static bool Start(string target) => Process.Start(new ProcessStartInfo
    {
        FileName = target,
        UseShellExecute = true
    }) is not null;
}

internal static partial class ApplicationVersionInfo
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ThreePartVersionPattern();

    public static string Current(Assembly? assembly = null)
    {
        assembly ??= typeof(GhostApplicationController).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var candidate = informational?.Split('+', 2)[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return ThreePartVersionPattern().IsMatch(candidate) ? candidate : "0.0.0";
    }
}

internal sealed partial class GitHubApplicationUpdateManager : IApplicationUpdateManager
{
    private const long MaximumInstallerBytes = 256L * 1024 * 1024;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumChecksumBytes = 4096;
    internal static readonly Uri SourceRepositoryUri = new("https://github.com/PomDetom/GhostSlacking");
    internal static readonly Uri ReleasesUri = new("https://github.com/PomDetom/GhostSlacking/releases");
    internal static readonly Uri LatestReleaseApiUri = new("https://api.github.com/repos/PomDetom/GhostSlacking/releases/latest");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly UpdateStateStore _stateStore;
    private readonly IUpdateInstallerLauncher _installerLauncher;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _updatesDirectory;
    private readonly Version _currentVersion;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private UpdateState _state;
    private bool _disposed;

    public GitHubApplicationUpdateManager(
        ILogger logger,
        HttpClient? httpClient = null,
        UpdateStateStore? stateStore = null,
        IUpdateInstallerLauncher? installerLauncher = null,
        IExternalLinkLauncher? linkLauncher = null,
        Func<DateTimeOffset>? utcNow = null,
        string? updatesDirectory = null,
        string? currentVersion = null,
        Func<bool>? installationDetector = null)
    {
        _logger = logger;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GhostSlacking", currentVersion ?? ApplicationVersionInfo.Current()));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _stateStore = stateStore ?? new UpdateStateStore(logger: logger);
        _installerLauncher = installerLauncher ?? new ShellLauncher();
        _linkLauncher = linkLauncher ?? new ShellLauncher();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _updatesDirectory = updatesDirectory ?? Path.Combine(GhostSlackingDataPaths.RootDirectory, "updates");
        var versionText = currentVersion ?? ApplicationVersionInfo.Current();
        _currentVersion = Version.TryParse(versionText, out var parsedVersion) ? parsedVersion : new Version(0, 0, 0);
        _state = _stateStore.Load();
        CanInstallUpdates = (installationDetector ?? InstalledLocationMatchesCurrentApplication)();
        Snapshot = CreateSnapshot(ApplicationUpdateStatus.Idle);
        CleanupOldDownloads();
    }

    public ApplicationUpdateSnapshot Snapshot { get; private set; }

    public bool CanInstallUpdates { get; }

    public event EventHandler? Changed;

    public async Task<ApplicationUpdateSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Checking, Snapshot.Release));

            try
            {
                var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
                _state = _state with { LastSuccessfulCheckUtc = _utcNow() };
                _stateStore.Save(_state);

                if (release.Version <= _currentVersion)
                {
                    SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.UpToDate, release));
                    return Snapshot;
                }

                if (string.Equals(_state.SkippedVersion, release.VersionText, StringComparison.Ordinal))
                {
                    SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Skipped, release));
                    return Snapshot;
                }

                if (Version.TryParse(_state.SkippedVersion, out var skippedVersion) && release.Version > skippedVersion)
                {
                    _state = _state with { SkippedVersion = null };
                    _stateStore.Save(_state);
                }

                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Available, release));
                return Snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Idle, Snapshot.Release));
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidDataException or TaskCanceledException)
            {
                _logger.Log(LogLevel.Warning, "GitHub update check failed.", exception);
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, Snapshot.Release));
                return Snapshot;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? partialPath = null;
        try
        {
            var release = Snapshot.Release;
            if (release is null || release.Version <= _currentVersion || !CanInstallUpdates)
            {
                return null;
            }

            var releaseDirectory = Path.Combine(_updatesDirectory, release.VersionText);
            Directory.CreateDirectory(releaseDirectory);
            var installerPath = Path.Combine(releaseDirectory, release.Installer.Name);
            partialPath = $"{installerPath}.partial";
            File.Delete(partialPath);

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Downloading, release, 0));

            using var response = await _httpClient.GetAsync(
                release.Installer.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithProgressAsync(source, destination, release.Installer.Size, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Verifying, release));

            var fileInfo = new FileInfo(partialPath);
            if (fileInfo.Length != release.Installer.Size)
            {
                throw new InvalidDataException("The installer size does not match the release manifest.");
            }

            var checksumText = await ReadSmallTextAssetAsync(
                    release.Checksum,
                    MaximumChecksumBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var expectedFromChecksum = ParseChecksum(checksumText, release.Installer.Name);
            if (!string.Equals(expectedFromChecksum, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The checksum asset and release manifest disagree.");
            }

            string actualHash;
            await using (var installer = File.OpenRead(partialPath))
            {
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken));
            }
            if (!string.Equals(actualHash, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded installer checksum is invalid.");
            }

            File.Move(partialPath, installerPath, overwrite: true);
            partialPath = null;
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Ready, release, 100));
            return installerPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (partialPath is not null)
            {
                TryDelete(partialPath);
            }

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Available, Snapshot.Release));
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            if (partialPath is not null)
            {
                TryDelete(partialPath);
            }

            _logger.Log(LogLevel.Warning, "Update installer download or validation failed.", exception);
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, Snapshot.Release));
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public bool LaunchInstaller(string installerPath)
    {
        try
        {
            if (!CanInstallUpdates || !File.Exists(installerPath))
            {
                return false;
            }

            if (_installerLauncher.Launch(installerPath))
            {
                return true;
            }

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, Snapshot.Release));
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Log(LogLevel.Warning, "The update installer could not be started.", exception);
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, Snapshot.Release));
            return false;
        }
    }

    public void SkipCurrentRelease()
    {
        var release = Snapshot.Release;
        if (release is null || release.Version <= _currentVersion)
        {
            return;
        }

        _state = _state with { SkippedVersion = release.VersionText };
        _stateStore.Save(_state);
        SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Skipped, release));
    }

    public void ResumeCurrentRelease()
    {
        var release = Snapshot.Release;
        _state = _state with { SkippedVersion = null };
        _stateStore.Save(_state);
        SetSnapshot(CreateSnapshot(
            release is not null && release.Version > _currentVersion
                ? ApplicationUpdateStatus.Available
                : ApplicationUpdateStatus.Idle,
            release));
    }

    public bool OpenSourceRepository() => OpenLink(SourceRepositoryUri);

    public bool OpenReleasePage() => OpenLink(Snapshot.Release?.ReleasePageUrl ?? ReleasesUri);

    private bool OpenLink(Uri uri)
    {
        try
        {
            return _linkLauncher.Open(uri);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Log(LogLevel.Warning, $"Unable to open external link: {uri}", exception);
            return false;
        }
    }

    private async Task<UpdateRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUri);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var apiRelease = await response.Content.ReadFromJsonAsync<ApiRelease>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("GitHub returned an empty release.");

        if (apiRelease.Draft || apiRelease.Prerelease || apiRelease.TagName is null ||
            !ReleaseTagPattern().IsMatch(apiRelease.TagName))
        {
            throw new InvalidDataException("The latest GitHub release metadata is invalid.");
        }

        if (!Version.TryParse(apiRelease.TagName[1..], out var releaseVersion) ||
            !Uri.TryCreate(apiRelease.HtmlUrl, UriKind.Absolute, out var releasePageUrl))
        {
            throw new InvalidDataException("The latest GitHub release version or URL is invalid.");
        }

        var versionText = releaseVersion.ToString(3);
        var installerName = $"GhostSlacking-{versionText}-win-x64.msi";
        var installer = FindAsset(apiRelease.Assets, installerName);
        var checksum = FindAsset(apiRelease.Assets, $"{installerName}.sha256");
        var manifest = FindAsset(apiRelease.Assets, "release.json");
        if (installer.Size > MaximumInstallerBytes || checksum.Size > MaximumChecksumBytes ||
            manifest.Size > MaximumManifestBytes)
        {
            throw new InvalidDataException("One or more release assets exceed the allowed size.");
        }
        ValidateDownloadUri(installer.DownloadUrl, apiRelease.TagName);
        ValidateDownloadUri(checksum.DownloadUrl, apiRelease.TagName);
        ValidateDownloadUri(manifest.DownloadUrl, apiRelease.TagName);

        var manifestText = await ReadSmallTextAssetAsync(
                manifest,
                MaximumManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        var releaseManifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestText, JsonOptions) ??
            throw new InvalidDataException("The release manifest is empty.");
        if (!string.Equals(releaseManifest.Product, "GhostSlacking", StringComparison.Ordinal) ||
            !string.Equals(releaseManifest.Version, versionText, StringComparison.Ordinal) ||
            !string.Equals(releaseManifest.Architecture, "win-x64", StringComparison.Ordinal) ||
            releaseManifest.Installer is null ||
            !string.Equals(releaseManifest.Installer.File, installerName, StringComparison.Ordinal) ||
            releaseManifest.Installer.Bytes != installer.Size ||
            releaseManifest.Installer.Sha256 is null ||
            !Sha256Pattern().IsMatch(releaseManifest.Installer.Sha256))
        {
            throw new InvalidDataException("The release manifest does not match the GitHub release.");
        }

        var expectedHash = releaseManifest.Installer.Sha256.ToUpperInvariant();
        if (installer.Digest is not null &&
            (!installer.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(installer.Digest[7..], expectedHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The GitHub asset digest and release manifest disagree.");
        }

        return new UpdateRelease(
            releaseVersion,
            versionText,
            releasePageUrl,
            installer,
            checksum,
            manifest,
            expectedHash);
    }

    private static UpdateAsset FindAsset(IReadOnlyList<ApiAsset>? assets, string name)
    {
        var matches = assets?
            .Where(asset => string.Equals(asset.Name, name, StringComparison.Ordinal) &&
                            string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        if (matches.Length != 1 || matches[0].Size <= 0 ||
            !Uri.TryCreate(matches[0].BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl))
        {
            throw new InvalidDataException($"Required release asset is missing or duplicated: {name}");
        }

        return new UpdateAsset(name, matches[0].Size, downloadUrl, matches[0].Digest);
    }

    private static void ValidateDownloadUri(Uri uri, string tag)
    {
        var expectedPrefix = $"/PomDetom/GhostSlacking/releases/download/{tag}/";
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A release asset uses an unexpected download URL.");
        }
    }

    private async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        var lastPercent = -1;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            if (total > expectedSize || total > MaximumInstallerBytes)
            {
                throw new InvalidDataException("The installer is larger than the release metadata allows.");
            }

            var percent = expectedSize <= 0 ? 0 : (int)Math.Clamp((total * 100) / expectedSize, 0, 100);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Downloading, Snapshot.Release, percent));
            }
        }
    }

    private async Task<string> ReadSmallTextAssetAsync(
        UpdateAsset asset,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maximumBytes)
        {
            throw new InvalidDataException($"Release asset is too large: {asset.Name}");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Release asset is too large: {asset.Name}");
            }

            destination.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(destination.GetBuffer(), 0, checked((int)destination.Length));
    }

    private static string ParseChecksum(string content, string installerName)
    {
        var match = ChecksumPattern().Match(content.Trim());
        if (!match.Success || !string.Equals(match.Groups[2].Value, installerName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checksum asset is invalid.");
        }

        return match.Groups[1].Value.ToUpperInvariant();
    }

    private void SetSnapshot(ApplicationUpdateSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ApplicationUpdateSnapshot CreateSnapshot(
        ApplicationUpdateStatus status,
        UpdateRelease? release = null,
        int? progressPercent = null) =>
        new(status, _currentVersion.ToString(3), release, progressPercent, _state.LastSuccessfulCheckUtc);

    private static bool InstalledLocationMatchesCurrentApplication()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = localMachine.OpenSubKey("Software\\GhostSlacking");
            var installedLocation = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(installedLocation))
            {
                return false;
            }

            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(installedLocation)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(_updatesDirectory))
            {
                return;
            }

            foreach (var partial in Directory.EnumerateFiles(_updatesDirectory, "*.partial", SearchOption.AllDirectories))
            {
                TryDelete(partial);
            }

            foreach (var directory in Directory.EnumerateDirectories(_updatesDirectory))
            {
                var name = Path.GetFileName(directory);
                var obsoleteVersion = Version.TryParse(name, out var version) && version <= _currentVersion;
                var expired = Directory.GetLastWriteTimeUtc(directory) < _utcNow().UtcDateTime.AddDays(-30);
                if (obsoleteVersion || expired)
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Debug, "Old update downloads could not be fully cleaned.", exception);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagPattern();

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\s+\*?(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumPattern();

    private sealed record ApiRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        bool Draft,
        bool Prerelease,
        IReadOnlyList<ApiAsset>? Assets);

    private sealed record ApiAsset(
        string? Name,
        long Size,
        string? State,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
        string? Digest);

    private sealed record ReleaseManifest(
        string? Product,
        string? Version,
        string? Architecture,
        ReleaseInstallerManifest? Installer);

    private sealed record ReleaseInstallerManifest(string? File, long Bytes, string? Sha256);
}
