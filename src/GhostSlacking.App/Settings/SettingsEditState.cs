using GhostSlacking.Core;

namespace GhostSlacking.App;

internal enum SettingsStatus
{
    None,
    Modified,
    Saved,
    Warning,
    Error
}

internal sealed class SettingsEditState(AppSettings settings)
{
    public AppSettings SavedSettings { get; private set; } = settings.Normalize();
    public SettingsStatus Status { get; private set; }

    public SettingsStatus Refresh(AppSettings current)
    {
        Status = current.Normalize() == SavedSettings
            ? SettingsStatus.None
            : SettingsStatus.Modified;
        return Status;
    }

    public void MarkSaved(AppSettings current)
    {
        SavedSettings = current.Normalize();
        Status = SettingsStatus.Saved;
    }

    public void SetStatus(SettingsStatus status)
    {
        Status = status;
    }

    public bool ExpireSaved()
    {
        if (Status != SettingsStatus.Saved)
        {
            return false;
        }

        Status = SettingsStatus.None;
        return true;
    }
}
