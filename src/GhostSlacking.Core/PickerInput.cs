namespace GhostSlacking.Core;

public readonly record struct PickerEscapeDecision(
    bool Handled,
    bool CancelPicking,
    bool ReleaseKeyboardHook);

public sealed class PickerEscapeGate
{
    public const int EscapeVirtualKey = 0x1B;

    public bool AwaitingRelease { get; private set; }

    public PickerEscapeDecision Process(bool pickerActive, int virtualKey, bool isDown)
    {
        if (virtualKey != EscapeVirtualKey || (!pickerActive && !AwaitingRelease))
        {
            return default;
        }

        if (isDown)
        {
            if (AwaitingRelease)
            {
                return new PickerEscapeDecision(true, false, false);
            }

            AwaitingRelease = true;
            return new PickerEscapeDecision(true, true, false);
        }

        AwaitingRelease = false;
        return new PickerEscapeDecision(true, false, true);
    }

    public void Reset()
    {
        AwaitingRelease = false;
    }
}
