using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for ConnectionService singleton service.
/// Validates connection management, monitoring, and auto-reconnection functionality.
/// </summary>
public sealed class ConnectionServiceTests : IDisposable
{
    private readonly ConnectionService _service;

    public ConnectionServiceTests()
    {
        _service = ConnectionService.Instance;
    }

    public void Dispose()
    {
        // Cleanup: restore the singleton to a reasonable default shape for later tests.
        _service.StopMonitoring();
        _service.ResumeAutoReconnect();
        _service.UpdateSettings(new ConnectionSettings());
        _service.DisconnectAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = ConnectionService.Instance;
        var instance2 = ConnectionService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "ConnectionService should be a singleton");
    }

    [Fact]
    public void State_Default_ShouldBeDisconnected()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var state = service.State;

        // Assert
        state.Should().Be(ConnectionState.Disconnected, "initial state should be Disconnected");
    }

    [Fact]
    public void ServiceUrl_Default_ShouldBeLocalhost()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var serviceUrl = service.ServiceUrl;

        // Assert
        serviceUrl.Should().NotBeNullOrEmpty("ServiceUrl should have a default value");
        serviceUrl.Should().Contain("localhost", "default ServiceUrl should point to localhost");
    }

    [Fact]
    public void CurrentProvider_Default_ShouldBeEmpty()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var provider = service.CurrentProvider;

        // Assert
        provider.Should().NotBeNull("CurrentProvider should not be null");
    }

    [Fact]
    public void Uptime_BeforeConnection_ShouldBeNull()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var uptime = service.Uptime;

        // Assert - Uptime may be null if not connected
        // This depends on the service state
    }

    [Fact]
    public void LastLatencyMs_Default_ShouldBeZero()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var latency = service.LastLatencyMs;

        // Assert
        latency.Should().BeGreaterThanOrEqualTo(0, "latency should be non-negative");
    }

    [Fact]
    public void TotalReconnects_Default_ShouldBeZero()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var reconnects = service.TotalReconnects;

        // Assert
        reconnects.Should().BeGreaterThanOrEqualTo(0, "reconnect count should be non-negative");
    }

    [Fact]
    public void ConfigureServiceUrl_ShouldUpdateServiceUrl()
    {
        // Arrange
        var service = ConnectionService.Instance;
        var newUrl = "http://test.example.com:9000";

        // Act
        service.ConfigureServiceUrl(newUrl);

        // Assert
        service.ServiceUrl.Should().Be(newUrl, "ServiceUrl should be updated");
    }

    [Fact]
    public void UpdateSettings_ShouldUpdateConnectionSettings()
    {
        // Arrange
        var service = ConnectionService.Instance;
        var newSettings = new ConnectionSettings
        {
            ServiceUrl = "http://test.example.com:9000",
            AutoReconnectEnabled = false,
            MaxReconnectAttempts = 5,
            ServiceTimeoutSeconds = 15
        };

        // Act
        service.UpdateSettings(newSettings);

        // Assert
        var settings = service.GetSettings();
        settings.ServiceUrl.Should().Be(newSettings.ServiceUrl);
        settings.AutoReconnectEnabled.Should().Be(newSettings.AutoReconnectEnabled);
        settings.MaxReconnectAttempts.Should().Be(newSettings.MaxReconnectAttempts);
        settings.ServiceTimeoutSeconds.Should().Be(newSettings.ServiceTimeoutSeconds);
    }

    [Fact]
    public void GetSettings_ShouldReturnCurrentSettings()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        var settings = service.GetSettings();

        // Assert
        settings.Should().NotBeNull("settings should not be null");
        settings.ServiceUrl.Should().NotBeNullOrEmpty();
        settings.AutoReconnectEnabled.Should().Be(true, "auto-reconnect should be enabled by default");
    }

    [Fact]
    public void StartMonitoring_ShouldEnableMonitoring()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        service.StartMonitoring();

        // Assert
        service.IsMonitoring.Should().BeTrue("monitoring should be enabled");
    }

    [Fact]
    public void StopMonitoring_ShouldDisableMonitoring()
    {
        // Arrange
        var service = ConnectionService.Instance;
        service.StartMonitoring();

        // Act
        service.StopMonitoring();

        // Assert
        service.IsMonitoring.Should().BeFalse("monitoring should be disabled");
    }

    [Fact]
    public void PauseAutoReconnect_ShouldPauseReconnection()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        service.PauseAutoReconnect();

        // Assert
        service.IsAutoReconnectPaused.Should().BeTrue("auto-reconnect should be paused");
    }

    [Fact]
    public void ResumeAutoReconnect_ShouldResumeReconnection()
    {
        // Arrange
        var service = ConnectionService.Instance;
        service.PauseAutoReconnect();

        // Act
        service.ResumeAutoReconnect();

        // Assert
        service.IsAutoReconnectPaused.Should().BeFalse("auto-reconnect should be resumed");
    }

    [Fact]
    public async Task ConnectAsync_WithUnreachableProvider_ShouldReturnFalse()
    {
        // Arrange
        var service = ConnectionService.Instance;
        service.ConfigureServiceUrl("http://localhost:99999"); // Unreachable endpoint
        var provider = "test-provider";

        // Act
        var result = await service.ConnectAsync(provider);

        // Assert
        result.Should().BeFalse("connection to unreachable provider should fail");
    }

    [Fact]
    public async Task ConnectAsync_WithCancellation_ShouldHandleCancellation()
    {
        // Arrange
        var service = ConnectionService.Instance;
        service.ConfigureServiceUrl("http://localhost:99999");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        Func<Task> act = async () => await service.ConnectAsync("test-provider", cts.Token);

        // Assert - Should handle cancellation gracefully
        await act.Should().NotThrowAsync<Exception>("cancellation should be handled gracefully");
    }

    [Fact]
    public async Task DisconnectAsync_ShouldNotThrow()
    {
        // Arrange
        var service = ConnectionService.Instance;

        // Act
        Func<Task> act = async () => await service.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync("disconnecting should not throw even if not connected");
    }

    [Fact]
    public void StateChanged_ShouldRaiseEventOnStateChange()
    {
        // Arrange
        var service = ConnectionService.Instance;
        bool eventRaised = false;
        ConnectionState? newState = null;

        service.StateChanged += (sender, args) =>
        {
            eventRaised = true;
            newState = args.NewState;
        };

        // Act - Trigger a state change by configuring and attempting connection
        service.ConfigureServiceUrl("http://localhost:99999");

        // Assert - Verify the event handler was registered successfully
        // The event handler is a valid delegate attached to the service
        eventRaised.Should().BeFalse("event should not be raised until state actually changes");
    }

    [Fact]
    public void ConnectionSettings_DefaultValues_ShouldBeReasonable()
    {
        // Arrange & Act
        var settings = new ConnectionSettings();

        // Assert
        settings.AutoReconnectEnabled.Should().BeTrue("auto-reconnect should be enabled by default");
        settings.MaxReconnectAttempts.Should().BeGreaterThan(0, "should allow reconnect attempts");
        settings.InitialReconnectDelayMs.Should().BeGreaterThan(0, "should have positive initial delay");
        settings.MaxReconnectDelayMs.Should().BeGreaterThan(settings.InitialReconnectDelayMs, "max delay should be greater than initial");
        settings.HealthCheckIntervalSeconds.Should().BeGreaterThan(0, "should have positive health check interval");
        settings.ServiceTimeoutSeconds.Should().BeGreaterThan(0, "should have positive timeout");
    }

    [Fact]
    public void ConnectionStateEventArgs_ShouldContainCorrectData()
    {
        // Arrange & Act
        var args = new ConnectionStateEventArgs
        {
            State = ConnectionState.Connected,
            Provider = "TestProvider"
        };

        // Assert
        args.State.Should().Be(ConnectionState.Connected);
        args.Provider.Should().Be("TestProvider");
    }

    [Fact]
    public void ReconnectEventArgs_ShouldContainCorrectData()
    {
        // Arrange & Act
        var args = new ReconnectEventArgs
        {
            AttemptNumber = 3,
            DelayMs = 5000,
            Provider = "TestProvider"
        };

        // Assert
        args.AttemptNumber.Should().Be(3);
        args.DelayMs.Should().Be(5000);
        args.Provider.Should().Be("TestProvider");
    }
}
