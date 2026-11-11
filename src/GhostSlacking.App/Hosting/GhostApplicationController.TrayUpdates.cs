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

    internal void ShowStartupNotification()
    {
        _logger.Log(LogLevel.Debug, "Startup notification requested.");
        var updateResult = _updateCompletionStore.Consume();
        if (updateResult is null)
        {
            ShowInfo(UiText.Text(_settings.Language, "startupReady"), UserNotificationSource.Startup);
            return;
        }

        _logger.Log(
            updateResult.Status == UpdateCompletionStatus.Failed ? LogLevel.Warning : LogLevel.Info,
            $"UpdateCompletion status={updateResult.Status} version={updateResult.Version} exitCode={updateResult.InstallerExitCode}");
        switch (updateResult.Status)
        {
            case UpdateCompletionStatus.LaunchFailed:
                ShowError(UiText.Text(_settings.Language, "updateLaunchFailed"), UserNotificationSource.Error);
                break;
            case UpdateCompletionStatus.Succeeded:
                ShowSuccess(UiText.Text(_settings.Language, "updateCompleted"), UserNotificationSource.Startup);
                break;
            case UpdateCompletionStatus.Cancelled:
                ShowInfo(UiText.Text(_settings.Language, "updateCancelled"), UserNotificationSource.Startup);
                break;
            default:
                ShowError(UiText.Text(_settings.Language, "automaticUpdateFailed"), UserNotificationSource.Error);
                break;
        }
    }

    internal async void BeginAutomaticUpdateCheck()
    {
        try
        {
            var snapshot = await _updates.CheckAsync(_updateCancellation.Token);
            if (snapshot.Status == ApplicationUpdateStatus.Available && snapshot.Release is not null && !_isExiting)
            {
                await Dispatcher.UIThread.InvokeAsync(() => _notifications.Show(
                    UiText.Text(_settings.Language, "updateAvailableNotification"),
                    UserNotificationSeverity.Info,
                    OpenUpdateWindow,
                    UserNotificationSource.Update));
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
    }

    private static NativeMenuItem CreateMenuItem(string header, Action? action, bool enabled = true)
    {
        var item = new NativeMenuItem(header) { IsEnabled = enabled };
        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        return item;
    }

    private void OnTrayIconClicked(object? sender, EventArgs args)
    {
        if (_trayDoubleClick.RegisterClick(DateTimeOffset.UtcNow))
        {
            BeginPicking();
        }
    }

    private void OpenUpdateWindow()
    {
        CancelBackgroundInteraction();
        if (_isExiting || _updates.Snapshot.Release is null)
        {
            return;
        }

        if (_updateWindow is not null)
        {
            _updateWindow.RefreshLanguage(_settings.Language);
            ActivateOwnedWindow(_updateWindow);
            return;
        }

        try
        {
            var window = new UpdateWindow(_updates, _settings.Language);
            _updateWindow = window;
            window.Closed += OnUpdateWindowClosed;
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            _updateWindow = null;
            _logger.Log(LogLevel.Error, "The update window could not be shown.", exception);
            ShowError(UiText.Text(_settings.Language, "updateWindowOpenFailed"));
        }
    }

    private void OnUpdateWindowClosed(object? sender, EventArgs args)
    {
        if (_updateWindow is null)
        {
            return;
        }

        _updateWindow.Closed -= OnUpdateWindowClosed;
        _updateWindow = null;
    }

    private void UpdateTrayStatus()
    {
        var profile = _coordinator.CurrentProfile;
        var target = profile?.Original.ProcessName ?? "none";
        var text = string.Format(UiText.Text(_settings.Language, "status"), UiText.State(_settings.Language, _coordinator.State), target);
        _statusItem.Header = text;
        _pickItem.Header = UiText.Text(_settings.Language, "pick");
        _toggleItem.Header = UiText.Text(_settings.Language, "toggle");
        _restoreItem.Header = UiText.Text(_settings.Language, "restore");
        _restoreAllItem.Header = UiText.Text(_settings.Language, "restoreAll");
        _settingsItem.Header = UiText.Text(_settings.Language, "settings");
        var update = _updates.Snapshot;
        var showUpdate = update.Release is not null &&
            update.Release.Version > ReleaseVersion.Parse(update.CurrentVersion) &&
            update.Status is not ApplicationUpdateStatus.Skipped and not ApplicationUpdateStatus.UpToDate and
                not ApplicationUpdateStatus.NoCompatibleRelease;
        _updateItem.IsVisible = showUpdate;
        _updateItem.Header = showUpdate
            ? string.Format(UiText.Text(_settings.Language, "updateAvailableTray"), update.Release!.VersionText)
            : string.Empty;
        _exitItem.Header = UiText.Text(_settings.Language, "exit");
        _restoreItem.IsEnabled = profile is not null;
        _restoreAllItem.IsEnabled = profile is not null;
        _toggleItem.IsEnabled = profile is not null;
        _trayIcon.ToolTipText = $"GhostSlacking - {UiText.State(_settings.Language, _coordinator.State)}";
        if (_backgroundInteraction.IsActive)
        {
            _trayIcon.ToolTipText += $" / {UiText.Text(_settings.Language, "backgroundActive")}";
        }
    }

    private void OnUpdateStateChanged(object? sender, EventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateTrayStatus();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateTrayStatus);
        }
    }

    private void ShowError(string message, UserNotificationSource source = UserNotificationSource.Error)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Error, source: source);
    }

    private void ShowCoreError(UserErrorKind kind)
    {
        ShowError(UiText.Error(_settings.Language, kind));
    }

    private void ShowInfo(
        string message,
        UserNotificationSource source = UserNotificationSource.General)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Info, source: source);
    }

    private void ShowSuccess(string message, UserNotificationSource source)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Success, source: source);
    }

    private void OnWindowSelected(TargetWindow target) =>
        ShowSuccess(UiText.Text(_settings.Language, "windowSelected"), UserNotificationSource.Selection);

}
