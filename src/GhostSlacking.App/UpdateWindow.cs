using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal enum ReleaseNotesBlockKind
{
    Heading,
    Bullet,
    Paragraph
}

internal sealed record ReleaseNotesBlock(ReleaseNotesBlockKind Kind, string Text);

internal static class ReleaseNotesFormatter
{
    public static IReadOnlyList<ReleaseNotesBlock> Parse(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return [];
        }

        var blocks = new List<ReleaseNotesBlock>();
        var paragraph = new List<string>();
        foreach (var rawLine in notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddIfNotEmpty(ReleaseNotesBlockKind.Heading, line[4..]);
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddIfNotEmpty(ReleaseNotesBlockKind.Bullet, line[2..]);
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph();
        return blocks;

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            AddIfNotEmpty(ReleaseNotesBlockKind.Paragraph, string.Join(' ', paragraph));
            paragraph.Clear();
        }

        void AddIfNotEmpty(ReleaseNotesBlockKind kind, string value)
        {
            value = value.Trim();
            if (value.Length > 0)
            {
                blocks.Add(new ReleaseNotesBlock(kind, value));
            }
        }
    }
}

internal sealed class UpdateWindow : AppWindow
{
    private readonly IApplicationUpdateManager _updates;
    private readonly SolidColorBrush _canvasBrush = new();
    private readonly SolidColorBrush _surfaceBrush = new();
    private readonly SolidColorBrush _primaryBrush = new();
    private readonly SolidColorBrush _secondaryBrush = new();
    private readonly TextBlock _versionText;
    private readonly TextBlock _statusText;
    private readonly TextBlock _historyWarningText;
    private readonly StackPanel _notesPanel;
    private readonly ProgressBar _progress;
    private readonly Button _installButton;
    private readonly Button _skipButton;
    private readonly Button _releaseButton;
    private readonly Button _laterButton;
    private CancellationTokenSource? _downloadCancellation;
    private UpdateRelease? _renderedRelease;
    private UiLanguage _language;
    private bool _closed;

    public UpdateWindow(IApplicationUpdateManager updates, UiLanguage language)
    {
        _updates = updates;
        _language = language;
        Title = UiText.Text(language, "updateWindowTitle");
        Width = 760;
        Height = 620;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.TitleBarImage;

        _versionText = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold };
        _statusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _historyWarningText = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _notesPanel = new StackPanel { Spacing = 8 };
        _progress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false };
        _installButton = new Button { MinWidth = 130 };
        _skipButton = new Button { MinWidth = 110 };
        _releaseButton = new Button { MinWidth = 120 };
        _laterButton = new Button { MinWidth = 90 };

        _installButton.Click += OnInstallClicked;
        _skipButton.Click += OnSkipClicked;
        _releaseButton.Click += OnReleaseClicked;
        _laterButton.Click += OnLaterClicked;
        _updates.Changed += OnUpdateStateChanged;
        Closed += OnClosed;
        ActualThemeVariantChanged += (_, _) => ApplyPalette();

        Content = BuildContent();
        ApplyPalette();
        RefreshView(rebuildNotes: true);
    }

    public void RefreshLanguage(UiLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        Title = UiText.Text(language, "updateWindowTitle");
        RefreshView(rebuildNotes: true);
    }

    private Control BuildContent()
    {
        var header = new Border
        {
            Background = _surfaceBrush,
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 6,
                Children = { _versionText, _statusText, _historyWarningText, _progress }
            }
        };

        var notes = new ScrollViewer
        {
            Padding = new Thickness(24, 18),
            Content = _notesPanel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { _releaseButton, _skipButton, _laterButton, _installButton }
        };
        var footer = new Border
        {
            Background = _surfaceBrush,
            Padding = new Thickness(24, 16),
            Child = actions
        };

        var root = new Grid { Background = _canvasBrush };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.Children.Add(header);
        Grid.SetRow(notes, 1);
        root.Children.Add(notes);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void ApplyPalette()
    {
        var dark = AppTheme.IsDark(ActualThemeVariant);
        _canvasBrush.Color = Color.Parse(dark ? "#090909" : "#F3F6F8");
        _surfaceBrush.Color = dark ? AppTheme.DarkFieldColor : Colors.White;
        _primaryBrush.Color = Color.Parse(dark ? "#F5F5F5" : "#172033");
        _secondaryBrush.Color = Color.Parse(dark ? "#A3A3A3" : "#667085");
        _versionText.Foreground = _primaryBrush;
        _statusText.Foreground = _secondaryBrush;
        _historyWarningText.Foreground = new SolidColorBrush(Color.Parse(dark ? "#FBBF24" : "#9A6700"));
        RefreshNotesColors(_notesPanel);
    }

    private void RefreshNotesColors(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is TextBlock text)
            {
                text.Foreground = text.Tag is ReleaseNotesBlockKind.Heading ? _primaryBrush : _secondaryBrush;
            }
            else if (child is Panel nested)
            {
                RefreshNotesColors(nested);
            }
        }
    }

    private void OnUpdateStateChanged(object? sender, EventArgs args)
    {
        if (_closed)
        {
            return;
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RefreshView(rebuildNotes: false);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshView(rebuildNotes: false));
        }
    }

    private void RefreshView(bool rebuildNotes)
    {
        if (_closed)
        {
            return;
        }

        var snapshot = _updates.Snapshot;
        var release = snapshot.Release;
        _versionText.Text = release is null
            ? UiText.Text(_language, "updateWindowNoRelease")
            : string.Format(UiText.Text(_language, "updateWindowVersion"), snapshot.CurrentVersion, release.VersionText);
        _statusText.Text = StatusText(snapshot);
        _historyWarningText.Text = UiText.Text(_language, "releaseHistoryIncomplete");
        _historyWarningText.IsVisible = release?.ReleaseHistoryIncomplete == true;
        _progress.IsVisible = snapshot.Status == ApplicationUpdateStatus.Downloading;
        _progress.Value = snapshot.ProgressPercent ?? 0;

        var busy = snapshot.Status is ApplicationUpdateStatus.Checking or
            ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying;
        var updateKnown = release is not null && release.Version > ReleaseVersion.Parse(snapshot.CurrentVersion);
        _installButton.IsVisible = updateKnown;
        _installButton.IsEnabled = updateKnown &&
            (_downloadCancellation is not null || (!busy && _updates.CanInstallUpdates));
        _installButton.Content = _downloadCancellation is not null
            ? UiText.Text(_language, "cancelDownload")
            : UiText.Text(_language, "downloadAndInstall");
        _skipButton.IsVisible = updateKnown;
        _skipButton.IsEnabled = !busy;
        _skipButton.Content = snapshot.Status == ApplicationUpdateStatus.Skipped
            ? UiText.Text(_language, "resumeReminders")
            : UiText.Text(_language, "skipVersion");
        _releaseButton.Content = UiText.Text(_language, "viewRelease");
        _releaseButton.IsEnabled = release is not null;
        _laterButton.Content = UiText.Text(_language, "later");

        if (rebuildNotes || !ReferenceEquals(_renderedRelease, release))
        {
            RebuildNotes(release);
            _renderedRelease = release;
        }
    }

    private string StatusText(ApplicationUpdateSnapshot snapshot)
    {
        if (!_updates.CanInstallUpdates && snapshot.Release is not null)
        {
            return UiText.Text(_language, "portableUpdateDescription");
        }

        return snapshot.Status switch
        {
            ApplicationUpdateStatus.Checking => UiText.Text(_language, "checkingUpdates"),
            ApplicationUpdateStatus.UpToDate => UiText.Text(_language, "upToDate"),
            ApplicationUpdateStatus.Available => string.Format(UiText.Text(_language, "updateAvailableDescription"), snapshot.Release?.VersionText),
            ApplicationUpdateStatus.Skipped => string.Format(UiText.Text(_language, "updateSkippedDescription"), snapshot.Release?.VersionText),
            ApplicationUpdateStatus.Downloading => string.Format(UiText.Text(_language, "downloadingUpdate"), snapshot.ProgressPercent ?? 0),
            ApplicationUpdateStatus.Verifying => UiText.Text(_language, "verifyingUpdate"),
            ApplicationUpdateStatus.Ready => UiText.Text(_language, "startingInstaller"),
            ApplicationUpdateStatus.Error => UiText.Text(_language, "updateFailed"),
            _ => UiText.Text(_language, "updateNotChecked")
        };
    }

    private void RebuildNotes(UpdateRelease? release)
    {
        _notesPanel.Children.Clear();
        if (release is null)
        {
            AddText(UiText.Text(_language, "noReleaseNotes"), ReleaseNotesBlockKind.Paragraph);
            return;
        }

        foreach (var entry in release.ReleaseNotes.OrderByDescending(item => item.Version))
        {
            AddText(string.Format(UiText.Text(_language, "releaseNotesVersion"), entry.VersionText), ReleaseNotesBlockKind.Heading, 18);
            var blocks = ReleaseNotesFormatter.Parse(entry.Notes.For(_language));
            if (blocks.Count == 0)
            {
                AddText(UiText.Text(_language, "noReleaseNotes"), ReleaseNotesBlockKind.Paragraph);
                continue;
            }

            foreach (var block in blocks)
            {
                AddText(block.Kind == ReleaseNotesBlockKind.Bullet ? $"• {block.Text}" : block.Text, block.Kind);
            }
        }

        ApplyPalette();
    }

    private void AddText(string text, ReleaseNotesBlockKind kind, double? fontSize = null)
    {
        _notesPanel.Children.Add(new TextBlock
        {
            Text = text,
            Tag = kind,
            FontSize = fontSize ?? (kind == ReleaseNotesBlockKind.Heading ? 15 : 14),
            FontWeight = kind == ReleaseNotesBlockKind.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = kind == ReleaseNotesBlockKind.Heading ? new Thickness(0, 10, 0, 2) : default
        });
    }

    private async void OnInstallClicked(object? sender, RoutedEventArgs args)
    {
        if (_downloadCancellation is not null)
        {
            _downloadCancellation.Cancel();
            return;
        }

        var release = _updates.Snapshot.Release;
        if (release is null || !_updates.CanInstallUpdates)
        {
            return;
        }

        _downloadCancellation = new CancellationTokenSource();
        RefreshView(rebuildNotes: false);
        try
        {
            var installerPath = await _updates.DownloadInstallerAsync(_downloadCancellation.Token);
            if (installerPath is not null)
            {
                _updates.BeginAutomaticInstall(installerPath);
            }
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            RefreshView(rebuildNotes: false);
        }
    }

    private void OnSkipClicked(object? sender, RoutedEventArgs args)
    {
        if (_updates.Snapshot.Status == ApplicationUpdateStatus.Skipped)
        {
            _updates.ResumeCurrentRelease();
        }
        else
        {
            _updates.SkipCurrentRelease();
            Close();
        }
    }

    private void OnReleaseClicked(object? sender, RoutedEventArgs args) => _updates.OpenReleasePage();

    private void OnLaterClicked(object? sender, RoutedEventArgs args) => Close();

    private void OnClosed(object? sender, EventArgs args)
    {
        _closed = true;
        _downloadCancellation?.Cancel();
        _updates.Changed -= OnUpdateStateChanged;
        Closed -= OnClosed;
        _installButton.Click -= OnInstallClicked;
        _skipButton.Click -= OnSkipClicked;
        _releaseButton.Click -= OnReleaseClicked;
        _laterButton.Click -= OnLaterClicked;
    }
}
