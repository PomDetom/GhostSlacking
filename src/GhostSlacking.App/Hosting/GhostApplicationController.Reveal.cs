using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Diagnostics;
using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed partial class GhostApplicationController : IDisposable
{

    private void OnPeekTimerTick(object? sender, EventArgs args)
    {
        var environmentReady = _coordinator.RefreshWindowEnvironment();
        var peekKeyDown = _windows.IsKeyDown(_settings.PeekVirtualKey);
        var peekPressed = peekKeyDown && !_lastPeekKeyDown;
        _lastPeekKeyDown = peekKeyDown;
        if (!peekKeyDown) _invalidContinuousGesture = false;
        _backgroundInteraction.Poll();
        if (_backgroundStartWait is not null && !BackgroundStartPolicy.KeepWaiting(_backgroundWaitTarget, _coordinator.CurrentProfile?.Original, _peekDown)) ResetPeekIntent();
        if (_backgroundInteraction.BlocksReveal && peekPressed)
        {
            _backgroundInteraction.AcknowledgeRevealBlock();
        }
        if (_backgroundInteraction.BlocksReveal)
        {
            _peekState.Reset(peekKeyDown);
            _peekDown = false;
        }
        else
        {
            _peekDown = _peekState.Update(peekKeyDown, _settings.PeekTrigger);
        }
        var cursor = _windows.GetCursorPosition();
        var startedAt = Stopwatch.GetTimestamp();
        var continuous = _settings.ExperimentalBackgroundInteraction && _settings.ContinuousBackgroundInteraction;
        var show = _peekDown;
        if (continuous)
        {
            if (_settings.PeekTrigger != PeekTrigger.Toggle)
            {
                if (_peekDown && !_invalidContinuousGesture)
                {
                    _invalidContinuousGesture = true;
                    ShowError(UiText.Text(_settings.Language, "backgroundToggleRequired"));
                }
                ResetPeekIntent();
                show = false;
            }
            else if (!environmentReady || _isExiting || _pickerActive || _settingsWindow is not null || _updateWindow is not null)
            {
                _backgroundInteraction.Stop(BackgroundInteractionStopReason.RevealEnded);
                ResetPeekIntent();
                show = false;
            }
            else if (!_peekDown || _coordinator.CurrentProfile?.Original.ScreenBounds.Contains(cursor) != true)
            {
                _backgroundInteraction.Stop(_peekDown ? BackgroundInteractionStopReason.CursorLeft : BackgroundInteractionStopReason.RevealEnded);
                if (!_peekDown) ClearBackgroundStartWait();
                show = false;
            }
            else
            {
                if (!_backgroundInteraction.IsActive && !_backgroundInteraction.IsPending && _coordinator.State == GhostState.Reveal)
                    _coordinator.UpdatePeek(cursor, false);
                if (_backgroundInteraction.IsDraining && !_backgroundInteraction.BlocksReveal) SetBackgroundStartWait(BackgroundArmStatus.AwaitingRelease);
                if (!_backgroundInteraction.IsActive && !_backgroundInteraction.IsPending &&
                    !_backgroundInteraction.IsDraining && !_backgroundInteraction.BlocksReveal &&
                    BackgroundStartPolicy.CanResume(_backgroundStartWait, _backgroundInteraction.HasSeparateForeground) &&
                    _coordinator.State is GhostState.Ghost or GhostState.Reveal)
                {
                    var result = ArmBackgroundInteraction(prepareHidden: true);
                    if (result.Status is BackgroundArmStatus.AwaitingForeground or BackgroundArmStatus.AwaitingRelease)
                        SetBackgroundStartWait(result.Status);
                    else if (result.Status == BackgroundArmStatus.Preparing)
                        ClearBackgroundStartWait(hide: false);
                    else
                    {
                        ClearBackgroundStartWait();
                        _logger.Log(LogLevel.Warning, $"ContinuousBackgroundInteraction prepare failed operation={result.Operation} error={result.ErrorCode}");
                        ResetPeekIntent();
                        ShowError(UiText.Text(_settings.Language, "backgroundFailed"));
                    }
                }
                _backgroundInteraction.Poll();
                show = _peekDown && (_backgroundInteraction.IsReady || _backgroundInteraction.IsActive);
            }
        }
        _coordinator.UpdatePeek(cursor, show);
        if (continuous && show && _backgroundInteraction.IsReady && !_backgroundInteraction.ActivateReveal())
        {
            // A new event arrived while the target was being shown. Keep it gated,
            // hide again and prepare a fresh session after the physical release.
            _backgroundInteraction.Stop(BackgroundInteractionStopReason.RevealEnded);
            _coordinator.UpdatePeek(cursor, false);
        }
        _backgroundInteraction.Poll();
        var revealActive = _peekDown && _coordinator.State == GhostState.Reveal;
        UpdateRevealFrameRate(cursor, revealActive);
        var updateDuration = Stopwatch.GetElapsedTime(startedAt);

        if (_settings.MinimumLogLevel != LogLevel.Debug || !revealActive)
        {
            _revealPerformance.Reset();
            return;
        }

        var summary = _revealPerformance.Record(
            cursor,
            updateDuration,
            _revealPerformanceClock.Elapsed,
            _revealFrameRate.Current.TargetFramesPerSecond);
        if (summary is null)
        {
            return;
        }

        _logger.Log(
            LogLevel.Debug,
            $"RevealPerformance frames={summary.FrameCount} targetFps={summary.TargetFramesPerSecond} " +
            $"effectiveFps={summary.EffectiveFramesPerSecond:F1} " +
            $"averageMs={summary.AverageDuration.TotalMilliseconds:F2} p95Ms={summary.P95Duration.TotalMilliseconds:F2} " +
            $"maximumMs={summary.MaximumDuration.TotalMilliseconds:F2} overBudget={summary.OverBudgetCount}");
    }

    private void UpdateRevealFrameRate(Point cursor, bool revealActive)
    {
        var display = revealActive ? _displayRefreshRates.GetForPoint(cursor) : null;
        var transition = _revealFrameRate.Update(revealActive, _settings.PeekFrameRateLimit, display);
        if (transition.IntervalChanged)
        {
            _timer.Interval = transition.Decision.Interval;
        }

        if (!transition.ContextChanged)
        {
            return;
        }

        _revealPerformance.Reset();
        var detected = transition.Decision.DetectedRefreshRate?.ToString() ?? "unavailable";
        var displayId = transition.Decision.DisplayId ?? "unknown";
        _logger.Log(
            LogLevel.Debug,
            $"RevealFrameRate active={transition.Decision.IsRevealActive} " +
            $"targetFps={transition.Decision.TargetFramesPerSecond} detectedHz={detected} " +
            $"limit={transition.Decision.Limit} display={displayId} fallback={transition.Decision.UsesFallback}");
    }

    private void OnDisplayConfigurationChanged()
    {
        _displayRefreshRates.Invalidate();
        _notifications?.RefreshLayout();
        _backgroundHud?.RefreshLayout();
        _logger.Log(LogLevel.Debug, "Display configuration changed; refresh-rate cache invalidated.");
    }

    private void ToggleWindowVisibility()
    {
        CancelBackgroundInteraction();
        if (_coordinator.CurrentProfile is null)
        {
            ShowError(UiText.Text(_settings.Language, "selectWindowFirst"));
            return;
        }

        _coordinator.ToggleWindowVisibility("window visibility hotkey");
    }

    private void RestoreCurrent(string reason)
    {
        CancelBackgroundInteraction();
        _coordinator.RestoreCurrent(reason);
        UpdateTrayStatus();
    }

    private void RestoreAll(string reason)
    {
        CancelBackgroundInteraction();
        _coordinator.RestoreAll(reason);
        UpdateTrayStatus();
    }

    private void AdjustRevealDiameter(int direction)
    {
        var result = TryAdjustRevealDiameter(_settings, direction, _settingsStore.Save, out var updated);
        if (result == DiameterAdjustmentResult.SaveFailed)
        {
            ShowError(UiText.Text(_settings.Language, "settingsSaveFailed"));
            return;
        }
        if (result == DiameterAdjustmentResult.Unchanged) return;

        _settings = updated;
        UpdateCurrentRevealSettings();
    }

    internal static DiameterAdjustmentResult TryAdjustRevealDiameter(
        AppSettings current, int direction, Func<AppSettings, bool> save, out AppSettings updated)
    {
        var candidate = current.AdjustRevealDiameter(direction);
        updated = current;
        if (candidate.RevealDiameterPx == current.RevealDiameterPx) return DiameterAdjustmentResult.Unchanged;
        if (!save(candidate)) return DiameterAdjustmentResult.SaveFailed;
        updated = candidate;
        return DiameterAdjustmentResult.Updated;
    }

    private void UpdateCurrentRevealSettings()
    {
        _coordinator.UpdateRevealSettings(new RevealSettings
        {
            DiameterPx = _settings.RevealDiameterPx,
            SoftEdgeWidthPx = _settings.RevealSoftEdgeWidthPx,
            BlurLevel = _settings.RevealBlurLevel,
            Shape = _settings.RevealShape,
            Trigger = _settings.PeekTrigger
        });
    }

}
