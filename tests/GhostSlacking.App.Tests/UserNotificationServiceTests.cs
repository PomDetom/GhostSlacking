using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class UserNotificationServiceTests
{
    [Fact]
    public void Show_stores_message_severity_and_expiration()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = new DateTimeOffset(2026, 9, 8, 1, 2, 3, TimeSpan.Zero);

        state.Show("picked", UserNotificationSeverity.Info, now);

        var notification = Assert.NotNull(state.Current);
        Assert.Equal("picked", notification.Message);
        Assert.Equal(UserNotificationSeverity.Info, notification.Severity);
        Assert.Equal(now.AddSeconds(2.5), notification.ExpiresAt);
    }

    [Fact]
    public void Consecutive_notifications_replace_the_previous_message()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;
        state.Show("one", UserNotificationSeverity.Info, now);

        state.Show("two", UserNotificationSeverity.Error, now.AddSeconds(1));

        var notification = Assert.NotNull(state.Current);
        Assert.Equal("two", notification.Message);
        Assert.Equal(UserNotificationSeverity.Error, notification.Severity);
        Assert.Equal(now.AddSeconds(3.5), notification.ExpiresAt);
    }

    [Fact]
    public void Notification_expires_only_after_its_deadline()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;
        state.Show("one", UserNotificationSeverity.Info, now);

        Assert.False(state.Expire(now.AddSeconds(2)));
        Assert.NotNull(state.Current);
        Assert.True(state.Expire(now.AddSeconds(3)));
        Assert.Null(state.Current);
        Assert.False(state.Expire(now.AddSeconds(4)));
    }
}
