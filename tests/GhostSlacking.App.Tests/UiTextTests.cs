using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class UiTextTests
{
    [Theory]
    [InlineData(UiLanguage.Chinese, "GhostSlacking 已启动，正在系统托盘运行。")]
    [InlineData(UiLanguage.English, "GhostSlacking is running in the system tray.")]
    public void Startup_notification_matches_the_selected_language(UiLanguage language, string expected)
    {
        Assert.Equal(expected, UiText.Text(language, "startupReady"));
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "窗口选择已开启。请单击目标窗口，或按 Esc 取消。")]
    [InlineData(UiLanguage.English, "Window selection is active. Click the target window, or press Esc to cancel.")]
    public void Window_picker_prompt_matches_the_selected_language(UiLanguage language, string expected)
    {
        Assert.Equal(expected, UiText.Text(language, "clickToPick"));
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "已取消选择窗口。")]
    [InlineData(UiLanguage.English, "Window selection cancelled.")]
    public void Escape_cancellation_has_localized_feedback(UiLanguage language, string expected)
    {
        Assert.Equal(expected, UiText.Text(language, "pickCancelled"));
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "导出日志", "导出配置", "导出成功")]
    [InlineData(UiLanguage.English, "Export logs", "Export settings", "Export completed")]
    public void Export_actions_have_localized_copy(
        UiLanguage language,
        string logs,
        string settings,
        string success)
    {
        Assert.Equal(logs, UiText.Text(language, "exportLogs"));
        Assert.Equal(settings, UiText.Text(language, "exportSettings"));
        Assert.Equal(success, UiText.Text(language, "exportSucceeded"));
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "GhostSlacking 已成功升级到版本 1.2.0。", "已取消软件更新，GhostSlacking 已恢复运行。", "版本 1.2.0 自动升级失败，GhostSlacking 已恢复运行。请重试或查看日志。")]
    [InlineData(UiLanguage.English, "GhostSlacking was updated successfully to version 1.2.0.", "The software update was cancelled and GhostSlacking is running again.", "The automatic update to version 1.2.0 failed. GhostSlacking is running again; retry or check the logs.")]
    public void Automatic_update_results_have_localized_feedback(
        UiLanguage language,
        string succeeded,
        string cancelled,
        string failed)
    {
        Assert.Equal(succeeded, string.Format(UiText.Text(language, "updateCompleted"), "1.2.0"));
        Assert.Equal(cancelled, string.Format(UiText.Text(language, "updateCancelled"), "1.2.0"));
        Assert.Equal(failed, string.Format(UiText.Text(language, "automaticUpdateFailed"), "1.2.0"));
    }

    [Theory]
    [InlineData(UserErrorKind.SelectionStateCaptureFailed, "无法选择该窗口。未能安全保存窗口状态，未作任何更改。")]
    [InlineData(UserErrorKind.GhostActivationFailed, "无法隐藏所选窗口。已尝试恢复原状态，请确认窗口显示正常。")]
    [InlineData(UserErrorKind.WindowPlacementCorrectionFailed, "无法保持窗口位置。正在恢复窗口，请确认其显示正常。")]
    [InlineData(UserErrorKind.HideWindowFailed, "无法隐藏目标窗口。请执行“恢复窗口”退出隐藏模式。")]
    [InlineData(UserErrorKind.RevealWindowFailed, "无法更新局部显示。请重试，或执行“恢复窗口”。")]
    [InlineData(UserErrorKind.RestoreFailed, "未能完整恢复窗口。请再次执行“恢复所有窗口”，或查看日志。")]
    public void Core_errors_have_scenario_specific_Chinese_copy(UserErrorKind kind, string expected)
    {
        Assert.Equal(expected, UiText.Error(UiLanguage.Chinese, kind));
    }

    [Theory]
    [InlineData(UserErrorKind.SelectionStateCaptureFailed, "That window could not be selected because its state could not be saved safely.")]
    [InlineData(UserErrorKind.GhostActivationFailed, "The selected window could not be hidden. Check that it was restored normally.")]
    [InlineData(UserErrorKind.WindowPlacementCorrectionFailed, "The window position could not be preserved. Check that it was restored normally.")]
    [InlineData(UserErrorKind.HideWindowFailed, "The target could not be hidden. Run Restore Window to leave hidden mode.")]
    [InlineData(UserErrorKind.RevealWindowFailed, "The local reveal could not be updated. Retry or run Restore Window.")]
    [InlineData(UserErrorKind.RestoreFailed, "The window could not be fully restored. Run Restore All Windows again or check the log.")]
    public void Core_errors_have_scenario_specific_English_copy(UserErrorKind kind, string expected)
    {
        Assert.Equal(expected, UiText.Error(UiLanguage.English, kind));
    }

    [Fact]
    public void Unknown_core_error_kind_uses_a_localized_safe_fallback()
    {
        var unknown = (UserErrorKind)int.MaxValue;

        Assert.Equal(
            "操作未能完成。请重试；若仍失败，请查看日志。",
            UiText.Error(UiLanguage.Chinese, unknown));
        Assert.Equal(
            "The operation could not be completed. Try again; if it still fails, check the log.",
            UiText.Error(UiLanguage.English, unknown));
    }
}
