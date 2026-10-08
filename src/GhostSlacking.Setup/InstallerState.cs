namespace GhostSlacking.Setup;

internal enum InstallerPhase { Ready, Closing, Installing, RollingBack, Succeeded, Cancelled, Failed }

internal sealed class InstallerState
{
    public InstallerPhase Phase { get; private set; }
    public int Percent { get; private set; }
    public bool IsBusy => Phase is InstallerPhase.Closing or InstallerPhase.Installing or InstallerPhase.RollingBack;

    public void Begin() { Phase = InstallerPhase.Closing; Percent = 0; }
    public void Install() => Phase = InstallerPhase.Installing;
    public void Rollback() { Phase = InstallerPhase.RollingBack; Percent = 0; }
    public void Progress(int percentage)
    {
        if (Phase == InstallerPhase.Installing) Percent = Math.Max(Percent, Math.Clamp(percentage, 0, 99));
    }
    public void Complete(int exitCode)
    {
        Phase = exitCode is 0 or 1641 or 3010 ? InstallerPhase.Succeeded
            : exitCode is 1602 or 1223 ? InstallerPhase.Cancelled : InstallerPhase.Failed;
        Percent = Phase == InstallerPhase.Succeeded ? 100 : 0;
    }
}
