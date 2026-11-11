using GhostSlacking.Core;

namespace GhostSlacking.App;

internal enum ApplicationUpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    NoCompatibleRelease,
    Available,
    Skipped,
    Downloading,
    Verifying,
    Ready,
    Preparing,
    Error
}

internal sealed record UpdateAsset(string Name, long Size, Uri DownloadUrl, string? Digest);

internal sealed record LocalizedReleaseNotes(string? Chinese, string? English)
{
    public string? For(UiLanguage language) => language == UiLanguage.Chinese
        ? Chinese ?? English
        : English ?? Chinese;
}

internal sealed record ReleaseNotesEntry(
    ReleaseVersion Version,
    string VersionText,
    Uri ReleasePageUrl,
    LocalizedReleaseNotes Notes);

internal sealed record UpdateRelease(
    ReleaseVersion Version,
    string VersionText,
    Uri ReleasePageUrl,
    UpdateAsset Installer,
    UpdateAsset Checksum,
    UpdateAsset Manifest,
    string ExpectedSha256,
    IReadOnlyList<ReleaseNotesEntry> ReleaseNotes,
    bool ReleaseHistoryIncomplete = false,
    byte[]? ManifestBytes = null,
    string? SignatureText = null);

internal sealed record ApplicationUpdateSnapshot(
    ApplicationUpdateStatus Status,
    string CurrentVersion,
    UpdateRelease? Release = null,
    int? ProgressPercent = null,
    DateTimeOffset? LastSuccessfulCheckUtc = null);
