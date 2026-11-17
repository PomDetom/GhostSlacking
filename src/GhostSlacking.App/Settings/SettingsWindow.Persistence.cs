using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;
using System.Globalization;
using Button = Avalonia.Controls.Button;
using ComboBox = Avalonia.Controls.ComboBox;
using Control = Avalonia.Controls.Control;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Orientation = Avalonia.Layout.Orientation;
using ToolTip = Avalonia.Controls.ToolTip;

namespace GhostSlacking.App;

internal sealed partial class SettingsWindow : AppWindow
{

    private async void OnSaveClicked(object? sender, RoutedEventArgs args)
    {
        CloseAdjustment();
        CancelKeyCapture();
        var candidate = GetSettings().Normalize();
        if (candidate.ExperimentalBackgroundInteraction && candidate.ContinuousBackgroundInteraction && candidate.PeekTrigger != PeekTrigger.Toggle)
        {
            var message = UiText.Text(CurrentLanguage, "continuousToggleRequired");
            ShowStatus(SettingsStatus.Warning, UiText.Text(CurrentLanguage, "backgroundToggleRequired"), message, InfoBarSeverity.Warning);
            await new ContentDialog
            {
                Title = UiText.Text(CurrentLanguage, "experimentalSettings"),
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
                CloseButtonText = UiText.Text(CurrentLanguage, "ok")
            }.ShowAsync(this);
            return;
        }
        var duplicate = FindDuplicateHotkey();
        if (duplicate is not null)
        {
            var message = string.Format(
                UiText.Text(CurrentLanguage, "duplicateHotkey"),
                UiText.ShortcutName(CurrentLanguage, duplicate.Value));
            ShowStatus(
                SettingsStatus.Warning,
                UiText.Text(CurrentLanguage, "hotkeyConflict"),
                message,
                InfoBarSeverity.Warning);

            var dialog = new ContentDialog
            {
                Title = UiText.Text(CurrentLanguage, "hotkeySettings"),
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 440
                },
                CloseButtonText = UiText.Text(CurrentLanguage, "ok"),
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync(this);
            return;
        }

        var updated = GetSettings().Normalize();
        if (!_save(updated))
        {
            ShowStatus(
                SettingsStatus.Error,
                UiText.Text(CurrentLanguage, "settingsSaveErrorTitle"),
                UiText.Text(CurrentLanguage, "settingsSaveFailed"),
                InfoBarSeverity.Error);
            return;
        }

        _editState.MarkSaved(updated);
        ShowSavedStatus();
    }

    private void CancelSettingsEdits()
    {
        CloseAdjustment();
        CancelKeyCapture();
        var saved = _editState.SavedSettings;
        _initializing = true;
        try
        {
            _diameter.Value = saved.RevealDiameterPx;
            _diameterStep.Value = saved.RevealDiameterStepPx;
            _softEdgeWidth.Value = saved.RevealSoftEdgeWidthPx;
            _peekVirtualKey = saved.PeekVirtualKey;
            _restoreOnExit.IsChecked = saved.RestoreOnExit;
            _startWithWindows.IsChecked = saved.StartWithWindows;
            _backgroundInteraction.IsChecked = saved.ExperimentalBackgroundInteraction;
            _continuousInteraction.IsChecked = saved.ContinuousBackgroundInteraction;
            _backgroundInteractionHotkey.Binding = saved.BackgroundInteractionHotkey;
            _notificationRadius.Value = saved.NotificationHud.CornerRadius;
            _notificationWidth.Value = saved.NotificationHud.Width;
            _notificationHeight.Value = saved.NotificationHud.Height;
            _notificationHorizontalPosition.Value = saved.NotificationHud.HorizontalPosition;
            _notificationVerticalPosition.Value = saved.NotificationHud.VerticalPosition;
            _pickHotkey.Binding = saved.PickHotkey;
            _windowToggleHotkey.Binding = saved.WindowToggleHotkey;
            _diameterIncreaseHotkey.Binding = saved.RevealDiameterIncreaseHotkey;
            _diameterDecreaseHotkey.Binding = saved.RevealDiameterDecreaseHotkey;
            _restoreHotkey.Binding = saved.RestoreHotkey;
            _restoreAllHotkey.Binding = saved.RestoreAllHotkey;
            _settingsHotkey.Binding = saved.SettingsHotkey;
            _exitHotkey.Binding = saved.ExitHotkey;
            PopulateChoices(saved);
            ApplyLanguage(saved.Language);
            UpdateHotkeyConflicts();
        }
        finally
        {
            _initializing = false;
        }

        _applyTheme(saved.ThemeMode);
        HideStatus();
    }

    private async void OnExportLogsClicked(object? sender, RoutedEventArgs args)
    {
        await ExportAsync(
            "zip",
            $"GhostSlacking-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            "ZIP archive",
            ["*.zip"],
            _exportLogs);
    }

    private async void OnExportSettingsClicked(object? sender, RoutedEventArgs args)
    {
        await ExportAsync(
            "json",
            $"GhostSlacking-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            "JSON file",
            ["*.json"],
            ExportSavedSettingsAsync);
    }

    internal Task<DataExportResult> ExportSavedSettingsAsync(Stream destination) =>
        _exportSettings(_editState.SavedSettings, destination);

    private async Task ExportAsync(
        string extension,
        string suggestedFileName,
        string fileTypeName,
        IReadOnlyList<string> patterns,
        Func<Stream, Task<DataExportResult>> export)
    {
        DataExportResult result;
        try
        {
            var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiText.Text(CurrentLanguage, "selectExportDestination"),
                SuggestedFileName = suggestedFileName,
                DefaultExtension = extension,
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = patterns }]
            });
            if (destination is null)
            {
                return;
            }

            SetExportButtonsEnabled(false);
            await using var stream = await destination.OpenWriteAsync();
            result = await export(stream);
        }
        catch
        {
            result = DataExportResult.Failure();
        }
        finally
        {
            SetExportButtonsEnabled(true);
        }

        var (key, severity) = result.Status switch
        {
            DataExportStatus.Success => ("exportSucceeded", InfoBarSeverity.Success),
            DataExportStatus.PartialSuccess => ("exportPartiallySucceeded", InfoBarSeverity.Warning),
            _ => ("exportFailed", InfoBarSeverity.Error)
        };
        ShowExportStatus(key, severity);
    }

    private void SetExportButtonsEnabled(bool enabled)
    {
        _exportLogsButton.IsEnabled = enabled;
        _exportSettingsButton.IsEnabled = enabled;
    }

    private AppSettings GetSettings() => _editState.SavedSettings with
    {
        RevealDiameterPx = (int)_diameter.Value,
        RevealDiameterStepPx = (int)_diameterStep.Value,
        RevealSoftEdgeWidthPx = (int)_softEdgeWidth.Value,
        RevealBlurLevel = SelectedChoice(_blurLevel, _editState.SavedSettings.RevealBlurLevel),
        RevealShape = SelectedChoice(_shape, _editState.SavedSettings.RevealShape),
        PeekTrigger = SelectedChoice(_peekMode, _editState.SavedSettings.PeekTrigger),
        PeekFrameRateLimit = SelectedChoice(
            _peekFrameRateLimit,
            _editState.SavedSettings.PeekFrameRateLimit),
        UpdateChannel = SelectedChoice(_updateChannel, _editState.SavedSettings.UpdateChannel),
        PeekVirtualKey = _peekVirtualKey,
        Language = CurrentLanguage,
        ThemeMode = SelectedChoice(_themeMode, _editState.SavedSettings.ThemeMode),
        NotificationHud = GetNotificationHudSettings(),
        PickHotkey = _pickHotkey.Binding,
        WindowToggleVirtualKey = _windowToggleHotkey.Binding.VirtualKey,
        WindowToggleHotkey = _windowToggleHotkey.Binding,
        RestoreHotkey = _restoreHotkey.Binding,
        RestoreAllHotkey = _restoreAllHotkey.Binding,
        SettingsHotkey = _settingsHotkey.Binding,
        ExitHotkey = _exitHotkey.Binding,
        RevealDiameterIncreaseHotkey = _diameterIncreaseHotkey.Binding,
        RevealDiameterDecreaseHotkey = _diameterDecreaseHotkey.Binding,
        RestoreOnExit = _restoreOnExit.IsChecked == true,
        StartWithWindows = _startWithWindows.IsChecked == true,
        ExperimentalBackgroundInteraction = _backgroundInteraction.IsChecked == true,
        ContinuousBackgroundInteraction = _continuousInteraction.IsChecked == true,
        BackgroundInteractionHotkey = _backgroundInteractionHotkey.Binding,
        MinimumLogLevel = SelectedChoice(_logLevel, _editState.SavedSettings.MinimumLogLevel)
    };

    private void SubscribeToSettingChanges()
    {
        foreach (var numberBox in new[] { _diameter, _diameterStep, _softEdgeWidth, _notificationRadius, _notificationWidth, _notificationHeight, _notificationHorizontalPosition, _notificationVerticalPosition })
        {
            numberBox.ValueChanged += (_, _) => OnSettingsEdited();
        }

        foreach (var comboBox in new[] { _blurLevel, _shape, _peekFrameRateLimit, _peekMode, _logLevel, _updateChannel })
        {
            comboBox.SelectionChanged += (_, _) => OnSettingsEdited();
        }

        _themeMode.SelectionChanged += OnThemeModeSelectionChanged;
        _restoreOnExit.IsCheckedChanged += (_, _) => OnSettingsEdited();
        _startWithWindows.IsCheckedChanged += (_, _) => OnSettingsEdited();
        _backgroundInteraction.IsCheckedChanged += (_, _) =>
        {
            UpdateExperimentalControls();
            OnSettingsEdited();
        };
        _continuousInteraction.IsCheckedChanged += (_, _) => OnSettingsEdited();
        UpdateExperimentalControls();
    }

    private void UpdateExperimentalControls()
    {
        _continuousInteraction.IsEnabled = _backgroundInteraction.IsChecked == true;
        _backgroundInteractionHotkey.Host.IsEnabled = _backgroundInteraction.IsChecked == true;
    }

    private void OnSettingsEdited()
    {
        if (_initializing || _updatingLanguage)
        {
            return;
        }

        RefreshAdjustmentValues();
        _savedStatusTimer.Stop();
        if (_editState.Refresh(GetSettings()) == SettingsStatus.None)
        {
            HideStatus();
            return;
        }

        ShowStatus(
            SettingsStatus.Modified,
            UiText.Text(CurrentLanguage, "settingsModified"),
            UiText.Text(CurrentLanguage, "settingsHint"),
            InfoBarSeverity.Warning);
    }

    private void ShowSavedStatus()
    {
        _exportStatusKey = null;
        ShowStatus(
            SettingsStatus.Saved,
            UiText.Text(CurrentLanguage, "saveSucceeded"),
            string.Empty,
            InfoBarSeverity.Success);
        _savedStatusTimer.Start();
    }

    private void ShowStatus(SettingsStatus status, string title, string message, InfoBarSeverity severity)
    {
        _exportStatusKey = null;
        _savedStatusTimer.Stop();
        _editState.SetStatus(status);
        _saveBar.ShowMessage(title, message, severity,
            GetSettings().Normalize() != _editState.SavedSettings);
    }

    private void ShowExportStatus(string key, InfoBarSeverity severity)
    {
        _savedStatusTimer.Stop();
        _exportStatusKey = key;
        _saveBar.ShowMessage(UiText.Text(CurrentLanguage, key), string.Empty, severity,
            GetSettings().Normalize() != _editState.SavedSettings);
        _savedStatusTimer.Start();
    }

    private void HideStatus()
    {
        _exportStatusKey = null;
        _savedStatusTimer.Stop();
        _editState.SetStatus(SettingsStatus.None);
        _saveBar.Hide();
    }

    private void RefreshStatusText()
    {
        if (_exportStatusKey is not null)
        {
            _saveBar.UpdateTitle(UiText.Text(CurrentLanguage, _exportStatusKey));
            return;
        }

        switch (_editState.Status)
        {
            case SettingsStatus.Modified:
                _saveBar.ShowMessage(
                    UiText.Text(CurrentLanguage, "settingsModified"),
                    UiText.Text(CurrentLanguage, "settingsHint"),
                    InfoBarSeverity.Warning,
                    showActions: true);
                break;
            case SettingsStatus.Saved:
                _saveBar.ShowMessage(UiText.Text(CurrentLanguage, "saveSucceeded"), string.Empty,
                    InfoBarSeverity.Success, showActions: false);
                break;
        }
    }

    private void OnSavedStatusTimerTick(object? sender, EventArgs args)
    {
        if (_exportStatusKey is not null)
        {
            _exportStatusKey = null;
            _savedStatusTimer.Stop();
            OnSettingsEdited();
            return;
        }

        if (_editState.ExpireSaved())
        {
            _savedStatusTimer.Stop();
            _saveBar.Hide();
        }
    }
}
