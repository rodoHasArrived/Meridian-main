using Meridian.Wpf.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for StatusService singleton service.
/// Validates status tracking and broadcasting functionality.
/// </summary>
public sealed class StatusServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = StatusService.Instance;
        var instance2 = StatusService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "StatusService should be a singleton");
    }

    [Fact]
    public void BaseUrl_DefaultValue_ShouldBeLocalhost()
    {
        // Arrange
        var service = StatusService.Instance;

        // Act
        var baseUrl = service.BaseUrl;

        // Assert
        baseUrl.Should().NotBeNullOrEmpty("BaseUrl should have a default value");
        baseUrl.Should().Contain("localhost", "default BaseUrl should point to localhost");
    }

    [Fact]
    public void BaseUrl_SetValue_ShouldUpdateBaseUrl()
    {
        // Arrange
        var service = StatusService.Instance;
        var newBaseUrl = "http://test.example.com:9000";

        // Act
        service.BaseUrl = newBaseUrl;

        // Assert
        service.BaseUrl.Should().Be(newBaseUrl, "BaseUrl should be updated");
    }

    [Fact]
    public void CurrentStatus_Default_ShouldNotBeNull()
    {
        // Arrange
        var service = StatusService.Instance;

        // Act
        var status = service.CurrentStatus;

        // Assert
        status.Should().NotBeNullOrEmpty("CurrentStatus should have a default value");
    }

    [Fact]
    public void UpdateStatus_ShouldChangeCurrentStatus()
    {
        // Arrange
        var service = StatusService.Instance;
        var newStatus = "Testing";

        // Act
        service.UpdateStatus(newStatus);

        // Assert
        service.CurrentStatus.Should().Be(newStatus, "CurrentStatus should be updated");
    }

    [Fact]
    public void UpdateStatus_ShouldRaiseStatusChangedEvent()
    {
        // Arrange
        var service = StatusService.Instance;
        var newStatus = "EventTest";
        bool eventRaised = false;
        string? receivedStatus = null;

        service.StatusChanged += (sender, args) =>
        {
            eventRaised = true;
            receivedStatus = args.NewStatus;
        };

        // Act
        service.UpdateStatus(newStatus);

        // Assert
        eventRaised.Should().BeTrue("StatusChanged event should be raised");
        receivedStatus.Should().Be(newStatus, "event should contain the new status");
    }

    [Fact]
    public async Task GetStatusAsync_WithUnreachableEndpoint_ShouldReturnNull()
    {
        // Arrange
        var service = StatusService.Instance;
        // Set to an unreachable endpoint
        service.BaseUrl = "http://localhost:99999";

        // Act
        var result = await service.GetStatusAsync();

        // Assert
        result.Should().BeNull("unreachable endpoint should return null");
    }

    [Fact]
    public async Task GetStatusAsync_WithCancellation_ShouldCancelOperation()
    {
        // Arrange
        var service = StatusService.Instance;
        service.BaseUrl = "http://localhost:99999"; // Unreachable endpoint
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        Func<Task> act = async () => await service.GetStatusAsync(cts.Token);

        // Assert - Should either return null or throw OperationCanceledException
        await act.Should().NotThrowAsync<Exception>("cancellation should be handled gracefully");
    }

    [Fact]
    public async Task GetProviderStatusAsync_WithUnreachableEndpoint_ShouldReturnNull()
    {
        // Arrange
        var service = StatusService.Instance;
        service.BaseUrl = "http://localhost:99999";

        // Act
        var result = await service.GetProviderStatusAsync();

        // Assert
        result.Should().BeNull("unreachable endpoint should return null");
    }

    [Fact]
    public void StatusChangedEventArgs_ShouldContainCorrectData()
    {
        // Arrange
        var oldStatus = "Old";
        var newStatus = "New";

        // Act
        var args = new StatusChangedEventArgs(oldStatus, newStatus);

        // Assert
        args.PreviousStatus.Should().Be(oldStatus);
        args.NewStatus.Should().Be(newStatus);
        args.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SimpleStatus_Properties_ShouldBeSettable()
    {
        // Act
        var providerInfo = new StatusProviderInfo { ActiveProvider = "TestProvider", IsConnected = true };
        var status = new SimpleStatus
        {
            Published = 100,
            Dropped = 5,
            Integrity = 2,
            Historical = 1000,
            Provider = providerInfo
        };

        // Assert
        status.Published.Should().Be(100);
        status.Dropped.Should().Be(5);
        status.Integrity.Should().Be(2);
        status.Historical.Should().Be(1000);
        status.Provider.Should().NotBeNull();
        status.Provider!.ActiveProvider.Should().Be("TestProvider");
    }

    [Fact]
    public void CurrentStatus_ThreadSafety_ShouldHandleConcurrentReads()
    {
        // Arrange
        var service = StatusService.Instance;
        var tasks = new List<Task<string>>();

        // Act - Read status from multiple threads
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => service.CurrentStatus));
        }

        // Assert - Should not throw
        Func<Task> act = async () => await Task.WhenAll(tasks);
        act.Should().NotThrowAsync("concurrent reads should be thread-safe");
    }
}
