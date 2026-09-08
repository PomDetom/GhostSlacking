using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class SettingsWindowHostTests
{
    [Fact]
    public void Avalonia_settings_host_opens_and_closes_without_error()
    {
        using var closed = new ManualResetEventSlim(false);
        using var host = new SettingsWindowHost(NullLogger.Instance);
        Exception? closeException = null;

        var shown = host.Show(
            new AppSettings(),
            _ => true,
            exception =>
            {
                closeException = exception;
                closed.Set();
            });

        Assert.True(shown);
        host.Dispose();
        Assert.True(closed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(closeException);
    }
}
