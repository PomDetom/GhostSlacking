using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class UiTextTests
{
    [Theory]
    [InlineData(UiLanguage.Chinese, "程序已启动")]
    [InlineData(UiLanguage.English, "Ready")]
    public void Startup_notification_matches_the_selected_language(UiLanguage language, string expected)
    {
        Assert.Equal(expected, UiText.Text(language, "startupReady"));
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "请点击窗口")]
    [InlineData(UiLanguage.English, "Select")]
    public void Window_picker_prompt_matches_the_selected_language(UiLanguage language, string expected)
    {
        Assert.Equal(expected, UiText.Text(language, "clickToPick"));
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "已取消选择")]
    [InlineData(UiLanguage.English, "Cancelled")]
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
    [InlineData(UiLanguage.Chinese, "更新已完成", "已取消更新", "更新未成功")]
    [InlineData(UiLanguage.English, "Updated", "Cancelled", "Failed")]
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
    [InlineData(UserErrorKind.SelectionStateCaptureFailed, "选窗未成功")]
    [InlineData(UserErrorKind.GhostActivationFailed, "隐藏未成功")]
    [InlineData(UserErrorKind.WindowPlacementCorrectionFailed, "窗口需恢复")]
    [InlineData(UserErrorKind.HideWindowFailed, "隐藏未成功")]
    [InlineData(UserErrorKind.RevealWindowFailed, "显示未成功")]
    [InlineData(UserErrorKind.RestoreFailed, "恢复未成功")]
    public void Core_errors_have_scenario_specific_Chinese_copy(UserErrorKind kind, string expected)
    {
        Assert.Equal(expected, UiText.Error(UiLanguage.Chinese, kind));
    }

    [Theory]
    [InlineData(UserErrorKind.SelectionStateCaptureFailed, "Failed")]
    [InlineData(UserErrorKind.GhostActivationFailed, "Failed")]
    [InlineData(UserErrorKind.WindowPlacementCorrectionFailed, "Restore")]
    [InlineData(UserErrorKind.HideWindowFailed, "Failed")]
    [InlineData(UserErrorKind.RevealWindowFailed, "Failed")]
    [InlineData(UserErrorKind.RestoreFailed, "Failed")]
    public void Core_errors_have_scenario_specific_English_copy(UserErrorKind kind, string expected)
    {
        Assert.Equal(expected, UiText.Error(UiLanguage.English, kind));
    }

    [Fact]
    public void Unknown_core_error_kind_uses_a_localized_safe_fallback()
    {
        var unknown = (UserErrorKind)int.MaxValue;

        Assert.Equal(
            "操作未成功",
            UiText.Error(UiLanguage.Chinese, unknown));
        Assert.Equal(
            "Failed",
            UiText.Error(UiLanguage.English, unknown));
    }
}
