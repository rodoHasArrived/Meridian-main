using System.Text.Json;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for the durable side of PendingOperationsQueueService: operations must survive
/// shutdown/restart via the persisted snapshot and replay through registered handlers.
/// </summary>
public sealed class PendingOperationsQueuePersistenceTests : IDisposable
{
    private readonly string _snapshotPath;

    public PendingOperationsQueuePersistenceTests()
    {
        _snapshotPath = Path.Combine(
            Path.GetTempPath(), "Meridian.Wpf.Tests", Guid.NewGuid().ToString("N"), "pending-operations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_snapshotPath)!);
        PendingOperationsQueueService.SetFilePathOverrideForTests(_snapshotPath);
    }

    public void Dispose()
    {
        PendingOperationsQueueService.SetFilePathOverrideForTests(null);
    }

    private sealed record ReviewPayload(string BreakId, string Notes);

    [Fact]
    public async Task PersistAndInitialize_RoundTripsQueueAcrossInstances()
    {
        var writer = new PendingOperationsQueueService();
        writer.Enqueue(new PendingOperation
        {
            OperationType = "test.review",
            Payload = new ReviewPayload("break-42", "checked"),
            RetryCount = 1,
            MaxRetries = 5
        });
        await writer.PersistAsync();

        var reader = new PendingOperationsQueueService();
        await reader.InitializeAsync();

        reader.PendingCount.Should().Be(1);
        var restored = reader.Dequeue();
        restored.Should().NotBeNull();
        restored!.OperationType.Should().Be("test.review");
        restored.RetryCount.Should().Be(1);
        restored.MaxRetries.Should().Be(5);

        // Payloads restored from disk surface as JsonElement and deserialize back to the DTO.
        restored.Payload.Should().BeOfType<JsonElement>();
        var payload = ((JsonElement)restored.Payload!).Deserialize<ReviewPayload>(
            Meridian.Ui.Services.DesktopJsonOptions.Api);
        payload.Should().NotBeNull();
        payload!.BreakId.Should().Be("break-42");
        payload.Notes.Should().Be("checked");
    }

    [Fact]
    public async Task ShutdownAsync_PersistsQueueBeforeClearingMemory()
    {
        var first = new PendingOperationsQueueService();
        first.Enqueue("test.op", new ReviewPayload("break-7", "pending"));

        await first.ShutdownAsync();
        first.PendingCount.Should().Be(0, "the in-memory queue is released on shutdown");

        var second = new PendingOperationsQueueService();
        await second.InitializeAsync();

        second.PendingCount.Should().Be(1, "shutdown must persist the queue instead of discarding it");
        second.Dequeue()!.OperationType.Should().Be("test.op");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_DoesNotDuplicateRestoredOperations()
    {
        var writer = new PendingOperationsQueueService();
        writer.Enqueue("test.op", null);
        await writer.PersistAsync();

        var reader = new PendingOperationsQueueService();
        await reader.InitializeAsync();
        await reader.InitializeAsync();

        reader.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAllAsync_KeepsOperationsWithoutHandlers()
    {
        var service = new PendingOperationsQueueService();
        service.Enqueue("test.unhandled", null);

        await service.ProcessAllAsync();

        service.PendingCount.Should().Be(1,
            "an operation with no registered handler must stay durable for a later handler");
    }

    [Fact]
    public async Task ProcessAllAsync_ReplaysRestoredJsonPayloadThroughHandler()
    {
        var writer = new PendingOperationsQueueService();
        writer.Enqueue("test.replay", new ReviewPayload("break-9", "resolve"));
        await writer.PersistAsync();

        var reader = new PendingOperationsQueueService();
        await reader.InitializeAsync();

        ReviewPayload? replayed = null;
        reader.RegisterHandler("test.replay", payload =>
        {
            replayed = ((JsonElement)payload!).Deserialize<ReviewPayload>(
                Meridian.Ui.Services.DesktopJsonOptions.Api);
            return Task.CompletedTask;
        });

        await reader.ProcessAllAsync();

        replayed.Should().NotBeNull();
        replayed!.BreakId.Should().Be("break-9");
        reader.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_WithCorruptSnapshot_StartsEmptyInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(_snapshotPath, "{ not json");

        var service = new PendingOperationsQueueService();
        var act = () => service.InitializeAsync();

        await act.Should().NotThrowAsync();
        service.PendingCount.Should().Be(0);
        service.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAllAsync_CancelledHandler_RetainsOperationWithoutBurningRetries()
    {
        // A handler cancelled mid-replay (for example during shutdown) must not lose the
        // operation or consume its retry budget — the next session replays it.
        var service = new PendingOperationsQueueService();
        service.RegisterHandler("test.cancelled", _ => throw new OperationCanceledException());
        service.Enqueue("test.cancelled", null);

        var act = () => service.ProcessAllAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
        service.PendingCount.Should().Be(1);
        service.Peek()!.RetryCount.Should().Be(0);

        var reader = new PendingOperationsQueueService();
        await reader.InitializeAsync();
        reader.PendingCount.Should().Be(1, "the rescued operation must be durable for the next session");
    }

    [Fact]
    public async Task ProcessAllAsync_CancelledHandler_PreservesOrderOfDependentOperationsAcrossSessions()
    {
        var service = new PendingOperationsQueueService();
        service.RegisterHandler("test.review", _ => throw new OperationCanceledException());
        service.RegisterHandler("test.resolve", _ => Task.CompletedTask);
        service.Enqueue("test.review", null);
        service.Enqueue("test.resolve", null);

        var act = () => service.ProcessAllAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
        service.GetAll().Select(operation => operation.OperationType)
            .Should().Equal("test.review", "test.resolve");
        service.Peek()!.RetryCount.Should().Be(0);

        var reader = new PendingOperationsQueueService();
        await reader.InitializeAsync();

        reader.GetAll().Select(operation => operation.OperationType)
            .Should().Equal("test.review", "test.resolve",
                "the durable rescue snapshot must retain the original replay order");
    }

    [Fact]
    public async Task PersistAsync_AfterShutdown_DoesNotOverwriteFinalSnapshot()
    {
        // An enqueue-scheduled background persist that loses the race with shutdown must not
        // replace the final snapshot with the cleared queue.
        var service = new PendingOperationsQueueService();
        service.Enqueue("test.op", null);
        await service.ShutdownAsync();

        await service.PersistAsync();

        var reader = new PendingOperationsQueueService();
        await reader.InitializeAsync();
        reader.PendingCount.Should().Be(1);
    }
}
