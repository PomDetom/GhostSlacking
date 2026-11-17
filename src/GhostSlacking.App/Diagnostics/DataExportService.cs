using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal enum DataExportStatus
{
    Success,
    PartialSuccess,
    Failure
}

internal sealed record DataExportResult(DataExportStatus Status, string? Error = null)
{
    public static DataExportResult Success() => new(DataExportStatus.Success);
    public static DataExportResult PartialSuccess() => new(DataExportStatus.PartialSuccess);
    public static DataExportResult Failure(string? error = null) => new(DataExportStatus.Failure, error);
}

internal sealed class DataExportService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Assembly _applicationAssembly;
    private readonly string _logDirectory;
    private readonly ILogger _logger;

    public DataExportService(ILogger logger, string? logDirectory = null, Assembly? applicationAssembly = null)
    {
        _logger = logger;
        _logDirectory = logDirectory ?? GhostSlackingDataPaths.LogDirectory;
        _applicationAssembly = applicationAssembly ?? typeof(GhostApplicationController).Assembly;
    }

    public async Task<DataExportResult> ExportLogsAsync(
        Stream destination,
        LogLevel minimumLogLevel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var included = new List<LogFileSnapshot>();
            var skipped = new List<SkippedLogFile>();
            foreach (var fileName in ManagedLogFileNames())
            {
                var path = Path.Combine(_logDirectory, fileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    await using var source = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var snapshot = new MemoryStream();
                    await source.CopyToAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    included.Add(new LogFileSnapshot(
                        fileName,
                        snapshot.ToArray(),
                        File.GetLastWriteTimeUtc(path)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new SkippedLogFile(fileName, exception.GetType().Name));
                }
            }

            using var package = new MemoryStream();
            using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var log in included)
                {
                    var entry = archive.CreateEntry($"logs/{log.FileName}", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(log.Content, cancellationToken).ConfigureAwait(false);
                }

                var manifest = new DiagnosticManifest(
                    DateTimeOffset.UtcNow,
                    ApplicationVersion(),
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    minimumLogLevel.ToString(),
                    included.Select(log => new IncludedLogFile(log.FileName, log.Content.LongLength, log.LastWriteTimeUtc)).ToArray(),
                    skipped);
                var manifestEntry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
                await manifestStream.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
            }

            await ReplaceDestinationAsync(destination, package, cancellationToken).ConfigureAwait(false);
            return skipped.Count == 0
                ? DataExportResult.Success()
                : DataExportResult.PartialSuccess();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Log(LogLevel.Error, "Unable to export diagnostic logs.", exception);
            TryClearDestination(destination);
            return DataExportResult.Failure(exception.GetType().Name);
        }
    }

    public async Task<DataExportResult> ExportSettingsAsync(
        Stream destination,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var content = Encoding.UTF8.GetBytes(AppSettingsJson.Serialize(settings));
            using var snapshot = new MemoryStream(content, writable: false);
            await ReplaceDestinationAsync(destination, snapshot, cancellationToken).ConfigureAwait(false);
            return DataExportResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Error, "Unable to export settings.", exception);
            TryClearDestination(destination);
            return DataExportResult.Failure(exception.GetType().Name);
        }
    }

    private string ApplicationVersion() =>
        _applicationAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        _applicationAssembly.GetName().Version?.ToString() ??
        "unknown";

    private static IEnumerable<string> ManagedLogFileNames()
    {
        foreach (var prefix in new[] { "ghostslacking", "watchdog" })
        {
            yield return $"{prefix}.log";
            for (var index = 1; index < LogRetentionOptions.Default.MaximumFileCount; index++)
            {
                yield return $"{prefix}.{index}.log";
            }
        }
    }

    private static async Task ReplaceDestinationAsync(
        Stream destination,
        MemoryStream content,
        CancellationToken cancellationToken)
    {
        if (!destination.CanWrite)
        {
            throw new IOException("The selected destination is not writable.");
        }

        if (destination.CanSeek)
        {
            destination.Position = 0;
            destination.SetLength(0);
        }

        content.Position = 0;
        await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryClearDestination(Stream destination)
    {
        try
        {
            if (destination.CanSeek && destination.CanWrite)
            {
                destination.Position = 0;
                destination.SetLength(0);
            }
        }
        catch
        {
            // The original export error is more useful than a cleanup failure.
        }
    }

    private sealed record LogFileSnapshot(string FileName, byte[] Content, DateTime LastWriteTimeUtc);

    private sealed record IncludedLogFile(string FileName, long SizeBytes, DateTime LastWriteTimeUtc);

    private sealed record SkippedLogFile(string FileName, string Reason);

    private sealed record DiagnosticManifest(
        DateTimeOffset ExportedAtUtc,
        string ApplicationVersion,
        string OperatingSystem,
        string Framework,
        string ProcessArchitecture,
        string MinimumLogLevel,
        IReadOnlyList<IncludedLogFile> IncludedFiles,
        IReadOnlyList<SkippedLogFile> SkippedFiles);
}
