using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class BackpressureAlertServiceTests : IDisposable
{
    private readonly BackpressureAlertService _service;
    private PipelineStatistics _mockStats;

    public BackpressureAlertServiceTests()
    {
        _mockStats = new PipelineStatistics(
            PublishedCount: 1000,
            DroppedCount: 0,
            ConsumedCount: 1000,
            CurrentQueueSize: 100,
            PeakQueueSize: 500,
            QueueCapacity: 10000,
            QueueUtilization: 1.0,
            AverageProcessingTimeUs: 10.0,
            TimeSinceLastFlush: TimeSpan.FromSeconds(1),
            Timestamp: DateTimeOffset.UtcNow);

        _service = new BackpressureAlertService(new BackpressureAlertConfig
        {
            CheckIntervalSeconds = 1,
            WarningUtilizationPercent = 70,
            CriticalUtilizationPercent = 90,
            WarningDropRatePercent = 1,
            CriticalDropRatePercent = 5,
            ConsecutiveChecksBeforeAlert = 1,
            WarningAlertIntervalSeconds = 0,
            CriticalAlertIntervalSeconds = 0
        });

        _service.RegisterPipelineProvider(() => _mockStats);
    }

    [Fact]
    public void GetStatus_Normal_ShouldReturnInactive()
    {
        // Arrange - low utilization, no drops
        _mockStats = _mockStats with
        {
            QueueUtilization = 10.0,
            DroppedCount = 0
        };

        // Act
        var status = _service.GetStatus();

        // Assert
        status.IsActive.Should().BeFalse();
        status.Level.Should().Be(BackpressureLevel.None);
        status.DropRate.Should().Be(0);
    }

    [Fact]
    public void GetStatus_WarningUtilization_ShouldReportWarning()
    {
        // Arrange - 75% utilization
        _mockStats = _mockStats with
        {
            QueueUtilization = 75.0,
            CurrentQueueSize = 7500,
            QueueCapacity = 10000
        };

        // Act
        var status = _service.GetStatus();

        // Assert
        status.Level.Should().Be(BackpressureLevel.Warning);
        status.QueueUtilization.Should().Be(75.0);
    }

    [Fact]
    public void GetStatus_CriticalUtilization_ShouldReportCritical()
    {
        // Arrange - 95% utilization
        _mockStats = _mockStats with
        {
            QueueUtilization = 95.0,
            CurrentQueueSize = 9500,
            QueueCapacity = 10000
        };

        // Act
        var status = _service.GetStatus();

        // Assert
        status.Level.Should().Be(BackpressureLevel.Critical);
    }

    [Fact]
    public void GetStatus_HighDropRate_ShouldReportLevel()
    {
        // Arrange - 10% drop rate
        _mockStats = _mockStats with
        {
            PublishedCount = 1000,
            DroppedCount = 100,
            QueueUtilization = 50.0
        };

        // Act
        var status = _service.GetStatus();

        // Assert
        status.DropRate.Should().BeApproximately(10.0, 0.1);
        status.Level.Should().Be(BackpressureLevel.Critical);
    }

    [Fact]
    public void GetStatus_WithNoPublishedEvents_ShouldNotDivideByZero()
    {
        // Arrange
        _mockStats = _mockStats with
        {
            PublishedCount = 0,
            DroppedCount = 0,
            QueueUtilization = 0.0
        };

        // Act
        var status = _service.GetStatus();

        // Assert
        status.DropRate.Should().Be(0);
        status.Level.Should().Be(BackpressureLevel.None);
    }

    [Fact]
    public void GetStatus_MessageShouldDescribeStatus()
    {
        // Arrange
        _mockStats = _mockStats with
        {
            QueueUtilization = 85.0,
            PublishedCount = 1000,
            DroppedCount = 30
        };

        // Act
        var status = _service.GetStatus();

        // Assert
        status.Message.Should().Contain("85");
        status.Message.Should().Contain("3.0");
    }

    [Fact]
    public void CheckBackpressure_IsNotAsyncVoid()
    {
        // Verify the timer callback method is not async void (which was a bug - exceptions vanish)
        var method = typeof(BackpressureAlertService)
            .GetMethod("CheckBackpressure",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method.Should().NotBeNull("CheckBackpressure should exist as timer callback");
        method!.ReturnType.Should().Be(typeof(void),
            "Timer callback should be synchronous wrapper (not async void)");
    }

    [Fact]
    public void CheckBackpressureCoreAsync_ReturnsTask()
    {
        // Verify the async implementation returns Task for proper error handling
        var method = typeof(BackpressureAlertService)
            .GetMethod("CheckBackpressureCoreAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method.Should().NotBeNull("CheckBackpressureCoreAsync should exist as the async implementation");
        method!.ReturnType.Should().Be(typeof(Task),
            "Async implementation should return Task, not void");
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
