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
        Assert.Single(snapshot.Release?.ReleaseNotes ?? []);
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
        Assert.Equal(3, handler.RequestCount);
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

        Assert.Equal(6, handler.RequestCount);
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
        var handoffStarted = 0;
        manager.InstallHandoffStarted += (_, _) => handoffStarted++;
        await manager.CheckAsync();

        var installerPath = await manager.DownloadInstallerAsync();

        Assert.True(installerPath is not null, logger.LastException?.ToString() ?? logger.LastMessage);
        Assert.Equal(release.InstallerBytes, await File.ReadAllBytesAsync(installerPath));
        Assert.Equal(ApplicationUpdateStatus.Ready, manager.Snapshot.Status);
        Assert.True(manager.BeginAutomaticInstall(installerPath));
        Assert.Equal(installerPath, launcher.LastRequest?.InstallerPath);
        Assert.Equal("1.2.0", launcher.LastRequest?.Version);
        Assert.Equal(1, handoffStarted);
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
        Assert.Null(launcher.LastRequest);
        Assert.Empty(Directory.EnumerateFiles(files.UpdatesDirectory, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Failed_installer_launch_does_not_signal_application_shutdown()
    {
        using var files = new TemporaryUpdateFiles();
        var release = TestRelease.Create("1.2.0");
        using var client = new HttpClient(new ReleaseHandler(release));
        var launcher = new RecordingInstallerLauncher(result: false);
        using var manager = CreateManager(files, client, "1.1.0", installerLauncher: launcher);
        var handoffStarted = 0;
        manager.InstallHandoffStarted += (_, _) => handoffStarted++;
        await manager.CheckAsync();
        var installerPath = await manager.DownloadInstallerAsync();

        Assert.NotNull(installerPath);
        Assert.False(manager.BeginAutomaticInstall(installerPath));
        Assert.Equal(0, handoffStarted);
    }

    [Fact]
    public async Task Installer_outside_the_verified_version_cache_is_never_launched()
    {
        using var files = new TemporaryUpdateFiles();
        var release = TestRelease.Create("1.2.0");
        using var client = new HttpClient(new ReleaseHandler(release));
        var launcher = new RecordingInstallerLauncher();
        using var manager = CreateManager(files, client, "1.1.0", installerLauncher: launcher);
        await manager.CheckAsync();
        var installerPath = await manager.DownloadInstallerAsync();
        Assert.NotNull(installerPath);
        var unexpectedPath = Path.Combine(files.UpdatesDirectory, release.InstallerName);
        File.Copy(installerPath, unexpectedPath);

        Assert.False(manager.BeginAutomaticInstall(unexpectedPath));
        Assert.Equal(ApplicationUpdateStatus.Error, manager.Snapshot.Status);
        Assert.Null(launcher.LastRequest);
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
        var handoffStarted = 0;
        manager.InstallHandoffStarted += (_, _) => handoffStarted++;
        await manager.CheckAsync();

        Assert.False(manager.CanInstallUpdates);
        Assert.Null(await manager.DownloadInstallerAsync());
        Assert.False(manager.BeginAutomaticInstall(Path.Combine(files.Root, "update.msi")));
        Assert.Null(launcher.LastRequest);
        Assert.Equal(0, handoffStarted);
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

    [Fact]
    public async Task Bilingual_release_notes_are_parsed_and_history_is_aggregated_newest_first()
    {
        using var files = new TemporaryUpdateFiles();
        var latest = TestRelease.Create("1.3.0") with
        {
            Body = TestRelease.Notes("新增更新窗口。", "Add the update window.")
        };
        var handler = new ReleaseHandler(latest)
        {
            History =
            [
                latest,
                TestRelease.Create("1.2.0") with
                {
                    Body = TestRelease.Notes("改进更新检查。", "Improve update checks.")
                },
                TestRelease.Create("1.1.0")
            ]
        };
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, snapshot.Status);
        Assert.NotNull(snapshot.Release);
        var notes = snapshot.Release.ReleaseNotes;
        Assert.Equal(["1.3.0", "1.2.0"], notes.Select(item => item.VersionText));
        Assert.Equal("### 更新内容\n- 新增更新窗口。", notes[0].Notes.Chinese);
        Assert.Equal("### What's new\n- Add the update window.", notes[0].Notes.English);
        Assert.Equal("改进更新检查。", notes[1].Notes.For(UiLanguage.Chinese)?.Split("- ")[1]);
        Assert.False(snapshot.Release.ReleaseHistoryIncomplete);
    }

    [Fact]
    public async Task Missing_or_unavailable_release_notes_do_not_block_a_valid_update()
    {
        using var files = new TemporaryUpdateFiles();
        var handler = new ReleaseHandler(TestRelease.Create("1.2.0")) { FailHistory = true };
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        using var manager = CreateManager(files, client, currentVersion: "1.1.0", logger: logger);

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, snapshot.Status);
        Assert.NotNull(snapshot.Release);
        var release = snapshot.Release;
        Assert.True(release.ReleaseHistoryIncomplete);
        var notes = Assert.Single(release.ReleaseNotes);
        Assert.Null(notes.Notes.Chinese);
        Assert.Null(notes.Notes.English);
        Assert.Contains("history could not be loaded", logger.LastMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_or_incomplete_markers_are_ignored_without_exposing_partial_content()
    {
        var duplicate = TestRelease.Notes("中文", "English") +
            "\n<!-- release-notes:zh:start -->duplicate<!-- release-notes:zh:end -->";
        var incomplete = "<!-- release-notes:zh:start -->中文";

        Assert.Null(GitHubApplicationUpdateManager.ParseReleaseNotes(duplicate).Chinese);
        Assert.Equal("### What's new\n- English", GitHubApplicationUpdateManager.ParseReleaseNotes(duplicate).English);
        Assert.Null(GitHubApplicationUpdateManager.ParseReleaseNotes(incomplete).Chinese);
    }

    [Fact]
    public async Task Release_history_filters_drafts_and_prereleases()
    {
        using var files = new TemporaryUpdateFiles();
        var latest = TestRelease.Create("1.3.0") with { Body = TestRelease.Notes("正式版", "Stable") };
        var handler = new ReleaseHandler(latest)
        {
            History =
            [
                latest,
                TestRelease.Create("1.2.0") with { Prerelease = true },
                TestRelease.Create("1.1.0") with { Draft = true }
            ]
        };
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.0.0");

        var snapshot = await manager.CheckAsync();

        Assert.Equal("1.3.0", Assert.Single(snapshot.Release?.ReleaseNotes ?? []).VersionText);
    }

    [Fact]
    public async Task Test_channel_discovers_prerelease_but_stable_channel_does_not()
    {
        using var files = new TemporaryUpdateFiles();
        var beta = TestRelease.Create("1.3.0-beta.1") with { Prerelease = true };
        var stable = TestRelease.Create("1.2.0");
        var handler = new ReleaseHandler(beta) { History = [beta, stable] };
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.0.0", channel: UpdateChannel.Test);

        var snapshot = await manager.CheckAsync();

        Assert.Equal(ApplicationUpdateStatus.Available, snapshot.Status);
        Assert.Equal("1.3.0-beta.1", snapshot.Release?.VersionText);
        Assert.True(manager.Channel == UpdateChannel.Test);
    }

    [Fact]
    public async Task Release_history_follows_pagination_until_the_current_version_is_reached()
    {
        using var files = new TemporaryUpdateFiles();
        var latest = TestRelease.Create("1.3.0");
        var firstPage = Enumerable.Range(0, 100)
            .Select(_ => TestRelease.Create("1.2.9") with { Prerelease = true })
            .ToArray();
        var handler = new ReleaseHandler(latest)
        {
            HistoryPages =
            [
                firstPage,
                [TestRelease.Create("1.2.0"), TestRelease.Create("1.1.0")]
            ]
        };
        using var client = new HttpClient(handler);
        using var manager = CreateManager(files, client, currentVersion: "1.1.0");

        var snapshot = await manager.CheckAsync();

        Assert.Equal(["1.3.0", "1.2.0"], snapshot.Release?.ReleaseNotes.Select(item => item.VersionText));
        Assert.Equal(4, handler.RequestCount);
    }

    private static GitHubApplicationUpdateManager CreateManager(
        TemporaryUpdateFiles files,
        HttpClient client,
        string currentVersion,
        Func<DateTimeOffset>? utcNow = null,
        IUpdateInstallerLauncher? installerLauncher = null,
        Func<bool>? installationDetector = null,
        ILogger? logger = null,
        UpdateChannel channel = UpdateChannel.Stable) => new(
            logger ?? NullLogger.Instance,
            client,
            new UpdateStateStore(files.StatePath),
            installerLauncher,
            new RecordingLinkLauncher(),
            utcNow,
            files.UpdatesDirectory,
            currentVersion,
            installationDetector ?? (() => true),
            channel);

    private sealed class ReleaseHandler(TestRelease release) : HttpMessageHandler
    {
        public TestRelease Release { get; set; } = release;
        public IReadOnlyList<TestRelease> History { get; init; } = [release];
        public IReadOnlyList<IReadOnlyList<TestRelease>>? HistoryPages { get; init; }
        public bool FailHistory { get; init; }
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

            if (uri?.StartsWith(GitHubApplicationUpdateManager.ReleasesApiUri.AbsoluteUri + "?", StringComparison.Ordinal) == true)
            {
                var page = request.RequestUri?.Query
                    .TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(part => part.Length == 2 && part[0] == "page")
                    .Select(part => int.TryParse(part[1], out var value) ? value : 1)
                    .FirstOrDefault(1) ?? 1;
                var history = HistoryPages is not null && page <= HistoryPages.Count
                    ? HistoryPages[page - 1]
                    : HistoryPages is null ? History : [];
                return FailHistory
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : JsonResponse(JsonSerializer.Serialize(history.Select(item => item.ApiData())));
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
        bool OmitChecksumAsset = false,
        string? Body = null,
        bool Draft = false,
        bool Prerelease = false)
    {
        public string Tag => $"v{Version}";
        public string InstallerName => $"GhostSlacking-{Version}-win-x64.msi";
        public string Sha256 => Convert.ToHexString(SHA256.HashData(InstallerBytes));
        private string DownloadRoot => $"https://github.com/PomDetom/GhostSlacking/releases/download/{Tag}";

        public static TestRelease Create(string version) => new(
            version,
            Encoding.UTF8.GetBytes($"test installer for {version}"));

        public static string Notes(string chinese, string english) => $$"""
            ## 更新内容
            <!-- release-notes:zh:start -->
            ### 更新内容
            - {{chinese}}
            <!-- release-notes:zh:end -->
            ## What's Changed
            <!-- release-notes:en:start -->
            ### What's new
            - {{english}}
            <!-- release-notes:en:end -->
            """;

        public string ApiJson() => JsonSerializer.Serialize(ApiData());

        public object ApiData()
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

            return new
            {
                tag_name = Tag,
                html_url = $"https://github.com/PomDetom/GhostSlacking/releases/tag/{Tag}",
                body = Body,
                draft = Draft,
                prerelease = Prerelease,
                assets
            };
        }

        public string ManifestJson() => JsonSerializer.Serialize(new
        {
            product = "GhostSlacking",
            version = Version,
            installerProductVersion = Version.Split('-')[0],
            channel = Version.Contains('-') ? "prerelease" : "stable",
            prerelease = Version.Contains('-'),
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

    private sealed class RecordingInstallerLauncher(bool result = true) : IUpdateInstallerLauncher
    {
        public UpdateInstallRequest? LastRequest { get; private set; }

        public bool Launch(UpdateInstallRequest request)
        {
            LastRequest = request;
            return result;
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
