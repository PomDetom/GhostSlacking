using System.Text;

namespace GhostSlacking.Core;

public sealed record LogRetentionOptions
{
    public static LogRetentionOptions Default { get; } = new();

    public long MaximumFileSizeBytes { get; init; } = 2 * 1024 * 1024;
    public int MaximumFileCount { get; init; } = 5;
    public TimeSpan MaximumBackupAge { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromDays(1);
}

public sealed class RollingFileLogger : ILogger, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _fileNameWithoutExtension;
    private readonly string _extension;
    private readonly LogRetentionOptions _retention;
    private readonly string _path;
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;
    private int _minimumLevel;

    public RollingFileLogger(
        string path,
        LogLevel minimumLevel = LogLevel.Info,
        LogRetentionOptions? retention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(_path) ?? throw new ArgumentException("A log directory is required.", nameof(path));
        _fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_path);
        _extension = Path.GetExtension(_path);
        _retention = retention ?? LogRetentionOptions.Default;
        ValidateRetention(_retention);
        _minimumLevel = (int)NormalizeLevel(minimumLevel);

        lock (_gate)
        {
            TryRunMaintenance(DateTimeOffset.UtcNow, force: true);
            TryRotateIfNeeded(0);
        }
    }

    public LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set => Volatile.Write(ref _minimumLevel, (int)NormalizeLevel(value));
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level > MinimumLevel)
        {
            return;
        }

        var suffix = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
        var bytes = Encoding.UTF8.GetBytes($"{DateTimeOffset.Now:O} [{level}] {message}{suffix}{Environment.NewLine}");
        if (bytes.LongLength > _retention.MaximumFileSizeBytes)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                TryRunMaintenance(now, force: false);
                if (!TryRotateIfNeeded(bytes.LongLength))
                {
                    return;
                }

                Directory.CreateDirectory(_directory);
                using var stream = new FileStream(
                    _path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
            }
        }
        catch
        {
            // Logging must never compromise the recovery path.
        }
    }

    public void Dispose() { }

    private bool TryRotateIfNeeded(long incomingBytes)
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length + incomingBytes <= _retention.MaximumFileSizeBytes)
            {
                return true;
            }

            if (_retention.MaximumFileCount == 1)
            {
                File.Delete(_path);
                return true;
            }

            File.Delete(BackupPath(_retention.MaximumFileCount - 1));
            for (var index = _retention.MaximumFileCount - 2; index >= 1; index--)
            {
                var source = BackupPath(index);
                if (File.Exists(source))
                {
                    File.Move(source, BackupPath(index + 1), true);
                }
            }

            File.Move(_path, BackupPath(1), true);
            return true;
        }
        catch
        {
            // Drop new records while the full file cannot be rotated so it cannot grow without bound.
            return false;
        }
    }

    private void TryRunMaintenance(DateTimeOffset now, bool force)
    {
        if (!force && now - _lastCleanupUtc < _retention.CleanupInterval)
        {
            return;
        }

        _lastCleanupUtc = now;
        try
        {
            Directory.CreateDirectory(_directory);
            var cutoff = now.UtcDateTime - _retention.MaximumBackupAge;
            for (var index = 1; index < _retention.MaximumFileCount; index++)
            {
                var backup = BackupPath(index);
                if (File.Exists(backup) && File.GetLastWriteTimeUtc(backup) < cutoff)
                {
                    File.Delete(backup);
                }
            }
        }
        catch
        {
            // Maintenance is best effort; the file-count limit still bounds future rotations.
        }
    }

    private string BackupPath(int index) =>
        Path.Combine(_directory, $"{_fileNameWithoutExtension}.{index}{_extension}");

    private static LogLevel NormalizeLevel(LogLevel level) =>
        level is LogLevel.Error or LogLevel.Warning or LogLevel.Info or LogLevel.Debug
            ? level
            : LogLevel.Info;

    private static void ValidateRetention(LogRetentionOptions retention)
    {
        if (retention.MaximumFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "The maximum file size must be positive.");
        }

        if (retention.MaximumFileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "The maximum file count must be positive.");
        }

        if (retention.MaximumBackupAge <= TimeSpan.Zero || retention.CleanupInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Retention durations must be positive.");
        }
    }
}
