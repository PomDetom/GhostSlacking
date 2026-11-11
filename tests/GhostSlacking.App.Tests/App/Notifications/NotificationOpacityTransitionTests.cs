using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class NotificationOpacityTransitionTests
{
    [Fact]
    public void Fade_in_reaches_full_opacity_after_150_milliseconds()
    {
        var fade = new NotificationOpacityTransition();
        var now = DateTimeOffset.UtcNow;

        fade.Start(1, now, AvaloniaNotificationService.FadeInDuration, reset: true);

        Assert.Equal(0, fade.Opacity);
        Assert.InRange(fade.Advance(now.AddMilliseconds(75)), 0.74, 0.76);
        Assert.Equal(1, fade.Advance(now.AddMilliseconds(150)));
        Assert.False(fade.IsAnimating);
    }

    [Fact]
    public void Fade_out_completes_at_the_existing_expiration_deadline()
    {
        var fade = new NotificationOpacityTransition();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        var fadeStart = deadline - AvaloniaNotificationService.FadeOutDuration;

        fade.Start(0, fadeStart, deadline - fadeStart);

        Assert.True(fade.IsFadingOut);
        Assert.InRange(fade.Advance(deadline.AddMilliseconds(-100)), 0.74, 0.76);
        Assert.Equal(0, fade.Advance(deadline));
        Assert.False(fade.IsAnimating);
    }

    [Fact]
    public void New_notification_or_hover_reverses_an_in_progress_fade_out()
    {
        var fade = new NotificationOpacityTransition();
        var now = DateTimeOffset.UtcNow;
        fade.Start(0, now, AvaloniaNotificationService.FadeOutDuration);
        fade.Advance(now.AddMilliseconds(100));

        fade.Start(1, now.AddMilliseconds(100), AvaloniaNotificationService.FadeInDuration);

        Assert.False(fade.IsFadingOut);
        Assert.InRange(fade.Opacity, 0.74, 0.76);
        Assert.Equal(1, fade.Advance(now.AddMilliseconds(250)));

        fade.Start(1, now.AddMilliseconds(250), AvaloniaNotificationService.FadeInDuration, reset: true);
        Assert.Equal(0, fade.Opacity);
    }
}
