using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;
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
    private static readonly TimeSpan SavedStatusDuration = TimeSpan.FromMilliseconds(2500);
    private static readonly AppSettings DefaultSettings = new();
    private readonly SolidColorBrush _canvasBrush = new();
    private readonly SolidColorBrush _cardBrush = new();
    private readonly SolidColorBrush _fieldBrush = new();
    private readonly SolidColorBrush _fieldBorderBrush = new();
    private readonly SolidColorBrush _primaryBrush = new();
    private readonly SolidColorBrush _secondaryBrush = new();
    private readonly SolidColorBrush _accentBrush = new();
    private readonly SolidColorBrush _errorBrush = new();

    private readonly Func<AppSettings, bool> _save;
    private readonly Action<UiThemeMode> _applyTheme;
    private readonly List<(TextBlock Text, string Key)> _localizedText = [];
    private readonly List<(Button Button, string Key)> _localizedButtons = [];
    private readonly Dictionary<string, Control> _pages = [];
    private readonly SettingsEditState _editState;
    private readonly NavigationView _navigation;
    private readonly NavigationViewItem _revealItem;
    private readonly NavigationViewItem _hotkeysItem;
    private readonly NavigationViewItem _generalItem;
    private readonly TextBlock _pageTitle;
    private readonly TextBlock _pageDescription;
    private readonly ContentControl _pageHost;
    private readonly InfoBar _infoBar;
    private readonly NumberBox _diameter;
    private readonly NumberBox _diameterStep;
    private readonly NumberBox _softEdgeWidth;
    private readonly ComboBox _blurLevel;
    private readonly ComboBox _shape;
    private readonly ComboBox _peekMode;
    private readonly Button _peekKeyButton;
    private readonly ComboBox _language;
    private readonly ComboBox _themeMode;
    private readonly ComboBox _logLevel;
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

    public SettingsWindow(
        AppSettings settings,
        Func<AppSettings, bool> save,
        Action<UiThemeMode>? applyTheme = null)
    {
        _editState = new SettingsEditState(settings);
        _save = save;
        _applyTheme = applyTheme ?? AppTheme.Apply;
        _peekVirtualKey = settings.PeekVirtualKey;

        ApplyThemePalette();

        Title = UiText.Text(settings.Language, "title");
        Width = 840;
        Height = 640;
        MinWidth = 840;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = _canvasBrush;
        Icon = AppIcon.TitleBarImage;

        _diameter = CreateNumberBox(settings.RevealDiameterPx, 64, 800, 8);
        _diameterStep = CreateNumberBox(settings.RevealDiameterStepPx, 8, 256, 8);
        _softEdgeWidth = CreateNumberBox(settings.RevealSoftEdgeWidthPx, 0, 128, 4);
        _blurLevel = CreateComboBox();
        _shape = CreateComboBox();
        _peekMode = CreateComboBox();
        _language = CreateComboBox();
        _themeMode = CreateComboBox();
        _logLevel = CreateComboBox();
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
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = _primaryBrush
        };
        _pageDescription = new TextBlock
        {
            FontSize = 14,
            Foreground = _secondaryBrush,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _pageHost = new ContentControl();
        _infoBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };
        _savedStatusTimer = new DispatcherTimer { Interval = SavedStatusDuration };
        _savedStatusTimer.Tick += OnSavedStatusTimerTick;

        _revealItem = CreateNavigationItem(RevealPage, Symbol.View);
        _hotkeysItem = CreateNavigationItem(HotkeysPage, Symbol.Keyboard);
        _generalItem = CreateNavigationItem(GeneralPage, Symbol.Settings);
        _navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneOpen = false,
            IsBackButtonVisible = false,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = true,
            AlwaysShowHeader = false,
            CompactPaneLength = 52,
            OpenPaneLength = 220,
            PaneTitle = "GhostSlacking",
            Background = _canvasBrush
        };
        _navigation.MenuItems.Add(_revealItem);
        _navigation.MenuItems.Add(_hotkeysItem);
        _navigation.MenuItems.Add(_generalItem);
        _navigation.SelectionChanged += OnNavigationSelectionChanged;

        PopulateChoices(settings);
        BuildPages(settings.Language);
        _navigation.Content = CreateContentShell(settings.Language);
        _pageHost.Content = _pages[RevealPage];
        _navigation.SelectedItem = _revealItem;
        Content = _navigation;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        Closed += OnClosed;
        _language.SelectionChanged += (_, _) =>
        {
            if (!_updatingLanguage)
            {
                ApplyLanguage(CurrentLanguage);
                OnSettingsEdited();
            }
        };

        ApplyLanguage(settings.Language);
        UpdateHotkeyConflicts();
        SubscribeToSettingChanges();
        _initializing = false;
    }

    private Control CreateContentShell(UiLanguage language)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var header = new StackPanel
        {
            Margin = new Thickness(34, 28, 34, 20),
            Children = { _pageTitle, _pageDescription }
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        Grid.SetRow(_pageHost, 1);
        root.Children.Add(_pageHost);

        var footer = new Border
        {
            Background = _cardBrush,
            BorderBrush = _fieldBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(34, 14)
        };
        var footerContent = new Grid();
        footerContent.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        footerContent.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _infoBar.MaxWidth = 560;
        _infoBar.Margin = new Thickness(0, 0, 20, 0);
        footerContent.Children.Add(_infoBar);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10
        };
        var cancel = CreateTextButton(language, "cancel");
        cancel.Click += (_, _) => Close();
        var save = CreateTextButton(language, "save");
        save.Classes.Add("accent");
        save.Click += OnSaveClicked;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 1);
        footerContent.Children.Add(buttons);
        footer.Child = footerContent;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
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
            CreateSection(language, "languageAndDiagnostics",
            [
                CreateSettingRow(language, "theme", "themeDescription", _themeMode),
                CreateSettingRow(language, "interfaceLanguage", "languageDescription", _language),
                CreateSettingRow(language, "logLevel", "logLevelDescription", _logLevel)
            ])
        ]);
    }

    private Control CreatePage(IEnumerable<Control> sections)
    {
        var stack = new StackPanel
        {
            Spacing = 24,
            Margin = new Thickness(34, 0, 34, 30)
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
        heading.FontWeight = FontWeight.SemiBold;
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
        Control editor)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(310)));

        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        var title = LocalizedText(language, titleKey);
        title.FontSize = 15;
        title.FontWeight = FontWeight.SemiBold;
        title.Foreground = _primaryBrush;
        var description = LocalizedText(language, descriptionKey);
        description.FontSize = 12.5;
        description.Foreground = _secondaryBrush;
        description.TextWrapping = TextWrapping.Wrap;
        copy.Children.Add(title);
        copy.Children.Add(description);
        grid.Children.Add(copy);

        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Center;
        editor.Margin = new Thickness(24, 0, 0, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        return new Border
        {
            Background = _cardBrush,
            BorderBrush = _fieldBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 15),
            Child = grid
        };
    }

    private Control CreateHotkeyRow(
        UiLanguage language,
        string titleKey,
        string descriptionKey,
        HotkeyEditor editor) => CreateSettingRow(language, titleKey, descriptionKey, editor.Host);

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
        IconSource = new SymbolIconSource { Symbol = symbol }
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
        PopulateLocalizedChoices(settings.Language, settings.RevealBlurLevel, settings.RevealShape, settings.PeekTrigger);
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

    private void PopulateLocalizedChoices(
        UiLanguage language,
        RevealBlurLevel blur,
        RevealShape shape,
        PeekTrigger trigger)
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
            var themeMode = SelectedChoice(_themeMode, _editState.SavedSettings.ThemeMode);
            PopulateLocalizedChoices(language, blur, shape, trigger);
            PopulateThemeChoices(language, themeMode);
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
        }
        finally
        {
            _updatingLanguage = false;
        }
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs args)
    {
        var duplicate = FindDuplicateHotkey();
        if (duplicate is not null)
        {
            var message = string.Format(
                UiText.Text(CurrentLanguage, "duplicateHotkey"),
                UiText.ShortcutName(CurrentLanguage, duplicate.Value));
            ShowStatus(
                SettingsStatus.Warning,
                UiText.Text(CurrentLanguage, "hotkeySettings"),
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
                UiText.Text(CurrentLanguage, "title"),
                UiText.Text(CurrentLanguage, "settingsSaveFailed"),
                InfoBarSeverity.Error);
            return;
        }

        _editState.MarkSaved(updated);
        ShowSavedStatus();
    }

    private AppSettings GetSettings() => _editState.SavedSettings with
    {
        RevealDiameterPx = (int)_diameter.Value,
        RevealDiameterStepPx = (int)_diameterStep.Value,
        RevealSoftEdgeWidthPx = (int)_softEdgeWidth.Value,
        RevealBlurLevel = SelectedChoice(_blurLevel, _editState.SavedSettings.RevealBlurLevel),
        RevealShape = SelectedChoice(_shape, _editState.SavedSettings.RevealShape),
        PeekTrigger = SelectedChoice(_peekMode, _editState.SavedSettings.PeekTrigger),
        PeekVirtualKey = _peekVirtualKey,
        Language = CurrentLanguage,
        ThemeMode = SelectedChoice(_themeMode, _editState.SavedSettings.ThemeMode),
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
        foreach (var numberBox in new[] { _diameter, _diameterStep, _softEdgeWidth })
        {
            numberBox.ValueChanged += (_, _) => OnSettingsEdited();
        }

        foreach (var comboBox in new[] { _blurLevel, _shape, _peekMode, _logLevel })
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
        ShowStatus(
            SettingsStatus.Saved,
            UiText.Text(CurrentLanguage, "saveSucceeded"),
            string.Empty,
            InfoBarSeverity.Success);
        _savedStatusTimer.Start();
    }

    private void ShowStatus(SettingsStatus status, string title, string message, InfoBarSeverity severity)
    {
        _savedStatusTimer.Stop();
        _editState.SetStatus(status);
        _infoBar.Title = StatusText(title, message);
        _infoBar.Message = string.Empty;
        _infoBar.Severity = severity;
        ToolTip.SetTip(_infoBar, StatusText(title, message));
        _infoBar.IsOpen = true;
    }

    private void HideStatus()
    {
        _savedStatusTimer.Stop();
        _editState.SetStatus(SettingsStatus.None);
        _infoBar.IsOpen = false;
    }

    private void RefreshStatusText()
    {
        switch (_editState.Status)
        {
            case SettingsStatus.Modified:
                _infoBar.Title = StatusText(
                    UiText.Text(CurrentLanguage, "settingsModified"),
                    UiText.Text(CurrentLanguage, "settingsHint"));
                _infoBar.Message = string.Empty;
                ToolTip.SetTip(_infoBar, _infoBar.Title);
                break;
            case SettingsStatus.Saved:
                _infoBar.Title = UiText.Text(CurrentLanguage, "saveSucceeded");
                _infoBar.Message = string.Empty;
                ToolTip.SetTip(_infoBar, _infoBar.Title);
                break;
        }
    }

    private static string StatusText(string title, string message) =>
        string.IsNullOrEmpty(message) ? title : $"{title} · {message}";

    private void OnSavedStatusTimerTick(object? sender, EventArgs args)
    {
        if (_editState.ExpireSaved())
        {
            _savedStatusTimer.Stop();
            _infoBar.IsOpen = false;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs args) => ApplyThemePalette();

    private void ApplyThemePalette()
    {
        var dark = AppTheme.IsDark(ActualThemeVariant);
        _canvasBrush.Color = Color.Parse(dark ? "#000000" : "#F5F9FC");
        _cardBrush.Color = Color.Parse(dark ? "#0A0A0A" : "#FFFFFF");
        _fieldBrush.Color = dark ? AppTheme.DarkFieldColor : Color.Parse("#F8FAFC");
        _fieldBorderBrush.Color = Color.Parse(dark ? "#2A2A2A" : "#DCE7EF");
        _primaryBrush.Color = Color.Parse(dark ? "#F5F5F5" : "#172033");
        _secondaryBrush.Color = Color.Parse(dark ? "#A3A3A3" : "#667085");
        _accentBrush.Color = AppTheme.AccentColor;
        _errorBrush.Color = Color.Parse(dark ? "#FF8A80" : "#C42B1C");

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
        _applyTheme(_editState.SavedSettings.ThemeMode);
        _savedStatusTimer.Stop();
        _savedStatusTimer.Tick -= OnSavedStatusTimerTick;
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
