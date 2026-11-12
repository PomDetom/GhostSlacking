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

    private Button CreateIconButton(Symbol symbol, string tooltipKey)
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
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

    private static NumberBox CreateNumberBox(double value, double minimum, double maximum, double step) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = step,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox CreateComboBox()
    {
        var combo = new ComboBox();
        SettingsFieldStyles.Configure(combo);
        return combo;
    }

    // The control group is 240/260 DIP wide; its gap from the copy is additional.
    private static double SettingEditorColumnWidth(UiLanguage language) =>
        (language == UiLanguage.English ? 260 : 240) + 16;

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

    private void ApplyLanguage(UiLanguage language)
    {
        CloseAdjustment();
        _updatingLanguage = true;
        try
        {
            Title = UiText.Text(language, "title");
            _revealItem.Content = UiText.Text(language, "revealSettings");
            _hotkeysItem.Content = UiText.Text(language, "hotkeySettings");
            _generalItem.Content = UiText.Text(language, "generalSettings");
            _notificationsItem.Content = UiText.Text(language, "notificationsPage");
            _experimentsItem.Content = UiText.Text(language, "experimentalSettings");
            _aboutItem.Content = UiText.Text(language, "about");
            _navigation.PaneTitle = UiText.Text(language, "settingsMenu");
            _saveBar.SetLanguage(language);
            foreach (var (column, hotkey) in _editorColumns)
            {
                column.Width = new GridLength(hotkey ? 280 : SettingEditorColumnWidth(language));
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
            foreach (var button in this.GetLogicalDescendants().OfType<Button>())
            {
                if (button.Tag is string tooltipKey)
                {
                    ToolTip.SetTip(button, UiText.Text(language, tooltipKey));
                }
            }

            RefreshAdjustmentValues();
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

    private void OnThemeModeSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_initializing || _updatingLanguage)
        {
            return;
        }

        _applyTheme(SelectedChoice(_themeMode, _editState.SavedSettings.ThemeMode));
        OnSettingsEdited();
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

    private sealed record Choice<T>(T Value, string Display) where T : struct, Enum
    {
        public override string ToString() => Display;
    }
}
