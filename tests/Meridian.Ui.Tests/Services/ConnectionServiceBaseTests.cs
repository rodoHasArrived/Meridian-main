using FluentAssertions;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ConnectionServiceBase"/> — state machine,
/// monitoring orchestration, auto-reconnect, and event raising.
/// Uses a concrete test subclass to exercise the abstract base.
/// </summary>
public sealed class ConnectionServiceBaseTests
{
    /// <summary>
    /// Concrete test implementation of the abstract ConnectionServiceBase.
    /// Records calls to abstract methods for verification.
    /// </summary>
    private sealed class TestConnectionService : ConnectionServiceBase
    {
        public bool HealthCheckResult { get; set; } = true;
        public Exception? HealthCheckException { get; set; }
        public int MonitoringTimerStartCount { get; private set; }
        public int MonitoringTimerStopCount { get; private set; }
        public int ReconnectTimerStartCount { get; private set; }
        public int ReconnectTimerStopCount { get; private set; }
        public int LastMonitoringIntervalMs { get; private set; }
        public int LastReconnectDelayMs { get; private set; }
        public List<string> LogMessages { get; } = new();

        protected override Task<bool> PerformHealthCheckCoreAsync(CancellationToken ct)
        {
            if (HealthCheckException != null)
            {
                return Task.FromException<bool>(HealthCheckException);
            }

            return Task.FromResult(HealthCheckResult);
        }

        protected override void StartMonitoringTimer(int intervalMs)
        {
            MonitoringTimerStartCount++;
            LastMonitoringIntervalMs = intervalMs;
        }

        protected override void StopMonitoringTimer()
            => MonitoringTimerStopCount++;

        protected override void StartReconnectTimer(int delayMs)
        {
            ReconnectTimerStartCount++;
            LastReconnectDelayMs = delayMs;
        }

        protected override void StopAutoReconnect()
            => ReconnectTimerStopCount++;

        protected override void OnSettingsUpdated(ConnectionSettings settings) { }

        protected override void LogOperation(string message, params (string Key, string Value)[] properties)
            => LogMessages.Add(message);

        protected override void LogWarning(string message, params (string Key, string Value)[] properties)
            => LogMessages.Add("[WARN] " + message);

        protected override void DisposePlatformResources() { }

        // Expose for testing
        public Task SimulateHealthCheck() => OnMonitoringTimerFired();
        public Task SimulateReconnect() => OnReconnectTimerFired();
    }

    // ── Initial state ────────────────────────────────────────────────

    [Fact]
    public void NewService_ShouldBeDisconnected()
    {
        using var svc = new TestConnectionService();
        svc.State.Should().Be(ConnectionState.Disconnected);
        svc.IsMonitoring.Should().BeFalse();
        svc.Uptime.Should().BeNull();
        svc.CurrentProvider.Should().BeEmpty();
    }

    // ── ConnectAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_ShouldTransitionToConnected()
    {
        using var svc = new TestConnectionService();

        var result = await svc.ConnectAsync("TestProvider");

        result.Should().BeTrue();
        svc.State.Should().Be(ConnectionState.Connected);
        svc.CurrentProvider.Should().Be("TestProvider");
        svc.IsMonitoring.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_ShouldStartTracking_Uptime()
    {
        using var svc = new TestConnectionService();

        await svc.ConnectAsync("UpProvider");

        svc.Uptime.Should().NotBeNull();
        svc.Uptime!.Value.Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConnectAsync_ShouldRaiseStateChangedEvent()
    {
        using var svc = new TestConnectionService();
        ConnectionStateChangedEventArgs? received = null;
        svc.StateChanged += (_, args) => received = args;

        await svc.ConnectAsync("EventProv");

        received.Should().NotBeNull();
        received!.OldState.Should().Be(ConnectionState.Disconnected);
        received.NewState.Should().Be(ConnectionState.Connected);
        received.Provider.Should().Be("EventProv");
    }

    [Fact]
    public async Task ConnectAsync_WhenHealthCheckIsCanceled_ReturnsFalseWithoutChangingState()
    {
        using var svc = new TestConnectionService
        {
            HealthCheckException = new OperationCanceledException()
        };

        var result = await svc.ConnectAsync("CanceledProvider");

        result.Should().BeFalse();
        svc.State.Should().Be(ConnectionState.Disconnected);
        svc.IsMonitoring.Should().BeFalse();
    }

    [Fact]
    public async Task HealthCheck_AfterThreeFailures_ShouldScheduleReconnect()
    {
        using var svc = new TestConnectionService();
        await svc.ConnectAsync("Provider");
        svc.HealthCheckResult = false;

        await svc.SimulateHealthCheck();
        await svc.SimulateHealthCheck();
        await svc.SimulateHealthCheck();

        svc.State.Should().Be(ConnectionState.Reconnecting);
        svc.ReconnectTimerStartCount.Should().Be(1);
        svc.LastReconnectDelayMs.Should().Be(1000);
    }

    [Fact]
    public async Task ResumeAutoReconnect_AfterManualDisconnect_ShouldNotScheduleReconnect()
    {
        using var svc = new TestConnectionService();
        await svc.ConnectAsync("Provider");
        await svc.DisconnectAsync();
        svc.PauseAutoReconnect();

        svc.ResumeAutoReconnect();

        svc.State.Should().Be(ConnectionState.Disconnected);
        svc.ReconnectTimerStartCount.Should().Be(0);
    }

    // ── DisconnectAsync ──────────────────────────────────────────────

    [Fact]
    public async Task DisconnectAsync_ShouldTransitionToDisconnected()
    {
        using var svc = new TestConnectionService();
        await svc.ConnectAsync("Provider");

        await svc.DisconnectAsync();

        svc.State.Should().Be(ConnectionState.Disconnected);
        svc.Uptime.Should().BeNull();
    }

    // ── Settings ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateSettings_ShouldApplyNewSettings()
    {
        using var svc = new TestConnectionService();
        var settings = new ConnectionSettings
        {
            ServiceUrl = "http://example.com:9090",
            ServiceTimeoutSeconds = 60
        };

        svc.UpdateSettings(settings);

        svc.ServiceUrl.Should().Be("http://example.com:9090");
        svc.GetSettings().ServiceTimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void UpdateSettings_Null_ShouldThrow()
    {
        using var svc = new TestConnectionService();
        var act = () => svc.UpdateSettings(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConfigureServiceUrl_ShouldUpdateUrl()
    {
        using var svc = new TestConnectionService();

        svc.ConfigureServiceUrl("http://custom:1234", 45);

        svc.ServiceUrl.Should().Be("http://custom:1234");
        svc.GetSettings().ServiceTimeoutSeconds.Should().Be(45);
    }

    // ── StartMonitoring / StopMonitoring ─────────────────────────────

    [Fact]
    public void StartMonitoring_ShouldSetIsMonitoringTrue()
    {
        using var svc = new TestConnectionService();

        svc.StartMonitoring();

        svc.IsMonitoring.Should().BeTrue();
        svc.MonitoringTimerStartCount.Should().Be(1);
    }

    [Fact]
    public void StartMonitoring_CalledTwice_ShouldNotStartTimerTwice()
    {
        using var svc = new TestConnectionService();

        svc.StartMonitoring();
        svc.StartMonitoring();

        svc.MonitoringTimerStartCount.Should().Be(1);
    }

    [Fact]
    public void StopMonitoring_ShouldSetIsMonitoringFalse()
    {
        using var svc = new TestConnectionService();
        svc.StartMonitoring();

        svc.StopMonitoring();

        svc.IsMonitoring.Should().BeFalse();
        svc.MonitoringTimerStopCount.Should().Be(1);
    }

    // ── Health check via monitoring ──────────────────────────────────

    [Fact]
    public async Task HealthCheck_Success_ShouldUpdateLatency()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = true;
        svc.StartMonitoring();

        await svc.SimulateHealthCheck();

        svc.LastLatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task HealthCheck_Success_ShouldRaiseConnectionHealthUpdated()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = true;
        svc.StartMonitoring();

        ConnectionHealthEventArgs? received = null;
        svc.ConnectionHealthUpdated += (_, args) => received = args;

        await svc.SimulateHealthCheck();

        received.Should().NotBeNull();
        received!.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_Success_WithoutConnect_ShouldNotTransitionToConnected()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = true;
        svc.StartMonitoring();

        await svc.SimulateHealthCheck();

        svc.State.Should().Be(ConnectionState.Disconnected);
        svc.Uptime.Should().BeNull();
    }

    [Fact]
    public async Task HealthCheck_Failure_ShouldRaiseConnectionHealthUpdated()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = false;
        svc.StartMonitoring();

        ConnectionHealthEventArgs? received = null;
        svc.ConnectionHealthUpdated += (_, args) => received = args;

        await svc.SimulateHealthCheck();

        received.Should().NotBeNull();
        received!.IsHealthy.Should().BeFalse();
    }

    // ── Auto-reconnect ───────────────────────────────────────────────

    [Fact]
    public void PauseAutoReconnect_ShouldSetPaused()
    {
        using var svc = new TestConnectionService();

        svc.PauseAutoReconnect();

        svc.IsAutoReconnectPaused.Should().BeTrue();
    }

    [Fact]
    public void ResumeAutoReconnect_ShouldClearPaused()
    {
        using var svc = new TestConnectionService();
        svc.PauseAutoReconnect();

        svc.ResumeAutoReconnect();

        svc.IsAutoReconnectPaused.Should().BeFalse();
    }

    [Fact]
    public async Task SuccessfulReconnect_ShouldIncrementTotalReconnects()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = true;
        await svc.ConnectAsync("Prov");

        var before = svc.TotalReconnects;
        await svc.SimulateReconnect();

        svc.TotalReconnects.Should().Be(before + 1);
        svc.State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public async Task SuccessfulReconnect_ShouldRaiseReconnectSucceeded()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = true;
        await svc.ConnectAsync("Prov");

        bool eventRaised = false;
        svc.ReconnectSucceeded += (_, _) => eventRaised = true;

        await svc.SimulateReconnect();

        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task FailedReconnect_ShouldRaiseReconnectFailed()
    {
        using var svc = new TestConnectionService();
        svc.HealthCheckResult = false;
        svc.PauseAutoReconnect(); // Prevent scheduling follow-up reconnects

        ReconnectFailedEventArgs? received = null;
        svc.ReconnectFailed += (_, args) => received = args;

        await svc.SimulateReconnect();

        received.Should().NotBeNull();
        received!.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task HealthCheck_Success_AfterManualDisconnect_ShouldRemainDisconnected()
    {
        using var svc = new TestConnectionService();
        await svc.ConnectAsync("Provider");
        await svc.DisconnectAsync();
        svc.StartMonitoring();
        svc.HealthCheckResult = true;

        await svc.SimulateHealthCheck();

        svc.State.Should().Be(ConnectionState.Disconnected);
        svc.Uptime.Should().BeNull();
    }

    // ── GetReconnectDelayMs ──────────────────────────────────────────

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 2000)]
    [InlineData(2, 5000)]
    [InlineData(3, 10000)]
    [InlineData(4, 30000)]
    [InlineData(100, 30000)] // capped at max
    public void GetReconnectDelayMs_ShouldFollowBackoffSchedule(int attempt, int expectedMs)
    {
        using var svc = new TestConnectionService();

        // Use reflection to call the protected virtual method
        var method = typeof(ConnectionServiceBase)
            .GetMethod("GetReconnectDelayMs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (int)method!.Invoke(svc, new object[] { attempt })!;

        result.Should().Be(expectedMs);
    }

    // ── ConnectionSettings model ─────────────────────────────────────

    [Fact]
    public void ConnectionSettings_ShouldHaveDefaults()
    {
        var settings = new ConnectionSettings();
        settings.ServiceUrl.Should().Be("http://localhost:8080");
        settings.ServiceTimeoutSeconds.Should().Be(30);
        settings.HealthCheckIntervalSeconds.Should().Be(5);
        settings.AutoReconnectEnabled.Should().BeTrue();
    }

    // ── Dispose ──────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ShouldStopMonitoring()
    {
        var svc = new TestConnectionService();
        svc.StartMonitoring();

        svc.Dispose();

        svc.IsMonitoring.Should().BeFalse();
    }
}
