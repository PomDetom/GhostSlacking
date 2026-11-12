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

    private Control CreateContentShell()
    {
        var root = _contentShell;
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        var header = new StackPanel
        {
            Margin = new Thickness(26, 26, 26, 22),
            Children = { _pageTitle, _pageDescription }
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        Grid.SetRow(_pageHost, 1);
        root.Children.Add(_pageHost);

        Grid.SetRowSpan(_saveBar, 2);
        _saveBar.ZIndex = 1;
        root.Children.Add(_saveBar);
        root.SizeChanged += (_, _) => UpdateSaveBarLayout();
        return root;
    }

    private void OnSaveBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == BoundsProperty || args.Property == IsVisibleProperty)
        {
            UpdateSaveBarLayout();
        }
    }

    private void UpdateSaveBarLayout()
    {
        var availableWidth = Math.Max(0, _contentShell.Bounds.Width - 32);
        _saveBar.MaxWidth = availableWidth;
        var clearance = _saveBar.IsVisible ? Math.Max(60, _saveBar.Bounds.Height) + 48 : 0;
        foreach (var page in _pages.Values.OfType<ScrollViewer>())
        {
            if (page.Content is StackPanel stack)
            {
                stack.Margin = new Thickness(26, 0, 26, 30 + clearance);
            }
        }
    }

    private void BuildPages(UiLanguage language)
    {
        _pages[RevealPage] = CreatePage(
        [
            CreateSection(language, "revealAppearance",
            [
                CreateAdjustmentRow(language, AdjustmentKind.Diameter, "diameterDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.DiameterStep, "diameterStepDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.SoftEdgeWidth, "softEdgeWidthDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.BlurLevel, "blurLevelDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.Shape, "shapeDescription")
            ]),
            CreateSection(language, "peekBehavior",
            [
                CreateAdjustmentRow(language, AdjustmentKind.FrameRate, "peekFrameRateLimitDescription"),
                CreateSettingRow(language, "peekMode", "peekModeDescription", CreateResettable(_peekMode, () => SelectChoice(_peekMode, DefaultSettings.PeekTrigger))),
                CreateSettingRow(language, "peekKey", "peekKeyDescription", CreatePeekKeyEditor())
            ])
        ]);

        var experimentalNote = LocalizedText(language, "backgroundInteractionNote");
        experimentalNote.FontSize = 13;
        experimentalNote.Foreground = _secondaryBrush;
        experimentalNote.TextWrapping = TextWrapping.Wrap;
        _pages[ExperimentsPage] = CreatePage(
        [
            CreateSection(language, "backgroundInteractionSection",
            [
                CreateSettingRow(language, "backgroundInteraction", "backgroundInteractionDescription", _backgroundInteraction),
                CreateSettingRow(language, "continuousInteraction", "continuousInteractionDescription", _continuousInteraction),
                CreateHotkeyRow(language, "backgroundInteractionHotkey", "backgroundInteractionHotkeyDescription", _backgroundInteractionHotkey)
            ]),
            experimentalNote
        ]);

        _pages[HotkeysPage] = CreatePage(
        [
            CreateSection(language, "windowActions",
            [
                CreateHotkeyRow(language, "pickHotkey", "pickHotkeyDescription", _pickHotkey),
                CreateHotkeyRow(language, "windowToggleHotkey", "windowToggleHotkeyDescription", _windowToggleHotkey),
                CreateHotkeyRow(language, "diameterIncreaseHotkey", "diameterIncreaseHotkeyDescription", _diameterIncreaseHotkey),
                CreateHotkeyRow(language, "diameterDecreaseHotkey", "diameterDecreaseHotkeyDescription", _diameterDecreaseHotkey),
                CreateHotkeyRow(language, "restoreHotkey", "restoreHotkeyDescription", _restoreHotkey),
                CreateHotkeyRow(language, "restoreAllHotkey", "restoreAllHotkeyDescription", _restoreAllHotkey)
            ]),
            CreateSection(language, "applicationActions",
            [
                CreateHotkeyRow(language, "settingsHotkey", "settingsHotkeyDescription", _settingsHotkey),
                CreateHotkeyRow(language, "exitHotkey", "exitHotkeyDescription", _exitHotkey)
            ])
        ]);

        _pages[GeneralPage] = CreatePage(
        [
            CreateSection(language, "startupAndSafety",
            [
                CreateSettingRow(language, "restoreOnExit", "restoreOnExitDescription", _restoreOnExit),
                CreateSettingRow(language, "startWindows", "startWindowsDescription", _startWithWindows)
            ]),
            CreateSection(language, "languageAndDiagnostics",
            [
                CreateSettingRow(language, "theme", "themeDescription", _themeMode),
                CreateSettingRow(language, "interfaceLanguage", "languageDescription", _language),
                CreateSettingRow(language, "logLevel", "logLevelDescription", _logLevel)
            ]),
            CreateSection(language, "dataAndDiagnostics",
            [
                CreateSettingRow(language, "exportLogs", "exportLogsDescription", _exportLogsButton),
                CreateSettingRow(language, "exportSettings", "exportSettingsDescription", _exportSettingsButton)
            ])
        ]);

        _pages[NotificationsPage] = CreatePage(
        [
            CreateSection(language, "notificationSettings",
            [
                CreateAdjustmentRow(language, AdjustmentKind.NotificationRadius, "notificationRadiusDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.NotificationWidth, "notificationWidthDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.NotificationHeight, "notificationHeightDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.NotificationHorizontal, "notificationHorizontalPositionDescription"),
                CreateAdjustmentRow(language, AdjustmentKind.NotificationVertical, "notificationVerticalPositionDescription")
            ])
        ]);

        _pages[AboutPage] = CreateAboutPage(language);
    }

    private Control CreateAboutPage(UiLanguage language)
    {
        _currentVersionText.Foreground = _primaryBrush;
        _currentVersionText.FontWeight = FontWeight.SemiBold;
        _latestVersionText.Foreground = _secondaryBrush;
        _lastUpdateCheckText.Foreground = _secondaryBrush;
        _updateStatusText.Foreground = _secondaryBrush;

        var projectButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { _sourceRepositoryButton, _releasePageButton }
        };

        var identity = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                new Image { Source = AppIcon.TitleBarImage, Width = 56, Height = 56 },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "GhostSlacking",
                            FontSize = 22,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = _primaryBrush
                        },
                        LocalizedText(language, "aboutProductDescription"),
                        projectButtons
                    }
                }
            }
        };

        var updateButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                _checkUpdatesButton,
                _viewUpdateButton
            }
        };
        var updatePanel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _currentVersionText,
                _latestVersionText,
                _lastUpdateCheckText,
                _updateStatusText,
                CreateSettingRow(language, "updateChannel", "updateChannelDescription", _updateChannel),
                _updateProgress,
                updateButtons
            }
        };

        return CreatePage(
        [
            CreateSection(language, "aboutGhostSlacking", [WrapCard(identity)]),
            CreateSection(language, "softwareUpdate", [WrapCard(updatePanel)])
        ]);
    }

    private Border WrapCard(Control child) => new()
    {
        Background = _cardBrush,
        BorderBrush = _fieldBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18, 15),
        Child = child
    };

    private Control CreatePage(IEnumerable<Control> sections)
    {
        var stack = new StackPanel
        {
            Spacing = 24,
            Margin = new Thickness(26, 0, 26, 30)
        };
        foreach (var section in sections)
        {
            stack.Children.Add(section);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = stack
        };
    }

    private Control CreateSection(UiLanguage language, string titleKey, IEnumerable<Control> rows)
    {
        var root = new StackPanel { Spacing = 9 };
        var heading = LocalizedText(language, titleKey);
        heading.FontSize = 12;
        heading.FontWeight = FontWeight.Medium;
        heading.Foreground = _accentBrush;
        heading.Margin = new Thickness(4, 0, 0, 0);
        root.Children.Add(heading);

        var card = new StackPanel { Spacing = 8 };
        foreach (var row in rows)
        {
            card.Children.Add(row);
        }
        root.Children.Add(card);
        return root;
    }

    private Control CreateSettingRow(
        UiLanguage language,
        string titleKey,
        string descriptionKey,
        Control editor,
        bool hotkey = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var editorColumn = new ColumnDefinition(new GridLength(hotkey ? 280 : SettingEditorColumnWidth(language)));
        grid.ColumnDefinitions.Add(editorColumn);
        _editorColumns.Add((editorColumn, hotkey));

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        var title = LocalizedText(language, titleKey);
        title.FontSize = 14;
        title.FontWeight = FontWeight.Medium;
        title.Foreground = _primaryBrush;
        var description = LocalizedText(language, descriptionKey);
        description.FontSize = 12;
        description.Foreground = _secondaryBrush;
        description.TextWrapping = TextWrapping.Wrap;
        copy.Children.Add(title);
        copy.Children.Add(description);
        grid.Children.Add(copy);

        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Center;
        editor.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        return new Border
        {
            Background = _cardBrush,
            BorderBrush = _fieldBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14),
            Child = grid
        };
    }

    private static NavigationViewItem CreateNavigationItem(string tag, Symbol symbol) => new()
    {
        Tag = tag,
        FontSize = 14,
        CornerRadius = new CornerRadius(9),
        IconSource = new SymbolIconSource { Symbol = symbol, FontSize = 20 }
    };

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs args)
    {
        CloseAdjustment();
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string page || !_pages.ContainsKey(page))
        {
            return;
        }

        _selectedPage = page;
        _pageHost.Content = _pages[page];
        UpdatePageHeader(CurrentLanguage);
        CancelKeyCapture();
    }

    private void UpdatePageHeader(UiLanguage language)
    {
        var (title, description) = _selectedPage switch
        {
            HotkeysPage => ("hotkeySettings", "hotkeySettingsDescription"),
            GeneralPage => ("generalSettings", "generalSettingsDescription"),
            NotificationsPage => ("notificationsPage", "notificationsPageDescription"),
            ExperimentsPage => ("experimentalSettings", "experimentalSettingsDescription"),
            AboutPage => ("about", "aboutDescription"),
            _ => ("revealSettings", "revealSettingsDescription")
        };
        _pageTitle.Text = UiText.Text(language, title);
        _pageDescription.Text = UiText.Text(language, description);
    }
}
