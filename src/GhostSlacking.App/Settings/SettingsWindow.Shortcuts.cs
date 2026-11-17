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

    private Control CreateHotkeyRow(
        UiLanguage language,
        string titleKey,
        string descriptionKey,
        HotkeyEditor editor) => CreateSettingRow(language, titleKey, descriptionKey, editor.Host, hotkey: true);

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
        _backgroundInteractionHotkey,
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
