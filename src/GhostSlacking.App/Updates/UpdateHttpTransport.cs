using System.Net;
using System.Net.Http.Headers;

namespace GhostSlacking.App;

internal sealed record UpdateDownloadOptions
{
    public long ParallelThreshold { get; init; } = 8L * 1024 * 1024;
    public int SegmentSize { get; init; } = 2 * 1024 * 1024;
    public int MaxParallelism { get; init; } = 4;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan HeaderTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

internal static class UpdateHttpTransport
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, Uri uri, Func<Uri, bool> allowed,
        CancellationToken cancellationToken, TimeSpan? headerTimeout = null, RangeHeaderValue? range = null, string? etag = null)
    {
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            if (!allowed(uri)) throw new InvalidDataException("The update source rejected an asset URL.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.Range = range;
            if (range is not null && EntityTagHeaderValue.TryParse(etag, out var parsed) && !parsed.IsWeak)
                request.Headers.IfRange = new RangeConditionHeaderValue(parsed);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(headerTimeout ?? TimeSpan.FromSeconds(30));
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException("The update server did not return headers in time.");
            }
            if (response.RequestMessage?.RequestUri is { } finalUri && !allowed(finalUri))
            {
                response.Dispose();
                throw new InvalidDataException("The update source rejected the redirected asset URL.");
            }
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("The download redirect has no location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                response.Dispose();
                throw new UpdateHttpException(status, delay);
            }
            return response;
        }
        throw new InvalidDataException("The asset redirected too many times.");
    }

    public static async Task<byte[]> ReadSmallAsync(HttpClient client, Uri uri, long maximumBytes,
        Func<Uri, bool> allowed, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, uri, allowed, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } size && size > maximumBytes)
            throw new InvalidDataException("The update asset exceeds its size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await ReadAsync(source, buffer, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes) throw new InvalidDataException("The update asset exceeds its size limit.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    public static async Task<int> ReadAsync(Stream stream, Memory<byte> buffer, TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(idleTimeout);
        try { return await stream.ReadAsync(buffer, idle.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The download stopped making progress.");
        }
    }
}

internal sealed class UpdateHttpException(HttpStatusCode status, TimeSpan? retryAfter) : HttpRequestException(
    $"The update server returned HTTP {(int)status}.", null, status)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
