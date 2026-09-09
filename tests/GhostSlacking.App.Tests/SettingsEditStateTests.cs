using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class SettingsEditStateTests
{
    [Fact]
    public void Editing_and_reverting_tracks_the_saved_baseline()
    {
        var settings = new AppSettings();
        var state = new SettingsEditState(settings);

        Assert.Equal(SettingsStatus.Modified, state.Refresh(settings with { RevealDiameterPx = 240 }));
        Assert.Equal(SettingsStatus.None, state.Refresh(settings));
    }

    [Fact]
    public void Saving_updates_the_baseline_and_shows_saved_status()
    {
        var state = new SettingsEditState(new AppSettings());
        var updated = new AppSettings { ThemeMode = UiThemeMode.Dark };

        state.MarkSaved(updated);

        Assert.Equal(SettingsStatus.Saved, state.Status);
        Assert.Equal(updated.Normalize(), state.SavedSettings);
        Assert.Equal(SettingsStatus.None, state.Refresh(updated));
    }

    [Fact]
    public void Saved_status_expires_without_clearing_other_feedback()
    {
        var state = new SettingsEditState(new AppSettings());
        state.MarkSaved(new AppSettings());

        Assert.True(state.ExpireSaved());
        Assert.Equal(SettingsStatus.None, state.Status);

        state.SetStatus(SettingsStatus.Error);
        Assert.False(state.ExpireSaved());
        Assert.Equal(SettingsStatus.Error, state.Status);
    }

    [Fact]
    public void Editing_after_save_replaces_saved_status()
    {
        var settings = new AppSettings();
        var state = new SettingsEditState(settings);
        state.MarkSaved(settings);

        Assert.Equal(
            SettingsStatus.Modified,
            state.Refresh(settings with { ThemeMode = UiThemeMode.Light }));
    }
}
