using System.Globalization;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal static class UpdateDownloadText
{
    public static string Format(UiLanguage language, UpdateDownloadProgress? progress)
    {
        if (progress is null) return string.Empty;
        string Bytes(double bytes) => bytes >= 1048576
            ? (bytes / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : bytes >= 1024 ? (bytes / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " KB"
            : bytes.ToString("0", CultureInfo.InvariantCulture) + " B";
        var text = $"{Bytes(progress.DownloadedBytes)} / {Bytes(progress.TotalBytes)}";
        if (progress.BytesPerSecond > 0)
        {
            text += $" · {Bytes(progress.BytesPerSecond)}/s";
            if (progress.Remaining is { } remaining)
                text += " · " + string.Format(UiText.Text(language, "downloadRemaining"),
                    $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}");
        }
        if (progress.SupportsResume == false) text += " · " + UiText.Text(language, "downloadNoResume");
        return text;
    }
}
