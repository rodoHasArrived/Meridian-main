using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="BackfillService"/> business logic.
/// </summary>
public sealed class BackfillServiceTests
{
    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        // Act
        var instance = BackfillService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = BackfillService.Instance;
        var instance2 = BackfillService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void CurrentProgress_InitiallyNull()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var progress = service.CurrentProgress;

        // Assert
        progress.Should().BeNull("no backfill has been started");
    }

    [Fact]
    public void IsRunning_WhenNoBackfill_ReturnsFalse()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var isRunning = service.IsRunning;

        // Assert
        isRunning.Should().BeFalse("no backfill is running");
    }

    [Fact]
    public void IsPaused_WhenNoBackfill_ReturnsFalse()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var isPaused = service.IsPaused;

        // Assert
        isPaused.Should().BeFalse("no backfill is paused");
    }

    [Fact]
    public void BarsPerSecond_WhenNoBackfill_ReturnsZero()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var bps = service.BarsPerSecond;

        // Assert
        bps.Should().Be(0, "no bars have been downloaded");
    }

    [Fact]
    public void EstimatedTimeRemaining_WhenNoBackfill_ReturnsNull()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var estimate = service.EstimatedTimeRemaining;

        // Assert
        estimate.Should().BeNull("no backfill is running");
    }

    [Fact]
    public void Constructor_WithUseInstanceTrue_ThrowsException()
    {
        // Act
        var act = () => new BackfillService(useInstance: true);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*singleton*");
    }

    [Fact]
    public void Constructor_WithUseInstanceFalse_DoesNotThrow()
    {
        // Act
        var act = () => new BackfillService(useInstance: false);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Instance_ThreadSafety_MultipleThreadsGetSameInstance()
    {
        // Arrange
        BackfillService? instance1 = null;
        BackfillService? instance2 = null;
        var task1 = Task.Run(() => instance1 = BackfillService.Instance);
        var task2 = Task.Run(() => instance2 = BackfillService.Instance);

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void CurrentProgress_AfterConstruction_InitializesCorrectly()
    {
        // Arrange & Act
        var service = BackfillService.Instance;

        // Assert
        service.CurrentProgress.Should().BeNull();
        service.IsRunning.Should().BeFalse();
        service.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void BarsPerSecond_WithNoProgress_ReturnsZero()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var bps = service.BarsPerSecond;

        // Assert
        bps.Should().Be(0);
    }

    [Fact]
    public void EstimatedTimeRemaining_WithNoBarsDownloaded_ReturnsNull()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var estimate = service.EstimatedTimeRemaining;

        // Assert
        estimate.Should().BeNull("no bars have been downloaded yet");
    }

    [Theory]
    [InlineData("Running", true)]
    [InlineData("Completed", false)]
    [InlineData("Failed", false)]
    [InlineData("Paused", false)]
    public void IsRunning_WithDifferentStatuses_ReturnsCorrectValue(string status, bool expectedRunning)
    {
        // Arrange
        var service = new BackfillService(useInstance: false);
        SetCurrentProgress(service, new Meridian.Contracts.Backfill.BackfillProgress { Status = status });

        // Act
        var result = service.IsRunning;

        // Assert
        result.Should().Be(expectedRunning);
    }

    [Theory]
    [InlineData("Paused", true)]
    [InlineData("Running", false)]
    [InlineData("Completed", false)]
    public void IsPaused_WithDifferentStatuses_ReturnsCorrectValue(string status, bool expectedPaused)
    {
        // Arrange
        var service = new BackfillService(useInstance: false);
        SetCurrentProgress(service, new Meridian.Contracts.Backfill.BackfillProgress { Status = status });

        // Act
        var result = service.IsPaused;

        // Assert
        result.Should().Be(expectedPaused);
    }

    private static void SetCurrentProgress(BackfillService service, Meridian.Contracts.Backfill.BackfillProgress? progress)
    {
        var field = typeof(BackfillService)
            .GetField("_currentProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field '_currentProgress' not found on BackfillService.");
        field.SetValue(service, progress);
    }

    [Fact]
    public void Constructor_CreatesNewInstance_WithoutUsingSharedSingleton()
    {
        // Arrange & Act
        var service1 = new BackfillService(useInstance: false);
        var service2 = new BackfillService(useInstance: false);

        // Assert
        service1.Should().NotBeNull();
        service2.Should().NotBeNull();
        service1.Should().NotBeSameAs(service2);
    }

    [Fact]
    public void BarsPerSecond_Calculation_ReturnsZeroWhenNotRunning()
    {
        // Arrange
        var service = BackfillService.Instance;

        // Act
        var speed = service.BarsPerSecond;

        // Assert
        speed.Should().Be(0, "no backfill is running");
    }

    [Fact]
    public void AllProperties_InitialState_HaveCorrectDefaultValues()
    {
        // Arrange & Act
        var service = BackfillService.Instance;

        // Assert - Verify all properties have sensible defaults
        service.CurrentProgress.Should().BeNull();
        service.IsRunning.Should().BeFalse();
        service.IsPaused.Should().BeFalse();
        service.BarsPerSecond.Should().Be(0);
        service.EstimatedTimeRemaining.Should().BeNull();
    }
}
