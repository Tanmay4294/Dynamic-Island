using System.IO;
using System.Text.Json;
using System.Threading;
using Spotifly.App.Models;
using Spotifly.App.Services;
using Spotifly.App.ViewModels;
using Xunit;

namespace Spotifly.Tests;

public class Phase12LifecycleAndRecoveryTests
{
    [Fact]
    public void SettingsService_LoadsValidSettings_Correctly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"spotifly_test_{Guid.NewGuid():N}.json");
        try
        {
            var initial = new AppSettings
            {
                Theme = AppTheme.Modern,
                ActivationDelayMs = 350,
                HideDelayMs = 900,
                HorizontalOffset = 50,
                VerticalOffset = 10,
                LaunchAtSignIn = true
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(initial));

            var service = new SettingsService(tempFile);
            var loaded = service.CurrentSettings;

            Assert.Equal(AppTheme.Modern, loaded.Theme);
            Assert.Equal(350, loaded.ActivationDelayMs);
            Assert.Equal(900, loaded.HideDelayMs);
            Assert.Equal(50, loaded.HorizontalOffset);
            Assert.Equal(10, loaded.VerticalOffset);
            Assert.True(loaded.LaunchAtSignIn);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SettingsService_CorruptSettingsFile_RecoversWithDefaultsWithoutCrashing()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"spotifly_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            // Write malformed JSON
            File.WriteAllText(tempFile, "{ this is not valid json : [ }");

            var service = new SettingsService(tempFile);
            var loaded = service.CurrentSettings;

            // Recovers to default settings
            Assert.Equal(AppTheme.Minimal, loaded.Theme);
            Assert.Equal(200, loaded.ActivationDelayMs);
            Assert.Equal(100, loaded.HideDelayMs);
            Assert.False(loaded.LaunchAtSignIn);

            // Re-saves valid defaults
            Assert.True(File.Exists(tempFile));
            var repaired = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(tempFile));
            Assert.NotNull(repaired);
            Assert.Equal(AppTheme.Minimal, repaired.Theme);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SettingsService_InvalidOutOfRangeValues_AreSanitized()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"spotifly_clamp_{Guid.NewGuid():N}.json");
        try
        {
            var invalid = new AppSettings
            {
                ActivationDelayMs = -9999, // below min 50
                HideDelayMs = 99999,       // above max 3000
                HorizontalOffset = -1500,  // below min -500
                VerticalOffset = 800       // above max 200
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(invalid));

            var service = new SettingsService(tempFile);
            var loaded = service.CurrentSettings;

            Assert.Equal(50, loaded.ActivationDelayMs);
            Assert.Equal(3000, loaded.HideDelayMs);
            Assert.Equal(-500.0, loaded.HorizontalOffset);
            Assert.Equal(200.0, loaded.VerticalOffset);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WindowsStartupService_SetLaunchAtStartup_IsIdempotent()
    {
        string testRunKey = @"Software\SpotiflyTest_RunKey_" + Guid.NewGuid().ToString("N");
        string testAppName = "SpotiflyTestApp";
        var startup = new WindowsStartupService(null, testRunKey, testAppName);

        try
        {
            Assert.False(startup.IsLaunchAtStartupEnabled());

            // 1. Enable startup
            bool enabled1 = startup.SetLaunchAtStartup(true);
            Assert.True(enabled1);
            Assert.True(startup.IsLaunchAtStartupEnabled());

            // 2. Enable again (idempotent call, must not duplicate or fail)
            bool enabled2 = startup.SetLaunchAtStartup(true);
            Assert.True(enabled2);
            Assert.True(startup.IsLaunchAtStartupEnabled());

            // 3. Disable startup
            bool disabled1 = startup.SetLaunchAtStartup(false);
            Assert.True(disabled1);
            Assert.False(startup.IsLaunchAtStartupEnabled());

            // 4. Disable again (idempotent call)
            bool disabled2 = startup.SetLaunchAtStartup(false);
            Assert.True(disabled2);
            Assert.False(startup.IsLaunchAtStartupEnabled());
        }
        finally
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testRunKey, false);
            }
            catch { }
        }
    }

    [Fact]
    public void AppLifecycleManager_CancellationAndDispose_SignalsStoppingToken()
    {
        var logger = new MemoryLoggerService();
        using var lifecycle = new AppLifecycleManager(logger);

        var token = lifecycle.ApplicationStoppingToken;
        Assert.False(token.IsCancellationRequested);

        bool disposedResource = false;
        var testDisposable = new AnonymousDisposable(() => disposedResource = true);
        lifecycle.RegisterDisposable(testDisposable);

        // Explicit exit request triggers token cancellation
        lifecycle.RequestExit();

        Assert.True(lifecycle.ApplicationStoppingToken.IsCancellationRequested);

        // Disposing lifecycle triggers registered disposables
        lifecycle.Dispose();
        Assert.True(disposedResource);
    }

    [Fact]
    public void SingleInstanceService_DetectsDuplicateInstance_AndPreventsSecondWindow()
    {
        string mutexName = "Local\\Spotifly_Test_Mutex_" + Guid.NewGuid().ToString("N");
        string eventName = "Local\\Spotifly_Test_Event_" + Guid.NewGuid().ToString("N");

        using var primary = new SingleInstanceService(mutexName, eventName);
        Assert.True(primary.IsFirstInstance);

        bool signalReceived = false;
        using var signalWait = new ManualResetEventSlim(false);
        primary.SecondInstanceLaunched += (s, e) =>
        {
            signalReceived = true;
            signalWait.Set();
        };

        // Second instance launch attempt
        using (var secondary = new SingleInstanceService(mutexName, eventName))
        {
            Assert.False(secondary.IsFirstInstance);
        }

        // Wait up to 2 seconds for signal notification
        signalWait.Wait(TimeSpan.FromSeconds(2));
        Assert.True(signalReceived);
    }

    [Fact]
    public async Task OfflineDisconnected_SpotifyService_ReturnsEmptySnapshotWithoutCrashing()
    {
        var authMock = new MockSpotifyAuthService(isConnected: false);
        var config = new SpotifyConfig();
        using var service = new SpotifyService(authMock, config);

        var snapshot = await service.RefreshPlaybackAsync();

        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsPlaying);
        Assert.Equal(TrackInfo.Empty.Name, snapshot.Track.Name);
    }

    [Fact]
    public async Task OfflineDisconnected_GetUserPlaylists_ReturnsEmptyOrCachedListGracefully()
    {
        var authMock = new MockSpotifyAuthService(isConnected: false);
        var config = new SpotifyConfig();
        using var service = new SpotifyService(authMock, config);

        var playlists = await service.GetUserPlaylistsAsync();

        Assert.NotNull(playlists);
        Assert.Empty(playlists);
    }

    private class AnonymousDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public AnonymousDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }

    private class MemoryLoggerService : ILoggerService
    {
        public void Log(LogLevel level, string message, Exception? exception = null) { }
        public void LogDebug(string message) { }
        public void LogInformation(string message) { }
        public void LogWarning(string message, Exception? ex = null) { }
        public void LogError(string message, Exception? ex = null) { }
        public void LogCritical(string message, Exception? ex = null) { }
    }

    private class MockSpotifyAuthService : ISpotifyAuthService
    {
        public bool IsConnected { get; }
        public event EventHandler<bool>? ConnectionStatusChanged { add { } remove { } }

        public MockSpotifyAuthService(bool isConnected)
        {
            IsConnected = isConnected;
        }

        public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsConnected);
        public Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
