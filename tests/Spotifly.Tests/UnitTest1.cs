using System.IO;
using Spotifly.App.Services;
using Xunit;

namespace Spotifly.Tests;

public class Phase1Tests : IDisposable
{
    private readonly string _tempLogDir;

    public Phase1Tests()
    {
        _tempLogDir = Path.Combine(Path.GetTempPath(), "SpotiflyTests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempLogDir))
            {
                Directory.Delete(_tempLogDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void FileLogger_SanitizesSensitiveBearerTokensAndSecrets()
    {
        var logger = new FileLoggerService(_tempLogDir);
        logger.LogInformation("Authorization: Bearer BQD1234567890abcdef1234567890");
        logger.LogInformation("client_secret: top_secret_value_123");
        logger.LogInformation("refresh_token = AQD9876543210zyxwv");
        logger.LogInformation("code: authorization_code_xyz");

        var content = File.ReadAllText(logger.PrimaryLogPath);

        Assert.DoesNotContain("BQD1234567890abcdef1234567890", content);
        Assert.DoesNotContain("top_secret_value_123", content);
        Assert.DoesNotContain("AQD9876543210zyxwv", content);
        Assert.DoesNotContain("authorization_code_xyz", content);

        Assert.Contains("Bearer [REDACTED]", content);
        Assert.Contains("client_secret: [REDACTED]", content);
        Assert.Contains("refresh_token = [REDACTED]", content);
        Assert.Contains("code: [REDACTED]", content);
    }

    [Fact]
    public void FileLogger_RotatesWhenSizeExceedsLimit()
    {
        // 500 byte limit for test
        var logger = new FileLoggerService(_tempLogDir, maxFileSizeBytes: 500, maxArchivedFiles: 3);

        for (int i = 0; i < 30; i++)
        {
            logger.LogInformation($"Test log message number {i:D3} with some padding text to fill bytes quickly");
        }

        var archive1 = Path.Combine(_tempLogDir, "spotifly.1.log");
        Assert.True(File.Exists(logger.PrimaryLogPath), "Primary log file must exist");
        Assert.True(File.Exists(archive1), "First rotated archive spotifly.1.log must exist");
    }

    [Fact]
    public void SingleInstanceService_PreventsDuplicateInstance()
    {
        var testMutexName = "Local\\TestMutex_" + Guid.NewGuid().ToString("N");
        var testEventName = "Local\\TestEvent_" + Guid.NewGuid().ToString("N");

        using var firstInstance = new SingleInstanceService(testMutexName, testEventName);
        Assert.True(firstInstance.IsFirstInstance, "First instance must acquire ownership");

        bool signaled = false;
        using var signalReceived = new ManualResetEventSlim(false);
        firstInstance.SecondInstanceLaunched += (s, e) =>
        {
            signaled = true;
            signalReceived.Set();
        };

        using var secondInstance = new SingleInstanceService(testMutexName, testEventName);
        Assert.False(secondInstance.IsFirstInstance, "Second instance must detect conflict");

        // Wait up to 2 seconds for signal
        signalReceived.Wait(TimeSpan.FromSeconds(2));
        Assert.True(signaled, "First instance must be signaled when second instance attempts to launch");
    }

    [Fact]
    public void AppLifecycleManager_TriggersCancellationAndDisposesResources()
    {
        var logger = new FileLoggerService(_tempLogDir);
        var lifecycle = new AppLifecycleManager(logger);

        bool disposed = false;
        var testDisposable = new DisposableAction(() => disposed = true);
        lifecycle.RegisterDisposable(testDisposable);

        Assert.False(lifecycle.ApplicationStoppingToken.IsCancellationRequested);

        lifecycle.Dispose();

        Assert.True(lifecycle.ApplicationStoppingToken.IsCancellationRequested);
        Assert.True(disposed, "Registered resource must be disposed when lifecycle manager disposes");
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }
}
