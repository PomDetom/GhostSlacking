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
    private const string RevealPage = "reveal";
    private const string HotkeysPage = "hotkeys";
    private const string GeneralPage = "general";
    private const string NotificationsPage = "notifications";
    private const string ExperimentsPage = "experiments";
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
    private readonly NavigationViewItem _notificationsItem;
    private readonly NavigationViewItem _experimentsItem;
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
    private readonly Dictionary<AdjustmentKind, SettingsAdjustmentRow> _adjustmentValues = [];
    private SettingsAdjustmentFlyout? _activeAdjustment;
    internal SettingsAdjustmentFlyout? ActiveAdjustment => _activeAdjustment;
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
    private readonly Button _sourceRepositoryButton;
    private readonly Button _releasePageButton;
    private readonly ToggleSwitch _restoreOnExit;
    private readonly ToggleSwitch _startWithWindows;
    private readonly ToggleSwitch _backgroundInteraction;
    private readonly ToggleSwitch _continuousInteraction;
    private readonly HotkeyEditor _backgroundInteractionHotkey;
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

    public SettingsWindow(
        AppSettings settings,
        Func<AppSettings, bool> save,
        Action<UiThemeMode>? applyTheme = null,
        Func<Stream, Task<DataExportResult>>? exportLogs = null,
        Func<AppSettings, Stream, Task<DataExportResult>>? exportSettings = null,
        IApplicationUpdateManager? updates = null,
        Action? openUpdateWindow = null,
        string? initialPage = null)
    {
        _editState = new SettingsEditState(settings);
        _save = save;
        _applyTheme = applyTheme ?? AppTheme.Apply;
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
        _logLevel = CreateComboBox();
        _updateChannel = CreateComboBox();
        _exportLogsButton = CreateTextButton(settings.Language, "exportLogs");
        _exportSettingsButton = CreateTextButton(settings.Language, "exportSettings");
        _exportLogsButton.Click += OnExportLogsClicked;
        _exportSettingsButton.Click += OnExportSettingsClicked;
        _currentVersionText = new TextBlock();
        _latestVersionText = new TextBlock();
        _lastUpdateCheckText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _updateStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = false };
        _checkUpdatesButton = new Button { MinWidth = 120 };
        _sourceRepositoryButton = CreateTextButton(settings.Language, "sourceRepository");
        _releasePageButton = CreateTextButton(settings.Language, "releasePage");
        _checkUpdatesButton.Click += OnCheckUpdatesClicked;
        _sourceRepositoryButton.Click += OnSourceRepositoryClicked;
        _releasePageButton.Click += OnReleasePageClicked;
        _restoreOnExit = new ToggleSwitch { IsChecked = settings.RestoreOnExit };
        _startWithWindows = new ToggleSwitch { IsChecked = settings.StartWithWindows };
        _backgroundInteraction = new ToggleSwitch { IsChecked = settings.ExperimentalBackgroundInteraction };
        _continuousInteraction = new ToggleSwitch { IsChecked = settings.ContinuousBackgroundInteraction };
        _backgroundInteractionHotkey = CreateHotkeyEditor(settings.BackgroundInteractionHotkey, HotkeyBinding.Disabled);
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
        _notificationsItem = CreateNavigationItem(NotificationsPage, Symbol.Message);
        _experimentsItem = CreateNavigationItem(ExperimentsPage, Symbol.Settings);
        _experimentsItem.IconSource = new PathIconSource
        {
            Data = ExperimentNavigationIcon.Geometry,
            Stretch = Stretch.Uniform
        };
        ExperimentNavigationIcon.FollowNavigationForeground(_experimentsItem);
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
        _navigation.MenuItems.Add(_notificationsItem);
        _navigation.MenuItems.Add(_experimentsItem);
        _navigation.FooterMenuItems.Add(_aboutItem);
        _navigation.SelectionChanged += OnNavigationSelectionChanged;

        PopulateChoices(settings);
        BuildPages(settings.Language);
        _navigation.Content = CreateContentShell();
        _pageHost.Content = _pages[RevealPage];
        _navigation.SelectedItem = _revealItem;
        Content = _navigation;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, (_, _) => CloseAdjustment(), RoutingStrategies.Tunnel);
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

    private void OnClosed(object? sender, EventArgs args)
    {
        CloseAdjustment();
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
        _sourceRepositoryButton.Click -= OnSourceRepositoryClicked;
        _releasePageButton.Click -= OnReleasePageClicked;
        _themeMode.SelectionChanged -= OnThemeModeSelectionChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        Closed -= OnClosed;
    }
}
