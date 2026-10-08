using GhostSlacking.Platform;
using GhostSlacking.Setup;

namespace GhostSlacking.App.Tests;

public sealed class InstallerStateTests
{
    [Fact]
    public void Progress_never_finishes_before_apply_succeeds()
    {
        var state = new InstallerState();
        state.Begin(); state.Install(); state.Progress(100);
        Assert.Equal(99, state.Percent);
        state.Progress(30);
        Assert.Equal(99, state.Percent);
        state.Complete(0);
        Assert.Equal(100, state.Percent);
        Assert.Equal(InstallerPhase.Succeeded, state.Phase);
    }

    [Theory]
    [InlineData(1602, "Cancelled")]
    [InlineData(1223, "Cancelled")]
    [InlineData(1603, "Failed")]
    public void Rollback_and_failure_never_show_success(int exitCode, string expected)
    {
        var state = new InstallerState(); state.Begin(); state.Install(); state.Progress(80);
        state.Rollback(); state.Progress(100);
        Assert.Equal(0, state.Percent);
        Assert.True(state.IsBusy);
        state.Complete(exitCode);
        Assert.Equal(expected, state.Phase.ToString());
        Assert.Equal(0, state.Percent);
        Assert.False(state.IsBusy);
    }

    [Fact]
    public void Shutdown_verification_requires_the_exact_registered_executable_path()
    {
        Assert.True(InstallerShutdown.MatchesExecutable(@"C:\Apps\GhostSlacking\GhostSlacking.App.exe", @"C:\Apps\GhostSlacking", "GhostSlacking.App.exe"));
        Assert.False(InstallerShutdown.MatchesExecutable(@"C:\Apps\GhostSlacking-other\GhostSlacking.App.exe", @"C:\Apps\GhostSlacking", "GhostSlacking.App.exe"));
        Assert.False(InstallerShutdown.MatchesExecutable(null, @"C:\Apps\GhostSlacking", "GhostSlacking.App.exe"));
    }
}
