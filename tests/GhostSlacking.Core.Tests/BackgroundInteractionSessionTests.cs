using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class BackgroundInteractionSessionTests
{
    [Fact]
    public void Old_settings_keep_background_input_disabled_and_new_settings_round_trip()
    {
        var old = AppSettingsJson.Deserialize("{\"SchemaVersion\":8}");
        Assert.False(old.ExperimentalBackgroundInteraction);
        Assert.False(old.ContinuousBackgroundInteraction);
        Assert.True(old.BackgroundInteractionHotkey.IsDisabled);
        Assert.Equal(10, old.SchemaVersion);
        var enabled = old with
        {
            ExperimentalBackgroundInteraction = true,
            ContinuousBackgroundInteraction = true,
            BackgroundInteractionHotkey = new(0x49, ShortcutModifiers.Control | ShortcutModifiers.Alt)
        };
        Assert.Equal(enabled, AppSettingsJson.Deserialize(AppSettingsJson.Serialize(enabled)));
        Assert.True((enabled with { BackgroundInteractionHotkey = new(0x11, ShortcutModifiers.Control) })
            .Normalize().BackgroundInteractionHotkey.IsDisabled);
    }

    [Fact]
    public void Invalid_identity_hidden_target_and_same_foreground_are_rejected_before_style_mutation()
    {
        foreach (var scenario in new[] { "identity", "hidden", "minimized", "foreground", "missing" })
        {
            var fixture = new Fixture();
            if (scenario == "identity") fixture.Windows.IdentityMatches = false;
            if (scenario == "hidden") fixture.Windows.Observation = fixture.Windows.Observation! with { IsVisible = false };
            if (scenario == "minimized") fixture.Windows.Observation = fixture.Windows.Observation! with { IsMinimized = true };
            if (scenario == "foreground") fixture.Backend.ForegroundWindow = fixture.Profile.Hwnd;
            if (scenario == "missing") fixture.Windows.Observation = null;
            Assert.False(fixture.Session.Start(fixture.Profile).Success);
            Assert.Empty(fixture.Backend.StyleChanges);
        }
    }

    [Fact]
    public void Hidden_preparation_still_requires_identity_and_protected_foreground_and_ends_on_restore()
    {
        var fixture = new Fixture();
        fixture.Windows.Observation = fixture.Windows.Observation! with { IsVisible = false };
        Assert.True(fixture.Session.Start(fixture.Profile, allowHidden: true).Success);
        Assert.True(fixture.Session.Check(GhostState.Ghost, fixture.Profile, fixture.Cursor, allowHidden: true));
        Assert.False(fixture.Session.Check(GhostState.Restoring, fixture.Profile, fixture.Cursor, allowHidden: true));
        Assert.Equal([false, true], fixture.Backend.StyleChanges);

        var changed = new Fixture();
        changed.Windows.Observation = changed.Windows.Observation! with { IsVisible = false };
        Assert.True(changed.Session.Start(changed.Profile, allowHidden: true).Success);
        changed.Backend.ForegroundWindow = 999;
        Assert.False(changed.Session.Check(GhostState.Ghost, changed.Profile, changed.Cursor, allowHidden: true));
        Assert.Equal(BackgroundInteractionStopReason.ForegroundChanged, changed.Stopped);

        var reused = new Fixture();
        reused.Windows.IdentityMatches = false;
        Assert.False(reused.Session.Start(reused.Profile, allowHidden: true).Success);
        Assert.Empty(reused.Backend.StyleChanges);
    }

    [Fact]
    public void Disabling_background_interaction_preserves_continuous_preference()
    {
        var disabled = new AppSettings { ContinuousBackgroundInteraction = true }.Normalize();
        Assert.False(disabled.ExperimentalBackgroundInteraction);
        Assert.True(AppSettingsJson.Deserialize(AppSettingsJson.Serialize(disabled)).ContinuousBackgroundInteraction);
    }

    [Fact]
    public void Partially_failed_style_application_attempts_identity_checked_rollback()
    {
        var fixture = new Fixture();
        fixture.Backend.ApplyStyleFails = true;
        Assert.False(fixture.Session.Start(fixture.Profile).Success);
        Assert.Equal([false, true], fixture.Backend.StyleChanges);
        Assert.False(fixture.Session.IsActive);
    }

    [Fact]
    public void Foreground_switch_stops_even_if_target_is_still_visible_and_releases_held_input()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor);
        fixture.Session.MouseDown(BackgroundMouseButton.Left, 7, fixture.Cursor);
        fixture.Backend.ForegroundWindow = 999;
        Assert.False(fixture.Check());
        Assert.Equal(BackgroundInteractionStopReason.ForegroundChanged, fixture.Stopped);
        Assert.False(Assert.Single(fixture.Backend.Keys, key => !key.Input.IsDown).Input.IsDown);
        Assert.Single(fixture.Backend.MouseReleases);
        Assert.Equal([false, true], fixture.Backend.StyleChanges);
    }

    [Fact]
    public void Every_non_reveal_state_ends_the_session_and_restores_the_style()
    {
        foreach (var state in Enum.GetValues<GhostState>().Where(state => state != GhostState.Reveal))
        {
            var fixture = new Fixture();
            fixture.Start();
            Assert.False(fixture.Session.Check(state, fixture.Profile, fixture.Cursor));
            Assert.Equal(BackgroundInteractionStopReason.RevealEnded, fixture.Stopped);
            Assert.Equal([false, true], fixture.Backend.StyleChanges);
        }
    }

    [Fact]
    public void Cursor_leaving_negative_coordinate_window_stops_the_session()
    {
        var fixture = new Fixture();
        fixture.Start();
        Assert.True(fixture.Check());
        Assert.False(fixture.Session.Check(GhostState.Reveal, fixture.Profile, new Point(0, 100)));
        Assert.Equal(BackgroundInteractionStopReason.CursorLeft, fixture.Stopped);
    }

    [Fact]
    public void Reused_target_receives_neither_release_messages_nor_style_restoration()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor);
        fixture.Session.MouseDown(BackgroundMouseButton.Right, 7, fixture.Cursor);
        fixture.Windows.IdentityMatches = false;
        Assert.False(fixture.Check());
        Assert.Equal(BackgroundInteractionStopReason.TargetChanged, fixture.Stopped);
        Assert.Single(fixture.Backend.Keys);
        Assert.Empty(fixture.Backend.MouseReleases);
        Assert.Equal([false], fixture.Backend.StyleChanges);
        Assert.True(fixture.Session.ConsumeKeyTail(Key(0x41, false)));
        Assert.True(fixture.Session.MouseUp(BackgroundMouseButton.Right));
        Assert.False(fixture.Session.HasPendingReleases);
    }

    [Fact]
    public void Key_up_is_sent_to_the_original_recipient_after_cursor_or_control_changes()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor);
        fixture.Backend.Recipient = 8;
        fixture.Session.HandleKey(Key(0x41, false), fixture.Cursor);
        Assert.Equal([7, 7], fixture.Backend.Keys.Select(key => (int)key.Recipient));
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        Assert.Equal(2, fixture.Backend.Keys.Count);
        Assert.False(fixture.Session.HasPendingReleases);
    }

    [Fact]
    public void Stopping_consumes_repeats_and_physical_releases_without_forwarding_them_to_the_host()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Session.HandleKey(Key(0xA2, true), fixture.Cursor);
        fixture.Session.SuppressKeyUntilRelease(0x49);
        fixture.Session.MouseDown(BackgroundMouseButton.Left, 7, fixture.Cursor);
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        Assert.False(fixture.Session.Start(fixture.Profile).Success);
        Assert.True(fixture.Session.HandleKey(Key(0xA2, true), fixture.Cursor));
        Assert.True(fixture.Session.HandleKey(Key(0xA2, false), fixture.Cursor));
        Assert.True(fixture.Session.HandleKey(Key(0x49, false), fixture.Cursor));
        Assert.True(fixture.Session.ConsumeMouseTail(BackgroundMouseButton.Left));
        Assert.True(fixture.Session.MouseUp(BackgroundMouseButton.Left));
        Assert.False(fixture.Session.HandleKey(Key(0x42, true), fixture.Cursor));
        Assert.False(fixture.Session.HasPendingReleases);
        Assert.Equal(2, fixture.Backend.Keys.Count);
        Assert.Equal([false, true], fixture.Backend.StyleChanges);
    }

    [Fact]
    public void Unsupported_input_and_partial_delivery_failure_stop_and_clean_up_the_key()
    {
        foreach (var unsupported in new[] { true, false })
        {
            var fixture = new Fixture();
            fixture.Start();
            fixture.Backend.FailNextKey = unsupported ? "UnsupportedBackgroundInput" : "BackgroundKey";
            Assert.True(fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor));
            Assert.False(fixture.Session.IsActive);
            Assert.Equal(unsupported ? BackgroundInteractionStopReason.UnsupportedInput : BackgroundInteractionStopReason.DeliveryFailed,
                fixture.Stopped);
            Assert.Equal(2, fixture.Backend.Keys.Count);
            Assert.False(fixture.Backend.Keys[1].Input.IsDown);
        }
    }

    [Fact]
    public void Missing_keyboard_recipient_swallows_the_triggering_input_and_reports_unsupported()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Backend.Recipient = 0;
        Assert.True(fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor));
        Assert.True(fixture.Session.IsActive);
        Assert.Null(fixture.Stopped);
        Assert.Equal(BackgroundInputRejectionReason.NoInputRecipient, Assert.Single(fixture.Rejections));
        Assert.Empty(fixture.Backend.Keys);
        Assert.True(fixture.Session.HandleKey(Key(0x41, false), fixture.Cursor));
    }

    [Fact]
    public void Cleanup_failure_is_reported_instead_of_claiming_a_clean_stop()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Backend.RestoreStyleFails = true;
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        Assert.Equal(BackgroundInteractionStopReason.CleanupFailed, fixture.Stopped);
    }

    [Fact]
    public void Character_only_delivery_does_not_create_a_native_key_release_on_stop()
    {
        var fixture = new Fixture();
        fixture.Backend.DispatchCategory = BackgroundInputCategory.Character;
        fixture.Start();
        Assert.True(fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor));
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        Assert.Single(fixture.Backend.Keys);
        Assert.True(fixture.Session.ConsumeKeyTail(Key(0x41, false)));
        Assert.False(fixture.Session.HasPendingReleases);
    }

    [Fact]
    public void Rejected_input_consumes_its_repeat_and_release_without_ending_the_session()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Backend.RejectKey = BackgroundInputRejectionReason.ImeComposition;
        Assert.True(fixture.Session.HandleKey(Key(0xE5, true), fixture.Cursor));
        Assert.True(fixture.Session.HandleKey(Key(0xE5, true), fixture.Cursor));
        Assert.True(fixture.Session.HandleKey(Key(0xE5, false), fixture.Cursor));
        Assert.True(fixture.Session.IsActive);
        Assert.Equal(BackgroundInputRejectionReason.ImeComposition, Assert.Single(fixture.Rejections));
        Assert.Empty(fixture.Backend.Keys);
        fixture.Backend.RejectKey = null;
        fixture.Backend.DispatchCategory = BackgroundInputCategory.Character;
        fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor);
        Assert.Equal(1, fixture.ResumeCount);
    }

    [Fact]
    public void A_failed_start_with_failed_rollback_requires_recovery()
    {
        var fixture = new Fixture();
        fixture.Backend.ApplyStyleFails = true;
        fixture.Backend.RestoreStyleFails = true;
        Assert.Equal("BackgroundInteractionCleanup", fixture.Session.Start(fixture.Profile).Operation);
    }

    [Fact]
    public void A_blocked_mouse_down_consumes_its_repeats_and_release()
    {
        var fixture = new Fixture();
        fixture.Session.SuppressMouseUntilRelease(BackgroundMouseButton.X1);
        Assert.True(fixture.Session.HasPendingReleases);
        Assert.True(fixture.Session.ConsumeMouseTail(BackgroundMouseButton.X1));
        Assert.True(fixture.Session.MouseUp(BackgroundMouseButton.X1));
        Assert.False(fixture.Session.HasPendingReleases);
    }

    [Fact]
    public void Inactive_session_only_consumes_keys_whose_down_was_intercepted()
    {
        var fixture = new Fixture();
        fixture.Start();
        fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor);
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        Assert.False(fixture.Session.HandleKey(Key(0xA0, true), fixture.Cursor));
        Assert.False(fixture.Session.HandleKey(Key(0xA0, false), fixture.Cursor));
        Assert.True(fixture.Session.HandleKey(Key(0x41, false), fixture.Cursor));
        Assert.Equal(1, fixture.Backend.ResetCount);
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        Assert.Equal(1, fixture.Backend.ResetCount);
    }

    [Fact]
    public void Lost_release_notifications_can_be_reconciled_without_discarding_held_inputs()
    {
        var fixture = new Fixture();
        fixture.Session.SuppressKeyUntilRelease(0xA2);
        fixture.Session.SuppressMouseUntilRelease(BackgroundMouseButton.Left);
        fixture.Session.ReconcileReleasedInputs(_ => true, _ => false);
        Assert.True(fixture.Session.HasPendingReleases);
        Assert.False(fixture.Session.ConsumeMouseTail(BackgroundMouseButton.Left));
        fixture.Session.ReconcileReleasedInputs(_ => false, _ => false);
        Assert.False(fixture.Session.HasPendingReleases);
        Assert.False(fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor));
    }

    private static BackgroundKeyInput Key(int key, bool down) => new(key, 30, down, false, false);

    [Fact]
    public void Waiting_for_a_foreground_keeps_the_first_peek_intent_and_cancels_on_close_or_target_change()
    {
        var fixture = new Fixture();
        var peek = new PeekStateTracker();
        Assert.True(peek.Update(true, PeekTrigger.Toggle));
        Assert.True(peek.Update(false, PeekTrigger.Toggle));
        Assert.Equal(BackgroundArmStatus.AwaitingForeground, BackgroundStartPolicy.ForegroundStatus(fixture.Profile.Hwnd, fixture.Profile.Hwnd));
        Assert.True(BackgroundStartPolicy.KeepWaiting(fixture.Profile.Original, fixture.Profile.Original, true));
        Assert.False(BackgroundStartPolicy.CanResume(BackgroundArmStatus.AwaitingForeground, false));
        Assert.True(BackgroundStartPolicy.CanResume(BackgroundArmStatus.AwaitingForeground, true));
        Assert.False(peek.Update(true, PeekTrigger.Toggle));
        Assert.False(BackgroundStartPolicy.KeepWaiting(fixture.Profile.Original, fixture.Profile.Original, false));
        Assert.False(BackgroundStartPolicy.KeepWaiting(fixture.Profile.Original, fixture.Profile.Original with { Hwnd = 999 }, true));
        Assert.False(BackgroundStartPolicy.KeepWaiting(fixture.Profile.Original, null, true));
    }

    [Fact]
    public void Missing_or_target_owned_foreground_is_a_wait_while_a_separate_foreground_can_prepare()
    {
        Assert.Equal(BackgroundArmStatus.AwaitingForeground, BackgroundStartPolicy.ForegroundStatus(0, 42));
        Assert.Equal(BackgroundArmStatus.AwaitingForeground, BackgroundStartPolicy.ForegroundStatus(42, 42));
        Assert.Equal(BackgroundArmStatus.AwaitingForeground, BackgroundStartPolicy.ForegroundStatus(99, 42, belongsToTarget: true));
        Assert.Equal(BackgroundArmStatus.Preparing, BackgroundStartPolicy.ForegroundStatus(99, 42));
        Assert.True(BackgroundArmResult.Waiting(BackgroundArmStatus.AwaitingForeground).Success);
        Assert.False(BackgroundArmResult.Reject(BackgroundArmRejection.TargetChanged).Success);
    }

    [Fact]
    public void Pending_input_only_completes_in_its_own_live_generation()
    {
        var fixture = new Fixture(); fixture.Start();
        fixture.Backend.KeyDispatches.Enqueue(BackgroundInputDispatchResult.Pending(100, BackgroundInputCategory.EditCommand));
        var changes = new List<bool>(); fixture.Session.PendingChanged += changes.Add;
        fixture.Session.HandleKey(Key(0x43, true), fixture.Cursor);
        var oldGeneration = fixture.Session.Generation;
        Assert.True(fixture.Session.HasPendingOperations);
        fixture.Session.Stop(BackgroundInteractionStopReason.User);
        fixture.Session.ConsumeKeyTail(Key(0x43, false));
        fixture.Start();
        fixture.Backend.KeyDispatches.Enqueue(BackgroundInputDispatchResult.Pending(100, BackgroundInputCategory.EditCommand));
        fixture.Session.HandleKey(Key(0x43, true), fixture.Cursor);
        fixture.Session.CompleteInput(new(oldGeneration, 100, BackgroundOperationDeadlines.Timeout(true)));
        Assert.True(fixture.Session.IsActive);
        Assert.True(fixture.Session.HasPendingOperations);
        fixture.Session.CompleteInput(new(fixture.Session.Generation, 100, BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand)));
        Assert.False(fixture.Session.HasPendingOperations);
        Assert.Equal(new[] { true, false, true, false }, changes);
    }

    [Fact]
    public void Read_only_discovery_timeout_rejects_one_operation_while_an_issued_timeout_stops_the_session()
    {
        var fixture = new Fixture(); fixture.Start();
        fixture.Backend.KeyDispatches.Enqueue(BackgroundInputDispatchResult.Pending(1, BackgroundInputCategory.EditCommand));
        fixture.Session.HandleKey(Key(0x43, true), fixture.Cursor);
        fixture.Session.CompleteInput(new(fixture.Session.Generation, 1, BackgroundOperationDeadlines.Timeout(false)));
        Assert.True(fixture.Session.IsActive);
        Assert.Contains(BackgroundInputRejectionReason.DiscoveryUnavailable, fixture.Rejections);
        fixture.Backend.KeyDispatches.Enqueue(BackgroundInputDispatchResult.Pending(2, BackgroundInputCategory.EditCommand));
        fixture.Session.HandleKey(Key(0x56, true), fixture.Cursor);
        fixture.Session.CompleteInput(new(fixture.Session.Generation, 2, BackgroundOperationDeadlines.Timeout(true)));
        Assert.False(fixture.Session.IsActive);
        Assert.Equal(BackgroundInteractionStopReason.DeliveryFailed, fixture.Stopped);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), BackgroundOperationDeadlines.Discovery);
        Assert.Equal(TimeSpan.FromMilliseconds(500), BackgroundOperationDeadlines.Mutation);
    }

    [Fact]
    public void Coalesced_mouse_excursions_survive_session_gesture_assignment()
    {
        var fixture = new Fixture(); fixture.Start();
        var motion = BackgroundMouseMotion.At(fixture.Cursor)
            .Include(BackgroundMouseMotion.At(new(fixture.Cursor.X + 20, fixture.Cursor.Y)))
            .Include(BackgroundMouseMotion.At(fixture.Cursor));
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Down, BackgroundMouseButton.Left), fixture.Cursor, 7);
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Move, Motion: motion), fixture.Cursor, 0);
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Up, BackgroundMouseButton.Left), fixture.Cursor, 0);
        Assert.Equal(motion, fixture.Backend.MouseInputs[1].Motion);
        Assert.Equal(fixture.Backend.MouseInputs[0].GestureId, fixture.Backend.MouseInputs[1].GestureId);
        Assert.Equal(fixture.Cursor.X + 20, fixture.Backend.MouseInputs[1].Motion!.Value.Maximum.X);
    }

    [Fact]
    public void An_old_pending_mouse_down_rejection_does_not_clear_a_newer_drag()
    {
        var fixture = new Fixture(); fixture.Start();
        fixture.Backend.MouseDispatches.Enqueue(BackgroundInputDispatchResult.Pending(1, BackgroundInputCategory.EditCommand));
        fixture.Backend.MouseDispatches.Enqueue(BackgroundInputDispatchResult.Pending(2, BackgroundInputCategory.EditCommand));
        fixture.Backend.MouseDispatches.Enqueue(BackgroundInputDispatchResult.Pending(3, BackgroundInputCategory.EditCommand));
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Down, BackgroundMouseButton.Left), fixture.Cursor, 7);
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Up, BackgroundMouseButton.Left), fixture.Cursor, 0);
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Down, BackgroundMouseButton.Left), fixture.Cursor, 7);
        var sent = fixture.Backend.MouseInputs;
        Assert.Equal(sent[0].GestureId, sent[1].GestureId);
        Assert.NotEqual(sent[0].GestureId, sent[2].GestureId);
        fixture.Session.CompleteInput(new(fixture.Session.Generation, 1, BackgroundOperationDeadlines.Timeout(false)));
        Assert.True(fixture.Session.HasHeldMouseButtons);
    }

    [Fact]
    public void A_key_blocked_only_by_pending_text_work_resumes_after_the_last_receipt()
    {
        var fixture = new Fixture(); fixture.Start();
        fixture.Backend.MouseDispatches.Enqueue(BackgroundInputDispatchResult.Pending(1, BackgroundInputCategory.EditCommand));
        fixture.Session.HandleMouse(new(BackgroundMouseAction.Down, BackgroundMouseButton.Left), fixture.Cursor, 7);
        fixture.Backend.KeyDispatches.Enqueue(BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.PendingTextOperation));
        fixture.Session.HandleKey(Key(0x41, true), fixture.Cursor);
        Assert.True(fixture.Session.HasRejectedInput);
        fixture.Session.CompleteInput(new(fixture.Session.Generation, 1, BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored)));
        Assert.True(fixture.Session.IsActive);
        Assert.False(fixture.Session.HasPendingOperations);
        Assert.False(fixture.Session.HasRejectedInput);
        Assert.Equal(1, fixture.ResumeCount);
    }

    private sealed class Fixture
    {
        public FakeWindows Windows { get; } = new();
        public FakeBackend Backend { get; } = new();
        public BackgroundInteractionSession Session { get; }
        public Point Cursor { get; } = new(-400, 100);
        public BackgroundInteractionStopReason? Stopped { get; private set; }
        public List<BackgroundInputRejectionReason> Rejections { get; } = [];
        public int ResumeCount { get; private set; }
        public GhostWindowProfile Profile { get; } = new(new WindowSnapshot
        {
            Hwnd = 42, ProcessId = 100, ProcessStartIdentity = "start", ScreenBounds = new(-500, 0, 500, 400),
            WasVisible = true, WasMinimized = false, Styles = new(0, 0), CapturedAt = DateTimeOffset.UtcNow,
            Placement = new(0, 1, Point.Empty, Point.Empty, new(-500, 0, 500, 400), Rectangle.Empty)
        }, new RevealSettings());
        public Fixture()
        {
            Session = new(Windows, Backend);
            Session.Stopped += reason => Stopped = reason;
            Session.InputRejected += Rejections.Add;
            Session.InputResumed += () => ResumeCount++;
        }
        public void Start() => Assert.True(Session.Start(Profile).Success);
        public bool Check() => Session.Check(GhostState.Reveal, Profile, Cursor);
    }

    private sealed class FakeWindows : IWindowApi
    {
        public bool IdentityMatches { get; set; } = true;
        public WindowObservation? Observation { get; set; } = new()
        {
            Hwnd = 42, ProcessId = 100, ScreenBounds = new(-500, 0, 500, 400),
            IsVisible = true, IsMinimized = false, IsMaximized = false
        };
        public bool IsWindow(nint hwnd) => Observation?.Hwnd == hwnd;
        public WindowObservation? Observe(nint hwnd) => Observation;
        public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation) => IdentityMatches &&
            snapshot.Hwnd == observation.Hwnd && snapshot.ProcessId == observation.ProcessId;
        public Point GetCursorPosition() => new(-400, 100);
        public OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target) => throw new NotSupportedException();
    }

    private sealed class FakeBackend : IBackgroundInteractionBackend
    {
        public nint ForegroundWindow { get; set; } = 99;
        public nint Recipient { get; set; } = 7;
        public Queue<BackgroundInputDispatchResult> KeyDispatches { get; } = [];
        public Queue<BackgroundInputDispatchResult> MouseDispatches { get; } = [];
        public List<BackgroundMouseInput> MouseInputs { get; } = [];
        public BackgroundInputDispatchResult DispatchMouse(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor)
        {
            MouseInputs.Add(input);
            return MouseDispatches.TryDequeue(out var result) ? result : BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Mouse);
        }
        public int ResetCount { get; private set; }
        public void ResetInputState() => ResetCount++;
        public bool ApplyStyleFails { get; set; }
        public bool RestoreStyleFails { get; set; }
        public string? FailNextKey { get; set; }
        public BackgroundInputCategory DispatchCategory { get; set; } = BackgroundInputCategory.RawKey;
        public BackgroundInputRejectionReason? RejectKey { get; set; }
        public List<bool> StyleChanges { get; } = [];
        public List<(nint Recipient, BackgroundKeyInput Input)> Keys { get; } = [];
        public List<BackgroundMouseButton> MouseReleases { get; } = [];
        public NativeResult SetNoActivate(WindowSnapshot snapshot, bool restore)
        {
            StyleChanges.Add(restore);
            return (restore ? RestoreStyleFails : ApplyStyleFails) ? NativeResult.Failed("Style", 5) : NativeResult.Ok("Style");
        }
        public nint ResolveKeyboardTarget(WindowSnapshot snapshot, Point cursor) => Recipient;
        public BackgroundInputDispatchResult DispatchKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input)
        {
            if (KeyDispatches.TryDequeue(out var pending)) return pending;
            if (RejectKey is { } reason)
                return input.IsDown ? BackgroundInputDispatchResult.Rejected(reason)
                    : BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
            return new(PostKey(snapshot, recipient, input), DispatchCategory,
                input.IsDown && DispatchCategory == BackgroundInputCategory.RawKey);
        }
        public NativeResult PostKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input)
        {
            Keys.Add((recipient, input));
            var failure = FailNextKey;
            FailNextKey = null;
            return failure is null ? NativeResult.Ok("Key") : NativeResult.Failed(failure, 5);
        }
        public NativeResult ReleaseMouseButton(WindowSnapshot snapshot, nint recipient, BackgroundMouseButton button, Point cursor)
        {
            MouseReleases.Add(button);
            return NativeResult.Ok("MouseRelease");
        }
    }
}
