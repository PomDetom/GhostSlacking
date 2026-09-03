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
            GhostState.Restoring => "Restoring",
            GhostState.RecoveryError => "Recovery error",
            _ => state.ToString()
        }
    };

    public static string Text(UiLanguage language, string key) => language == UiLanguage.Chinese ? key switch
    {
        "status" => "状态：{0}（{1}）",
        "pick" => "选择窗口",
        "toggle" => "切换 Ghost",
        "restore" => "恢复窗口",
        "restoreAll" => "恢复所有窗口",
        "settings" => "设置",
        "exit" => "退出",
        "clickToPick" => "点击窗口以选择。按 Esc 取消。",
        "noWindow" => "此处没有找到可用的可见顶层窗口。",
        "windowSelected" => "已将 {0} 设为 Ghost。按住 {1} 可查看。",
        "restoreSome" => "部分窗口无法恢复。请再次使用“紧急恢复”并查看日志。",
        "pickFailed" => "无法启动窗口选择。",
        "unexpected" => "发生未预期的界面错误：{0}",
        "title" => "GhostSlacking 设置",
        "diameter" => "Reveal 直径",
        "shape" => "Reveal 形状",
        "circle" => "圆形",
        "rectangle" => "矩形",
        "roundedRectangle" => "圆角矩形",
        "peekKey" => "Peek 按键",
        "restoreOnExit" => "退出时恢复窗口",
        "startWindows" => "随 Windows 启动",
        "logLevel" => "日志级别",
        "cancel" => "取消",
        "save" => "保存",
        "emergency" => "紧急恢复",
        _ => key
    } : key switch
    {
        "status" => "Status: {0} ({1})",
        "pick" => "Pick Window",
        "toggle" => "Toggle Ghost",
        "restore" => "Restore Window",
        "restoreAll" => "Restore All Windows",
        "settings" => "Settings",
        "exit" => "Exit",
        "clickToPick" => "Click a window to select it. Press Esc to cancel.",
        "noWindow" => "No supported visible top-level window was found at that point.",
        "windowSelected" => "Ghosted {0}. Hold {1} to peek.",
        "restoreSome" => "Some windows could not be restored. Try Emergency Restore again and check the log.",
        "pickFailed" => "Could not start window picking.",
        "unexpected" => "Unexpected UI error: {0}",
        "title" => "GhostSlacking Settings",
        "diameter" => "Reveal diameter",
        "shape" => "Reveal shape",
        "circle" => "Circle",
        "rectangle" => "Rectangle",
        "roundedRectangle" => "Rounded rectangle",
        "peekKey" => "Peek key",
        "restoreOnExit" => "Restore windows when exiting",
        "startWindows" => "Start with Windows",
        "logLevel" => "Log level",
        "cancel" => "Cancel",
        "save" => "Save",
        "emergency" => "Emergency Restore",
        _ => key
    };

    public static string PeekKeyName(UiLanguage language, int virtualKey) => virtualKey switch
    {
        0x20 => language == UiLanguage.Chinese ? "空格" : "Space",
        0x10 => language == UiLanguage.Chinese ? "Shift" : "Shift",
        _ => "Alt"
    };

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
