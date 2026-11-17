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
    public void Notification_source_is_preserved_for_replacement_policy()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;

        state.Show("picking", UserNotificationSeverity.Info, now, source: UserNotificationSource.Picker);

        Assert.Equal(UserNotificationSource.Picker, state.Current?.Source);
        Assert.False(UserNotificationPolicy.ShouldSuppress(
            UserNotificationSource.Selection,
            state.Current!.Value,
            state.IsPaused,
            now.AddSeconds(1)));
        Assert.True(UserNotificationPolicy.ShouldSuppress(
            UserNotificationSource.Startup,
            state.Current.Value,
            state.IsPaused,
            now.AddSeconds(1)));
    }

    [Fact]
    public void Lower_priority_notification_is_allowed_after_the_current_one_expires()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;
        state.Show("selection", UserNotificationSeverity.Info, now, source: UserNotificationSource.Selection);

        Assert.False(UserNotificationPolicy.ShouldSuppress(
            UserNotificationSource.Startup,
            state.Current!.Value,
            state.IsPaused,
            now.AddSeconds(3)));
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

    [Fact]
    public void Clickable_notification_can_use_the_longer_display_duration()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;

        state.Show(
            "update",
            UserNotificationSeverity.Info,
            now,
            () => { },
            TimeSpan.FromSeconds(4.5));

        Assert.Equal(now.AddSeconds(4.5), state.Current?.ExpiresAt);
        Assert.False(state.Expire(now.AddSeconds(4)));
        Assert.True(state.Expire(now.AddSeconds(5)));
    }

    [Fact]
    public void Hover_pauses_clickable_notification_and_resumes_from_remaining_time()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;
        state.Show(
            "update",
            UserNotificationSeverity.Info,
            now,
            () => { },
            TimeSpan.FromSeconds(4.5));

        Assert.True(state.Pause(now.AddSeconds(1.5)));
        Assert.True(state.IsPaused);
        Assert.False(state.Expire(now.AddSeconds(20)));
        Assert.Equal(TimeSpan.FromSeconds(3), state.Resume(now.AddSeconds(20)));
        Assert.False(state.IsPaused);
        Assert.Equal(now.AddSeconds(23), state.Current?.ExpiresAt);
        Assert.False(state.Expire(now.AddSeconds(22.9)));
        Assert.True(state.Expire(now.AddSeconds(23)));
    }

    [Fact]
    public void Click_action_is_consumed_once()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var clicks = 0;
        state.Show("update", UserNotificationSeverity.Info, DateTimeOffset.UtcNow, () => clicks++);

        var action = state.TakeClickAction();
        Assert.NotNull(action);
        action();

        Assert.Equal(1, clicks);
        Assert.Null(state.Current);
        Assert.Null(state.TakeClickAction());
    }

    [Fact]
    public void Non_clickable_replacement_drops_the_previous_action_without_expiring_itself()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;
        state.Show("update", UserNotificationSeverity.Info, now, () => { });

        state.Show("ordinary", UserNotificationSeverity.Error, now.AddSeconds(1));

        Assert.Null(state.TakeClickAction());
        Assert.Equal("ordinary", state.Current?.Message);
        Assert.False(state.IsPaused);
        Assert.False(state.Pause(now.AddSeconds(1.5)));
    }

    [Fact]
    public void Replacement_clears_a_paused_notification_and_uses_its_own_deadline()
    {
        var state = new TransientNotificationState(TimeSpan.FromSeconds(2.5));
        var now = DateTimeOffset.UtcNow;
        state.Show("update", UserNotificationSeverity.Info, now, () => { }, TimeSpan.FromSeconds(4.5));
        Assert.True(state.Pause(now.AddSeconds(1)));

        state.Show("ordinary", UserNotificationSeverity.Error, now.AddSeconds(2));

        Assert.False(state.IsPaused);
        Assert.Equal(now.AddSeconds(4.5), state.Current?.ExpiresAt);
        Assert.True(state.Expire(now.AddSeconds(4.5)));
    }
}
