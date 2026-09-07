using GhostSlacking.Core;

namespace GhostSlacking.App;

internal static class UiText
{
    public static bool IsChinese(UiLanguage language) => language == UiLanguage.Chinese;

    public static string LanguageName(UiLanguage language) => IsChinese(language) ? "中文" : "English";

    public static string State(UiLanguage language, GhostState state) => language switch
    {
        UiLanguage.Chinese => state switch
        {
            GhostState.Idle => "空闲",
            GhostState.Picking => "选择窗口",
            GhostState.Preparing => "准备中",
            GhostState.Ghost => "Ghost 隐藏",
            GhostState.Reveal => "Reveal 显示",
            GhostState.Visible => "窗口显示",
            GhostState.Restoring => "恢复中",
            GhostState.RecoveryError => "恢复错误",
            _ => state.ToString()
        },
        _ => state switch
        {
            GhostState.Idle => "Idle",
            GhostState.Picking => "Picking",
            GhostState.Preparing => "Preparing",
            GhostState.Ghost => "Ghost",
            GhostState.Reveal => "Reveal",
            GhostState.Visible => "Visible",
            GhostState.Restoring => "Restoring",
            GhostState.RecoveryError => "Recovery error",
            _ => state.ToString()
        }
    };

    public static string Text(UiLanguage language, string key) => language == UiLanguage.Chinese ? key switch
    {
        "status" => "状态：{0}（{1}）",
        "pick" => "选择窗口",
        "toggle" => "切换窗口显隐",
        "restore" => "恢复窗口",
        "restoreAll" => "恢复所有窗口",
        "settings" => "设置",
        "exit" => "退出",
        "clickToPick" => "点击窗口以选择。按 Esc 取消。",
        "noWindow" => "此处没有找到可用的可见顶层窗口。",
        "windowSelected" => "已将 {0} 设为 Ghost。使用 {1} 可查看。",
        "restoreSome" => "部分窗口无法恢复。请再次使用“紧急恢复”并查看日志。",
        "pickFailed" => "无法启动窗口选择。",
        "unexpected" => "发生未预期的界面错误：{0}",
        "title" => "GhostSlacking 设置",
        "settingsCaption" => "偏好设置",
        "settingsHint" => "更改将在保存后生效",
        "collapseSidebar" => "收起侧栏",
        "expandSidebar" => "展开侧栏",
        "revealSettings" => "显示与操作",
        "revealSettingsDescription" => "调整窗口的 Reveal 外观与 Peek 操作方式",
        "hotkeySettings" => "快捷键",
        "hotkeySettingsDescription" => "为常用操作设置全局组合键",
        "generalSettings" => "常规",
        "generalSettingsDescription" => "管理启动、恢复、语言和诊断选项",
        "revealAppearance" => "REVEAL 外观",
        "peekBehavior" => "PEEK 操作",
        "windowActions" => "窗口操作",
        "applicationActions" => "应用操作",
        "startupAndSafety" => "启动与安全",
        "languageAndDiagnostics" => "语言与诊断",
        "diameter" => "Reveal 直径",
        "diameterDescription" => "光标周围可见区域的大小",
        "shape" => "Reveal 形状",
        "shapeDescription" => "选择局部显示区域的轮廓",
        "circle" => "圆形",
        "rectangle" => "矩形",
        "roundedRectangle" => "圆角矩形",
        "peekKey" => "Peek 按键",
        "peekKeyDescription" => "触发局部显示时使用的单键",
        "pickHotkey" => "选择窗口",
        "pickHotkeyDescription" => "进入目标窗口选择模式",
        "windowToggleHotkey" => "窗口显隐",
        "windowToggleHotkeyDescription" => "切换当前目标的隐藏或显示状态",
        "restoreHotkey" => "恢复当前窗口",
        "restoreHotkeyDescription" => "恢复并解除当前目标窗口",
        "restoreAllHotkey" => "恢复所有窗口",
        "restoreAllHotkeyDescription" => "紧急恢复所有已隐藏窗口",
        "settingsHotkey" => "打开设置",
        "settingsHotkeyDescription" => "随时打开此设置窗口",
        "exitHotkey" => "退出应用",
        "exitHotkeyDescription" => "退出 GhostSlacking",
        "hotkeyNotSet" => "未设置",
        "pressKey" => "请按下按键",
        "pressShortcut" => "请按下组合键",
        "resetHotkey" => "重置为默认按键",
        "duplicateHotkey" => "快捷键 {0} 已分配给多个操作，请为每个操作设置不同的组合键。",
        "selectWindowFirst" => "当前没有目标窗口，请先使用“选择窗口快捷键”选择窗口。",
        "peekMode" => "Peek 模式",
        "peekModeDescription" => "选择按住显示或按键切换",
        "holdPeek" => "按住显示",
        "togglePeek" => "按下切换",
        "restoreOnExit" => "退出时恢复窗口",
        "restoreOnExitDescription" => "关闭应用前自动恢复隐藏窗口",
        "startWindows" => "随 Windows 启动",
        "startWindowsDescription" => "登录系统后自动在托盘中运行",
        "logLevel" => "日志级别",
        "logLevelDescription" => "控制诊断日志记录的详细程度",
        "interfaceLanguage" => "界面语言",
        "languageDescription" => "保存后更新应用界面语言",
        "cancel" => "取消",
        "save" => "保存",
        "saveSucceeded" => "设置已保存",
        "emergency" => "紧急恢复",
        _ => key
    } : key switch
    {
        "status" => "Status: {0} ({1})",
        "pick" => "Pick Window",
        "toggle" => "Toggle window visibility",
        "restore" => "Restore Window",
        "restoreAll" => "Restore All Windows",
        "settings" => "Settings",
        "exit" => "Exit",
        "clickToPick" => "Click a window to select it. Press Esc to cancel.",
        "noWindow" => "No supported visible top-level window was found at that point.",
        "windowSelected" => "Ghosted {0}. Use {1} to peek.",
        "restoreSome" => "Some windows could not be restored. Try Emergency Restore again and check the log.",
        "pickFailed" => "Could not start window picking.",
        "unexpected" => "Unexpected UI error: {0}",
        "title" => "GhostSlacking Settings",
        "settingsCaption" => "Preferences",
        "settingsHint" => "Changes take effect after saving",
        "collapseSidebar" => "Collapse sidebar",
        "expandSidebar" => "Expand sidebar",
        "revealSettings" => "Reveal & Peek",
        "revealSettingsDescription" => "Adjust the Reveal appearance and how Peek behaves",
        "hotkeySettings" => "Keyboard shortcuts",
        "hotkeySettingsDescription" => "Assign global shortcuts to common actions",
        "generalSettings" => "General",
        "generalSettingsDescription" => "Manage startup, recovery, language, and diagnostics",
        "revealAppearance" => "REVEAL APPEARANCE",
        "peekBehavior" => "PEEK BEHAVIOR",
        "windowActions" => "WINDOW ACTIONS",
        "applicationActions" => "APPLICATION ACTIONS",
        "startupAndSafety" => "STARTUP & SAFETY",
        "languageAndDiagnostics" => "LANGUAGE & DIAGNOSTICS",
        "diameter" => "Reveal diameter",
        "diameterDescription" => "Size of the visible area around the pointer",
        "shape" => "Reveal shape",
        "shapeDescription" => "Choose the outline of the locally visible area",
        "circle" => "Circle",
        "rectangle" => "Rectangle",
        "roundedRectangle" => "Rounded rectangle",
        "peekKey" => "Peek key",
        "peekKeyDescription" => "Single key used to trigger the local Reveal",
        "pickHotkey" => "Pick window",
        "pickHotkeyDescription" => "Enter target-window selection mode",
        "windowToggleHotkey" => "Window visibility",
        "windowToggleHotkeyDescription" => "Hide or show the current target",
        "restoreHotkey" => "Restore current window",
        "restoreHotkeyDescription" => "Restore and release the current target",
        "restoreAllHotkey" => "Restore all windows",
        "restoreAllHotkeyDescription" => "Emergency restore for every hidden window",
        "settingsHotkey" => "Open settings",
        "settingsHotkeyDescription" => "Open this settings window from anywhere",
        "exitHotkey" => "Exit application",
        "exitHotkeyDescription" => "Quit GhostSlacking",
        "hotkeyNotSet" => "Not set",
        "pressKey" => "Press a key",
        "pressShortcut" => "Press a shortcut",
        "resetHotkey" => "Reset to default",
        "duplicateHotkey" => "The shortcut {0} is assigned to multiple actions. Assign a different shortcut to each action.",
        "selectWindowFirst" => "No target window is selected. Use the Pick window hotkey first.",
        "peekMode" => "Peek mode",
        "peekModeDescription" => "Choose between hold-to-show and press-to-toggle",
        "holdPeek" => "Hold to show",
        "togglePeek" => "Press to toggle",
        "restoreOnExit" => "Restore windows when exiting",
        "restoreOnExitDescription" => "Restore hidden windows before the app closes",
        "startWindows" => "Start with Windows",
        "startWindowsDescription" => "Run in the system tray after sign-in",
        "logLevel" => "Log level",
        "logLevelDescription" => "Control how much diagnostic detail is recorded",
        "interfaceLanguage" => "Interface language",
        "languageDescription" => "Update the app language after saving",
        "cancel" => "Cancel",
        "save" => "Save",
        "saveSucceeded" => "Settings saved",
        "emergency" => "Emergency Restore",
        _ => key
    };

    public static string PeekKeyName(UiLanguage language, int virtualKey)
    {
        var name = virtualKey switch
        {
            0x08 => language == UiLanguage.Chinese ? "退格" : "Backspace",
            0x09 => "Tab",
            0x0D => language == UiLanguage.Chinese ? "回车" : "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => language == UiLanguage.Chinese ? "Caps Lock" : "Caps Lock",
            0x1B => language == UiLanguage.Chinese ? "Esc" : "Esc",
            0x20 => language == UiLanguage.Chinese ? "空格" : "Space",
            0x21 => language == UiLanguage.Chinese ? "Page Up" : "Page Up",
            0x22 => language == UiLanguage.Chinese ? "Page Down" : "Page Down",
            0x23 => language == UiLanguage.Chinese ? "End" : "End",
            0x24 => language == UiLanguage.Chinese ? "Home" : "Home",
            0x25 => language == UiLanguage.Chinese ? "左方向键" : "Left Arrow",
            0x26 => language == UiLanguage.Chinese ? "上方向键" : "Up Arrow",
            0x27 => language == UiLanguage.Chinese ? "右方向键" : "Right Arrow",
            0x28 => language == UiLanguage.Chinese ? "下方向键" : "Down Arrow",
            0x2C => language == UiLanguage.Chinese ? "截图键" : "Print Screen",
            0x2D => language == UiLanguage.Chinese ? "Insert" : "Insert",
            0x2E => language == UiLanguage.Chinese ? "Delete" : "Delete",
            0x5B => language == UiLanguage.Chinese ? "左 Windows" : "Left Windows",
            0x5C => language == UiLanguage.Chinese ? "右 Windows" : "Right Windows",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0xA0 => language == UiLanguage.Chinese ? "左 Shift" : "Left Shift",
            0xA1 => language == UiLanguage.Chinese ? "右 Shift" : "Right Shift",
            0xA2 => language == UiLanguage.Chinese ? "左 Ctrl" : "Left Ctrl",
            0xA3 => language == UiLanguage.Chinese ? "右 Ctrl" : "Right Ctrl",
            0xA4 => language == UiLanguage.Chinese ? "左 Alt" : "Left Alt",
            0xA5 => language == UiLanguage.Chinese ? "右 Alt" : "Right Alt",
            _ when virtualKey is >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            _ when virtualKey is >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            _ when virtualKey is >= 0x60 and <= 0x69 => $"NumPad {virtualKey - 0x60}",
            _ when virtualKey is >= 0x70 and <= 0x7B => $"F{virtualKey - 0x6F}",
            _ => language == UiLanguage.Chinese ? $"按键 0x{virtualKey:X2}" : $"Key 0x{virtualKey:X2}"
        };

        return name;
    }

    public static string ShortcutName(UiLanguage language, HotkeyBinding binding)
    {
        if (binding.IsDisabled)
        {
            return Text(language, "hotkeyNotSet");
        }

        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(PeekKeyName(language, binding.VirtualKey));
        return string.Join("+", parts);
    }

    public static string Error(UiLanguage language, string message)
    {
        if (language == UiLanguage.English)
        {
            return message;
        }

        return message switch
        {
            "Could not start window picking." => Text(language, "pickFailed"),
            "No supported visible top-level window was found at that point." => Text(language, "noWindow"),
            "One or more windows could not be restored." => Text(language, "restoreSome"),
            _ when message.StartsWith("Restore failed:", StringComparison.Ordinal) => "恢复失败：" + message["Restore failed:".Length..].Trim(),
            _ => message
        };
    }
}
