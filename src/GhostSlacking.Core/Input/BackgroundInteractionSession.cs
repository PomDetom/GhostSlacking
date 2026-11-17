using System.Drawing;

namespace GhostSlacking.Core;

public enum BackgroundInteractionStopReason
{
    User,
    RevealEnded,
    PeekClosed,
    CursorLeft,
    TargetChanged,
    ForegroundChanged,
    UnsupportedInput,
    DeliveryFailed,
    CleanupFailed
}

public readonly record struct BackgroundKeyInput(int VirtualKey, uint ScanCode, bool IsDown, bool Extended, bool SystemKey, bool IsRepeat = false);
public enum BackgroundMouseAction
{
    Move,
    Down,
    Up,
    Wheel,
    HorizontalWheel
}

public readonly record struct BackgroundMouseInput(
    BackgroundMouseAction Action,
    BackgroundMouseButton? Button = null,
    int WheelDelta = 0,
    bool DoubleClick = false);

public enum BackgroundMouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2
}

public interface IBackgroundInteractionBackend
{
    nint ForegroundWindow { get; }
    bool ContainsInputPoint(WindowSnapshot snapshot, Rectangle targetBounds, Point cursor) => targetBounds.Contains(cursor);
    NativeResult SetNoActivate(WindowSnapshot snapshot, bool restore);
    nint ResolveKeyboardTarget(WindowSnapshot snapshot, Point cursor);
    NativeResult ReleaseKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input);
    void UpdateKeyboardState(BackgroundKeyInput input) { }
    void ResetInputState() { }
    BackgroundInputDispatchResult DispatchKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input);
    BackgroundInputDispatchResult DispatchMouse(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor);
    NativeResult ReleaseMouseButton(WindowSnapshot snapshot, nint recipient, BackgroundMouseButton button, Point cursor);
}

// Owns the input ledger and lifecycle, independently of hooks or the UI thread.
// The profile must already have been captured and registered by RecoveryManager.
public sealed class BackgroundInteractionSession(IWindowApi windows, IBackgroundInteractionBackend backend)
{
    private readonly Dictionary<int, (nint Recipient, BackgroundKeyInput Input, bool NeedsRelease)> _keys = [];
    private readonly HashSet<int> _rejectedKeys = [];
    private readonly Dictionary<BackgroundMouseButton, (nint Recipient, Point Point)> _buttons = [];
    private readonly HashSet<int> _keyTails = [];
    private readonly HashSet<BackgroundMouseButton> _buttonTails = [];
    private GhostWindowProfile? _profile;
    private nint _foreground;
    private bool _inputRejected;

    public (int PostedKeys, int MouseButtons) LastCleanup { get; private set; }
    public bool IsActive => _profile is not null;
    public bool HasHeldMouseButtons => _buttons.Count != 0;
    public bool HasPendingReleases => _keyTails.Count != 0 || _buttonTails.Count != 0;
    public event Action<BackgroundInteractionStopReason>? Stopped;
    public event Action<BackgroundInputRejectionReason>? InputRejected;
    public event Action? InputResumed;

    public NativeResult Start(GhostWindowProfile profile, bool allowHidden = false, nint expectedForeground = 0)
    {
        if (IsActive || HasPendingReleases)
        {
            return NativeResult.Failed("BackgroundSessionBusy", 0, "An input session or its release tail is still active.");
        }

        var foreground = backend.ForegroundWindow;
        var observation = windows.Observe(profile.Hwnd);
        if (expectedForeground != 0 && foreground != expectedForeground)
            return NativeResult.Failed("BackgroundForegroundChanged", 0, "The protected foreground changed before preparation.");
        if (foreground == 0) return NativeResult.Failed("BackgroundForegroundUnavailable", 0, "No protected foreground is available.");
        if (foreground == profile.Hwnd) return NativeResult.Failed("BackgroundTargetForeground", 0, "The target is still foreground.");
        if (observation is null) return NativeResult.Failed("BackgroundTargetMissing", 0, "The registered target is unavailable.");
        if (!windows.IsSameIdentity(profile.Original, observation)) return NativeResult.Failed("BackgroundTargetChanged", 0, "The target identity changed.");
        if (observation.IsMinimized) return NativeResult.Failed("BackgroundTargetMinimized", 0, "The target is minimized.");
        if (!allowHidden && !observation.IsVisible) return NativeResult.Failed("BackgroundTargetHidden", 0, "A visible Reveal is required.");

        LastCleanup = default;
        var applied = backend.SetNoActivate(profile.Original, restore: false);
        if (!applied.Success)
        {
            // A backend may fail after writing the style; rollback is identity-checked too.
            var rollback = backend.SetNoActivate(profile.Original, restore: true);
            return rollback.Success ? applied : NativeResult.Failed("BackgroundInteractionCleanup", rollback.ErrorCode,
                "Could not roll back the non-activation style; restore the registered target.");
        }

        _foreground = foreground;
        _inputRejected = false;
        _profile = profile;
        if (backend.ForegroundWindow != foreground)
        {
            Stop(BackgroundInteractionStopReason.ForegroundChanged);
            return NativeResult.Failed("BackgroundForegroundChanged", 0, "The foreground changed while starting.");
        }

        return NativeResult.Ok("BackgroundInteraction");
    }

    public bool Check(GhostState state, GhostWindowProfile? current, Point cursor, bool allowHidden = false)
    {
        if (_profile is not { } profile)
        {
            return false;
        }

        var observation = windows.Observe(profile.Hwnd);
        var reason = current?.Original != profile.Original || observation is null ||
              !windows.IsSameIdentity(profile.Original, observation) ? BackgroundInteractionStopReason.TargetChanged
            : backend.ForegroundWindow != _foreground ? BackgroundInteractionStopReason.ForegroundChanged
            : state != GhostState.Reveal && !(allowHidden && state == GhostState.Ghost) ? BackgroundInteractionStopReason.RevealEnded
            : (!allowHidden && !observation.IsVisible) || observation.IsMinimized ? BackgroundInteractionStopReason.RevealEnded
            : !backend.ContainsInputPoint(profile.Original, observation.ScreenBounds, cursor) ? BackgroundInteractionStopReason.CursorLeft
            : (BackgroundInteractionStopReason?)null;
        if (reason is { } stopped)
        {
            Stop(stopped);
            return false;
        }

        return true;
    }

    public bool HandleKey(BackgroundKeyInput input, Point cursor)
    {
        if (!IsActive)
        {
            return ConsumeKeyTail(input);
        }

        var profile = _profile!;
        var hasHeld = _keys.TryGetValue(input.VirtualKey, out var held);
        input = input with { IsRepeat = input.IsDown && hasHeld };
        backend.UpdateKeyboardState(input);
        _keyTails.Add(input.VirtualKey);
        if (input.IsDown && _rejectedKeys.Contains(input.VirtualKey)) return true;
        var recipient = hasHeld ? held.Recipient
            : backend.ResolveKeyboardTarget(profile.Original, cursor);
        if (input.IsDown) _keys[input.VirtualKey] = (recipient, input, hasHeld && held.NeedsRelease);
        if (recipient == 0)
        {
            if (input.IsDown && !hasHeld)
            {
                _rejectedKeys.Add(input.VirtualKey);
                RejectInput(BackgroundInputRejectionReason.NoInputRecipient);
            }
        }
        else
        {
            var dispatched = backend.DispatchKey(profile.Original, recipient, input);
            if (input.IsDown && dispatched.KeyDownPosted)
            {
                _keys[input.VirtualKey] = (recipient, input, true);
            }
            if (dispatched.Rejection is { } rejected)
            {
                if (input.IsDown && !input.IsRepeat)
                {
                    _rejectedKeys.Add(input.VirtualKey);
                    RejectInput(rejected);
                }
            }
            else if (!dispatched.Result.Success)
            {
                Stop(FailureReason(dispatched.Result));
            }
            else ResumeInput(dispatched.Category);
        }

        if (!input.IsDown)
        {
            _keyTails.Remove(input.VirtualKey);
            _keys.Remove(input.VirtualKey);
            _rejectedKeys.Remove(input.VirtualKey);
        }
        return true;
    }

    private bool ConsumeKeyTail(BackgroundKeyInput input)
    {
        if (!_keyTails.Contains(input.VirtualKey))
        {
            return false;
        }
        if (!input.IsDown)
        {
            _keyTails.Remove(input.VirtualKey);
        }
        return true;
    }

    public bool HandleMouse(BackgroundMouseInput input, Point cursor, nint hitRecipient)
    {
        if (_profile is not { } profile) return false;
        var recipient = hitRecipient;
        if (input.Action == BackgroundMouseAction.Up && input.Button is { } released)
        {
            if (!_buttons.TryGetValue(released, out var held))
            {
                _buttonTails.Remove(released);
                return true;
            }
            recipient = held.Recipient;
        }
        else if (input.Action == BackgroundMouseAction.Move && _buttons.Count != 0)
        {
            recipient = _buttons.First().Value.Recipient;
            foreach (var button in _buttons.Keys.ToArray())
            {
                _buttons[button] = (_buttons[button].Recipient, cursor);
            }
        }

        if (input.Action == BackgroundMouseAction.Down && input.Button is { } pressed)
        {
            // The physical down is consumed. Retain its tail even if posting fails.
            _buttonTails.Add(pressed);
            if (recipient != 0) _buttons[pressed] = (recipient, cursor);
        }
        if (recipient == 0)
        {
            Stop(BackgroundInteractionStopReason.UnsupportedInput);
        }
        else
        {
            var dispatched = backend.DispatchMouse(profile.Original, recipient, input, cursor);
            if (!dispatched.Result.Success)
            {
                Stop(FailureReason(dispatched.Result));
            }
            else if (input.Action == BackgroundMouseAction.Up && input.Button is { } completed)
            {
                _buttons.Remove(completed);
                _buttonTails.Remove(completed);
            }
            if (dispatched.Result.Success) ResumeInput(dispatched.Category);
        }
        if (input.Action == BackgroundMouseAction.Up && input.Button is { } finished)
        {
            _buttonTails.Remove(finished);
            _buttons.Remove(finished);
        }
        return true;
    }

    public void DiscardReleaseTails()
    {
        if (IsActive) return;
        _keyTails.Clear();
        _buttonTails.Clear();
    }

    private void RejectInput(BackgroundInputRejectionReason reason)
    {
        _inputRejected = true;
        InputRejected?.Invoke(reason);
    }

    private void ResumeInput(BackgroundInputCategory category)
    {
        if (!_inputRejected || category == BackgroundInputCategory.Ignored) return;
        _inputRejected = false;
        InputResumed?.Invoke();
    }

    private static BackgroundInteractionStopReason FailureReason(NativeResult result) => result.Operation switch
    {
        "BackgroundForegroundChanged" => BackgroundInteractionStopReason.ForegroundChanged,
        "BackgroundInputIdentity" => BackgroundInteractionStopReason.TargetChanged,
        "UnsupportedBackgroundInput" => BackgroundInteractionStopReason.UnsupportedInput,
        _ => BackgroundInteractionStopReason.DeliveryFailed
    };

    public void Stop(BackgroundInteractionStopReason reason)
    {
        if (_profile is not { } profile)
        {
            return;
        }
        _profile = null;

        // Never send cleanup to a reused HWND. The same check is repeated by the backend.
        var observation = windows.Observe(profile.Hwnd);
        var same = observation is not null && windows.IsSameIdentity(profile.Original, observation);
        var cleanupFailed = false;
        LastCleanup = (_keys.Values.Count(key => key.NeedsRelease), _buttons.Count);
        foreach (var (recipient, input, needsRelease) in _keys.Values)
        {
            if (same && needsRelease && !backend.ReleaseKey(profile.Original, recipient, input with { IsDown = false }).Success)
            {
                cleanupFailed = true;
            }
        }
        foreach (var (button, held) in _buttons)
        {
            _buttonTails.Add(button);
            if (same && !backend.ReleaseMouseButton(profile.Original, held.Recipient, button, held.Point).Success)
            {
                cleanupFailed = true;
            }
        }
        _keys.Clear();
        _rejectedKeys.Clear();
        _buttons.Clear();
        if (same && !backend.SetNoActivate(profile.Original, restore: true).Success)
        {
            cleanupFailed = true;
        }
        backend.ResetInputState();
        Stopped?.Invoke(cleanupFailed ? BackgroundInteractionStopReason.CleanupFailed : reason);
    }
}
