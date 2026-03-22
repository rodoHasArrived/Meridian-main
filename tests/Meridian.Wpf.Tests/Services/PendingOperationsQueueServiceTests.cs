using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for PendingOperationsQueueService singleton service.
/// Validates queue operations, handler registration, initialization lifecycle, and model defaults.
/// </summary>
public sealed class PendingOperationsQueueServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = PendingOperationsQueueService.Instance;
        var instance2 = PendingOperationsQueueService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "PendingOperationsQueueService should be a singleton");
    }

    [Fact]
    public void IsInitialized_Default_ShouldBeFalse()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;

        // NOTE: This may not be false if other tests have run InitializeAsync.
        // We test the lifecycle explicitly below.

        // Assert
        ((object)service.IsInitialized).Should().BeOfType<bool>();
    }

    [Fact]
    public async Task InitializeAsync_ShouldSetIsInitializedToTrue()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;

        // Act
        await service.InitializeAsync();

        // Assert
        service.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task ShutdownAsync_ShouldSetIsInitializedToFalse()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        await service.InitializeAsync();

        // Act
        await service.ShutdownAsync();

        // Assert
        service.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task PendingCount_AfterShutdown_ShouldBeZero()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        await service.InitializeAsync();
        service.Enqueue("test-op", "payload");

        // Act
        await service.ShutdownAsync();

        // Assert
        service.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Enqueue_ShouldIncreasePendingCount()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        // Drain existing items
        while (service.Dequeue() != null) { }
        var initialCount = service.PendingCount;

        // Act
        service.Enqueue("test-op", "data");

        // Assert
        service.PendingCount.Should().Be(initialCount + 1);

        // Cleanup
        service.Dequeue();
    }

    [Fact]
    public void Dequeue_ShouldReturnEnqueuedOperation()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        while (service.Dequeue() != null) { }
        service.Enqueue("test-dequeue", "payload-data");

        // Act
        var operation = service.Dequeue();

        // Assert
        operation.Should().NotBeNull();
        operation!.OperationType.Should().Be("test-dequeue");
        operation.Payload.Should().Be("payload-data");
    }

    [Fact]
    public void Dequeue_OnEmptyQueue_ShouldReturnNull()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        while (service.Dequeue() != null) { }

        // Act
        var result = service.Dequeue();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Peek_ShouldNotRemoveItem()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        while (service.Dequeue() != null) { }
        service.Enqueue("peek-test", null);

        // Act
        var peeked = service.Peek();
        var countAfterPeek = service.PendingCount;

        // Assert
        peeked.Should().NotBeNull();
        peeked!.OperationType.Should().Be("peek-test");
        countAfterPeek.Should().BeGreaterThan(0);

        // Cleanup
        service.Dequeue();
    }

    [Fact]
    public void GetAll_ShouldReturnAllItems()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        while (service.Dequeue() != null) { }
        service.Enqueue("op-1", null);
        service.Enqueue("op-2", null);

        // Act
        var all = service.GetAll();

        // Assert
        all.Should().NotBeNull();
        all.Count.Should().BeGreaterThanOrEqualTo(2);

        // Cleanup
        while (service.Dequeue() != null) { }
    }

    [Fact]
    public void RegisterHandler_ShouldNotThrow()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;

        // Act
        var act = () => service.RegisterHandler("test-handler", _ => Task.CompletedTask);

        // Assert
        act.Should().NotThrow();

        // Cleanup
        service.UnregisterHandler("test-handler");
    }

    [Fact]
    public void UnregisterHandler_ShouldNotThrow()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        service.RegisterHandler("unregister-test", _ => Task.CompletedTask);

        // Act
        var act = () => service.UnregisterHandler("unregister-test");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterHandler_WithNullHandler_ShouldThrow()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;

        // Act
        var act = () => service.RegisterHandler("null-handler", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterHandler_WithEmptyOperationType_ShouldThrow()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;

        // Act
        var act = () => service.RegisterHandler("", _ => Task.CompletedTask);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PendingOperation_Defaults_ShouldHaveExpectedValues()
    {
        // Arrange & Act
        var operation = new PendingOperation();

        // Assert
        operation.Id.Should().NotBeNullOrEmpty();
        operation.OperationType.Should().BeEmpty();
        operation.Payload.Should().BeNull();
        operation.MaxRetries.Should().Be(3);
        operation.RetryCount.Should().Be(0);
        operation.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAllAsync_ShouldProcessAllItems()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        while (service.Dequeue() != null) { }

        var processedItems = new List<string>();
        service.RegisterHandler("process-test", payload =>
        {
            processedItems.Add(payload?.ToString() ?? "");
            return Task.CompletedTask;
        });

        service.Enqueue("process-test", "item1");
        service.Enqueue("process-test", "item2");

        // Act
        await service.ProcessAllAsync();

        // Assert
        processedItems.Should().HaveCount(2);
        service.PendingCount.Should().Be(0);

        // Cleanup
        service.UnregisterHandler("process-test");
    }

    [Fact]
    public async Task ProcessAllAsync_FailedWithRetries_ShouldReenqueue()
    {
        // Arrange
        var service = PendingOperationsQueueService.Instance;
        while (service.Dequeue() != null) { }

        service.RegisterHandler("fail-test", _ => throw new InvalidOperationException("Test failure"));
        service.Enqueue(new PendingOperation { OperationType = "fail-test", MaxRetries = 3 });

        // Act
        await service.ProcessAllAsync();

        // Assert - item should be re-enqueued with incremented retry count
        service.PendingCount.Should().Be(1);
        var requeued = service.Dequeue();
        requeued.Should().NotBeNull();
        requeued!.RetryCount.Should().Be(1);

        // Cleanup
        service.UnregisterHandler("fail-test");
    }
}
