using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;
using Button = Avalonia.Controls.Button;
using Brush = Avalonia.Media.Brush;
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
    private static readonly AppSettings DefaultSettings = new();
    private static readonly IBrush CanvasBrush = Brush.Parse("#F5F7FA");
    private static readonly IBrush CardBrush = Brush.Parse("#FFFFFF");
    private static readonly IBrush FieldBorderBrush = Brush.Parse("#E4E9F0");
    private static readonly IBrush PrimaryBrush = Brush.Parse("#172033");
    private static readonly IBrush SecondaryBrush = Brush.Parse("#667085");
    private static readonly IBrush AccentBrush = Brush.Parse("#087F78");
    private static readonly IBrush ErrorBrush = Brush.Parse("#C42B1C");

    private readonly Func<AppSettings, bool> _save;
    private readonly List<(TextBlock Text, string Key)> _localizedText = [];
    private readonly List<(Button Button, string Key)> _localizedButtons = [];
    private readonly Dictionary<string, Control> _pages = [];
    private readonly AppSettings _initialSettings;
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
    private HotkeyEditor? _activeHotkeyEditor;
    private int _peekVirtualKey;
    private bool _capturingPeekKey;
    private bool _updatingLanguage;
    private string _selectedPage = RevealPage;

    public SettingsWindow(AppSettings settings, Func<AppSettings, bool> save)
    {
        _initialSettings = settings;
        _save = save;
        _peekVirtualKey = settings.PeekVirtualKey;

        Title = UiText.Text(settings.Language, "title");
        Width = 1020;
        Height = 740;
        MinWidth = 840;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = CanvasBrush;
        ((Window)this).Icon = CreateWindowIcon();

        _diameter = CreateNumberBox(settings.RevealDiameterPx, 64, 800, 8);
        _diameterStep = CreateNumberBox(settings.RevealDiameterStepPx, 8, 256, 8);
        _softEdgeWidth = CreateNumberBox(settings.RevealSoftEdgeWidthPx, 0, 128, 4);
        _blurLevel = CreateComboBox();
        _shape = CreateComboBox();
        _peekMode = CreateComboBox();
        _language = CreateComboBox();
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
            Foreground = PrimaryBrush
        };
        _pageDescription = new TextBlock
        {
            FontSize = 14,
            Foreground = SecondaryBrush,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _pageHost = new ContentControl();
        _infoBar = new InfoBar
        {
            IsOpen = false,
            Margin = new Thickness(32, 0, 32, 12)
        };

        _revealItem = CreateNavigationItem(RevealPage, Symbol.View);
        _hotkeysItem = CreateNavigationItem(HotkeysPage, Symbol.Keyboard);
        _generalItem = CreateNavigationItem(GeneralPage, Symbol.Settings);
        _navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = false,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = true,
            AlwaysShowHeader = false,
            CompactPaneLength = 52,
            OpenPaneLength = 220,
            PaneTitle = "GhostSlacking",
            Background = CanvasBrush
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
        _language.SelectionChanged += (_, _) =>
        {
            if (!_updatingLanguage)
            {
                ApplyLanguage(CurrentLanguage);
            }
        };

        ApplyLanguage(settings.Language);
        UpdateHotkeyConflicts();
    }

    private static WindowIcon? CreateWindowIcon()
    {
        return AppIcon.Instance;
    }

    private Control CreateContentShell(UiLanguage language)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
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

        Grid.SetRow(_infoBar, 1);
        root.Children.Add(_infoBar);

        Grid.SetRow(_pageHost, 2);
        root.Children.Add(_pageHost);

        var footer = new Border
        {
            Background = CardBrush,
            BorderBrush = FieldBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(34, 14)
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        var cancel = CreateTextButton(language, "cancel");
        cancel.Click += (_, _) => Close();
        var save = CreateTextButton(language, "save");
        save.Classes.Add("accent");
        save.Click += OnSaveClicked;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        footer.Child = buttons;
        Grid.SetRow(footer, 3);
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
        heading.Foreground = AccentBrush;
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
        title.Foreground = PrimaryBrush;
        var description = LocalizedText(language, descriptionKey);
        description.FontSize = 12.5;
        description.Foreground = SecondaryBrush;
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
            Background = CardBrush,
            BorderBrush = FieldBorderBrush,
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
        };
        reset.Click += (_, _) =>
        {
            CancelKeyCapture();
            editor.Binding = editor.DefaultBinding;
            RefreshKeyDisplays();
            UpdateHotkeyConflicts();
        };
        return editor;
    }

    private static Button CreateCaptureButton() => new()
    {
        MinHeight = 36,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(12, 6),
        Background = Brush.Parse("#F8FAFC"),
        BorderBrush = FieldBorderBrush,
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

    private UiLanguage CurrentLanguage => SelectedChoice(_language, _initialSettings.Language);

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

            var blur = SelectedChoice(_blurLevel, _initialSettings.RevealBlurLevel);
            var shape = SelectedChoice(_shape, _initialSettings.RevealShape);
            var trigger = SelectedChoice(_peekMode, _initialSettings.PeekTrigger);
            PopulateLocalizedChoices(language, blur, shape, trigger);
            foreach (var button in this.GetLogicalDescendants().OfType<Button>())
            {
                if (button.Tag is string tooltipKey)
                {
                    ToolTip.SetTip(button, UiText.Text(language, tooltipKey));
                }
            }

            UpdatePageHeader(language);
            RefreshKeyDisplays();
            _infoBar.IsOpen = false;
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
            _infoBar.Title = UiText.Text(CurrentLanguage, "hotkeySettings");
            _infoBar.Message = string.Format(
                UiText.Text(CurrentLanguage, "duplicateHotkey"),
                UiText.ShortcutName(CurrentLanguage, duplicate.Value));
            _infoBar.Severity = InfoBarSeverity.Warning;
            _infoBar.IsOpen = true;

            var dialog = new ContentDialog
            {
                Title = UiText.Text(CurrentLanguage, "hotkeySettings"),
                Content = new TextBlock
                {
                    Text = _infoBar.Message,
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
            _infoBar.Title = UiText.Text(CurrentLanguage, "title");
            _infoBar.Message = UiText.Text(CurrentLanguage, "settingsSaveFailed");
            _infoBar.Severity = InfoBarSeverity.Error;
            _infoBar.IsOpen = true;
            return;
        }

        _infoBar.Title = UiText.Text(CurrentLanguage, "saveSucceeded");
        _infoBar.Message = UiText.Text(CurrentLanguage, "settingsHint");
        _infoBar.Severity = InfoBarSeverity.Success;
        _infoBar.IsOpen = true;
    }

    private AppSettings GetSettings() => _initialSettings with
    {
        RevealDiameterPx = (int)_diameter.Value,
        RevealDiameterStepPx = (int)_diameterStep.Value,
        RevealSoftEdgeWidthPx = (int)_softEdgeWidth.Value,
        RevealBlurLevel = SelectedChoice(_blurLevel, _initialSettings.RevealBlurLevel),
        RevealShape = SelectedChoice(_shape, _initialSettings.RevealShape),
        PeekTrigger = SelectedChoice(_peekMode, _initialSettings.PeekTrigger),
        PeekVirtualKey = _peekVirtualKey,
        Language = CurrentLanguage,
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
        MinimumLogLevel = SelectedChoice(_logLevel, _initialSettings.MinimumLogLevel)
    };

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
            return;
        }

        _peekVirtualKey = virtualKey;
        _capturingPeekKey = false;
        RefreshKeyDisplays();
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
            editor.Display.BorderBrush = conflict ? ErrorBrush : FieldBorderBrush;
            editor.Display.Foreground = conflict ? ErrorBrush : PrimaryBrush;
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
