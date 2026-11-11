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

    public async Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? partialPath = null;
        try
        {
            var release = Snapshot.Release;
            if (release is null || release.Version <= _currentVersion || !CanInstallUpdates)
            {
                return null;
            }

            var releaseDirectory = Path.Combine(_updatesDirectory, release.VersionText);
            Directory.CreateDirectory(releaseDirectory);
            var installerPath = Path.Combine(releaseDirectory, release.Installer.Name);
            partialPath = $"{installerPath}.partial";
            File.Delete(partialPath);

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Downloading, release, 0));

            using var response = await _httpClient.GetAsync(
                release.Installer.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithProgressAsync(source, destination, release.Installer.Size, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Verifying, release));

            var fileInfo = new FileInfo(partialPath);
            if (fileInfo.Length != release.Installer.Size)
            {
                throw new InvalidDataException("The installer size does not match the release manifest.");
            }

            var checksumText = await ReadSmallTextAssetAsync(
                    release.Checksum,
                    MaximumChecksumBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var expectedFromChecksum = ParseChecksum(checksumText, release.Installer.Name);
            if (!string.Equals(expectedFromChecksum, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The checksum asset and release manifest disagree.");
            }

            string actualHash;
            await using (var installer = File.OpenRead(partialPath))
            {
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken));
            }
            if (!string.Equals(actualHash, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded installer checksum is invalid.");
            }

            File.Move(partialPath, installerPath, overwrite: true);
            await File.WriteAllBytesAsync(Path.Combine(releaseDirectory, "release.json"), release.ManifestBytes!, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(releaseDirectory, "release.json.sig"), release.SignatureText!, cancellationToken);
            partialPath = null;
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Ready, release, 100));
            return installerPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (partialPath is not null)
            {
                TryDelete(partialPath);
            }

            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Available, Snapshot.Release));
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            if (partialPath is not null)
            {
                TryDelete(partialPath);
            }

            _logger.Log(LogLevel.Warning, "Update installer download or validation failed.", exception);
            SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Error, Snapshot.Release));
            return null;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static void ValidateDownloadUri(Uri uri, string tag)
    {
        var expectedPrefix = $"/PomDetom/GhostSlacking/releases/download/{tag}/";
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A release asset uses an unexpected download URL.");
        }
    }

    private async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        var lastPercent = -1;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            if (total > expectedSize || total > MaximumInstallerBytes)
            {
                throw new InvalidDataException("The installer is larger than the release metadata allows.");
            }

            var percent = expectedSize <= 0 ? 0 : (int)Math.Clamp((total * 100) / expectedSize, 0, 100);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                SetSnapshot(CreateSnapshot(ApplicationUpdateStatus.Downloading, Snapshot.Release, percent));
            }
        }
    }

    private async Task<string> ReadSmallTextAssetAsync(
        UpdateAsset asset, long maximumBytes, CancellationToken cancellationToken) =>
        Encoding.UTF8.GetString(await ReadSmallBytesAssetAsync(asset, maximumBytes, cancellationToken));

    private async Task<byte[]> ReadSmallBytesAssetAsync(
        UpdateAsset asset,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maximumBytes)
        {
            throw new InvalidDataException($"Release asset is too large: {asset.Name}");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Release asset is too large: {asset.Name}");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static string ParseChecksum(string content, string installerName)
    {
        var match = ChecksumPattern().Match(content.Trim());
        if (!match.Success || !string.Equals(match.Groups[2].Value, installerName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checksum asset is invalid.");
        }

        return match.Groups[1].Value.ToUpperInvariant();
    }

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\s+\*?(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumPattern();

}
