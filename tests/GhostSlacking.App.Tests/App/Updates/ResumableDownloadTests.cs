using System.Net;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class ResumableDownloadTests
{
    [Fact]
    public async Task Exiting_cancels_and_joins_an_inflight_metadata_check_before_disposal()
    {
        using var fixture = new DownloadFixture();
        fixture.Source.BlockCheck = true;
        using var manager = fixture.Manager();
        var check = manager.CheckAsync();
        await fixture.Source.CheckEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var queued = manager.CheckAsync();
        await manager.PrepareForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAll(check, queued);
        Assert.Equal(1, fixture.Source.Checks);
        Assert.Equal(0, fixture.Installer.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validated_cdn_locations_are_reused_and_expired_grants_are_resolved_again(bool expire)
    {
        using var fixture = new DownloadFixture();
        fixture.Source.AllowCdn = true;
        fixture.Handler.RedirectToCdn = true;
        fixture.Handler.ExpiredCdnResponses = expire ? 1 : 0;
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        Assert.NotNull(await manager.DownloadInstallerAsync());
        var origins = fixture.Handler.Requests.Count(request => request.Host == "downloads.example.test");
        if (expire) Assert.InRange(origins, 2, 5);
        else Assert.Equal(1, origins);
        var index = File.ReadAllText(Path.Combine(fixture.PackageDirectory, "download.json"));
        Assert.DoesNotContain("cdn.example.test", index);
    }

    [Fact]
    public async Task Parallel_ranges_download_a_verified_package_from_an_independent_source()
    {
        using var fixture = new DownloadFixture();
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var path = await manager.DownloadInstallerAsync();
        Assert.NotNull(path);
        Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(path));
        Assert.InRange(fixture.Handler.MaxTransfers, 2, 4);
        Assert.Equal(ApplicationUpdateStatus.Ready, manager.Snapshot.Status);
        Assert.Equal(0, fixture.Installer.Calls);
        Assert.All(fixture.Handler.Requests.Where(request => !request.Probe), request => Assert.Equal("\"download-v1\"", request.IfRange));
    }

    [Fact]
    public async Task Pausing_and_resuming_reuses_every_checkpointed_prefix()
    {
        using var fixture = new DownloadFixture();
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var task = manager.DownloadInstallerAsync();
        await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes >= 96 * 1024);
        await manager.PauseDownloadAsync();
        Assert.Null(await task);
        Assert.Equal(ApplicationUpdateStatus.Paused, manager.Snapshot.Status);
        var state = Assert.Single(UpdateDownloadCache.ReadAll(Path.Combine(fixture.Root, "updates")));
        Assert.True(state.Segments.Sum(segment => segment.SavedBytes) > 0);
        var before = fixture.Handler.Requests.Count;
        var path = await manager.DownloadInstallerAsync();
        Assert.NotNull(path);
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(path));
        foreach (var request in fixture.Handler.Requests.Skip(before).Where(request => !request.Probe))
        {
            var segment = state.Segments.Single(segment => request.Start >= segment.Offset && request.Start < segment.Offset + segment.Length);
            Assert.True(request.Start >= segment.Offset + segment.SavedBytes);
        }
    }

    [Fact]
    public async Task Restart_restores_a_paused_task_without_checking_or_installing_automatically()
    {
        using var fixture = new DownloadFixture();
        using (var manager = fixture.Manager())
        {
            await manager.CheckAsync();
            var task = manager.StartDownloadAndInstallAsync();
            await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes >= 96 * 1024);
            await manager.PrepareForExitAsync();
            await task;
        }
        var requests = fixture.Handler.Requests.Count;
        using var restarted = fixture.Manager();
        Assert.Equal(ApplicationUpdateStatus.Paused, restarted.Snapshot.Status);
        Assert.True(restarted.Snapshot.Download?.DownloadedBytes > 0);
        await restarted.CheckAsync();
        Assert.Equal(1, fixture.Source.Checks);
        Assert.Equal(requests, fixture.Handler.Requests.Count);
        Assert.Equal(0, fixture.Installer.Calls);
        await restarted.StartDownloadAndInstallAsync();
        Assert.Equal(1, fixture.Installer.Calls);
        Assert.Equal(ApplicationUpdateStatus.Preparing, restarted.Snapshot.Status);
    }

    [Fact]
    public async Task A_complete_package_is_revalidated_and_reused_without_network_requests()
    {
        using var fixture = new DownloadFixture();
        string? original;
        using (var manager = fixture.Manager())
        {
            await manager.CheckAsync();
            original = await manager.DownloadInstallerAsync();
        }
        Assert.NotNull(original);
        var requests = fixture.Handler.Requests.Count;
        using var restarted = fixture.Manager();
        var path = await restarted.DownloadInstallerAsync();
        Assert.Equal(original, path);
        Assert.Equal(requests, fixture.Handler.Requests.Count);
        Assert.Equal(1, fixture.Source.Checksums);
    }

    [Fact]
    public async Task Stop_clears_only_download_files_and_never_launches_the_cancelled_task()
    {
        using var fixture = new DownloadFixture();
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var task = manager.StartDownloadAndInstallAsync();
        await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes > 0);
        var unrelated = Path.Combine(fixture.PackageDirectory, "unrelated.txt");
        var staging = Path.Combine(fixture.PackageDirectory, "updater-test");
        Directory.CreateDirectory(staging);
        File.WriteAllText(unrelated, "keep");
        File.WriteAllText(Path.Combine(staging, "updater.exe"), "keep");
        await manager.StopDownloadAsync();
        await task;
        Assert.Equal(ApplicationUpdateStatus.Available, manager.Snapshot.Status);
        Assert.False(manager.Snapshot.HasDownloadTask);
        Assert.Equal(0, fixture.Installer.Calls);
        Assert.Empty(Directory.EnumerateFiles(fixture.PackageDirectory, "*.partial"));
        Assert.False(File.Exists(Path.Combine(fixture.PackageDirectory, "download.json")));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(Path.Combine(staging, "updater.exe")));
    }

    [Fact]
    public async Task Cached_package_reuse_repairs_missing_handoff_material_without_downloading_again()
    {
        using var fixture = new DownloadFixture(32 * 1024);
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        Assert.NotNull(await manager.DownloadInstallerAsync());
        File.Delete(Path.Combine(fixture.PackageDirectory, "release.json.sig"));
        var requests = fixture.Handler.Requests.Count;
        await manager.StartDownloadAndInstallAsync();
        Assert.Equal(requests, fixture.Handler.Requests.Count);
        Assert.True(File.Exists(Path.Combine(fixture.PackageDirectory, "release.json.sig")));
        Assert.Equal(1, fixture.Installer.Calls);
    }

    [Fact]
    public async Task A_damaged_complete_cache_is_redownloaded_in_the_download_phase()
    {
        using var fixture = new DownloadFixture(32 * 1024);
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var path = await manager.DownloadInstallerAsync();
        Assert.NotNull(path);
        File.WriteAllBytes(path, new byte[fixture.Bytes.Length]);
        var downloading = false;
        manager.Changed += (_, _) => downloading |= manager.Snapshot.Status == ApplicationUpdateStatus.Downloading && manager.Snapshot.Download?.DownloadedBytes > 0;
        Assert.Equal(path, await manager.DownloadInstallerAsync());
        Assert.True(downloading);
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pause_or_stop_winning_the_completion_race_revokes_automatic_installation(bool stop)
    {
        using var fixture = new DownloadFixture(32 * 1024);
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        Task? control = null;
        manager.Changed += (_, _) =>
        {
            if (manager.Snapshot.Status == ApplicationUpdateStatus.Ready)
                control = stop ? manager.StopDownloadAsync() : manager.PauseDownloadAsync();
        };
        await manager.StartDownloadAndInstallAsync();
        Assert.NotNull(control);
        await control;
        Assert.Equal(0, fixture.Installer.Calls);
        Assert.Equal(stop ? ApplicationUpdateStatus.Available : ApplicationUpdateStatus.Paused, manager.Snapshot.Status);
    }

    [Fact]
    public async Task A_download_without_a_window_still_verifies_and_installs_once()
    {
        using var fixture = new DownloadFixture(32 * 1024);
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var first = manager.StartDownloadAndInstallAsync();
        var duplicate = manager.StartDownloadAndInstallAsync();
        await Task.WhenAll(first, duplicate);
        Assert.Equal(1, fixture.Installer.Calls);
        Assert.Equal(ApplicationUpdateStatus.Preparing, manager.Snapshot.Status);
        await manager.StopDownloadAsync();
        Assert.Equal(ApplicationUpdateStatus.Preparing, manager.Snapshot.Status);
    }

    [Fact]
    public async Task A_live_task_pins_its_release_and_source_despite_channel_changes_and_checks()
    {
        using var fixture = new DownloadFixture();
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var task = manager.DownloadInstallerAsync();
        await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes > 0);
        await manager.PauseDownloadAsync();
        await task;
        manager.SetChannel(UpdateChannel.Test);
        fixture.Source.Release = fixture.Source.Release with { VersionText = "3.0.0", Version = ReleaseVersion.Parse("3.0.0") };
        await manager.CheckAsync();
        Assert.Equal("2.0.0", manager.Snapshot.Release?.VersionText);
        Assert.Equal(1, fixture.Source.Checks);
        Assert.Equal(UpdateChannel.Test, manager.Channel);
    }

    [Fact]
    public async Task A_corrupt_segment_is_redownloaded_without_discarding_other_valid_prefixes()
    {
        using var fixture = new DownloadFixture();
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var task = manager.DownloadInstallerAsync();
        await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes >= 128 * 1024);
        await manager.PauseDownloadAsync();
        await task;
        var state = Assert.Single(UpdateDownloadCache.ReadAll(Path.Combine(fixture.Root, "updates")));
        var index = Enumerable.Range(0, state.Segments.Count).First(index => state.Segments[index].SavedBytes > 0);
        var part = Path.Combine(fixture.PackageDirectory, fixture.Source.Release.Installer.Name + $".segment-{index:D4}.partial");
        using (var file = File.OpenWrite(part)) file.WriteByte((byte)(fixture.Bytes[state.Segments[index].Offset] ^ 255));
        var before = fixture.Handler.Requests.Count;
        var path = await manager.DownloadInstallerAsync();
        Assert.NotNull(path);
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(path));
        Assert.Contains(fixture.Handler.Requests.Skip(before), request => !request.Probe && request.Start == state.Segments[index].Offset);
        foreach (var request in fixture.Handler.Requests.Skip(before).Where(request => !request.Probe))
        {
            var other = state.Segments.Single(segment => request.Start >= segment.Offset && request.Start < segment.Offset + segment.Length);
            if (other.Offset != state.Segments[index].Offset) Assert.True(request.Start >= other.Offset + other.SavedBytes);
        }
    }

    [Fact]
    public async Task A_changed_etag_discards_old_ranges_and_restarts_safely()
    {
        using var fixture = new DownloadFixture();
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var task = manager.DownloadInstallerAsync();
        await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes > 0);
        await manager.PauseDownloadAsync();
        await task;
        var before = fixture.Handler.Requests.Count;
        fixture.Handler.ETag = "\"download-v2\"";
        Assert.NotNull(await manager.DownloadInstallerAsync());
        var requests = fixture.Handler.Requests.Skip(before).Where(request => !request.Probe).ToArray();
        Assert.Contains(requests, request => request.Start == 0);
        Assert.All(requests, request => Assert.Equal("\"download-v2\"", request.IfRange));
    }

    [Fact]
    public async Task An_ignored_range_falls_back_to_one_stream_and_requires_explicit_restart_to_resume()
    {
        using var fixture = new DownloadFixture();
        fixture.Handler.IgnoreRange = true;
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var task = manager.DownloadInstallerAsync();
        await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes > 0);
        await manager.PauseDownloadAsync();
        await task;
        var saved = manager.Snapshot.Download!.DownloadedBytes;
        Assert.Null(await manager.DownloadInstallerAsync());
        Assert.Equal(UpdateDownloadError.ResumeUnsupported, manager.Snapshot.DownloadError);
        Assert.Equal(saved, manager.Snapshot.Download!.DownloadedBytes);
        await manager.StopDownloadAsync();
        Assert.NotNull(await manager.DownloadInstallerAsync());
        Assert.Equal(1, fixture.Handler.MaxTransfers);
    }

    [Theory]
    [InlineData("wrong-range")]
    [InlineData("416")]
    [InlineData("corrupt-body")]
    [InlineData("redirect")]
    public async Task Invalid_responses_never_install_or_report_success(string failure)
    {
        using var fixture = new DownloadFixture();
        switch (failure)
        {
            case "wrong-range": fixture.Handler.WrongRange = true; break;
            case "416": fixture.Handler.Status = HttpStatusCode.RequestedRangeNotSatisfiable; break;
            case "corrupt-body": fixture.Bytes[0] ^= 255; break;
            case "redirect": fixture.Handler.RejectedRedirect = true; break;
        }
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        await manager.StartDownloadAndInstallAsync();
        Assert.Equal(ApplicationUpdateStatus.Error, manager.Snapshot.Status);
        Assert.Equal(0, fixture.Installer.Calls);
        Assert.False(File.Exists(Path.Combine(fixture.PackageDirectory, fixture.Source.Release.Installer.Name)));
        if (failure == "redirect") Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Transient_headers_and_broken_streams_retry_without_losing_saved_bytes()
    {
        using var fixture = new DownloadFixture();
        fixture.Handler.TransientResponses = 1;
        fixture.Handler.FailTransfers = 1;
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        Assert.NotNull(await manager.DownloadInstallerAsync());
        Assert.Contains(fixture.Handler.Requests, request => request.Start == 16384 && !request.Probe);
    }

    [Fact]
    public async Task Retry_after_is_honoured_and_retries_are_bounded()
    {
        using var fixture = new DownloadFixture();
        fixture.Handler.Status = HttpStatusCode.TooManyRequests;
        fixture.Handler.RetryAfter = TimeSpan.FromMilliseconds(30);
        fixture.Options = fixture.Options with { MaxRetries = 2 };
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await manager.DownloadInstallerAsync());
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(55));
        Assert.Equal(3, fixture.Handler.Requests.Count);
        Assert.Equal(ApplicationUpdateStatus.Error, manager.Snapshot.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Header_and_idle_timeouts_finish_and_leave_a_recoverable_task(bool headers)
    {
        using var fixture = new DownloadFixture(32 * 1024);
        fixture.Handler.HangHeaders = headers;
        fixture.Handler.HangBody = !headers;
        fixture.Options = fixture.Options with { HeaderTimeout = TimeSpan.FromMilliseconds(30), IdleTimeout = TimeSpan.FromMilliseconds(30), MaxRetries = 0 };
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        Assert.Null(await manager.DownloadInstallerAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(ApplicationUpdateStatus.Error, manager.Snapshot.Status);
        Assert.True(manager.Snapshot.HasDownloadTask);
    }

    [Fact]
    public async Task A_corrupt_checkpoint_is_replaced_and_never_trusted_for_installation()
    {
        using var fixture = new DownloadFixture();
        using (var manager = fixture.Manager())
        {
            await manager.CheckAsync();
            var task = manager.DownloadInstallerAsync();
            await DownloadFixture.WaitAsync(manager, snapshot => snapshot.Download?.DownloadedBytes > 0);
            await manager.PauseDownloadAsync();
            await task;
        }
        File.WriteAllText(Path.Combine(fixture.PackageDirectory, "download.json"), "{broken");
        using var restarted = fixture.Manager();
        Assert.Equal(ApplicationUpdateStatus.Idle, restarted.Snapshot.Status);
        await restarted.CheckAsync();
        var path = await restarted.DownloadInstallerAsync();
        Assert.NotNull(path);
        Assert.Equal(fixture.Bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_checkpoint_write_failure_does_not_install_or_claim_completion()
    {
        using var fixture = new DownloadFixture();
        Directory.CreateDirectory(Path.Combine(fixture.PackageDirectory, "download.json.tmp"));
        using var manager = fixture.Manager();
        await manager.CheckAsync();
        await manager.StartDownloadAndInstallAsync();
        Assert.Equal(ApplicationUpdateStatus.Error, manager.Snapshot.Status);
        Assert.Equal(0, fixture.Installer.Calls);
    }
}
