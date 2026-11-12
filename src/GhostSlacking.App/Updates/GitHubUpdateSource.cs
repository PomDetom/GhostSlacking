using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed partial class GitHubUpdateSource : IUpdateSource
{
    private const long MaximumInstallerBytes = 256L * 1024 * 1024;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumChecksumBytes = 4096;
    internal static readonly Uri SourceRepositoryUri = new("https://github.com/PomDetom/GhostSlacking");
    internal static readonly Uri ReleasesUri = new("https://github.com/PomDetom/GhostSlacking/releases");
    internal static readonly Uri LatestReleaseApiUri = new("https://api.github.com/repos/PomDetom/GhostSlacking/releases/latest");
    internal static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/PomDetom/GhostSlacking/releases");
    private const int ReleasesPerPage = 100;
    private const int MaximumReleaseHistoryPages = 20;
    private const string ChineseNotesStart = "<!-- release-notes:zh:start -->";
    private const string ChineseNotesEnd = "<!-- release-notes:zh:end -->";
    private const string EnglishNotesStart = "<!-- release-notes:en:start -->";
    private const string EnglishNotesEnd = "<!-- release-notes:en:end -->";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string? _releasePublicKey;
    private UpdateChannel _channel;
    private ReleaseVersion _currentVersion = new(0, 0, 0);

    public GitHubUpdateSource(HttpClient httpClient, ILogger logger, string? releasePublicKey = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _releasePublicKey = releasePublicKey;
    }

    public string Id => "github";
    public Uri RepositoryUri => SourceRepositoryUri;
    public Uri ReleaseListUri => ReleasesUri;

    public void ValidateRelease(UpdateRelease release)
    {
        UpdateReleaseValidation.Validate(release, _releasePublicKey);
        var tag = "v" + release.VersionText;
        if (!IsReleasePageUrl(release.ReleasePageUrl, tag)) throw new InvalidDataException("Invalid release page.");
        foreach (var asset in new[] { release.Installer, release.Checksum, release.Manifest })
        {
            var uri = asset.DownloadUrl;
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/PomDetom/GhostSlacking/releases/download/{tag}/", StringComparison.Ordinal))
                throw new InvalidDataException("A release asset uses an unexpected download URL.");
        }
    }

    public bool IsAllowedAssetUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "release-assets.githubusercontent.com" ||
        uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/PomDetom/GhostSlacking/releases/download/", StringComparison.Ordinal));

    public Task<string> ReadChecksumAsync(UpdateRelease release, CancellationToken cancellationToken) =>
        ReadSmallTextAssetAsync(release.Checksum, MaximumChecksumBytes, cancellationToken);

    private Task<byte[]> ReadSmallBytesAssetAsync(UpdateAsset asset, long maximumBytes, CancellationToken cancellationToken) =>
        UpdateHttpTransport.ReadSmallAsync(_httpClient, asset.DownloadUrl, maximumBytes, IsAllowedAssetUri, cancellationToken);

    private async Task<string> ReadSmallTextAssetAsync(UpdateAsset asset, long maximumBytes, CancellationToken cancellationToken) =>
        System.Text.Encoding.UTF8.GetString(await ReadSmallBytesAssetAsync(asset, maximumBytes, cancellationToken).ConfigureAwait(false));

    private static void ValidateDownloadUri(Uri uri, string tag)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || uri.Host != "github.com" ||
            !uri.AbsolutePath.StartsWith($"/PomDetom/GhostSlacking/releases/download/{tag}/", StringComparison.Ordinal))
            throw new InvalidDataException("A release asset uses an unexpected download URL.");
    }

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
