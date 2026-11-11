namespace GhostSlacking.App.Tests;

public sealed class RevealRecoveryBackoffTests
{
    [Fact]
    public void Retry_delays_follow_the_recovery_schedule_and_stop_growing_at_one_second()
    {
        var backoff = new RevealRecoveryBackoff();

        Assert.Equal(TimeSpan.FromMilliseconds(100), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromMilliseconds(250), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromMilliseconds(500), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.NextDelay());
    }

    [Fact]
    public void Successful_recovery_resets_the_retry_schedule()
    {
        var backoff = new RevealRecoveryBackoff();
        _ = backoff.NextDelay();
        _ = backoff.NextDelay();

        backoff.Reset();

        Assert.Equal(TimeSpan.FromMilliseconds(100), backoff.NextDelay());
    }
}
