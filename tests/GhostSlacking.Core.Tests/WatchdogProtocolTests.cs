using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class WatchdogProtocolTests
{
    private static readonly Guid SessionId = Guid.Parse("d72a9196-0202-4666-8a17-e254d45d4fea");
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Protocol_round_trips_a_versioned_recovery_manifest()
    {
        var manifest = CreateManifest();
        var message = Message(WatchdogMessageKind.RecoveryManifest) with { Manifest = manifest };

        var json = WatchdogProtocol.Serialize(message);
        var restored = WatchdogProtocol.Deserialize(json);

        Assert.Equal(WatchdogProtocol.Version, restored.ProtocolVersion);
        Assert.Equal(SessionId, restored.SessionId);
        Assert.Equal(WatchdogMessageKind.RecoveryManifest, restored.Kind);
        Assert.NotNull(restored.Manifest);
        Assert.Equal(manifest.ManifestId, restored.Manifest.ManifestId);
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.Manifest.Items[0].OriginalRegionData);
        Assert.Equal("process-start", restored.Manifest.Items[0].ProcessStartIdentity);
    }

    [Fact]
    public void Session_rejects_an_incompatible_protocol_version()
    {
        var session = CreateSession();
        var hello = Hello() with { ProtocolVersion = WatchdogProtocol.Version + 1 };

        var disposition = session.Receive(hello, Now);

        Assert.Equal(WatchdogMessageDisposition.RejectedProtocolVersion, disposition);
        Assert.False(session.HandshakeCompleted);
    }

    [Fact]
    public void Session_rejects_a_message_from_another_session()
    {
        var session = CreateSession();
        var hello = Hello() with { SessionId = Guid.NewGuid() };

        var disposition = session.Receive(hello, Now);

        Assert.Equal(WatchdogMessageDisposition.RejectedSession, disposition);
        Assert.False(session.HandshakeCompleted);
    }

    [Fact]
    public void Heartbeat_timeout_returns_and_clears_the_last_manifest()
    {
        var session = CreateSession();
        Assert.Equal(WatchdogMessageDisposition.Accepted, session.Receive(Hello(), Now));
        Assert.Equal(
            WatchdogMessageDisposition.Accepted,
            session.Receive(Message(WatchdogMessageKind.RecoveryManifest) with { Manifest = CreateManifest() }, Now.AddSeconds(1)));
        Assert.Equal(
            WatchdogMessageDisposition.Accepted,
            session.Receive(Message(WatchdogMessageKind.Heartbeat), Now.AddSeconds(3)));

        Assert.False(session.HasHeartbeatTimedOut(Now.AddSeconds(7)));
        Assert.True(session.HasHeartbeatTimedOut(Now.AddSeconds(8)));
        Assert.NotNull(session.TakeTimedOutManifest(Now.AddSeconds(8)));
        Assert.Null(session.LastManifest);
        Assert.Null(session.TakeTimedOutManifest(Now.AddSeconds(9)));
    }

    [Fact]
    public void Normal_shutdown_clears_the_manifest_and_disables_timeout_recovery()
    {
        var session = CreateSession();
        session.Receive(Hello(), Now);
        session.Receive(Message(WatchdogMessageKind.RecoveryManifest) with { Manifest = CreateManifest() }, Now);

        var disposition = session.Receive(Message(WatchdogMessageKind.ShutdownCompleted), Now.AddSeconds(1));

        Assert.Equal(WatchdogMessageDisposition.ShutdownCompleted, disposition);
        Assert.True(session.IsShutdownCompleted);
        Assert.Null(session.LastManifest);
        Assert.False(session.HasHeartbeatTimedOut(Now.AddMinutes(1)));
    }

    [Fact]
    public void Session_rejects_a_manifest_with_an_old_incomplete_placement_snapshot()
    {
        var session = CreateSession();
        session.Receive(Hello(), Now);
        var manifest = CreateManifest() with
        {
            Items = [CreateItem() with { SnapshotSchemaVersion = WatchdogProtocol.SnapshotSchemaVersion - 1 }]
        };

        var disposition = session.Receive(
            Message(WatchdogMessageKind.RecoveryManifest) with { Manifest = manifest },
            Now.AddSeconds(1));

        Assert.Equal(WatchdogMessageDisposition.RejectedPayload, disposition);
        Assert.Null(session.LastManifest);
    }

    [Fact]
    public void Process_start_identity_mismatch_is_skipped()
    {
        var target = new FakeRecoveryTarget
        {
            Identity = new WatchdogTargetIdentity(42, 100, "different-start")
        };
        var executor = new WatchdogRecoveryExecutor(target);

        var report = executor.Execute(CreateManifest());

        var result = Assert.Single(report.Items);
        Assert.True(result.Skipped);
        Assert.False(result.Success);
        Assert.Contains("start identity mismatch", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, target.RestoreCalls);
    }

    [Fact]
    public void Reused_hwnd_with_another_pid_is_skipped()
    {
        var target = new FakeRecoveryTarget
        {
            Identity = new WatchdogTargetIdentity(42, 101, "process-start")
        };
        var executor = new WatchdogRecoveryExecutor(target);

        var report = executor.Execute(CreateManifest());

        var result = Assert.Single(report.Items);
        Assert.True(result.Skipped);
        Assert.False(result.Success);
        Assert.Contains("HWND may have been reused", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, target.RestoreCalls);
    }

    [Fact]
    public void Reprocessing_the_same_manifest_is_idempotent()
    {
        var target = new FakeRecoveryTarget
        {
            Identity = new WatchdogTargetIdentity(42, 100, "process-start")
        };
        var executor = new WatchdogRecoveryExecutor(target);
        var manifest = CreateManifest();

        var first = executor.Execute(manifest);
        var second = executor.Execute(manifest);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, target.RestoreCalls);
        Assert.True(Assert.Single(second.Items).Skipped);
    }

    [Fact]
    public void Manifest_factory_preserves_the_complete_recovery_snapshot()
    {
        var snapshot = CreateItem().ToSnapshot();
        var profile = new GhostWindowProfile(snapshot, new RevealSettings());

        var manifest = RecoveryManifest.FromProfiles(SessionId, [profile], Now);
        var restored = Assert.Single(manifest.Items).ToSnapshot();

        Assert.Equal(snapshot.Hwnd, restored.Hwnd);
        Assert.Equal(snapshot.ProcessId, restored.ProcessId);
        Assert.Equal(snapshot.ProcessStartIdentity, restored.ProcessStartIdentity);
        Assert.Equal(snapshot.ScreenBounds, restored.ScreenBounds);
        Assert.Equal(snapshot.OriginalRegionData, restored.OriginalRegionData);
        Assert.Equal(snapshot.Styles, restored.Styles);
        Assert.Equal(snapshot.WasVisible, restored.WasVisible);
        Assert.Equal(snapshot.WasMinimized, restored.WasMinimized);
        Assert.Equal(snapshot.Placement, restored.Placement);
    }

    private static WatchdogSession CreateSession() => new(SessionId, TimeSpan.FromSeconds(5));

    private static WatchdogMessage Hello() => Message(WatchdogMessageKind.Hello) with
    {
        MainProcessId = 777,
        MainProcessStartIdentity = "main-start"
    };

    private static WatchdogMessage Message(WatchdogMessageKind kind) => new()
    {
        SessionId = SessionId,
        Kind = kind,
        SentAtUtc = Now
    };

    private static RecoveryManifest CreateManifest() => new()
    {
        ManifestId = Guid.Parse("e285ecdb-a211-41f2-aab9-a448f795644c"),
        SessionId = SessionId,
        UpdatedAtUtc = Now,
        Items = [CreateItem()]
    };

    private static WatchdogRecoveryItem CreateItem() => new()
    {
        Hwnd = 42,
        ProcessId = 100,
        ProcessStartIdentity = "process-start",
        ProcessName = "test",
        BoundsX = -100,
        BoundsY = 50,
        BoundsWidth = 800,
        BoundsHeight = 600,
        WasVisible = true,
        WasMinimized = false,
        Placement = new WindowPlacementSnapshot(
            0,
            1,
            Point.Empty,
            Point.Empty,
            new Rectangle(-100, 50, 800, 600),
            new Rectangle(-1920, 0, 1920, 1080)),
        OriginalRegionData = [1, 2, 3],
        Style = 10,
        ExtendedStyle = 20,
        OriginalSystemBackdropType = 2,
        OriginalNonClientRenderingPolicy = 1,
        OriginalWindowCornerPreference = 3,
        OriginalBorderColor = 4,
        CapturedAt = Now
    };

    private sealed class FakeRecoveryTarget : IWatchdogRecoveryTarget
    {
        public WatchdogTargetIdentity? Identity { get; init; }
        public int RestoreCalls { get; private set; }

        public WatchdogTargetIdentity? Observe(long hwnd) => Identity;

        public NativeResult Restore(WatchdogRecoveryItem item)
        {
            RestoreCalls++;
            return NativeResult.Ok("Restore");
        }
    }
}
