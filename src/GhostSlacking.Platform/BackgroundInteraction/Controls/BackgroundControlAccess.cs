using System.Diagnostics;
using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

// Only scalar messages are permitted here. No cross-process pointer or document
// content is transferred. Each bounded call rechecks the registered identity.
internal sealed class BackgroundControlAccess(nint hwnd, Func<NativeResult> guard)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    public nint Window => hwnd;
    public BackgroundControlAccess Fresh() => new(hwnd, guard);
    public NativeResult Validate() => guard();
    public Point ScreenPoint(Point client)
    {
        var point = new Win32NativeMethods.POINT { X = client.X, Y = client.Y };
        return Win32BackgroundInputNative.ClientToScreen(hwnd, ref point) ? new(point.X, point.Y) : new(int.MinValue, int.MinValue);
    }
    public NativeResult Query(uint message, nint first, nint second, out nint value)
    {
        value = 0;
        var remaining = 75 - Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        if (remaining <= 0) return NativeResult.Failed("BackgroundControlDeadline", 0, "The control operation exceeded its total deadline.");
        var valid = guard();
        if (!valid.Success) return valid;
        Win32NativeMethods.SetLastError(0);
        return Win32BackgroundInputNative.SendMessageTimeout(hwnd, message, first, second, 3, (uint)Math.Max(1, Math.Min(50, remaining)), out value) != 0
            ? NativeResult.Ok("BackgroundControlMessage")
            : NativeResult.Failed("BackgroundControlMessage", Win32NativeMethods.LastError, "The control call failed or timed out.");
    }

    public BackgroundInputDispatchResult ReplaceSelection(string text)
    {
        var valid = guard();
        if (!valid.Success) return new(valid, BackgroundInputCategory.EditCommand);
        if (!Win32BackgroundInputNative.IsWindowUnicode(hwnd))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
        var sent = Win32BackgroundInputNative.SendTextTimeout(hwnd, 0x00C2, 1, text, 3, 50, out _);
        valid = guard();
        return new(!valid.Success ? valid : sent != 0 ? NativeResult.Ok("BackgroundTextCommit") :
            NativeResult.Failed("BackgroundTextCommit", Win32NativeMethods.LastError, "Text delivery failed; verify any partial result."), BackgroundInputCategory.EditCommand);
    }

    public BackgroundInputDispatchResult Command(uint message, nint first = 0, nint second = 0) =>
        new(Query(message, first, second, out _), BackgroundInputCategory.EditCommand);
}
