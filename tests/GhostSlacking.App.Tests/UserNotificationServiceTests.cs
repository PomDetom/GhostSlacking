using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class UserNotificationServiceTests
{
    [Fact]
    public void Successful_show_uses_transient_identity_and_expiration()
    {
        var backend = new FakeBackend();
        var fallback = new FakeFallback();
        var logger = new RecordingLogger();
        using var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, logger);
        var before = DateTimeOffset.Now.Add(WindowsAppNotificationService.Expiration);

        service.Show("picked", UserNotificationSeverity.Info);

        var request = Assert.Single(backend.Requests);
        Assert.Equal("picked", request.Message);
        Assert.Equal(UserNotificationSeverity.Info, request.Severity);
        Assert.Equal(WindowsAppNotificationService.TransientTag, request.Tag);
        Assert.Equal(WindowsAppNotificationService.TransientGroup, request.Group);
        Assert.InRange(request.Expiration, before, DateTimeOffset.Now.Add(WindowsAppNotificationService.Expiration));
        Assert.Empty(fallback.Requests);
    }

    [Fact]
    public void Consecutive_notifications_reuse_the_same_identity()
    {
        var backend = new FakeBackend();
        using var service = WindowsAppNotificationService.CreateForTesting(
            backend,
            new FakeFallback(),
            NullLogger.Instance);

        service.Show("one", UserNotificationSeverity.Info);
        service.Show("two", UserNotificationSeverity.Error);

        Assert.Equal(2, backend.Requests.Count);
        Assert.Equal(backend.Requests[0].Tag, backend.Requests[1].Tag);
        Assert.Equal(backend.Requests[0].Group, backend.Requests[1].Group);
    }

    [Fact]
    public void Disabled_notifications_are_logged_once_without_sending()
    {
        var backend = new FakeBackend { Enabled = false, Setting = "DisabledForApplication" };
        var fallback = new FakeFallback();
        var logger = new RecordingLogger();
        using var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, logger);

        service.Show("one", UserNotificationSeverity.Info);
        service.Show("two", UserNotificationSeverity.Info);

        Assert.Empty(backend.Requests);
        Assert.Empty(fallback.Requests);
        Assert.Single(logger.Messages, message => message.Contains("DisabledForApplication", StringComparison.Ordinal));
    }

    [Fact]
    public void Initialization_failure_is_logged_and_show_uses_fallback()
    {
        var backend = new FakeBackend { Initializes = false, InitializationFailure = "runtime missing" };
        var fallback = new FakeFallback();
        var logger = new RecordingLogger();
        using var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, logger);

        service.Show("fallback", UserNotificationSeverity.Error);

        Assert.Empty(backend.Requests);
        var request = Assert.Single(fallback.Requests);
        Assert.Equal("fallback", request.Message);
        Assert.Equal(UserNotificationSeverity.Error, request.Severity);
        Assert.Contains(logger.Messages, message => message.Contains("runtime missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Repeated_send_failure_is_logged_once()
    {
        var backend = new FakeBackend { ShowException = new InvalidOperationException("send failed") };
        var fallback = new FakeFallback();
        var logger = new RecordingLogger();
        using var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, logger);

        service.Show("one", UserNotificationSeverity.Info);
        service.Show("two", UserNotificationSeverity.Error);

        Assert.Single(logger.Messages, message => message == "Windows app notification could not be sent.");
        Assert.Collection(
            fallback.Requests,
            request => Assert.Equal(("one", UserNotificationSeverity.Info), request),
            request => Assert.Equal(("two", UserNotificationSeverity.Error), request));
    }

    [Fact]
    public void Setting_query_failure_uses_fallback()
    {
        var backend = new FakeBackend { CanShowException = new InvalidOperationException("query failed") };
        var fallback = new FakeFallback();
        using var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, NullLogger.Instance);

        service.Show("fallback", UserNotificationSeverity.Info);

        Assert.Empty(backend.Requests);
        Assert.Equal(("fallback", UserNotificationSeverity.Info), Assert.Single(fallback.Requests));
    }

    [Fact]
    public void Empty_message_and_show_after_dispose_are_ignored()
    {
        var backend = new FakeBackend();
        var fallback = new FakeFallback();
        var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, NullLogger.Instance);

        service.Show(" ", UserNotificationSeverity.Info);
        service.Dispose();
        service.Show("disposed", UserNotificationSeverity.Error);

        Assert.Empty(backend.Requests);
        Assert.Empty(fallback.Requests);
    }

    [Fact]
    public void Fallback_failure_is_logged_and_does_not_escape()
    {
        var backend = new FakeBackend { Initializes = false, InitializationFailure = "unsupported" };
        var fallback = new FakeFallback { ShowException = new InvalidOperationException("fallback failed") };
        var logger = new RecordingLogger();
        using var service = WindowsAppNotificationService.CreateForTesting(backend, fallback, logger);

        service.Show("safe", UserNotificationSeverity.Error);
        service.Show("still safe", UserNotificationSeverity.Error);

        Assert.Single(logger.Messages, message => message.Contains("unsupported", StringComparison.Ordinal));
        Assert.Single(logger.Messages, message => message == "Tray notification could not be sent.");
    }

    [Fact]
    public void Dispose_releases_backend_once()
    {
        var backend = new FakeBackend();
        var service = WindowsAppNotificationService.CreateForTesting(
            backend,
            new FakeFallback(),
            NullLogger.Instance);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, backend.DisposeCount);
    }

    private sealed class FakeBackend : IWindowsAppNotificationBackend
    {
        public bool Initializes { get; init; } = true;
        public string InitializationFailure { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public string Setting { get; init; } = "Enabled";
        public Exception? CanShowException { get; init; }
        public Exception? ShowException { get; init; }
        public List<UserNotificationRequest> Requests { get; } = [];
        public int DisposeCount { get; private set; }

        public bool TryInitialize(out string failureReason)
        {
            failureReason = InitializationFailure;
            return Initializes;
        }

        public bool CanShow(out string setting)
        {
            if (CanShowException is not null)
            {
                throw CanShowException;
            }

            setting = Setting;
            return Enabled;
        }

        public void Show(UserNotificationRequest request)
        {
            if (ShowException is not null)
            {
                throw ShowException;
            }

            Requests.Add(request);
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeFallback : IUserNotificationFallback
    {
        public Exception? ShowException { get; init; }
        public List<(string Message, UserNotificationSeverity Severity)> Requests { get; } = [];

        public void Show(string message, UserNotificationSeverity severity)
        {
            if (ShowException is not null)
            {
                throw ShowException;
            }

            Requests.Add((message, severity));
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public void Log(LogLevel level, string message, Exception? exception = null) => Messages.Add(message);
    }
}
