using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GhostSlacking.Core;
using GhostSlacking.Platform;
using Microsoft.Win32;

namespace GhostSlacking.App;

internal sealed partial class GitHubApplicationUpdateManager : IApplicationUpdateManager
{

    private async Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var requestUri = _channel == UpdateChannel.Test
            ? new Uri($"{ReleasesApiUri.AbsoluteUri}?per_page={ReleasesPerPage}")
            : LatestReleaseApiUri;
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var apiReleases = _channel == UpdateChannel.Test
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<ApiRelease>>(JsonOptions, cancellationToken).ConfigureAwait(false)
            : [await response.Content.ReadFromJsonAsync<ApiRelease>(JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("GitHub returned an empty release.")];
        if (apiReleases is null || apiReleases.Count == 0)
        {
            throw new InvalidDataException("GitHub returned no releases.");
        }

        var candidates = apiReleases
            .Where(release => !release.Draft && (_channel == UpdateChannel.Test || !release.Prerelease))
            .Select(release => (Release: release, Version: TryParseReleaseVersion(release.TagName)))
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidDataException("No valid release was found for the selected update channel.");
        }

        var skippedLegacyInstaller = false;
        var skippedInvalidRelease = false;
        foreach (var candidate in candidates)
        {
            if (HasLegacyMsiInstaller(candidate.Release, candidate.Version!))
            {
                skippedLegacyInstaller = true;
                _logger.Log(LogLevel.Info,
                    $"Skipping legacy MSI release {candidate.Release.TagName}; signed NSIS installers are required.");
                continue;
            }

            try
            {
                return await CreateUpdateReleaseAsync(candidate.Release, candidate.Version!, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                skippedInvalidRelease = true;
                _logger.Log(LogLevel.Warning, $"Skipping invalid GitHub release {candidate.Release.TagName}.", exception);
            }
        }

        if (skippedLegacyInstaller && !skippedInvalidRelease)
        {
            return null;
        }

        throw new InvalidDataException("No valid installable release was found.");
    }

    private static bool HasLegacyMsiInstaller(ApiRelease release, ReleaseVersion version)
    {
        var expectedMsiName = $"GhostSlacking-{version.Text}-win-x64.msi";
        var hasMsi = release.Assets?.Any(asset =>
            string.Equals(asset.Name, expectedMsiName, StringComparison.Ordinal) &&
            string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase)) == true;
        var hasNsis = release.Assets?.Any(asset =>
            string.Equals(asset.Name, SignedReleaseManifest.InstallerName(version.Text), StringComparison.Ordinal) &&
            string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase)) == true;
        return hasMsi && !hasNsis;
    }

    private async Task<UpdateRelease> CreateUpdateReleaseAsync(
        ApiRelease apiRelease,
        ReleaseVersion releaseVersion,
        CancellationToken cancellationToken)
    {
        if (apiRelease.TagName is null || !Uri.TryCreate(apiRelease.HtmlUrl, UriKind.Absolute, out var releasePageUrl) ||
            !IsReleasePageUrl(releasePageUrl, apiRelease.TagName))
        {
            throw new InvalidDataException("The release version or URL is invalid.");
        }

        var versionText = releaseVersion.Text;
        var installerName = SignedReleaseManifest.InstallerName(versionText);
        var installer = FindAsset(apiRelease.Assets, installerName);
        var checksum = FindAsset(apiRelease.Assets, $"{installerName}.sha256");
        var manifest = FindAsset(apiRelease.Assets, "release.json");
        var signature = FindAsset(apiRelease.Assets, "release.json.sig");
        if (installer.Size > MaximumInstallerBytes || checksum.Size > MaximumChecksumBytes ||
            manifest.Size > MaximumManifestBytes || signature.Size > MaximumChecksumBytes)
        {
            throw new InvalidDataException("One or more release assets exceed the allowed size.");
        }
        ValidateDownloadUri(installer.DownloadUrl, apiRelease.TagName);
        ValidateDownloadUri(checksum.DownloadUrl, apiRelease.TagName);
        ValidateDownloadUri(manifest.DownloadUrl, apiRelease.TagName);
        ValidateDownloadUri(signature.DownloadUrl, apiRelease.TagName);

        var manifestBytes = await ReadSmallBytesAssetAsync(
                manifest,
                MaximumManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        var signatureText = await ReadSmallTextAssetAsync(signature, MaximumChecksumBytes, cancellationToken).ConfigureAwait(false);
        var releaseManifest = SignedReleaseManifest.Verify(manifestBytes, signatureText, _releasePublicKey);
        if (!string.Equals(releaseManifest.Product, "GhostSlacking", StringComparison.Ordinal) ||
            !string.Equals(releaseManifest.Version, versionText, StringComparison.Ordinal) ||
            !string.Equals(releaseManifest.Architecture, "win-x64", StringComparison.Ordinal) ||
            releaseManifest.Installer is null ||
            !string.Equals(releaseManifest.Installer.File, installerName, StringComparison.Ordinal) ||
            releaseManifest.Installer.Bytes != installer.Size ||
            releaseManifest.Installer.Sha256 is null ||
            !Sha256Pattern().IsMatch(releaseManifest.Installer.Sha256))
        {
            throw new InvalidDataException("The release manifest does not match the GitHub release.");
        }

        var expectedHash = releaseManifest.Installer.Sha256.ToUpperInvariant();
        if (installer.Digest is not null &&
            (!installer.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(installer.Digest[7..], expectedHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The GitHub asset digest and release manifest disagree.");
        }

        var targetNotes = CreateReleaseNotesEntry(
            releaseVersion,
            versionText,
            releasePageUrl,
            apiRelease.Body);
        var (releaseNotes, historyIncomplete) = releaseVersion > _currentVersion
            ? await GetReleaseNotesHistoryAsync(targetNotes, releaseVersion, cancellationToken).ConfigureAwait(false)
            : ([targetNotes], false);

        return new UpdateRelease(
            releaseVersion,
            versionText,
            releasePageUrl,
            installer,
            checksum,
            manifest,
            expectedHash,
            releaseNotes,
            historyIncomplete,
            manifestBytes,
            signatureText);
    }

    private async Task<(IReadOnlyList<ReleaseNotesEntry> Notes, bool Incomplete)> GetReleaseNotesHistoryAsync(
        ReleaseNotesEntry targetNotes,
        ReleaseVersion targetVersion,
        CancellationToken cancellationToken)
    {
        var notesByVersion = new Dictionary<ReleaseVersion, ReleaseNotesEntry>
        {
            [targetNotes.Version] = targetNotes
        };
        var incomplete = false;

        try
        {
            for (var page = 1; page <= MaximumReleaseHistoryPages; page++)
            {
                var pageUri = new Uri($"{ReleasesApiUri.AbsoluteUri}?per_page={ReleasesPerPage}&page={page}");
                using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var releases = await response.Content.ReadFromJsonAsync<IReadOnlyList<ApiRelease>>(
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false) ?? throw new InvalidDataException("GitHub returned empty release history.");

                var reachedCurrentVersion = false;
                foreach (var release in releases)
                {
                    if (release.Draft || (_channel == UpdateChannel.Stable && release.Prerelease) || release.TagName is null ||
                        !ReleaseTagPattern().IsMatch(release.TagName) ||
                        TryParseReleaseVersion(release.TagName) is not { } version)
                    {
                        continue;
                    }

                    if (version <= _currentVersion)
                    {
                        reachedCurrentVersion = true;
                        continue;
                    }

                    if (version > targetVersion)
                    {
                        continue;
                    }

                    if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releasePageUrl) ||
                        !IsReleasePageUrl(releasePageUrl, release.TagName))
                    {
                        incomplete = true;
                        continue;
                    }

                    notesByVersion.TryAdd(
                        version,
                        CreateReleaseNotesEntry(version, version.Text, releasePageUrl, release.Body));
                }

                if (releases.Count < ReleasesPerPage || reachedCurrentVersion)
                {
                    return (notesByVersion.Values.OrderByDescending(entry => entry.Version).ToArray(), incomplete);
                }
            }

            _logger.Log(LogLevel.Warning, "GitHub release history exceeded the supported pagination limit.");
            return (notesByVersion.Values.OrderByDescending(entry => entry.Version).ToArray(), true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
        {
            _logger.Log(LogLevel.Warning, "GitHub release notes history could not be loaded; using the latest release notes only.", exception);
            return (notesByVersion.Values.OrderByDescending(entry => entry.Version).ToArray(), true);
        }
    }

    internal static LocalizedReleaseNotes ParseReleaseNotes(string? body) => new(
        ExtractReleaseNotesSection(body, ChineseNotesStart, ChineseNotesEnd),
        ExtractReleaseNotesSection(body, EnglishNotesStart, EnglishNotesEnd));

    private static ReleaseNotesEntry CreateReleaseNotesEntry(
        ReleaseVersion version,
        string versionText,
        Uri releasePageUrl,
        string? body) => new(version, versionText, releasePageUrl, ParseReleaseNotes(body));

    private static ReleaseVersion? TryParseReleaseVersion(string? tag)
    {
        if (tag is null || !ReleaseTagPattern().IsMatch(tag))
        {
            return null;
        }

        return ReleaseVersion.TryParse(tag[1..], out var version) ? version : null;
    }

    private static string? ExtractReleaseNotesSection(string? body, string startMarker, string endMarker)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var start = body.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0 || body.IndexOf(startMarker, start + startMarker.Length, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        start += startMarker.Length;
        var end = body.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0 || body.IndexOf(endMarker, end + endMarker.Length, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        var value = body[start..end]
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        return value.Length == 0 ? null : value;
    }

    private static UpdateAsset FindAsset(IReadOnlyList<ApiAsset>? assets, string name)
    {
        var matches = assets?
            .Where(asset => string.Equals(asset.Name, name, StringComparison.Ordinal) &&
                            string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        if (matches.Length != 1 || matches[0].Size <= 0 ||
            !Uri.TryCreate(matches[0].BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl))
        {
            throw new InvalidDataException($"Required release asset is missing or duplicated: {name}");
        }

        return new UpdateAsset(name, matches[0].Size, downloadUrl, matches[0].Digest);
    }

    private static bool IsReleasePageUrl(Uri uri, string tag) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.AbsolutePath, $"/PomDetom/GhostSlacking/releases/tag/{tag}", StringComparison.Ordinal);

    [GeneratedRegex(@"^v\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagPattern();

    private sealed record ApiRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        string? Body,
        bool Draft,
        bool Prerelease,
        IReadOnlyList<ApiAsset>? Assets);

    private sealed record ApiAsset(
        string? Name,
        long Size,
        string? State,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
        string? Digest);

}
