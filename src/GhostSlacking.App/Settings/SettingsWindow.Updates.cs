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

    internal void NavigateToAbout()
    {
        _navigation.SelectedItem = _aboutItem;
        _selectedPage = AboutPage;
        _pageHost.Content = _pages[AboutPage];
        UpdatePageHeader(CurrentLanguage);
    }

    private async void OnCheckUpdatesClicked(object? sender, RoutedEventArgs args)
    {
        if (_updates is null || !_checkUpdatesButton.IsEnabled)
        {
            return;
        }

        if (HasKnownUpdate(_updates.Snapshot))
        {
            _openUpdateWindow?.Invoke();
        }
        else
        {
            await _updates.CheckAsync();
        }
    }

    private static bool HasKnownUpdate(ApplicationUpdateSnapshot snapshot) =>
        snapshot.Release is { } release && release.Version > ReleaseVersion.Parse(snapshot.CurrentVersion);

    private void OnSourceRepositoryClicked(object? sender, RoutedEventArgs args) => _updates?.OpenSourceRepository();

    private void OnReleasePageClicked(object? sender, RoutedEventArgs args) => _updates?.OpenReleasePage();

    private void OnUpdateStateChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshUpdateView();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshUpdateView);
        }
    }

    private void RefreshUpdateView()
    {
        var language = CurrentLanguage;
        var snapshot = _updates?.Snapshot ?? new ApplicationUpdateSnapshot(
            ApplicationUpdateStatus.Idle,
            ApplicationVersionInfo.Current());
        var releaseVersion = snapshot.Release?.VersionText;
        _currentVersionText.Text = string.Format(UiText.Text(language, "currentVersion"), snapshot.CurrentVersion);
        _latestVersionText.Text = releaseVersion is null
            ? string.Empty
            : string.Format(UiText.Text(language, "latestVersion"), releaseVersion);
        _latestVersionText.IsVisible = releaseVersion is not null;
        _lastUpdateCheckText.Text = snapshot.LastSuccessfulCheckUtc is { } lastSuccessfulCheck
            ? string.Format(
                UiText.Text(language, "lastSuccessfulUpdateCheck"),
                lastSuccessfulCheck.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            : UiText.Text(language, "noSuccessfulUpdateCheck");
        _updateStatusText.Text = snapshot.Status switch
        {
            ApplicationUpdateStatus.Checking => UiText.Text(language, "checkingUpdates"),
            ApplicationUpdateStatus.UpToDate => UiText.Text(language, "upToDate"),
            ApplicationUpdateStatus.NoCompatibleRelease => UiText.Text(language, "noCompatibleRelease"),
            ApplicationUpdateStatus.Available => string.Format(UiText.Text(language, "updateAvailableDescription"), releaseVersion),
            ApplicationUpdateStatus.Skipped => string.Format(UiText.Text(language, "updateSkippedDescription"), releaseVersion),
            ApplicationUpdateStatus.Downloading => string.Format(UiText.Text(language, "downloadingUpdate"), snapshot.ProgressPercent ?? 0),
            ApplicationUpdateStatus.Paused => UiText.Text(language, "downloadPaused"),
            ApplicationUpdateStatus.Pausing => UiText.Text(language, "pausingDownload"),
            ApplicationUpdateStatus.Stopping => UiText.Text(language, "stoppingDownload"),
            ApplicationUpdateStatus.Verifying => UiText.Text(language, "verifyingUpdate"),
            ApplicationUpdateStatus.Ready => UiText.Text(language, "updateVerified"),
            ApplicationUpdateStatus.Preparing => UiText.Text(language, "updatePreparing"),
            ApplicationUpdateStatus.Error => UiText.Text(language, snapshot.DownloadError == UpdateDownloadError.ResumeUnsupported ? "resumeUnsupported" : "updateFailed"),
            _ => UiText.Text(language, "updateNotChecked")
        };
        if (snapshot.HasDownloadTask && snapshot.Download is not null)
            _updateStatusText.Text += "\n" + UpdateDownloadText.Format(language, snapshot.Download);

        var updateKnown = HasKnownUpdate(snapshot);
        var blocked = snapshot.Status is ApplicationUpdateStatus.Checking or ApplicationUpdateStatus.Preparing or ApplicationUpdateStatus.Pausing or ApplicationUpdateStatus.Stopping;
        _checkUpdatesButton.Content = UiText.Text(language, snapshot.Status switch
        {
            ApplicationUpdateStatus.Checking => "checkingUpdatesAction",
            ApplicationUpdateStatus.Preparing => "updatePreparing",
            ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Paused => "viewUpdateProgress",
            _ => updateKnown ? "viewUpdateDetails" : "checkUpdates"
        });
        _checkUpdatesButton.IsEnabled = !blocked && _updates is not null && (!updateKnown || _openUpdateWindow is not null);
        _checkUpdatesButton.Classes.Set("accent", updateKnown);
        _updateProgress.IsVisible = snapshot.Status is ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Paused or
            ApplicationUpdateStatus.Preparing;
        _updateProgress.IsIndeterminate = snapshot.Status is not ApplicationUpdateStatus.Downloading and not ApplicationUpdateStatus.Paused;
        _updateProgress.Value = snapshot.ProgressPercent ?? 0;
    }
}
