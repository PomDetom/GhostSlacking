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
        if (_updates is null)
        {
            return;
        }

        await _updates.CheckAsync();
    }

    private void OnViewUpdateClicked(object? sender, RoutedEventArgs args) => _openUpdateWindow?.Invoke();

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
            ApplicationUpdateStatus.Verifying => UiText.Text(language, "verifyingUpdate"),
            ApplicationUpdateStatus.Ready => UiText.Text(language, "startingInstaller"),
            ApplicationUpdateStatus.Preparing => UiText.Text(language, "updatePreparing"),
            ApplicationUpdateStatus.Error => UiText.Text(language, "updateFailed"),
            _ => UiText.Text(language, "updateNotChecked")
        };

        var busy = snapshot.Status is ApplicationUpdateStatus.Checking or
            ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Preparing;
        _checkUpdatesButton.Content = UiText.Text(language, "checkUpdates");
        _checkUpdatesButton.IsEnabled = !busy && _updates is not null;
        _updateProgress.IsVisible = snapshot.Status == ApplicationUpdateStatus.Downloading;
        _updateProgress.Value = snapshot.ProgressPercent ?? 0;

        var updateKnown = snapshot.Release is not null && snapshot.Release.Version >
            ReleaseVersion.Parse(snapshot.CurrentVersion);
        _viewUpdateButton.Content = UiText.Text(language, "viewUpdateDetails");
        _viewUpdateButton.IsVisible = updateKnown;
        _viewUpdateButton.IsEnabled = !busy && _openUpdateWindow is not null;
    }
}
