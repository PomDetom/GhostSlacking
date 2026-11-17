using System.IO.Compression;
using System.Text.Json;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class DataExportServiceTests
{
    [Fact]
    public async Task Log_export_without_existing_logs_still_contains_a_manifest()
    {
        using var directory = new TemporaryDirectory();
        var service = new DataExportService(NullLogger.Instance, directory.Path);
        using var destination = new MemoryStream();

        var result = await service.ExportLogsAsync(destination, LogLevel.Info);

        Assert.Equal(DataExportStatus.Success, result.Status);
        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);
        Assert.Single(archive.Entries);
        Assert.NotNull(archive.GetEntry("diagnostics.json"));
    }

    [Fact]
    public async Task Log_export_contains_managed_logs_and_privacy_safe_manifest()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "ghostslacking.log"), "app-log");
        File.WriteAllText(Path.Combine(directory.Path, "watchdog.1.log"), "watchdog-log");
        File.WriteAllText(Path.Combine(directory.Path, "unrelated.log"), "private");
        var service = new DataExportService(NullLogger.Instance, directory.Path);
        using var destination = new MemoryStream();

        var result = await service.ExportLogsAsync(destination, LogLevel.Info);

        Assert.Equal(DataExportStatus.Success, result.Status);
        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("logs/ghostslacking.log"));
        Assert.NotNull(archive.GetEntry("logs/watchdog.1.log"));
        Assert.Null(archive.GetEntry("logs/unrelated.log"));
        var manifestEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("diagnostics.json"));
        using var manifest = JsonDocument.Parse(manifestEntry.Open());
        var root = manifest.RootElement;
        Assert.Equal("Info", root.GetProperty("minimumLogLevel").GetString());
        Assert.False(root.TryGetProperty("userName", out _));
        Assert.False(root.TryGetProperty("machineName", out _));
        Assert.False(root.TryGetProperty("settings", out _));
        Assert.Equal(2, root.GetProperty("includedFiles").GetArrayLength());
        Assert.Empty(root.GetProperty("skippedFiles").EnumerateArray());
    }

    [Fact]
    public async Task Locked_log_is_reported_as_partial_without_aborting_export()
    {
        using var directory = new TemporaryDirectory();
        var readable = Path.Combine(directory.Path, "ghostslacking.log");
        var locked = Path.Combine(directory.Path, "watchdog.log");
        File.WriteAllText(readable, "app-log");
        File.WriteAllText(locked, "watchdog-log");
        var service = new DataExportService(NullLogger.Instance, directory.Path);
        using var destination = new MemoryStream();

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await service.ExportLogsAsync(destination, LogLevel.Debug);
            Assert.Equal(DataExportStatus.PartialSuccess, result.Status);
        }

        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("logs/ghostslacking.log"));
        Assert.Null(archive.GetEntry("logs/watchdog.log"));
        var manifestEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("diagnostics.json"));
        using var manifest = JsonDocument.Parse(manifestEntry.Open());
        Assert.Single(manifest.RootElement.GetProperty("skippedFiles").EnumerateArray());
    }

    [Fact]
    public async Task Settings_export_writes_normalized_reloadable_json()
    {
        var service = new DataExportService(NullLogger.Instance);
        using var destination = new MemoryStream();

        var result = await service.ExportSettingsAsync(
            destination,
            new AppSettings { RevealDiameterPx = 5000, MinimumLogLevel = LogLevel.Debug });

        Assert.Equal(DataExportStatus.Success, result.Status);
        var json = System.Text.Encoding.UTF8.GetString(destination.ToArray());
        var restored = AppSettingsJson.Deserialize(json);
        Assert.Equal(800, restored.RevealDiameterPx);
        Assert.Equal(LogLevel.Debug, restored.MinimumLogLevel);
        Assert.DoesNotContain("IsDisabled", json);
    }

    [Fact]
    public async Task Export_to_a_read_only_destination_returns_failure()
    {
        var service = new DataExportService(NullLogger.Instance);
        using var destination = new MemoryStream(Array.Empty<byte>(), writable: false);

        var result = await service.ExportSettingsAsync(destination, new AppSettings());

        Assert.Equal(DataExportStatus.Failure, result.Status);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"GhostSlacking-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
