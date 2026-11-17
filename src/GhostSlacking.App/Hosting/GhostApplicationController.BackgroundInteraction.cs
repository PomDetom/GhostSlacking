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

    private void ToggleBackgroundInteraction()
    {
        if (_backgroundStartWait is not null)
        {
            CancelBackgroundInteraction();
            return;
        }
        if (_backgroundInteraction.IsActive || _backgroundInteraction.IsPending)
        {
            _backgroundInteraction.Stop();
            return;
        }
        if (!_settings.ExperimentalBackgroundInteraction) return;
        if (_settings.ContinuousBackgroundInteraction)
        {
            var hadIntent = _peekDown;
            CancelBackgroundInteraction();
            ShowInfo(UiText.Text(_settings.Language, hadIntent ? "backgroundStopped" : "backgroundUnavailable"));
            return;
        }
        if (_settings.PeekTrigger != PeekTrigger.Toggle)
        {
            ShowError(UiText.Text(_settings.Language, "backgroundToggleRequired"));
            return;
        }
        var result = ArmBackgroundInteraction();
        if (result.Status is BackgroundArmStatus.AwaitingForeground or BackgroundArmStatus.AwaitingRelease)
        {
            CancelBackgroundInteraction();
            ShowInfo(UiText.Text(_settings.Language, result.Status == BackgroundArmStatus.AwaitingForeground ? "backgroundFocusFirst" : "backgroundPending"));
        }
        else if (!result.Success)
        {
            _logger.Log(LogLevel.Warning, $"BackgroundInteraction arm failed operation={result.Operation} error={result.ErrorCode}");
            ShowError(UiText.Text(_settings.Language, _coordinator.State == GhostState.Reveal ? "backgroundFailed" : "backgroundUnavailable"));
        }
    }

    private BackgroundArmResult ArmBackgroundInteraction(bool prepareHidden = false)
    {
        var shortcuts = GetShortcuts().Where(item => !item.Binding.IsDisabled)
            .GroupBy(item => item.Binding).Where(group => group.Count() == 1).SelectMany(group => group)
            .ToDictionary(item => item.Id, item => item.Binding);
        var preservingShortcuts = shortcuts.Keys.Where(PreservesReveal).ToHashSet();
        return _backgroundInteraction.Arm(shortcuts, preservingShortcuts, prepareHidden, _settings.PeekVirtualKey);
    }

    private void SetBackgroundStartWait(BackgroundArmStatus status)
    {
        if (_backgroundStartWait == status && _backgroundWaitTarget == _coordinator.CurrentProfile?.Original) return;
        _backgroundStartWait = status;
        _backgroundWaitTarget = _coordinator.CurrentProfile?.Original;
        _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, status == BackgroundArmStatus.AwaitingForeground ? "backgroundFocusFirst" : "backgroundPending"));
    }

    private void ClearBackgroundStartWait(bool hide = true)
    {
        if (_backgroundStartWait is null) return;
        _backgroundStartWait = null;
        _backgroundWaitTarget = null;
        if (hide) _backgroundHud.Hide();
    }

    private void ResetPeekIntent()
    {
        ClearBackgroundStartWait();
        _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
        _peekDown = false;
    }

    private void CancelBackgroundInteraction()
    {
        ResetPeekIntent();
        _backgroundInteraction.Stop(BackgroundInteractionStopReason.RevealEnded);
        if (_coordinator.State == GhostState.Reveal)
            _coordinator.UpdatePeek(_windows.GetCursorPosition(), false);
        _backgroundHud.Hide();
    }

    private void OnBackgroundInteractionStopped(BackgroundInteractionStopReason reason)
    {
        if (_isExiting) return;
        if (_backgroundStartWait is not null && !_backgroundInteraction.BlocksReveal &&
            reason is BackgroundInteractionStopReason.CursorLeft or BackgroundInteractionStopReason.RevealEnded)
        {
            UpdateTrayStatus();
            return;
        }
        _backgroundHud.Hide();
        if (_backgroundInteraction.BlocksReveal)
        {
            _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
            _peekDown = false;
            if (_coordinator.State == GhostState.Reveal)
                _coordinator.UpdatePeek(_windows.GetCursorPosition(), false);
        }
        if (reason == BackgroundInteractionStopReason.PeekClosed ||
            (_settings.ExperimentalBackgroundInteraction && _settings.ContinuousBackgroundInteraction &&
             reason is BackgroundInteractionStopReason.RevealEnded or BackgroundInteractionStopReason.CursorLeft))
        {
            UpdateTrayStatus();
            return;
        }
        var key = reason switch
        {
            BackgroundInteractionStopReason.ForegroundChanged => "backgroundForegroundChanged",
            BackgroundInteractionStopReason.UnsupportedInput or BackgroundInteractionStopReason.TargetChanged => "backgroundUnsupported",
            BackgroundInteractionStopReason.DeliveryFailed => "backgroundFailed",
            BackgroundInteractionStopReason.CleanupFailed => "backgroundCleanupFailed",
            _ => "backgroundStopped"
        };
        if (reason is BackgroundInteractionStopReason.UnsupportedInput or BackgroundInteractionStopReason.DeliveryFailed or BackgroundInteractionStopReason.CleanupFailed)
            ShowError(UiText.Text(_settings.Language, key));
        else ShowInfo(UiText.Text(_settings.Language, key));
        UpdateTrayStatus();
    }

    private void OnBackgroundInputRejected(BackgroundInputRejectionReason reason)
    {
        if (_isExiting || !_backgroundInteraction.IsActive) return;
        var key = reason switch
        {
            BackgroundInputRejectionReason.PendingTextOperation => "backgroundStarting",
            BackgroundInputRejectionReason.DiscoveryUnavailable => "backgroundDiscoveryRejected",
            BackgroundInputRejectionReason.PasteUnavailable => "backgroundPasteRejected",
            BackgroundInputRejectionReason.PasteRequiresEmptyEditor => "backgroundEmptyEditorRequired",
            BackgroundInputRejectionReason.RichContentUnverified => "backgroundRichContentRejected",
            BackgroundInputRejectionReason.ClipboardUnavailable => "backgroundClipboardRejected",
            BackgroundInputRejectionReason.ControlUnavailable => "backgroundControlRejected",
            BackgroundInputRejectionReason.NoInputRecipient => "backgroundClickEditor",
            BackgroundInputRejectionReason.ImeComposition => "backgroundImeRejected",
            BackgroundInputRejectionReason.DeadKey => "backgroundDeadKeyRejected",
            BackgroundInputRejectionReason.SystemShortcut => "backgroundSystemKeyRejected",
            BackgroundInputRejectionReason.UnsupportedMouse => "backgroundMouseRejected",
            BackgroundInputRejectionReason.OutsideEditor => "backgroundTextOnly",
            _ => "backgroundKeyRejected"
        };
        _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, key));
    }

}
