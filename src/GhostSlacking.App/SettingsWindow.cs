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

internal sealed class SettingsWindow : AppWindow
{
    private const string RevealPage = "reveal";
    private const string HotkeysPage = "hotkeys";
    private const string GeneralPage = "general";
    private const string AboutPage = "about";
    private static readonly TimeSpan SavedStatusDuration = TimeSpan.FromMilliseconds(2500);
    private static readonly AppSettings DefaultSettings = new();
    private readonly SolidColorBrush _canvasBrush = new();
    private readonly SolidColorBrush _panelBrush = new();
    private readonly SolidColorBrush _selectionBrush = new();
    private readonly SolidColorBrush _cardBrush = new();
    private readonly SolidColorBrush _fieldBrush = new();
    private readonly SolidColorBrush _fieldBorderBrush = new();
    private readonly SolidColorBrush _primaryBrush = new();
    private readonly SolidColorBrush _secondaryBrush = new();
    private readonly SolidColorBrush _accentBrush = new();
    private readonly SolidColorBrush _errorBrush = new();

    private readonly Func<AppSettings, bool> _save;
    private readonly Func<Stream, Task<DataExportResult>> _exportLogs;
    private readonly Func<AppSettings, Stream, Task<DataExportResult>> _exportSettings;
    private readonly Action<UiThemeMode> _applyTheme;
    private readonly Func<System.Drawing.Point> _getCursorPosition;
    private readonly IApplicationUpdateManager? _updates;
    private readonly Action? _openUpdateWindow;
    private readonly List<(TextBlock Text, string Key)> _localizedText = [];
    private readonly List<(Button Button, string Key)> _localizedButtons = [];
    private readonly List<(ColumnDefinition Column, bool Hotkey)> _editorColumns = [];
    private readonly Dictionary<string, Control> _pages = [];
    private readonly SettingsEditState _editState;
    private readonly NavigationView _navigation;
    private readonly NavigationViewItem _revealItem;
    private readonly NavigationViewItem _hotkeysItem;
    private readonly NavigationViewItem _generalItem;
    private readonly NavigationViewItem _aboutItem;
    private readonly TextBlock _pageTitle;
    private readonly TextBlock _pageDescription;
    private readonly ContentControl _pageHost;
    private readonly SettingsSaveBar _saveBar;
    private readonly Grid _contentShell = new();
    private readonly NumberBox _diameter;
    private readonly NumberBox _diameterStep;
    private readonly NumberBox _softEdgeWidth;
    private readonly ComboBox _blurLevel;
    private readonly ComboBox _shape;
    private readonly ComboBox _peekFrameRateLimit;
    private readonly ComboBox _peekMode;
    private readonly Button _peekKeyButton;
    private readonly ComboBox _language;
    private readonly ComboBox _themeMode;
    private readonly NumberBox _notificationRadius;
    private readonly NumberBox _notificationWidth;
    private readonly NumberBox _notificationHeight;
    private readonly NumberBox _notificationHorizontalPosition;
    private readonly NumberBox _notificationVerticalPosition;
    private readonly Button _notificationPreviewButton;
    private readonly Button _notificationPreviewCloseButton;
    private readonly ComboBox _logLevel;
    private readonly ComboBox _updateChannel;
    private readonly Button _exportLogsButton;
    private readonly Button _exportSettingsButton;
    private readonly TextBlock _currentVersionText;
    private readonly TextBlock _latestVersionText;
    private readonly TextBlock _lastUpdateCheckText;
    private readonly TextBlock _updateStatusText;
    private readonly ProgressBar _updateProgress;
    private readonly Button _checkUpdatesButton;
    private readonly Button _viewUpdateButton;
    private readonly Button _sourceRepositoryButton;
    private readonly Button _releasePageButton;
    private readonly ToggleSwitch _restoreOnExit;
    private readonly ToggleSwitch _startWithWindows;
    private readonly HotkeyEditor _pickHotkey;
    private readonly HotkeyEditor _windowToggleHotkey;
    private readonly HotkeyEditor _restoreHotkey;
    private readonly HotkeyEditor _restoreAllHotkey;
    private readonly HotkeyEditor _settingsHotkey;
    private readonly HotkeyEditor _exitHotkey;
    private readonly HotkeyEditor _diameterIncreaseHotkey;
    private readonly HotkeyEditor _diameterDecreaseHotkey;
    private readonly DispatcherTimer _savedStatusTimer;
    private HotkeyEditor? _activeHotkeyEditor;
    private int _peekVirtualKey;
    private bool _capturingPeekKey;
    private bool _updatingLanguage;
    private bool _initializing = true;
    private string _selectedPage = RevealPage;
    private string? _exportStatusKey;
    private NotificationWindow? _notificationPreview;
    private readonly DispatcherTimer _notificationPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private PixelPoint _notificationPreviewScreenPoint;
    private readonly GhostSlacking.Platform.Win32WindowApi _previewWindowApi = new();

    public SettingsWindow(
        AppSettings settings,
        Func<AppSettings, bool> save,
        Action<UiThemeMode>? applyTheme = null,
        Func<Stream, Task<DataExportResult>>? exportLogs = null,
        Func<AppSettings, Stream, Task<DataExportResult>>? exportSettings = null,
        IApplicationUpdateManager? updates = null,
        Action? openUpdateWindow = null,
        Func<System.Drawing.Point>? getCursorPosition = null,
        string? initialPage = null)
    {
        _editState = new SettingsEditState(settings);
        _save = save;
        _applyTheme = applyTheme ?? AppTheme.Apply;
        _getCursorPosition = getCursorPosition ?? (() => System.Drawing.Point.Empty);
        _exportLogs = exportLogs ?? (_ => Task.FromResult(DataExportResult.Failure()));
        _exportSettings = exportSettings ?? ((_, _) => Task.FromResult(DataExportResult.Failure()));
        _updates = updates;
        _openUpdateWindow = openUpdateWindow;
        _peekVirtualKey = settings.PeekVirtualKey;

        ApplyThemePalette();

        Title = UiText.Text(settings.Language, "title");
        Width = 840;
        Height = 640;
        MinWidth = 840;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = _canvasBrush;
        CornerRadius = new CornerRadius(12);
        Icon = AppIcon.TitleBarImage;

        _diameter = CreateNumberBox(settings.RevealDiameterPx, 64, 800, 8);
        _diameterStep = CreateNumberBox(settings.RevealDiameterStepPx, 8, 256, 8);
        _softEdgeWidth = CreateNumberBox(settings.RevealSoftEdgeWidthPx, 0, 128, 4);
        _blurLevel = CreateComboBox();
        _shape = CreateComboBox();
        _peekFrameRateLimit = CreateComboBox();
        _peekMode = CreateComboBox();
        _language = CreateComboBox();
        _themeMode = CreateComboBox();
        var hud = _editState.SavedSettings.NotificationHud;
        _notificationRadius = CreateNumberBox(hud.CornerRadius, 0, 48, 1);
        _notificationWidth = CreateNumberBox(hud.Width, 100, 300, 1);
        _notificationHeight = CreateNumberBox(hud.Height, 32, 96, 1);
        _notificationHorizontalPosition = CreateNumberBox(hud.HorizontalPosition, 0, 100, 1);
        _notificationVerticalPosition = CreateNumberBox(hud.VerticalPosition, 0, 100, 1);
        _notificationPreviewButton = CreateTextButton(settings.Language, "previewNotification");
        _notificationPreviewCloseButton = CreateTextButton(settings.Language, "closeNotificationPreview");
        _notificationPreviewCloseButton.IsVisible = false;
        _notificationPreviewButton.Click += (_, _) => BeginNotificationPreview();
        _notificationPreviewCloseButton.Click += (_, _) => CancelNotificationPreview();
        _notificationPreviewTimer.Tick += OnNotificationPreviewTimerTick;
        _logLevel = CreateComboBox();
        _updateChannel = CreateComboBox();
        _exportLogsButton = CreateTextButton(settings.Language, "exportLogs");
        _exportSettingsButton = CreateTextButton(settings.Language, "exportSettings");
        _exportLogsButton.Click += OnExportLogsClicked;
        _exportSettingsButton.Click += OnExportSettingsClicked;
        _currentVersionText = new TextBlock();
        _latestVersionText = new TextBlock();
        _lastUpdateCheckText = new TextBlock();
        _updateStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false };
        _checkUpdatesButton = new Button { MinWidth = 120 };
        _viewUpdateButton = new Button { MinWidth = 130, IsVisible = false };
        _sourceRepositoryButton = CreateTextButton(settings.Language, "sourceRepository");
        _releasePageButton = CreateTextButton(settings.Language, "releasePage");
        _checkUpdatesButton.Click += OnCheckUpdatesClicked;
        _viewUpdateButton.Click += OnViewUpdateClicked;
        _sourceRepositoryButton.Click += OnSourceRepositoryClicked;
        _releasePageButton.Click += OnReleasePageClicked;
        _restoreOnExit = new ToggleSwitch { IsChecked = settings.RestoreOnExit };
        _startWithWindows = new ToggleSwitch { IsChecked = settings.StartWithWindows };
        _peekKeyButton = CreateCaptureButton();

        _pickHotkey = CreateHotkeyEditor(settings.PickHotkey, DefaultSettings.PickHotkey);
        _windowToggleHotkey = CreateHotkeyEditor(settings.WindowToggleHotkey, DefaultSettings.WindowToggleHotkey);
        _restoreHotkey = CreateHotkeyEditor(settings.RestoreHotkey, DefaultSettings.RestoreHotkey);
        _restoreAllHotkey = CreateHotkeyEditor(settings.RestoreAllHotkey, DefaultSettings.RestoreAllHotkey);
        _settingsHotkey = CreateHotkeyEditor(settings.SettingsHotkey, DefaultSettings.SettingsHotkey);
        _exitHotkey = CreateHotkeyEditor(settings.ExitHotkey, DefaultSettings.ExitHotkey);
        _diameterIncreaseHotkey = CreateHotkeyEditor(
            settings.RevealDiameterIncreaseHotkey,
            DefaultSettings.RevealDiameterIncreaseHotkey);
        _diameterDecreaseHotkey = CreateHotkeyEditor(
            settings.RevealDiameterDecreaseHotkey,
            DefaultSettings.RevealDiameterDecreaseHotkey);

        _pageTitle = new TextBlock
        {
            FontSize = 26,
            FontWeight = FontWeight.Medium,
            Foreground = _primaryBrush
        };
        _pageDescription = new TextBlock
        {
            FontSize = 13,
            Foreground = _secondaryBrush,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _pageHost = new ContentControl();
        _saveBar = new SettingsSaveBar(_cardBrush, _fieldBorderBrush, _primaryBrush);
        _saveBar.SetLanguage(settings.Language);
        _saveBar.CancelRequested += (_, _) => CancelSettingsEdits();
        _saveBar.SaveRequested += (_, _) => OnSaveClicked(null, new RoutedEventArgs());
        _saveBar.PropertyChanged += OnSaveBarPropertyChanged;
        _savedStatusTimer = new DispatcherTimer { Interval = SavedStatusDuration };
        _savedStatusTimer.Tick += OnSavedStatusTimerTick;

        _revealItem = CreateNavigationItem(RevealPage, Symbol.View);
        _hotkeysItem = CreateNavigationItem(HotkeysPage, Symbol.Keyboard);
        _generalItem = CreateNavigationItem(GeneralPage, Symbol.Settings);
        _aboutItem = CreateNavigationItem(AboutPage, Symbol.Help);
        _navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneOpen = false,
            IsBackButtonVisible = false,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = true,
            AlwaysShowHeader = false,
            CompactPaneLength = 56,
            OpenPaneLength = 184,
            PaneTitle = UiText.Text(settings.Language, "settingsMenu"),
            Background = _canvasBrush
        };
        _navigation.Resources["NavigationViewContentGridCornerRadius"] = new CornerRadius(12);
        _navigation.Resources["NavigationViewContentGridBorderThickness"] = new Thickness(1);
        _navigation.Resources["NavigationViewContentMargin"] = new Thickness(0, 0, 10d / 3, 10d / 3);
        _navigation.Resources["NavigationViewContentBackground"] = _panelBrush;
        _navigation.Resources["NavigationViewContentGridBorderBrush"] = _fieldBorderBrush;
        _navigation.Resources["NavigationViewDefaultPaneBackground"] = _canvasBrush;
        _navigation.Resources["NavigationViewItemBackgroundSelected"] = _selectionBrush;
        _navigation.Resources["NavigationViewItemBackgroundSelectedPointerOver"] = _selectionBrush;
        _navigation.Resources["NavigationViewItemForegroundSelected"] = _accentBrush;
        _navigation.Resources["NavigationViewSelectionIndicatorForeground"] = Brushes.Transparent;
        _navigation.Resources["NavigationViewItemOnLeftIconBoxHeight"] = 20d;
        _navigation.Resources["NavigationViewItemOnLeftMinHeight"] = 38d;
        _navigation.Resources["PaneToggleButtonWidth"] = 48d;
        _navigation.MenuItems.Add(_revealItem);
        _navigation.MenuItems.Add(_hotkeysItem);
        _navigation.MenuItems.Add(_generalItem);
        _navigation.FooterMenuItems.Add(_aboutItem);
        _navigation.SelectionChanged += OnNavigationSelectionChanged;

        PopulateChoices(settings);
        BuildPages(settings.Language);
        _navigation.Content = CreateContentShell();
        _pageHost.Content = _pages[RevealPage];
        _navigation.SelectedItem = _revealItem;
        Content = _navigation;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        Closed += OnClosed;
        if (_updates is not null)
        {
            _updates.Changed += OnUpdateStateChanged;
        }
        _language.SelectionChanged += (_, _) =>
        {
            if (!_initializing && !_updatingLanguage)
            {
                ApplyLanguage(CurrentLanguage);
                OnSettingsEdited();
            }
        };

        ApplyLanguage(settings.Language);
        RefreshUpdateView();
        UpdateHotkeyConflicts();
        SubscribeToSettingChanges();
        _initializing = false;
        if (string.Equals(initialPage, AboutPage, StringComparison.Ordinal))
        {
            NavigateToAbout();
        }
    }

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
                CreateSettingRow(language, "diameter", "diameterDescription", CreateResettable(_diameter, () => _diameter.Value = DefaultSettings.RevealDiameterPx)),
                CreateSettingRow(language, "diameterStep", "diameterStepDescription", CreateResettable(_diameterStep, () => _diameterStep.Value = DefaultSettings.RevealDiameterStepPx)),
                CreateSettingRow(language, "softEdgeWidth", "softEdgeWidthDescription", CreateResettable(_softEdgeWidth, () => _softEdgeWidth.Value = DefaultSettings.RevealSoftEdgeWidthPx)),
                CreateSettingRow(language, "blurLevel", "blurLevelDescription", CreateResettable(_blurLevel, () => SelectChoice(_blurLevel, DefaultSettings.RevealBlurLevel))),
                CreateSettingRow(language, "shape", "shapeDescription", CreateResettable(_shape, () => SelectChoice(_shape, DefaultSettings.RevealShape)))
            ]),
            CreateSection(language, "peekBehavior",
            [
                CreateSettingRow(language, "peekFrameRateLimit", "peekFrameRateLimitDescription", CreateResettable(_peekFrameRateLimit, () => SelectChoice(_peekFrameRateLimit, DefaultSettings.PeekFrameRateLimit))),
                CreateSettingRow(language, "peekMode", "peekModeDescription", CreateResettable(_peekMode, () => SelectChoice(_peekMode, DefaultSettings.PeekTrigger))),
                CreateSettingRow(language, "peekKey", "peekKeyDescription", CreatePeekKeyEditor())
            ])
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
            CreateSection(language, "notificationSettings",
            [
                CreateSettingRow(language, "notificationRadius", "notificationRadiusDescription", _notificationRadius),
                CreateSettingRow(language, "notificationWidth", "notificationWidthDescription", _notificationWidth),
                CreateSettingRow(language, "notificationHeight", "notificationHeightDescription", _notificationHeight),
                CreateSettingRow(language, "notificationHorizontalPosition", "notificationHorizontalPositionDescription", _notificationHorizontalPosition),
                CreateSettingRow(language, "notificationVerticalPosition", "notificationVerticalPositionDescription", _notificationVerticalPosition),
                CreateSettingRow(language, "notificationPreview", "notificationPreviewDescription", CreateNotificationPreviewEditor())
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
        var editorColumn = new ColumnDefinition(new GridLength(hotkey ? 280 : 240));
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

    private Control CreateHotkeyRow(
        UiLanguage language,
        string titleKey,
        string descriptionKey,
        HotkeyEditor editor) => CreateSettingRow(language, titleKey, descriptionKey, editor.Host, hotkey: true);

    private Control CreateResettable(Control editor, Action reset)
    {
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(editor);

        var resetButton = CreateIconButton(Symbol.Refresh, "resetSetting");
        resetButton.Margin = new Thickness(8, 0, 0, 0);
        resetButton.Click += (_, _) => reset();
        Grid.SetColumn(resetButton, 1);
        grid.Children.Add(resetButton);
        return grid;
    }

    private Control CreatePeekKeyEditor()
    {
        _peekKeyButton.Click += (_, _) => BeginPeekKeyCapture();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(_peekKeyButton);
        var reset = CreateIconButton(Symbol.Refresh, "resetHotkey");
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Click += (_, _) =>
        {
            CancelKeyCapture();
            _peekVirtualKey = DefaultSettings.PeekVirtualKey;
            RefreshKeyDisplays();
            OnSettingsEdited();
        };
        Grid.SetColumn(reset, 1);
        grid.Children.Add(reset);
        return grid;
    }

    private Control CreateNotificationPreviewEditor() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { _notificationPreviewButton, _notificationPreviewCloseButton }
    };

    private NotificationHudSettings GetNotificationHudSettings() => new NotificationHudSettings
    {
        CornerRadius = _notificationRadius.Value,
        Width = _notificationWidth.Value,
        Height = _notificationHeight.Value,
        HorizontalPosition = _notificationHorizontalPosition.Value,
        VerticalPosition = _notificationVerticalPosition.Value
    }.Normalize();

    private void BeginNotificationPreview()
    {
        if (_notificationPreview is not null) return;
        var cursor = _getCursorPosition();
        _notificationPreviewScreenPoint = new PixelPoint(cursor.X, cursor.Y);
        _notificationPreview = new NotificationWindow(preview: true);
        UpdateNotificationPreview();
        _notificationPreview.Show();
        UpdateNotificationPreview();
        var result = _previewWindowApi.SetNotificationClickThrough(_notificationPreview.TryGetPlatformHandle()?.Handle ?? 0, true);
        if (!result.Success)
        {
            CancelNotificationPreview();
            ShowStatus(SettingsStatus.Error, UiText.Text(CurrentLanguage, "notificationPreviewFailed"), string.Empty, InfoBarSeverity.Error);
            return;
        }
        _notificationPreviewButton.IsVisible = false;
        _notificationPreviewCloseButton.IsVisible = true;
        _notificationPreviewTimer.Start();
    }

    private void OnNotificationPreviewTimerTick(object? sender, EventArgs args) => UpdateNotificationPreview(updateContent: false);

    private void UpdateNotificationPreview(bool updateContent = true)
    {
        if (_notificationPreview is null) return;
        var hud = GetNotificationHudSettings();
        var screen = _notificationPreview.Screens.ScreenFromPoint(_notificationPreviewScreenPoint) ?? _notificationPreview.Screens.Primary;
        if (screen is not null)
        {
            var geometry = NotificationLayout.Calculate(screen.WorkingArea, screen.Scaling, hud);
            _notificationPreview.Configure(hud, geometry);
            if (_notificationPreview.Position != geometry.Position) _notificationPreview.Position = geometry.Position;
        }
        if (updateContent) _notificationPreview.Update(UiText.Text(CurrentLanguage, "notificationPreviewMessage"), UserNotificationSeverity.Info, false);
    }

    private void CancelNotificationPreview()
    {
        _notificationPreviewTimer.Stop();
        var preview = _notificationPreview;
        _notificationPreview = null;
        preview?.Close();
        _notificationPreviewButton.IsVisible = true;
        _notificationPreviewCloseButton.IsVisible = false;
    }

    private HotkeyEditor CreateHotkeyEditor(HotkeyBinding binding, HotkeyBinding defaultBinding)
    {
        var display = CreateCaptureButton();
        var clear = CreateIconButton(Symbol.Clear, "clearHotkey");
        var reset = CreateIconButton(Symbol.Refresh, "resetHotkey");
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        host.Children.Add(display);
        Grid.SetColumn(clear, 1);
        host.Children.Add(clear);
        Grid.SetColumn(reset, 2);
        host.Children.Add(reset);

        var editor = new HotkeyEditor(binding, defaultBinding, display, clear, host);
        clear.Margin = new Thickness(8, 0, 0, 0);
        reset.Margin = new Thickness(8, 0, 0, 0);
        display.Click += (_, _) => BeginHotkeyCapture(editor);
        clear.Click += (_, _) =>
        {
            CancelKeyCapture();
            editor.Binding = HotkeyBinding.Disabled;
            RefreshKeyDisplays();
            UpdateHotkeyConflicts();
            OnSettingsEdited();
        };
        reset.Click += (_, _) =>
        {
            CancelKeyCapture();
            editor.Binding = editor.DefaultBinding;
            RefreshKeyDisplays();
            UpdateHotkeyConflicts();
            OnSettingsEdited();
        };
        return editor;
    }

    private Button CreateCaptureButton() => new()
    {
        MinHeight = 36,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(12, 6),
        Background = _fieldBrush,
        BorderBrush = _fieldBorderBrush,
        BorderThickness = new Thickness(1)
    };

    private Button CreateIconButton(Symbol symbol, string tooltipKey)
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(8),
            Content = new SymbolIcon { Symbol = symbol, FontSize = 16 }
        };
        button.Tag = tooltipKey;
        ToolTip.SetTip(button, UiText.Text(CurrentLanguage, tooltipKey));
        return button;
    }

    private Button CreateTextButton(UiLanguage language, string key)
    {
        var button = new Button
        {
            Content = UiText.Text(language, key),
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _localizedButtons.Add((button, key));
        return button;
    }

    private TextBlock LocalizedText(UiLanguage language, string key)
    {
        var text = new TextBlock { Text = UiText.Text(language, key) };
        _localizedText.Add((text, key));
        return text;
    }

    private static NavigationViewItem CreateNavigationItem(string tag, Symbol symbol) => new()
    {
        Tag = tag,
        FontSize = 14,
        CornerRadius = new CornerRadius(9),
        IconSource = new SymbolIconSource { Symbol = symbol, FontSize = 20 }
    };

    private static NumberBox CreateNumberBox(double value, double minimum, double maximum, double step) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = step,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox CreateComboBox() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinHeight = 36
    };

    private void PopulateChoices(AppSettings settings)
    {
        PopulateLocalizedChoices(
            settings.Language,
            settings.RevealBlurLevel,
            settings.RevealShape,
            settings.PeekTrigger,
            settings.PeekFrameRateLimit);
        PopulateThemeChoices(settings.Language, settings.ThemeMode);
        _language.ItemsSource = new[]
        {
            new Choice<UiLanguage>(UiLanguage.Chinese, "中文"),
            new Choice<UiLanguage>(UiLanguage.English, "English")
        };
        SelectChoice(_language, settings.Language);
        _logLevel.ItemsSource = Enum.GetValues<LogLevel>()
            .Select(level => new Choice<LogLevel>(level, level.ToString()))
            .ToArray();
        SelectChoice(_logLevel, settings.MinimumLogLevel);
        PopulateUpdateChannels(settings.Language, settings.UpdateChannel);
    }

    private void PopulateThemeChoices(UiLanguage language, UiThemeMode themeMode)
    {
        _themeMode.ItemsSource = new[]
        {
            new Choice<UiThemeMode>(UiThemeMode.System, UiText.Text(language, "themeSystem")),
            new Choice<UiThemeMode>(UiThemeMode.Light, UiText.Text(language, "themeLight")),
            new Choice<UiThemeMode>(UiThemeMode.Dark, UiText.Text(language, "themeDark"))
        };
        SelectChoice(_themeMode, themeMode);
    }

    private void PopulateUpdateChannels(UiLanguage language, UpdateChannel channel)
    {
        _updateChannel.ItemsSource = new[]
        {
            new Choice<UpdateChannel>(UpdateChannel.Stable, UiText.Text(language, "updateChannelStable")),
            new Choice<UpdateChannel>(UpdateChannel.Test, UiText.Text(language, "updateChannelTest"))
        };
        SelectChoice(_updateChannel, channel);
    }

    private void PopulateLocalizedChoices(
        UiLanguage language,
        RevealBlurLevel blur,
        RevealShape shape,
        PeekTrigger trigger,
        PeekFrameRateLimit frameRateLimit)
    {
        _blurLevel.ItemsSource = new[]
        {
            new Choice<RevealBlurLevel>(RevealBlurLevel.Low, UiText.Text(language, "blurLow")),
            new Choice<RevealBlurLevel>(RevealBlurLevel.Medium, UiText.Text(language, "blurMedium")),
            new Choice<RevealBlurLevel>(RevealBlurLevel.High, UiText.Text(language, "blurHigh"))
        };
        SelectChoice(_blurLevel, blur);
        _shape.ItemsSource = new[]
        {
            new Choice<RevealShape>(RevealShape.Circle, UiText.Text(language, "circle")),
            new Choice<RevealShape>(RevealShape.Rectangle, UiText.Text(language, "rectangle")),
            new Choice<RevealShape>(RevealShape.RoundedRectangle, UiText.Text(language, "roundedRectangle"))
        };
        SelectChoice(_shape, shape);
        _peekMode.ItemsSource = new[]
        {
            new Choice<PeekTrigger>(PeekTrigger.Hold, UiText.Text(language, "holdPeek")),
            new Choice<PeekTrigger>(PeekTrigger.Toggle, UiText.Text(language, "togglePeek"))
        };
        SelectChoice(_peekMode, trigger);
        _peekFrameRateLimit.ItemsSource = new[]
        {
            new Choice<PeekFrameRateLimit>(PeekFrameRateLimit.Auto, UiText.Text(language, "peekFrameRateAuto")),
            new Choice<PeekFrameRateLimit>(PeekFrameRateLimit.Fps60, "60 FPS"),
            new Choice<PeekFrameRateLimit>(PeekFrameRateLimit.Fps90, "90 FPS"),
            new Choice<PeekFrameRateLimit>(PeekFrameRateLimit.Fps120, "120 FPS")
        };
        SelectChoice(_peekFrameRateLimit, frameRateLimit);
    }

    private static void SelectChoice<T>(ComboBox comboBox, T value) where T : struct, Enum
    {
        if (comboBox.ItemsSource is not IEnumerable<Choice<T>> choices)
        {
            return;
        }

        comboBox.SelectedItem = choices.FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
    }

    private static T SelectedChoice<T>(ComboBox comboBox, T fallback) where T : struct, Enum =>
        comboBox.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private UiLanguage CurrentLanguage => SelectedChoice(_language, _editState.SavedSettings.Language);

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs args)
    {
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
            AboutPage => ("about", "aboutDescription"),
            _ => ("revealSettings", "revealSettingsDescription")
        };
        _pageTitle.Text = UiText.Text(language, title);
        _pageDescription.Text = UiText.Text(language, description);
    }

    private void ApplyLanguage(UiLanguage language)
    {
        _updatingLanguage = true;
        try
        {
            Title = UiText.Text(language, "title");
            _revealItem.Content = UiText.Text(language, "revealSettings");
            _hotkeysItem.Content = UiText.Text(language, "hotkeySettings");
            _generalItem.Content = UiText.Text(language, "generalSettings");
            _aboutItem.Content = UiText.Text(language, "about");
            _navigation.PaneTitle = UiText.Text(language, "settingsMenu");
            _saveBar.SetLanguage(language);
            foreach (var (column, hotkey) in _editorColumns)
            {
                column.Width = new GridLength(hotkey ? 280 : language == UiLanguage.English ? 260 : 240);
            }
            foreach (var (text, key) in _localizedText)
            {
                text.Text = UiText.Text(language, key);
            }
            foreach (var (button, key) in _localizedButtons)
            {
                button.Content = UiText.Text(language, key);
            }

            var blur = SelectedChoice(_blurLevel, _editState.SavedSettings.RevealBlurLevel);
            var shape = SelectedChoice(_shape, _editState.SavedSettings.RevealShape);
            var trigger = SelectedChoice(_peekMode, _editState.SavedSettings.PeekTrigger);
            var frameRateLimit = SelectedChoice(
                _peekFrameRateLimit,
                _editState.SavedSettings.PeekFrameRateLimit);
            var themeMode = SelectedChoice(_themeMode, _editState.SavedSettings.ThemeMode);
            var updateChannel = SelectedChoice(_updateChannel, _editState.SavedSettings.UpdateChannel);
            PopulateLocalizedChoices(language, blur, shape, trigger, frameRateLimit);
            PopulateUpdateChannels(language, updateChannel);
            PopulateThemeChoices(language, themeMode);
            UpdateNotificationPreview();
            foreach (var button in this.GetLogicalDescendants().OfType<Button>())
            {
                if (button.Tag is string tooltipKey)
                {
                    ToolTip.SetTip(button, UiText.Text(language, tooltipKey));
                }
            }

            UpdatePageHeader(language);
            RefreshKeyDisplays();
            RefreshStatusText();
            RefreshUpdateView();
        }
        finally
        {
            _updatingLanguage = false;
        }
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs args)
    {
        CancelKeyCapture();
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

        CancelNotificationPreview();
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
        CancelKeyCapture();
        CancelNotificationPreview();
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
    }

    private void OnThemeModeSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_initializing || _updatingLanguage)
        {
            return;
        }

        _applyTheme(SelectedChoice(_themeMode, _editState.SavedSettings.ThemeMode));
        OnSettingsEdited();
    }

    private void OnSettingsEdited()
    {
        if (_initializing || _updatingLanguage)
        {
            return;
        }

        UpdateNotificationPreview();
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

    private void OnActualThemeVariantChanged(object? sender, EventArgs args) => ApplyThemePalette();

    private void ApplyThemePalette()
    {
        var dark = AppTheme.IsDark(ActualThemeVariant);
        var palette = AppTheme.Palette(ActualThemeVariant);
        _canvasBrush.Color = Color.Parse(dark ? "#161819" : "#F2F4F5");
        _panelBrush.Color = Color.Parse(dark ? "#202223" : "#FBFCFC");
        _selectionBrush.Color = Color.Parse(dark ? "#25433E" : "#DCEFEB");
        _cardBrush.Color = palette.Surface;
        _fieldBrush.Color = Color.Parse(dark ? "#1E2021" : "#F6F8F8");
        _fieldBorderBrush.Color = palette.Border;
        _primaryBrush.Color = palette.Primary;
        _secondaryBrush.Color = palette.Secondary;
        _accentBrush.Color = palette.Accent;
        _errorBrush.Color = palette.Error;

        TitleBar.BackgroundColor = _canvasBrush.Color;
        TitleBar.ForegroundColor = _primaryBrush.Color;
        TitleBar.InactiveBackgroundColor = _canvasBrush.Color;
        TitleBar.InactiveForegroundColor = _secondaryBrush.Color;
        TitleBar.ButtonBackgroundColor = _canvasBrush.Color;
        TitleBar.ButtonForegroundColor = _primaryBrush.Color;
        TitleBar.ButtonHoverBackgroundColor = Color.Parse(dark ? "#262626" : "#DDF7F5");
        TitleBar.ButtonHoverForegroundColor = _primaryBrush.Color;
        TitleBar.ButtonPressedBackgroundColor = Color.Parse(dark ? "#333333" : "#BFECE8");
        TitleBar.ButtonPressedForegroundColor = _primaryBrush.Color;
        TitleBar.ButtonInactiveBackgroundColor = _canvasBrush.Color;
        TitleBar.ButtonInactiveForegroundColor = _secondaryBrush.Color;
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        CancelNotificationPreview();
        _notificationPreviewTimer.Tick -= OnNotificationPreviewTimerTick;
        if (_updates is not null)
        {
            _updates.Changed -= OnUpdateStateChanged;
        }
        _applyTheme(_editState.SavedSettings.ThemeMode);
        _savedStatusTimer.Stop();
        _savedStatusTimer.Tick -= OnSavedStatusTimerTick;
        _saveBar.PropertyChanged -= OnSaveBarPropertyChanged;
        _saveBar.Dispose();
        _exportLogsButton.Click -= OnExportLogsClicked;
        _exportSettingsButton.Click -= OnExportSettingsClicked;
        _checkUpdatesButton.Click -= OnCheckUpdatesClicked;
        _viewUpdateButton.Click -= OnViewUpdateClicked;
        _sourceRepositoryButton.Click -= OnSourceRepositoryClicked;
        _releasePageButton.Click -= OnReleasePageClicked;
        _themeMode.SelectionChanged -= OnThemeModeSelectionChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        Closed -= OnClosed;
    }

    private void BeginPeekKeyCapture()
    {
        CancelKeyCapture();
        _capturingPeekKey = true;
        _peekKeyButton.Content = UiText.Text(CurrentLanguage, "pressKey");
        _peekKeyButton.Focus();
    }

    private void BeginHotkeyCapture(HotkeyEditor editor)
    {
        CancelKeyCapture();
        _activeHotkeyEditor = editor;
        editor.Display.Content = UiText.Text(CurrentLanguage, "pressShortcut");
        editor.Display.Focus();
    }

    private void CancelKeyCapture()
    {
        _activeHotkeyEditor = null;
        _capturingPeekKey = false;
        RefreshKeyDisplays();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (_activeHotkeyEditor is null && !_capturingPeekKey)
        {
            return;
        }

        args.Handled = true;
        if (args.Key == Key.Escape)
        {
            CancelKeyCapture();
            return;
        }

        var virtualKey = ToVirtualKey(args.Key);
        if (virtualKey is < 1 or > 255)
        {
            return;
        }

        if (_activeHotkeyEditor is not null)
        {
            if (HotkeyBinding.IsModifierVirtualKey(virtualKey))
            {
                return;
            }

            var modifiers = ShortcutModifiers.None;
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                modifiers |= ShortcutModifiers.Control;
            }
            if (args.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                modifiers |= ShortcutModifiers.Alt;
            }
            if (args.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                modifiers |= ShortcutModifiers.Shift;
            }

            _activeHotkeyEditor.Binding = new HotkeyBinding(virtualKey, modifiers);
            _activeHotkeyEditor = null;
            RefreshKeyDisplays();
            UpdateHotkeyConflicts();
            OnSettingsEdited();
            return;
        }

        _peekVirtualKey = virtualKey;
        _capturingPeekKey = false;
        RefreshKeyDisplays();
        OnSettingsEdited();
    }

    private void RefreshKeyDisplays()
    {
        if (!_capturingPeekKey)
        {
            _peekKeyButton.Content = UiText.PeekKeyName(CurrentLanguage, _peekVirtualKey);
        }

        foreach (var editor in GetHotkeyEditors())
        {
            if (!ReferenceEquals(editor, _activeHotkeyEditor))
            {
                editor.Display.Content = UiText.ShortcutName(CurrentLanguage, editor.Binding);
            }
            editor.Clear.IsEnabled = !editor.Binding.IsDisabled;
        }
    }

    private HotkeyBinding? FindDuplicateHotkey() => GetHotkeyEditors()
        .Select(editor => editor.Binding)
        .Where(binding => !binding.IsDisabled)
        .GroupBy(binding => binding)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;

    private void UpdateHotkeyConflicts()
    {
        var duplicateBindings = GetHotkeyEditors()
            .Select(editor => editor.Binding)
            .Where(binding => !binding.IsDisabled)
            .GroupBy(binding => binding)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var editor in GetHotkeyEditors())
        {
            var conflict = duplicateBindings.Contains(editor.Binding);
            editor.Display.BorderBrush = conflict ? _errorBrush : _fieldBorderBrush;
            editor.Display.Foreground = conflict ? _errorBrush : _primaryBrush;
            editor.Display.BorderThickness = new Thickness(conflict ? 2 : 1);
        }
    }

    private HotkeyEditor[] GetHotkeyEditors() =>
    [
        _pickHotkey,
        _windowToggleHotkey,
        _diameterIncreaseHotkey,
        _diameterDecreaseHotkey,
        _restoreHotkey,
        _restoreAllHotkey,
        _settingsHotkey,
        _exitHotkey
    ];

    private static int ToVirtualKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return 0x41 + (key - Key.A);
        }
        if (key is >= Key.D0 and <= Key.D9)
        {
            return 0x30 + (key - Key.D0);
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return 0x60 + (key - Key.NumPad0);
        }
        if (key is >= Key.F1 and <= Key.F24)
        {
            return 0x70 + (key - Key.F1);
        }

        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Return or Key.Enter => 0x0D,
            Key.LeftShift => 0xA0,
            Key.RightShift => 0xA1,
            Key.LeftCtrl => 0xA2,
            Key.RightCtrl => 0xA3,
            Key.LeftAlt => 0xA4,
            Key.RightAlt => 0xA5,
            Key.Pause => 0x13,
            Key.CapsLock => 0x14,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Print or Key.PrintScreen => 0x2C,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.LWin => 0x5B,
            Key.RWin => 0x5C,
            Key.NumLock => 0x90,
            Key.Scroll => 0x91,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            _ => 0
        };
    }

    private sealed record Choice<T>(T Value, string Display) where T : struct, Enum
    {
        public override string ToString() => Display;
    }

    private sealed class HotkeyEditor(
        HotkeyBinding binding,
        HotkeyBinding defaultBinding,
        Button display,
        Button clear,
        Control host)
    {
        public HotkeyBinding Binding { get; set; } = binding;
        public HotkeyBinding DefaultBinding { get; } = defaultBinding;
        public Button Display { get; } = display;
        public Button Clear { get; } = clear;
        public Control Host { get; } = host;
    }
}
