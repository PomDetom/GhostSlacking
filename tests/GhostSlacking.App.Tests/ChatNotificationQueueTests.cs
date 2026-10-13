using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class ChatNotificationQueueTests
{
    [Fact]
    public void New_messages_push_older_messages_up_and_keep_the_latest_five()
    {
        var queue = new ChatNotificationQueue();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 6; index++)
        {
            Assert.NotNull(queue.Add($"message {index}", UserNotificationSeverity.Info, now));
        }

        Assert.Equal(5, queue.Entries.Count);
        Assert.Equal(["message 1", "message 2", "message 3", "message 4", "message 5"],
            queue.Entries.Select(entry => entry.Message));
        Assert.Equal(now.AddSeconds(5), queue.VisibleUntil);
    }

    [Fact]
    public void Whole_chat_box_hides_five_seconds_after_the_latest_message_and_retains_history()
    {
        var queue = new ChatNotificationQueue();
        var now = DateTimeOffset.UtcNow;
        queue.Add("first", UserNotificationSeverity.Info, now);
        queue.Add("second", UserNotificationSeverity.Info, now.AddSeconds(2));

        Assert.False(queue.HideIfExpired(now.AddSeconds(5)));
        Assert.True(queue.IsVisible);
        Assert.True(queue.HideIfExpired(now.AddSeconds(7)));
        Assert.False(queue.IsVisible);
        Assert.Equal(["first", "second"], queue.Entries.Select(entry => entry.Message));

        queue.Add("third", UserNotificationSeverity.Info, now.AddSeconds(8));
        Assert.Equal(["first", "second", "third"], queue.Entries.Select(entry => entry.Message));
        Assert.Equal(now.AddSeconds(13), queue.VisibleUntil);
    }

    [Fact]
    public void Hovering_a_clickable_line_pauses_the_whole_chat_box()
    {
        var queue = new ChatNotificationQueue();
        var now = DateTimeOffset.UtcNow;
        var actionable = queue.Add("update", UserNotificationSeverity.Info, now, () => { });
        var ordinary = queue.Add("ordinary", UserNotificationSeverity.Info, now);
        Assert.NotNull(actionable);
        Assert.NotNull(ordinary);

        Assert.True(queue.Pause(actionable.Id, now.AddSeconds(2)));
        Assert.False(queue.Pause(ordinary.Id, now.AddSeconds(2)));
        Assert.False(queue.HideIfExpired(now.AddSeconds(6)));
        Assert.Equal(TimeSpan.FromSeconds(3), queue.Resume(actionable.Id, now.AddSeconds(6)));
        Assert.False(queue.HideIfExpired(now.AddSeconds(8)));
        Assert.True(queue.HideIfExpired(now.AddSeconds(9)));
        Assert.Equal(2, queue.Entries.Count);
    }

    [Fact]
    public void Clicking_an_action_hides_the_box_and_preserves_nonclickable_history()
    {
        var queue = new ChatNotificationQueue();
        var now = DateTimeOffset.UtcNow;
        var clicks = 0;
        var entry = queue.Add("update", UserNotificationSeverity.Info, now, () => clicks++);
        Assert.NotNull(entry);

        var action = queue.TakeClickAction(entry.Id);
        Assert.NotNull(action);
        action();

        Assert.Equal(1, clicks);
        Assert.False(queue.IsVisible);
        Assert.Null(Assert.Single(queue.Entries).ClickAction);
        queue.Add("next", UserNotificationSeverity.Info, now.AddSeconds(1));
        Assert.Equal(["update", "next"], queue.Entries.Select(item => item.Message));
        Assert.Null(queue.TakeClickAction(entry.Id));
    }

    [Fact]
    public void Hidden_error_history_does_not_suppress_a_new_lower_priority_message()
    {
        var queue = new ChatNotificationQueue();
        var now = DateTimeOffset.UtcNow;
        queue.Add("error", UserNotificationSeverity.Error, now, source: UserNotificationSource.Error);

        Assert.Null(queue.Add("startup", UserNotificationSeverity.Info, now, source: UserNotificationSource.Startup));
        Assert.True(queue.HideIfExpired(now.AddSeconds(5)));
        Assert.NotNull(queue.Add("startup", UserNotificationSeverity.Info, now.AddSeconds(6),
            source: UserNotificationSource.Startup));
        Assert.Equal(["error", "startup"], queue.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void Hover_after_deadline_hides_the_box_without_discarding_history()
    {
        var queue = new ChatNotificationQueue();
        var now = DateTimeOffset.UtcNow;
        var entry = queue.Add("update", UserNotificationSeverity.Info, now, () => { });
        Assert.NotNull(entry);

        Assert.False(queue.Pause(entry.Id, now.AddSeconds(6)));
        Assert.False(queue.IsVisible);
        Assert.Single(queue.Entries);
    }
}
