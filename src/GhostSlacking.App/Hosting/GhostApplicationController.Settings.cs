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

    private void OpenSettings(string? initialPage = null)
    {
        CancelBackgroundInteraction();
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            if (string.Equals(initialPage, "about", StringComparison.Ordinal))
            {
                _settingsWindow.NavigateToAbout();
            }

            _settingsWindow.Activate();
            return;
        }

        _hotkeys.UnregisterAll();
        try
        {
            var window = new SettingsWindow(
                _settings,
                SaveSettings,
                exportLogs: stream => _dataExport.ExportLogsAsync(stream, _settings.MinimumLogLevel),
                exportSettings: (settings, stream) => _dataExport.ExportSettingsAsync(stream, settings),
                updates: _updates,
                openUpdateWindow: OpenUpdateWindow,
                initialPage: initialPage);
            _settingsWindow = window;
            window.Closed += OnSettingsClosed;
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            _settingsWindow = null;
            _logger.Log(LogLevel.Error, "The Avalonia settings window could not be shown.", exception);
            ShowError(UiText.Text(_settings.Language, "settingsOpenFailed"));
            if (!_isExiting)
            {
                RegisterHotkeys();
            }
        }
    }

    private void ActivateOwnedWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isExiting || !window.IsVisible)
            {
                return;
            }

            var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
            var result = _windows.BringToForeground(handle);
            if (result.Success)
            {
                return;
            }

            _logger.Log(
                LogLevel.Warning,
                $"Update window foreground activation failed error={result.ErrorCode} message={result.ErrorMessage}");
            window.Activate();
        }, DispatcherPriority.Background);
    }

    private void OnSettingsClosed(object? sender, EventArgs args)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsClosed;
            _settingsWindow = null;
        }

        if (!_isExiting)
        {
            RegisterHotkeys();
        }
    }

    private bool SaveSettings(AppSettings updated)
    {
        CancelBackgroundInteraction();
        var previousKey = _settings.PeekVirtualKey;
        var previousTrigger = _settings.PeekTrigger;
        updated = updated.Normalize();
        if (!_settingsStore.Save(updated))
        {
            return false;
        }

        _settings = updated;
        _notifications.Configure(_settings);
        _backgroundHud.Configure(_settings);
        _updates.SetChannel(_settings.UpdateChannel);
        _logger.MinimumLevel = _settings.MinimumLogLevel;
        AppTheme.Apply(_settings.ThemeMode);
        _updateWindow?.RefreshLanguage(_settings.Language);
        if (previousKey != _settings.PeekVirtualKey || previousTrigger != _settings.PeekTrigger)
        {
            _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
            _peekDown = false;
        }
        UpdateCurrentRevealSettings();
        if (!StartupManager.Apply(_settings.StartWithWindows))
        {
            _logger.Log(LogLevel.Warning, "Could not update the Windows startup registration.");
        }
        UpdateTrayStatus();
        return true;
    }

}
