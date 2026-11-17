using GhostSlacking.Core;

namespace GhostSlacking.App;

internal interface IUpdateSource
{
    string Id { get; }
    Uri RepositoryUri { get; }
    Uri ReleaseListUri { get; }
    Task<UpdateRelease?> GetLatestReleaseAsync(UpdateChannel channel, ReleaseVersion currentVersion, CancellationToken cancellationToken);
    void ValidateRelease(UpdateRelease release);
    bool IsAllowedAssetUri(Uri uri);
    Task<string> ReadChecksumAsync(UpdateRelease release, CancellationToken cancellationToken);
}

internal static class UpdateReleaseValidation
{
    public static SignedReleaseManifest Validate(UpdateRelease release, string? publicKey)
    {
        var signed = SignedReleaseManifest.Verify(release.ManifestBytes ?? [], release.SignatureText ?? string.Empty, publicKey);
        if (release.Version is null || !ReleaseVersion.TryParse(release.VersionText, out var parsed) || parsed != release.Version ||
            signed.Version != release.VersionText || release.Installer is null || release.Checksum is null || release.Manifest is null ||
            release.Checksum.Name != signed.Installer.File + ".sha256" || release.Checksum.Size is <= 0 or > 4096 ||
            release.Manifest.Name != "release.json" || release.Manifest.Size is <= 0 or > 1048576 ||
            release.ReleaseNotes is null || release.ReleaseNotes.Any(entry => entry is null || entry.Version is null || entry.Notes is null) ||
            signed.Installer.File != release.Installer.Name || signed.Installer.Bytes != release.Installer.Size ||
            !string.Equals(signed.Installer.Sha256, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The release does not match its signed manifest.");
        return signed;
    }

    public static void ValidateChecksum(string text, UpdateRelease release)
    {
        var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals(release.ExpectedSha256, StringComparison.OrdinalIgnoreCase) ||
            parts[1].TrimStart('*') != release.Installer.Name)
            throw new InvalidDataException("The checksum asset and signed manifest disagree.");
    }
}
