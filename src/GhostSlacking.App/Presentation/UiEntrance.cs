using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace GhostSlacking.App;

internal sealed class UiEntrance(int durationMilliseconds, double slideDistance = 0)
{
    internal const int PopupDuration = 220;

    internal static readonly AttachedProperty<int> DurationProperty =
        AvaloniaProperty.RegisterAttached<UiEntrance, Control, int>("Duration");

    private static readonly ConditionalWeakTable<Control, UiEntrance> AttachedEntrances = new();
    private CancellationTokenSource? _playback;

    static UiEntrance() => DurationProperty.Changed.AddClassHandler<Control>(OnDurationChanged);

    internal async void Play(Visual content, Visual? heading = null)
    {
        Stop();
        if (UiMotionSettings.Current is not { } settings || !settings.Refresh()) return;

        using var playback = new CancellationTokenSource();
        _playback = playback;
        settings.Changed += OnMotionChanged;
        try
        {
            var animation = CreateAnimation();
            var contentPlayback = animation.RunAsync(content, playback.Token);
            await (heading is null ? contentPlayback : Task.WhenAll(contentPlayback, animation.RunAsync(heading, playback.Token)));
        }
        catch (OperationCanceledException) when (playback.IsCancellationRequested)
        {
        }
        finally
        {
            settings.Changed -= OnMotionChanged;
            if (ReferenceEquals(_playback, playback)) _playback = null;
        }

        void OnMotionChanged(object? sender, EventArgs args)
        {
            if (!settings.Enabled) playback.Cancel();
        }
    }

    internal void Stop()
    {
        var playback = _playback;
        _playback = null;
        playback?.Cancel();
    }

    private Animation CreateAnimation()
    {
        var start = new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.36) } };
        var end = new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } };
        if (slideDistance > 0)
        {
            start.Setters.Add(new Setter(TranslateTransform.YProperty, slideDistance));
            end.Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
        }
        else
        {
            start.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, 0.94));
            start.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, 0.94));
            end.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, 1d));
            end.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, 1d));
        }

        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            Easing = new SineEaseOut(),
            Children = { start, end }
        };
    }

    private static void OnDurationChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (AttachedEntrances.TryGetValue(control, out var previous))
        {
            control.AttachedToVisualTree -= OnAttached;
            control.DetachedFromVisualTree -= OnDetached;
            previous.Stop();
            AttachedEntrances.Remove(control);
        }

        var duration = args.GetNewValue<int>();
        if (duration <= 0) return;
        var entrance = new UiEntrance(duration);
        AttachedEntrances.Add(control, entrance);
        control.AttachedToVisualTree += OnAttached;
        control.DetachedFromVisualTree += OnDetached;
        if (control.IsAttachedToVisualTree()) entrance.Play(control);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control && AttachedEntrances.TryGetValue(control, out var entrance)) entrance.Play(control);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control && AttachedEntrances.TryGetValue(control, out var entrance)) entrance.Stop();
    }
}
