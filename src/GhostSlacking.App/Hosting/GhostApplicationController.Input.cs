using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Diagnostics;
using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed partial class GhostApplicationController : IDisposable
{

    private void RegisterHotkeys()
    {
        var shortcuts = GetShortcuts();
        var duplicates = shortcuts
            .Where(shortcut => !shortcut.Binding.IsDisabled)
            .GroupBy(shortcut => shortcut.Binding)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var shortcut in shortcuts)
        {
            if (shortcut.Binding.IsDisabled)
            {
                continue;
            }
            if (duplicates.Contains(shortcut.Binding))
            {
                _logger.Log(LogLevel.Warning, $"Duplicate hotkey was not registered: {UiText.ShortcutName(_settings.Language, shortcut.Binding)}");
                continue;
            }
            RegisterShortcut(shortcut.Id, shortcut.Binding);
        }
    }

    private (int Id, HotkeyBinding Binding)[] GetShortcuts() =>
        [
            (PickHotkey, _settings.PickHotkey),
            (WindowVisibilityHotkey, _settings.WindowToggleHotkey),
            (RestoreHotkey, _settings.RestoreHotkey),
            (EmergencyRestoreHotkey, _settings.RestoreAllHotkey),
            (SettingsHotkey, _settings.SettingsHotkey),
            (QuitHotkey, _settings.ExitHotkey),
            (DiameterIncreaseHotkey, _settings.RevealDiameterIncreaseHotkey),
            (DiameterDecreaseHotkey, _settings.RevealDiameterDecreaseHotkey),
            (BackgroundInteractionHotkey, _settings.ExperimentalBackgroundInteraction
                ? _settings.BackgroundInteractionHotkey : HotkeyBinding.Disabled)
        ];

    private void RegisterShortcut(int id, HotkeyBinding binding)
    {
        var modifiers = HotkeyModifiers.NoRepeat;
        if (binding.Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        Register(id, modifiers, binding.VirtualKey, UiText.ShortcutName(_settings.Language, binding));
    }

    private void Register(int id, HotkeyModifiers modifiers, int key, string display)
    {
        if (!_hotkeys.Register(id, modifiers, key, out var error))
        {
            _logger.Log(LogLevel.Warning, $"Hotkey registration failed: {display}, error={error}");
        }
    }

    private void OnHotkeyPressed(int id)
    {
        DispatchHotkey(id, CancelBackgroundInteraction, ExecuteHotkey);
    }

    internal static void DispatchHotkey(int id, Action cancel, Action<int> execute)
    {
        if (id != BackgroundInteractionHotkey && !PreservesReveal(id)) cancel();
        execute(id);
    }

    private void ExecuteHotkey(int id)
    {
        switch (id)
        {
            case PickHotkey:
                BeginPicking();
                break;
            case WindowVisibilityHotkey:
                ToggleWindowVisibility();
                break;
            case RestoreHotkey:
                RestoreCurrent("hotkey");
                break;
            case EmergencyRestoreHotkey:
                RestoreAll("emergency hotkey");
                break;
            case SettingsHotkey:
                OpenSettings();
                break;
            case QuitHotkey:
                ExitApplication();
                break;
            case DiameterIncreaseHotkey:
                AdjustRevealDiameter(1);
                break;
            case DiameterDecreaseHotkey:
                AdjustRevealDiameter(-1);
                break;
            case BackgroundInteractionHotkey:
                ToggleBackgroundInteraction();
                break;
        }
    }

    internal static bool PreservesReveal(int id) => id is DiameterIncreaseHotkey or DiameterDecreaseHotkey;

    private void BeginPicking()
    {
        CancelBackgroundInteraction();
        if (_pickerActive || _pickerEscapeGate.AwaitingRelease)
        {
            return;
        }

        if (!_coordinator.BeginPicking())
        {
            return;
        }

        var mouseStarted = _mouseHook.Start();
        var keyboardStarted = mouseStarted && _keyboardHook.Start();
        _pickerActive = mouseStarted && keyboardStarted;
        if (!_pickerActive)
        {
            CancelPicking();
            ShowError(UiText.Text(_settings.Language, "pickFailed"));
        }
        else
        {
            ShowInfo(UiText.Text(_settings.Language, "clickToPick"), UserNotificationSource.Picker);
        }
    }

    private void OnPickerClick(object? sender, MouseButtonEventArgs args)
    {
        Dispatcher.UIThread.Post(() => FinishPicking(args.ScreenPoint));
    }

    private void OnPickerKeyStateChanged(object? sender, KeyStateChangedEventArgs args)
    {
        var decision = _pickerEscapeGate.Process(_pickerActive, args.VirtualKey, args.IsDown);
        if (!decision.Handled)
        {
            return;
        }

        args.Handled = true;
        if (decision.CancelPicking)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CancelPicking(waitForEscapeRelease: true);
                ShowInfo(UiText.Text(_settings.Language, "pickCancelled"), UserNotificationSource.Picker);
            });
        }
        else if (decision.ReleaseKeyboardHook)
        {
            Dispatcher.UIThread.Post(_keyboardHook.Stop);
        }
    }

    private void CancelPicking(bool waitForEscapeRelease = false)
    {
        _pickerActive = false;
        _mouseHook.Stop();
        if (!waitForEscapeRelease)
        {
            _pickerEscapeGate.Reset();
            _keyboardHook.Stop();
        }

        _coordinator.CancelPicking();
    }

    private void FinishPicking(Point screenPoint)
    {
        if (!_pickerActive)
        {
            return;
        }

        _pickerActive = false;
        _mouseHook.Stop();
        _keyboardHook.Stop();
        _pickerEscapeGate.Reset();
        var target = _picker.PickAt(screenPoint);
        if (target is null)
        {
            _coordinator.CancelPicking();
            ShowError(UiText.Text(_settings.Language, "noWindow"));
            return;
        }

        var settings = new RevealSettings
        {
            DiameterPx = _settings.RevealDiameterPx,
            SoftEdgeWidthPx = _settings.RevealSoftEdgeWidthPx,
            BlurLevel = _settings.RevealBlurLevel,
            Shape = _settings.RevealShape,
            Trigger = _settings.PeekTrigger
        };
        if (!_coordinator.SelectWindow(target, settings))
        {
            return;
        }

    }

}
