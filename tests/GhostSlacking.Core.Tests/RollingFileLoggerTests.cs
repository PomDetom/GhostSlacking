using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class RollingFileLoggerTests
{
    [Fact]
    public void Default_retention_is_bounded_to_two_megabytes_and_five_files()
    {
        Assert.Equal(2 * 1024 * 1024, LogRetentionOptions.Default.MaximumFileSizeBytes);
        Assert.Equal(5, LogRetentionOptions.Default.MaximumFileCount);
        Assert.Equal(TimeSpan.FromDays(30), LogRetentionOptions.Default.MaximumBackupAge);
    }

    [Fact]
    public void Minimum_level_can_be_updated_while_running()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "test.log");
        using var logger = new RollingFileLogger(path, LogLevel.Info);

        logger.Log(LogLevel.Debug, "hidden-debug");
        logger.Log(LogLevel.Info, "visible-info");
        logger.MinimumLevel = LogLevel.Debug;
        logger.Log(LogLevel.Debug, "visible-debug");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("hidden-debug", content);
        Assert.Contains("visible-info", content);
        Assert.Contains("visible-debug", content);
    }

    [Fact]
    public void Rotation_keeps_only_the_configured_newest_files()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "test.log");
        var retention = new LogRetentionOptions
        {
            MaximumFileSizeBytes = 90,
            MaximumFileCount = 3,
            MaximumBackupAge = TimeSpan.FromDays(30),
            CleanupInterval = TimeSpan.FromDays(1)
        };
        using var logger = new RollingFileLogger(path, LogLevel.Info, retention);

        logger.Log(LogLevel.Info, "record-one-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        logger.Log(LogLevel.Info, "record-two-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        logger.Log(LogLevel.Info, "record-three-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        logger.Log(LogLevel.Info, "record-four-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

        Assert.Contains("record-four", File.ReadAllText(path));
        Assert.Contains("record-three", File.ReadAllText(Path.Combine(directory.Path, "test.1.log")));
        Assert.Contains("record-two", File.ReadAllText(Path.Combine(directory.Path, "test.2.log")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "test.3.log")));
        Assert.DoesNotContain(
            Directory.GetFiles(directory.Path).Select(File.ReadAllText),
            content => content.Contains("record-one", StringComparison.Ordinal));
    }

    [Fact]
    public void Startup_cleanup_removes_expired_backups_but_keeps_current_log()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "test.log");
        var backup = Path.Combine(directory.Path, "test.1.log");
        File.WriteAllText(path, "current");
        File.WriteAllText(backup, "expired");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-60));
        File.SetLastWriteTimeUtc(backup, DateTime.UtcNow.AddDays(-60));

        using var logger = new RollingFileLogger(path);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public void Full_log_does_not_grow_when_rotation_is_blocked()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "test.log");
        var retention = new LogRetentionOptions
        {
            MaximumFileSizeBytes = 128,
            MaximumFileCount = 2,
            MaximumBackupAge = TimeSpan.FromDays(30),
            CleanupInterval = TimeSpan.FromDays(1)
        };
        using var logger = new RollingFileLogger(path, LogLevel.Info, retention);
        logger.Log(LogLevel.Info, "first-record-that-fills-the-file");
        var length = new FileInfo(path).Length;

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            logger.Log(LogLevel.Info, "this-record-must-be-dropped");
        }

        Assert.Equal(length, new FileInfo(path).Length);
        Assert.DoesNotContain("must-be-dropped", File.ReadAllText(path));
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
