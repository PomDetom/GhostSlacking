using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class ApplicationUpdateManagerTests
{
    [Fact]
    public async Task New_stable_release_is_reported_and_successful_check_is_persisted()
    {
        using var files = new TemporaryUpdateFiles();
        var now = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        var release = TestRelease.Create("1.2.0");
        using var client = new HttpClient(new ReleaseHandler(release));
        using var manager = CreateManager(files, client, "1.1.0", () => now);

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, snapshot.Status);
        Assert.Equal("1.2.0", snapshot.Release?.VersionText);
        Assert.Equal(now, snapshot.LastSuccessfulCheckUtc);
        var state = new UpdateStateStore(files.StatePath).Load();
        Assert.Equal(now, state.LastSuccessfulCheckUtc);
    }

    [Fact]
    public async Task Same_or_older_release_is_not_offered_as_an_update()
    {
        using var files = new TemporaryUpdateFiles();
        using var client = new HttpClient(new ReleaseHandler(TestRelease.Create("1.9.9")));
        using var manager = CreateManager(files, client, currentVersion: "2.0.0");

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.UpToDate, snapshot.Status);
    }

    [Fact]
    public async Task Non_three_part_release_tag_is_rejected()
    {
        using var files = new TemporaryUpdateFiles();
        using var client = new HttpClient(new ReleaseHandler(TestRelease.Create("1.2")));
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Error, snapshot.Status);
    }

    [Fact]
    public async Task Startup_check_queries_github_even_after_a_recent_successful_check()
    {
        using var files = new TemporaryUpdateFiles();
        var now = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        var previousCheck = now.AddMinutes(-5);
        Assert.True(new UpdateStateStore(files.StatePath).Save(new UpdateState(previousCheck, null)));
        var handler = new ReleaseHandler(TestRelease.Create("1.2.0"));
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, "1.1.0", () => now);

        Assert.Equal(previousCheck, manager.Snapshot.LastSuccessfulCheckUtc);
        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, snapshot.Status);
        Assert.Equal(now, snapshot.LastSuccessfulCheckUtc);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Repeated_checks_each_query_github()
    {
        using var files = new TemporaryUpdateFiles();
        var handler = new ReleaseHandler(TestRelease.Create("1.2.0"));
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");

        await manager.CheckAsync();
        await manager.CheckAsync();

        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task Skipped_version_is_suppressed_but_a_higher_version_restores_reminders()
    {
        using var files = new TemporaryUpdateFiles();
        var handler = new ReleaseHandler(TestRelease.Create("1.2.0"));
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");
        await manager.CheckAsync();
        manager.SkipCurrentRelease();

        Assert.Equal(ApplicationUpdateStatus.Skipped, manager.Snapshot.Status);
        Assert.Equal("1.2.0", new UpdateStateStore(files.StatePath).Load().SkippedVersion);

        using var restartedManager = CreateManager(files, client, currentVersion: "1.1.0");
        var skipped = await restartedManager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Skipped, skipped.Status);
        Assert.Equal("1.2.0", skipped.Release?.VersionText);

        handler.Release = TestRelease.Create("1.3.0");
        var newer = await restartedManager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, newer.Status);
        Assert.Equal("1.3.0", newer.Release?.VersionText);
        Assert.Null(new UpdateStateStore(files.StatePath).Load().SkippedVersion);
    }

    [Fact]
    public async Task Matching_installer_is_downloaded_verified_and_launched()
    {
        using var files = new TemporaryUpdateFiles();
        var release = TestRelease.Create("1.2.0");
        using var client = new HttpClient(new ReleaseHandler(release));
        var launcher = new RecordingInstallerLauncher();
        var logger = new RecordingLogger();
        using var manager = CreateManager(files, client, "1.1.0", installerLauncher: launcher, logger: logger);
        await manager.CheckAsync();

        var installerPath = await manager.DownloadInstallerAsync();

        Assert.True(installerPath is not null, logger.LastException?.ToString() ?? logger.LastMessage);
        Assert.Equal(release.InstallerBytes, await File.ReadAllBytesAsync(installerPath));
        Assert.Equal(ApplicationUpdateStatus.Ready, manager.Snapshot.Status);
        Assert.True(manager.LaunchInstaller(installerPath));
        Assert.Equal(installerPath, launcher.LastPath);
    }

    [Fact]
    public async Task Corrupt_installer_is_deleted_and_never_launched()
    {
        using var files = new TemporaryUpdateFiles();
        var release = TestRelease.Create("1.2.0") with { DownloadedInstallerBytes = [1, 2, 3, 4] };
        using var client = new HttpClient(new ReleaseHandler(release));
        var launcher = new RecordingInstallerLauncher();
        using var manager = CreateManager(files, client, "1.1.0", installerLauncher: launcher);
        await manager.CheckAsync();

        var installerPath = await manager.DownloadInstallerAsync();

        Assert.Null(installerPath);
        Assert.Equal(ApplicationUpdateStatus.Error, manager.Snapshot.Status);
        Assert.Null(launcher.LastPath);
        Assert.Empty(Directory.EnumerateFiles(files.UpdatesDirectory, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancelled_download_removes_partial_file_and_returns_to_available_state()
    {
        using var files = new TemporaryUpdateFiles();
        var handler = new ReleaseHandler(TestRelease.Create("1.2.0")) { PauseInstallerDownload = true };
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");
        await manager.CheckAsync();
        using var cancellation = new CancellationTokenSource();

        var download = manager.DownloadInstallerAsync(cancellation.Token);
        await handler.InstallerRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var installerPath = await download;

        Assert.Null(installerPath);
        Assert.Equal(ApplicationUpdateStatus.Available, manager.Snapshot.Status);
        Assert.Empty(Directory.EnumerateFiles(files.UpdatesDirectory, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Portable_copy_can_check_but_cannot_download_or_launch_installer()
    {
        using var files = new TemporaryUpdateFiles();
        var release = TestRelease.Create("1.2.0");
        using var client = new HttpClient(new ReleaseHandler(release));
        var launcher = new RecordingInstallerLauncher();
        using var manager = CreateManager(
            files,
            client,
            "1.1.0",
            installerLauncher: launcher,
            installationDetector: () => false);
        await manager.CheckAsync();

        Assert.False(manager.CanInstallUpdates);
        Assert.Null(await manager.DownloadInstallerAsync());
        Assert.False(manager.LaunchInstaller(Path.Combine(files.Root, "update.msi")));
        Assert.Null(launcher.LastPath);
    }

    [Fact]
    public async Task Malformed_or_incomplete_release_is_rejected_without_advancing_check_time()
    {
        using var files = new TemporaryUpdateFiles();
        var previousCheck = new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);
        Assert.True(new UpdateStateStore(files.StatePath).Save(new UpdateState(previousCheck, null)));
        var release = TestRelease.Create("1.2.0") with { OmitChecksumAsset = true };
        using var client = new HttpClient(new ReleaseHandler(release));
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Error, snapshot.Status);
        Assert.Equal(previousCheck, snapshot.LastSuccessfulCheckUtc);
        Assert.Equal(previousCheck, new UpdateStateStore(files.StatePath).Load().LastSuccessfulCheckUtc);
    }

    private static GitHubApplicationUpdateManager CreateManager(
        TemporaryUpdateFiles files,
        HttpClient client,
        string currentVersion,
        Func<DateTimeOffset>? utcNow = null,
        IUpdateInstallerLauncher? installerLauncher = null,
        Func<bool>? installationDetector = null,
        ILogger? logger = null) => new(
            logger ?? NullLogger.Instance,
            client,
            new UpdateStateStore(files.StatePath),
            installerLauncher,
            new RecordingLinkLauncher(),
            utcNow,
            files.UpdatesDirectory,
            currentVersion,
            installationDetector ?? (() => true));

    private sealed class ReleaseHandler(TestRelease release) : HttpMessageHandler
    {
        public TestRelease Release { get; set; } = release;
        public int RequestCount { get; private set; }
        public bool PauseInstallerDownload { get; init; }
        public TaskCompletionSource InstallerRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var uri = request.RequestUri?.AbsoluteUri;
            if (uri == GitHubApplicationUpdateManager.LatestReleaseApiUri.AbsoluteUri)
            {
                return JsonResponse(Release.ApiJson());
            }

            if (uri?.EndsWith("/release.json", StringComparison.Ordinal) == true)
            {
                return JsonResponse(Release.ManifestJson());
            }

            if (uri?.EndsWith(".sha256", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{Release.Sha256.ToLowerInvariant()}  {Release.InstallerName}")
                };
            }

            if (uri?.EndsWith(".msi", StringComparison.Ordinal) == true)
            {
                InstallerRequested.TrySetResult();
                if (PauseInstallerDownload)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Release.DownloadedInstallerBytes ?? Release.InstallerBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed record TestRelease(
        string Version,
        byte[] InstallerBytes,
        byte[]? DownloadedInstallerBytes = null,
        bool OmitChecksumAsset = false)
    {
        public string Tag => $"v{Version}";
        public string InstallerName => $"GhostSlacking-{Version}-win-x64.msi";
        public string Sha256 => Convert.ToHexString(SHA256.HashData(InstallerBytes));
        private string DownloadRoot => $"https://github.com/PomDetom/GhostSlacking/releases/download/{Tag}";

        public static TestRelease Create(string version) => new(
            version,
            Encoding.UTF8.GetBytes($"test installer for {version}"));

        public string ApiJson()
        {
            var assets = new List<object>
            {
                Asset(InstallerName, InstallerBytes.Length, $"sha256:{Sha256.ToLowerInvariant()}"),
                Asset("release.json", 256, null)
            };
            if (!OmitChecksumAsset)
            {
                assets.Add(Asset($"{InstallerName}.sha256", 99, null));
            }

            return JsonSerializer.Serialize(new
            {
                tag_name = Tag,
                html_url = $"https://github.com/PomDetom/GhostSlacking/releases/tag/{Tag}",
                draft = false,
                prerelease = false,
                assets
            });
        }

        public string ManifestJson() => JsonSerializer.Serialize(new
        {
            product = "GhostSlacking",
            version = Version,
            architecture = "win-x64",
            installer = new
            {
                file = InstallerName,
                bytes = InstallerBytes.Length,
                sha256 = Sha256.ToLowerInvariant()
            }
        });

        private object Asset(string name, long size, string? digest) => new
        {
            name,
            size,
            state = "uploaded",
            browser_download_url = $"{DownloadRoot}/{name}",
            digest
        };
    }

    private sealed class RecordingInstallerLauncher : IUpdateInstallerLauncher
    {
        public string? LastPath { get; private set; }

        public bool Launch(string installerPath)
        {
            LastPath = installerPath;
            return true;
        }
    }

    private sealed class RecordingLinkLauncher : IExternalLinkLauncher
    {
        public bool Open(Uri uri) => true;
    }

    private sealed class RecordingLogger : ILogger
    {
        public string? LastMessage { get; private set; }
        public Exception? LastException { get; private set; }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            LastMessage = message;
            LastException = exception;
        }
    }

    private sealed class TemporaryUpdateFiles : IDisposable
    {
        public TemporaryUpdateFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), $"GhostSlacking-update-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public string StatePath => Path.Combine(Root, "update-state.json");
        public string UpdatesDirectory => Path.Combine(Root, "updates");

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
