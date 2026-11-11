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
using GhostSlacking.Platform;
using Microsoft.Win32;

namespace GhostSlacking.App;

internal sealed partial class GitHubApplicationUpdateManager : IApplicationUpdateManager
{
    private const long MaximumInstallerBytes = 256L * 1024 * 1024;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumChecksumBytes = 4096;
    internal static readonly Uri SourceRepositoryUri = new("https://github.com/PomDetom/GhostSlacking");
    internal static readonly Uri ReleasesUri = new("https://github.com/PomDetom/GhostSlacking/releases");
    internal static readonly Uri LatestReleaseApiUri = new("https://api.github.com/repos/PomDetom/GhostSlacking/releases/latest");
    internal static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/PomDetom/GhostSlacking/releases");
    private const int ReleasesPerPage = 100;
    private const int MaximumReleaseHistoryPages = 20;
    private const string ChineseNotesStart = "<!-- release-notes:zh:start -->";
    private const string ChineseNotesEnd = "<!-- release-notes:zh:end -->";
    private const string EnglishNotesStart = "<!-- release-notes:en:start -->";
    private const string EnglishNotesEnd = "<!-- release-notes:en:end -->";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly UpdateStateStore _stateStore;
    private readonly IUpdateInstallerLauncher _installerLauncher;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _updatesDirectory;
    private readonly ReleaseVersion _currentVersion;
    private readonly string? _releasePublicKey;
    private UpdateChannel _channel;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private UpdateState _state;
    private bool _disposed;
    private long _phaseStartedAt = Stopwatch.GetTimestamp();

    public GitHubApplicationUpdateManager(
        ILogger logger,
        HttpClient? httpClient = null,
        UpdateStateStore? stateStore = null,
        IUpdateInstallerLauncher? installerLauncher = null,
        IExternalLinkLauncher? linkLauncher = null,
        Func<DateTimeOffset>? utcNow = null,
        string? updatesDirectory = null,
        string? currentVersion = null,
        Func<bool>? installationDetector = null,
        UpdateChannel channel = UpdateChannel.Stable,
        string? releasePublicKey = null)
    {
        _logger = logger;
        _releasePublicKey = releasePublicKey;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GhostSlacking", currentVersion ?? ApplicationVersionInfo.Current()));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _stateStore = stateStore ?? new UpdateStateStore(logger: logger);
        _linkLauncher = linkLauncher ?? new ShellLauncher();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _updatesDirectory = updatesDirectory ?? Path.Combine(GhostSlackingDataPaths.RootDirectory, "updates");
        _installerLauncher = installerLauncher ?? new AutomatedUpdateLauncher(logger);
        var versionText = currentVersion ?? ApplicationVersionInfo.Current();
        _currentVersion = ReleaseVersion.TryParse(versionText, out var parsedVersion)
            ? parsedVersion
            : new ReleaseVersion(0, 0, 0);
        _channel = channel;
        _state = _stateStore.Load();
        CanInstallUpdates = (installationDetector ?? InstalledLocationMatchesCurrentApplication)();
        Snapshot = CreateSnapshot(ApplicationUpdateStatus.Idle);
        CleanupOldDownloads();
    }

    public ApplicationUpdateSnapshot Snapshot { get; private set; }

    public bool CanInstallUpdates { get; }
    public UpdateChannel Channel => _channel;

    public event EventHandler? Changed;
    public event EventHandler? InstallHandoffStarted;

    public void SetChannel(UpdateChannel channel)
    {
        _channel = channel is UpdateChannel.Stable or UpdateChannel.Test
            ? channel
            : UpdateChannel.Stable;
    }

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

                if (release is null)
                {
                    SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.NoCompatibleRelease));
                    return Snapshot;
                }

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

                if (ReleaseVersion.TryParse(_state.SkippedVersion, out var skippedVersion) && release.Version > skippedVersion)
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

    public bool BeginAutomaticInstall(string installerPath)
    {
        try
        {
            var release = Snapshot.Release;
            if (!CanInstallUpdates || Snapshot.Status != ApplicationUpdateStatus.Ready || release is null || !File.Exists(installerPath))
            {
                return false;
            }

            var expectedInstallerPath = Path.GetFullPath(Path.Combine(
                _updatesDirectory,
                release.VersionText,
                release.Installer.Name));
            var fullInstallerPath = Path.GetFullPath(installerPath);
            var applicationPath = Environment.ProcessPath;
            if (!string.Equals(fullInstallerPath, expectedInstallerPath, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(applicationPath))
            {
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, release));
                return false;
            }

            using var process = Process.GetCurrentProcess();
            var signed = SignedReleaseManifest.Verify(release.ManifestBytes!, release.SignatureText!, _releasePublicKey);
            using var verifiedInstaller = signed.OpenVerifiedInstaller(fullInstallerPath);
            var request = new UpdateInstallRequest(
                fullInstallerPath,
                release.VersionText,
                Path.GetFullPath(applicationPath),
                Environment.ProcessId,
                process.StartTime.ToUniversalTime().Ticks);
            if (_installerLauncher.Launch(request))
            {
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Preparing, release));
                InstallHandoffStarted?.Invoke(this, EventArgs.Empty);
                return true;
            }

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, release));
            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Log(LogLevel.Warning, "The automatic update handoff could not be started.", exception);
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

    private void SetSnapshot(ApplicationUpdateSnapshot snapshot)
    {
        if (Snapshot.Status != snapshot.Status)
        {
            _logger.Log(LogLevel.Info,
                $"UpdatePhase {Snapshot.Status} elapsedMs={Stopwatch.GetElapsedTime(_phaseStartedAt).TotalMilliseconds:F0} next={snapshot.Status}");
            _phaseStartedAt = Stopwatch.GetTimestamp();
        }
        Snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ApplicationUpdateSnapshot CreateSnapshot(
        ApplicationUpdateStatus status,
        UpdateRelease? release = null,
        int? progressPercent = null) =>
        new(status, _currentVersion.Text, release, progressPercent, _state.LastSuccessfulCheckUtc);

    private static bool InstalledLocationMatchesCurrentApplication()
    {
        try
        {
            return UserInstallation.Matches(AppContext.BaseDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
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
                var obsoleteVersion = ReleaseVersion.TryParse(name, out var version) && version <= _currentVersion;
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

}
