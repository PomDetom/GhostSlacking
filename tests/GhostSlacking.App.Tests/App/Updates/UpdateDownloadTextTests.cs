using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class UpdateDownloadTextTests
{
    [Theory]
    [InlineData(UiLanguage.Chinese, "剩余 1:05")]
    [InlineData(UiLanguage.English, "Remaining 1:05")]
    public void Slow_downloads_show_readable_kilobyte_rates_and_localized_remaining_time(UiLanguage language, string remaining)
    {
        var text = UpdateDownloadText.Format(language, new(512 * 1024, 16 * 1048576, 26 * 1024, TimeSpan.FromSeconds(65), true));
        Assert.Contains("512.0 KB / 16.0 MB", text);
        Assert.Contains("26.0 KB/s", text);
        Assert.Contains(remaining, text);
    }

    [Fact]
    public void Paused_downloads_keep_byte_progress_without_a_stale_speed_or_eta()
    {
        var text = UpdateDownloadText.Format(UiLanguage.English, new(1048576, 16 * 1048576));
        Assert.Equal("1.0 MB / 16.0 MB", text);
    }

    [Theory]
    [InlineData(UiLanguage.Chinese, "此来源不支持续传")]
    [InlineData(UiLanguage.English, "Resume unavailable")]
    public void Non_resumable_sources_have_an_explicit_hint(UiLanguage language, string hint)
    {
        Assert.Contains(hint, UpdateDownloadText.Format(language, new(0, 1048576, SupportsResume: false)));
    }
}
