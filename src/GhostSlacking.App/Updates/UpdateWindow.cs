using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed class UpdateWindow : AppWindow
{
    private readonly IApplicationUpdateManager _updates;
    private readonly SolidColorBrush _canvasBrush = new();
    private readonly SolidColorBrush _surfaceBrush = new();
    private readonly SolidColorBrush _borderBrush = new();
    private readonly SolidColorBrush _primaryBrush = new();
    private readonly SolidColorBrush _secondaryBrush = new();
    private readonly SolidColorBrush _accentBrush = new();
    private readonly SolidColorBrush _errorBrush = new();
    private readonly TextBlock _headingText;
    private readonly TextBlock _versionText;
    private readonly TextBlock _packageText;
    private readonly TextBlock _statusText;
    private readonly TextBlock _historyWarningText;
    private readonly Border _historyWarning;
    private readonly TextBlock _notesHeading;
    private readonly TextBlock _closeHint;
    private readonly StackPanel _notesPanel;
    private readonly ProgressBar _progress;
    private readonly Button _installButton;
    private readonly Button _moreButton;
    private readonly MenuItem _skipItem;
    private readonly MenuItem _releaseItem;
    private readonly MenuItem _recheckItem;
    private readonly Button _laterButton;
    private CancellationTokenSource? _downloadCancellation;
    private UpdateRelease? _renderedRelease;
    private UiLanguage _language;
    private bool _checking;
    private bool _closed;

    public UpdateWindow(IApplicationUpdateManager updates, UiLanguage language)
    {
        _updates = updates;
        _language = language;
        Title = T("updateWindowTitle");
        Width = 760;
        Height = 680;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.TitleBarImage;
        CornerRadius = new CornerRadius(12);

        _headingText = new TextBlock { FontSize = 12, FontWeight = FontWeight.Medium, Foreground = _accentBrush };
        _versionText = new TextBlock { FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = _primaryBrush };
        _packageText = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = _secondaryBrush };
        _statusText = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Polite);
        _historyWarningText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = _secondaryBrush };
        _historyWarning = Card(_historyWarningText, new Thickness(12, 9));
        _notesHeading = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = _primaryBrush };
        _closeHint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = _secondaryBrush };
        _notesPanel = new StackPanel { Spacing = 8 };
        _progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, IsVisible = false };
        _installButton = new Button { MinWidth = 130, HorizontalContentAlignment = HorizontalAlignment.Center };
        _installButton.Classes.Add("accent");
        _moreButton = new Button();
        _skipItem = new MenuItem();
        _releaseItem = new MenuItem();
        _recheckItem = new MenuItem();
        _moreButton.Flyout = new MenuFlyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Items = { _releaseItem, _skipItem, new Separator(), _recheckItem }
        };
        _laterButton = new Button { MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

        _installButton.Click += OnInstallClicked;
        _skipItem.Click += OnSkipClicked;
        _releaseItem.Click += OnReleaseClicked;
        _recheckItem.Click += OnRecheckClicked;
        _laterButton.Click += OnLaterClicked;
        _updates.Changed += OnUpdateStateChanged;
        Closed += OnClosed;
        ActualThemeVariantChanged += (_, _) => ApplyPalette();

        Content = BuildContent();
        ApplyPalette();
        RefreshView(rebuildNotes: true);
    }

    private string T(string key) => UiText.Text(_language, key);

    public void RefreshLanguage(UiLanguage language)
    {
        if (_language == language) return;
        _language = language;
        Title = T("updateWindowTitle");
        RefreshView(rebuildNotes: true);
    }

    private Border Card(Control child, Thickness padding) => new()
    {
        Background = _surfaceBrush,
        BorderBrush = _borderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = padding,
        Child = child
    };

    private Control BuildContent()
    {
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        identity.Children.Add(new Image
        {
            Source = AppIcon.TitleBarImage, Width = 44, Height = 44,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 16, 0)
        });
        var version = new StackPanel { Spacing = 6, Children = { _headingText, _versionText, _packageText } };
        Grid.SetColumn(version, 1);
        identity.Children.Add(version);
        identity.Margin = new Thickness(0, 0, 0, 16);

        var header = new StackPanel { Spacing = 12, Children = { identity, _historyWarning } };
        var notes = new ScrollViewer
        {
            Content = _notesPanel,
            Margin = new Thickness(0, 12, 0, 0),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var notesContent = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        notesContent.Children.Add(_notesHeading);
        Grid.SetRow(notes, 1);
        notesContent.Children.Add(notes);
        var notesCard = Card(notesContent, new Thickness(20, 16));
        notesCard.Margin = new Thickness(0, 16, 0, 16);

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        actions.Children.Add(_moreButton);
        var primaryActions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { _laterButton, _installButton }
        };
        Grid.SetColumn(primaryActions, 2);
        actions.Children.Add(primaryActions);
        var footer = new StackPanel { Spacing = 12, Children = { _statusText, _progress, _closeHint, actions } };

        var root = new Grid
        {
            Background = _canvasBrush, Margin = new Thickness(24, 20, 24, 24),
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        root.Children.Add(header);
        Grid.SetRow(notesCard, 1);
        root.Children.Add(notesCard);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void ApplyPalette()
    {
        var dark = AppTheme.IsDark(ActualThemeVariant);
        var palette = AppTheme.Palette(ActualThemeVariant);
        _canvasBrush.Color = Color.Parse(dark ? "#161819" : "#F2F4F5");
        _surfaceBrush.Color = palette.Surface;
        _borderBrush.Color = palette.Border;
        _primaryBrush.Color = palette.Primary;
        _secondaryBrush.Color = palette.Secondary;
        _accentBrush.Color = palette.Accent;
        _errorBrush.Color = palette.Error;
        Background = _canvasBrush;
        TitleBar.BackgroundColor = _canvasBrush.Color;
        TitleBar.ForegroundColor = palette.Primary;
        TitleBar.InactiveBackgroundColor = _canvasBrush.Color;
        TitleBar.InactiveForegroundColor = palette.Secondary;
        TitleBar.ButtonBackgroundColor = _canvasBrush.Color;
        TitleBar.ButtonForegroundColor = palette.Primary;
        TitleBar.ButtonInactiveBackgroundColor = _canvasBrush.Color;
        TitleBar.ButtonInactiveForegroundColor = palette.Secondary;
    }

    private void OnUpdateStateChanged(object? sender, EventArgs args)
    {
        if (_closed) return;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            RefreshView(rebuildNotes: false);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshView(rebuildNotes: false));
    }

    private static bool IsBusy(ApplicationUpdateStatus status) => status is ApplicationUpdateStatus.Checking or
        ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Preparing;

    private bool HasKnownUpdate(ApplicationUpdateSnapshot snapshot) =>
        snapshot.Release is { } release && release.Version > ReleaseVersion.Parse(snapshot.CurrentVersion);

    private void RefreshView(bool rebuildNotes)
    {
        if (_closed) return;
        var snapshot = _updates.Snapshot;
        var release = snapshot.Release;
        var updateKnown = HasKnownUpdate(snapshot);
        var busy = IsBusy(snapshot.Status) || _downloadCancellation is not null || _checking;
        var downloading = snapshot.Status == ApplicationUpdateStatus.Downloading;
        var cancelling = _downloadCancellation?.IsCancellationRequested == true;
        _headingText.Text = "GHOSTSLACKING · " + T("softwareUpdate");
        _versionText.Text = release is null ? T("updateWindowNoRelease") : string.Format(T("releaseNotesVersion"), release.VersionText);
        _packageText.Text = string.Format(T("currentVersion"), snapshot.CurrentVersion) +
            (release is null ? string.Empty : "  ·  " + string.Format(T("updatePackageSize"), (release.Installer.Size / 1048576d).ToString("0.0", CultureInfo.InvariantCulture)));
        _statusText.Text = cancelling ? T("cancellingDownload") : StatusText(snapshot);
        _statusText.Foreground = snapshot.Status == ApplicationUpdateStatus.Error ? _errorBrush : _secondaryBrush;
        _historyWarningText.Text = T("releaseHistoryIncomplete");
        _historyWarning.IsVisible = release?.ReleaseHistoryIncomplete == true;
        _notesHeading.Text = T("updateReleaseNotes");
        _closeHint.Text = T("closeCancelsDownload");
        _closeHint.IsVisible = snapshot.Status is ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying;
        _progress.IsVisible = snapshot.Status is ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Preparing ||
            (snapshot.Status == ApplicationUpdateStatus.Ready && _downloadCancellation is not null);
        _progress.IsIndeterminate = !downloading;
        _progress.Value = snapshot.ProgressPercent ?? 0;

        _installButton.IsVisible = updateKnown;
        _installButton.IsEnabled = updateKnown && (downloading
            ? _downloadCancellation is not null && !cancelling
            : !busy);
        _installButton.Content = T(snapshot.Status switch
        {
            ApplicationUpdateStatus.Downloading => cancelling ? "cancellingDownloadAction" : "cancelDownload",
            ApplicationUpdateStatus.Verifying => "verifyingUpdateAction",
            ApplicationUpdateStatus.Ready when _downloadCancellation is not null => "updatePreparing",
            ApplicationUpdateStatus.Preparing => "updatePreparing",
            _ when !_updates.CanInstallUpdates => "openReleasePage",
            ApplicationUpdateStatus.Error => "retryDownloadAndInstall",
            _ => "downloadAndInstall"
        });
        _skipItem.IsVisible = updateKnown;
        _skipItem.IsEnabled = !busy;
        _skipItem.Header = T(snapshot.Status == ApplicationUpdateStatus.Skipped ? "resumeReminders" : "skipVersion");
        _releaseItem.Header = T("viewRelease");
        _recheckItem.Header = T("recheckUpdates");
        _recheckItem.IsEnabled = !busy;
        _moreButton.Content = T("moreUpdateActions");
        _laterButton.Content = T("later");
        _laterButton.IsVisible = !downloading;
        _laterButton.IsEnabled = !busy;

        if (rebuildNotes || !ReferenceEquals(_renderedRelease, release))
        {
            RebuildNotes(release);
            _renderedRelease = release;
        }
    }

    private string StatusText(ApplicationUpdateSnapshot snapshot)
    {
        if (snapshot.Status is ApplicationUpdateStatus.Available or ApplicationUpdateStatus.Skipped &&
            !_updates.CanInstallUpdates && HasKnownUpdate(snapshot))
            return T("portableUpdateDescription");

        return snapshot.Status switch
        {
            ApplicationUpdateStatus.Checking => T("checkingUpdates"),
            ApplicationUpdateStatus.UpToDate => T("upToDate"),
            ApplicationUpdateStatus.NoCompatibleRelease => T("noCompatibleRelease"),
            ApplicationUpdateStatus.Available => string.Format(T("updateAvailableDescription"), snapshot.Release?.VersionText),
            ApplicationUpdateStatus.Skipped => string.Format(T("updateSkippedDescription"), snapshot.Release?.VersionText),
            ApplicationUpdateStatus.Downloading => string.Format(T("downloadingUpdate"), snapshot.ProgressPercent ?? 0),
            ApplicationUpdateStatus.Verifying => T("verifyingUpdate"),
            ApplicationUpdateStatus.Preparing => T("updatePreparing"),
            ApplicationUpdateStatus.Ready => T("updateVerified"),
            ApplicationUpdateStatus.Error => T("updateFailed"),
            _ => T("updateNotChecked")
        };
    }

    private void RebuildNotes(UpdateRelease? release)
    {
        _notesPanel.Children.Clear();
        if (release is null || release.ReleaseNotes.Count == 0)
        {
            AddText(T("noReleaseNotes"), ReleaseNotesBlockKind.Paragraph);
            return;
        }
        foreach (var entry in release.ReleaseNotes.OrderByDescending(item => item.Version))
        {
            AddText(string.Format(T("releaseNotesVersion"), entry.VersionText), ReleaseNotesBlockKind.Heading, 18);
            var blocks = ReleaseNotesFormatter.Parse(entry.Notes.For(_language));
            if (blocks.Count == 0)
            {
                AddText(T("noReleaseNotes"), ReleaseNotesBlockKind.Paragraph);
                continue;
            }
            foreach (var block in blocks)
                AddText(block.Kind == ReleaseNotesBlockKind.Bullet ? $"• {block.Text}" : block.Text, block.Kind);
        }
    }

    private void AddText(string text, ReleaseNotesBlockKind kind, double? fontSize = null) =>
        _notesPanel.Children.Add(new TextBlock
        {
            Text = text, Foreground = kind == ReleaseNotesBlockKind.Heading ? _primaryBrush : _secondaryBrush,
            FontSize = fontSize ?? (kind == ReleaseNotesBlockKind.Heading ? 15 : 14),
            FontWeight = kind == ReleaseNotesBlockKind.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = kind == ReleaseNotesBlockKind.Heading ? new Thickness(0, 10, 0, 2) : default
        });

    private async void OnInstallClicked(object? sender, RoutedEventArgs args)
    {
        if (_closed || !_installButton.IsEnabled || !HasKnownUpdate(_updates.Snapshot)) return;
        if (_updates.Snapshot.Status == ApplicationUpdateStatus.Downloading)
        {
            _downloadCancellation?.Cancel();
            RefreshView(rebuildNotes: false);
            return;
        }
        if (_downloadCancellation is not null || IsBusy(_updates.Snapshot.Status)) return;
        if (!_updates.CanInstallUpdates)
        {
            _updates.OpenReleasePage();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        RefreshView(rebuildNotes: false);
        try
        {
            var installerPath = await _updates.DownloadInstallerAsync(cancellation.Token);
            // A completed download must not authorize installation after the user closed or cancelled.
            if (!_closed && !cancellation.IsCancellationRequested && installerPath is not null)
                _updates.BeginAutomaticInstall(installerPath);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation can occur while the manager is still waiting for its operation gate.
        }
        finally
        {
            _downloadCancellation = null;
            RefreshView(rebuildNotes: false);
        }
    }

    private void OnSkipClicked(object? sender, RoutedEventArgs args)
    {
        if (!_skipItem.IsEnabled || !_skipItem.IsVisible) return;
        if (_updates.Snapshot.Status == ApplicationUpdateStatus.Skipped)
            _updates.ResumeCurrentRelease();
        else
        {
            _updates.SkipCurrentRelease();
            Close();
        }
    }

    private async void OnRecheckClicked(object? sender, RoutedEventArgs args)
    {
        if (_closed || !_recheckItem.IsEnabled) return;
        _checking = true;
        RefreshView(rebuildNotes: false);
        try { await _updates.CheckAsync(); }
        finally
        {
            _checking = false;
            RefreshView(rebuildNotes: false);
        }
    }

    private void OnReleaseClicked(object? sender, RoutedEventArgs args) => _updates.OpenReleasePage();
    private void OnLaterClicked(object? sender, RoutedEventArgs args) => Close();

    private void OnClosed(object? sender, EventArgs args)
    {
        _closed = true;
        if (_updates.Snapshot.Status != ApplicationUpdateStatus.Preparing)
            _downloadCancellation?.Cancel();
        _updates.Changed -= OnUpdateStateChanged;
        Closed -= OnClosed;
        _installButton.Click -= OnInstallClicked;
        _skipItem.Click -= OnSkipClicked;
        _releaseItem.Click -= OnReleaseClicked;
        _recheckItem.Click -= OnRecheckClicked;
        _laterButton.Click -= OnLaterClicked;
    }
}
