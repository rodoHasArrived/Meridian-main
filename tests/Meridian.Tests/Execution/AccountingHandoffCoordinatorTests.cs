using Meridian.Execution.Events;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class AccountingHandoffCoordinatorTests
{
    [Fact]
    public void Construction_DoesNotReadOrPublishRetainedFills()
    {
        var retainedFill = CreateFill("retained");
        var store = new RecordingFailureStore(retainedFill);
        var publisher = new IdempotentPublisher();

        var coordinator = CreateCoordinator(publisher, store);

        Assert.True(coordinator.HasPublisher);
        Assert.Equal(0, store.LoadCount);
        Assert.Empty(publisher.Attempts);
        Assert.Same(retainedFill, Assert.Single(store.Pending).TradeEvent);
    }

    [Fact]
    public async Task Publish_WhenDurableAcceptanceIsAmbiguous_RetainsAndReplaysTheSameFillIdentity()
    {
        var fill = CreateFill("ambiguous");
        var lostAcknowledgment = new IOException("Publisher committed the fill but its acknowledgment was lost.");
        var publisher = new IdempotentPublisher();
        publisher.AfterAcceptanceAsync = _ => publisher.Attempts.Count == 1
            ? Task.FromException(lostAcknowledgment)
            : Task.CompletedTask;
        var store = new RecordingFailureStore();
        var coordinator = CreateCoordinator(publisher, store);

        var failure = await Assert.ThrowsAsync<AccountingHandoffException>(
            () => coordinator.PublishAsync(fill));

        Assert.True(failure.WasRetained);
        Assert.Same(lostAcknowledgment, failure.InnerException);
        Assert.Equal(
            $"Execution fill '{fill.FillId:D}' was accepted by the broker but its accounting handoff failed; the event is durably retained for restart replay.",
            failure.Message);
        var retained = Assert.Single(await coordinator.GetPendingAsync());
        Assert.Same(fill, retained.TradeEvent);
        Assert.Equal(lostAcknowledgment.Message, retained.LastFailure);
        Assert.False(Assert.Single(store.RetentionTokens).CanBeCanceled);

        await coordinator.ReplayPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { fill, fill }, publisher.Attempts);
        Assert.Equal(fill.FillId, Assert.Single(publisher.AcceptedFillIds));
        Assert.Empty(await coordinator.GetPendingAsync());
        Assert.False(Assert.Single(store.AcknowledgmentTokens).CanBeCanceled);
    }

    [Fact]
    public async Task Replay_WhenAcknowledgmentFails_RetainsThatFillAndContinuesTheBacklog()
    {
        var first = CreateFill("first");
        var second = CreateFill("second");
        var store = new RecordingFailureStore(first, second)
        {
            FailNextAcknowledgmentFor = first.FillId
        };
        var publisher = new IdempotentPublisher();
        var coordinator = CreateCoordinator(publisher, store);

        await coordinator.ReplayPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { first, second }, publisher.Attempts);
        var retained = Assert.Single(await coordinator.GetPendingAsync());
        Assert.Same(first, retained.TradeEvent);
        Assert.Equal(2, retained.FailureCount);
        Assert.Equal("Replay acknowledgment unavailable.", retained.LastFailure);

        await coordinator.ReplayPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { first, second, first }, publisher.Attempts);
        Assert.Equal(2, publisher.AcceptedFillIds.Count);
        Assert.Empty(await coordinator.GetPendingAsync());
        Assert.All(store.RetentionTokens, token => Assert.False(token.CanBeCanceled));
        Assert.All(store.AcknowledgmentTokens, token => Assert.False(token.CanBeCanceled));
    }

    [Fact]
    public async Task Publish_WhenPublisherAndFallbackFail_PreservesThePublisherFailureAndFailsClosed()
    {
        var fill = CreateFill("unretained");
        var publisherFailure = new IOException("Publisher storage unavailable.");
        var publisher = new IdempotentPublisher { Rejection = publisherFailure };
        var store = new RecordingFailureStore()
        {
            RetentionFailure = new IOException("Independent fallback storage unavailable.")
        };
        var coordinator = CreateCoordinator(publisher, store);

        var failure = await Assert.ThrowsAsync<AccountingHandoffException>(
            () => coordinator.PublishAsync(fill));

        Assert.False(failure.WasRetained);
        Assert.Same(publisherFailure, failure.InnerException);
        Assert.Equal(
            $"Execution fill '{fill.FillId:D}' was accepted by the broker but its accounting handoff failed; no durable fallback accepted the event and the order result is fail-closed.",
            failure.Message);
        Assert.Empty(publisher.AcceptedFillIds);
        Assert.Empty(await coordinator.GetPendingAsync());
        Assert.False(Assert.Single(store.RetentionTokens).CanBeCanceled);
    }

    [Fact]
    public async Task Replay_WhenCanceledDuringAcceptance_AcknowledgesTheSelectedFillAndLeavesTheRestPending()
    {
        var first = CreateFill("selected");
        var second = CreateFill("not-selected");
        var acceptanceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishAcceptance = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new IdempotentPublisher
        {
            AfterAcceptanceAsync = async _ =>
            {
                acceptanceStarted.TrySetResult();
                await finishAcceptance.Task;
            }
        };
        var store = new RecordingFailureStore(first, second);
        var coordinator = CreateCoordinator(publisher, store);
        using var cancellation = new CancellationTokenSource();

        var replay = coordinator.ReplayPendingAsync(cancellation.Token);
        try
        {
            await acceptanceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.False(replay.IsCompleted);
        }
        finally
        {
            finishAcceptance.TrySetResult();
            await replay.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Same(first, Assert.Single(publisher.Attempts));
        Assert.Same(second, Assert.Single(await coordinator.GetPendingAsync()).TradeEvent);
        Assert.False(Assert.Single(store.AcknowledgmentTokens).CanBeCanceled);
    }

    private static AccountingHandoffCoordinator CreateCoordinator(
        IdempotentPublisher publisher,
        RecordingFailureStore store)
        => new(publisher, store, NullLogger.Instance);

    private static TradeExecutedEvent CreateFill(string orderId) => new(
        Guid.NewGuid(),
        orderId,
        "AAPL",
        OrderSide.Buy,
        FilledQuantity: 10m,
        FillPrice: 150m,
        Commission: 0m,
        RealizedPnl: 0m,
        NewCash: 0m,
        OccurredAt: DateTimeOffset.UtcNow);

    private sealed class IdempotentPublisher : IScopedTradeEventPublisher
    {
        public string PostingScope => "test-book/open-period";
        public List<TradeExecutedEvent> Attempts { get; } = [];
        public HashSet<Guid> AcceptedFillIds { get; } = [];
        public Exception? Rejection { get; init; }
        public Func<TradeExecutedEvent, Task>? AfterAcceptanceAsync { get; set; }

        public void Publish(TradeExecutedEvent tradeEvent)
            => throw new InvalidOperationException("Accounting handoffs must use asynchronous publication.");

        public async Task PublishAsync(TradeExecutedEvent tradeEvent)
        {
            Attempts.Add(tradeEvent);
            if (Rejection is not null)
                throw Rejection;

            AcceptedFillIds.Add(tradeEvent.FillId);
            if (AfterAcceptanceAsync is not null)
                await AfterAcceptanceAsync(tradeEvent);
        }
    }

    private sealed class RecordingFailureStore : ITradeFillHandoffFailureStore
    {
        public RecordingFailureStore(params TradeExecutedEvent[] pending)
        {
            foreach (var tradeEvent in pending)
                Pending.Add(CreateFailure(tradeEvent, "Publisher previously rejected the fill."));
        }

        public string PostingScope => "test-book/open-period";
        public List<RetainedTradeFillHandoffFailure> Pending { get; } = [];
        public List<CancellationToken> RetentionTokens { get; } = [];
        public List<CancellationToken> AcknowledgmentTokens { get; } = [];
        public int LoadCount { get; private set; }
        public Guid? FailNextAcknowledgmentFor { get; set; }
        public Exception? RetentionFailure { get; init; }

        public Task RetainAsync(TradeExecutedEvent tradeEvent, string failure, CancellationToken ct = default)
        {
            RetentionTokens.Add(ct);
            if (RetentionFailure is not null)
                throw RetentionFailure;

            var existingIndex = Pending.FindIndex(item => item.TradeEvent.FillId == tradeEvent.FillId);
            if (existingIndex < 0)
                Pending.Add(CreateFailure(tradeEvent, failure));
            else
                Pending[existingIndex] = Pending[existingIndex] with
                {
                    FailureCount = Pending[existingIndex].FailureCount + 1,
                    LastFailure = failure,
                    LastAttemptAtUtc = DateTimeOffset.UtcNow
                };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> LoadPendingAsync(CancellationToken ct = default)
        {
            LoadCount++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RetainedTradeFillHandoffFailure>>(Pending.ToArray());
        }

        public Task MarkReplayedAsync(Guid fillId, CancellationToken ct = default)
        {
            AcknowledgmentTokens.Add(ct);
            if (FailNextAcknowledgmentFor == fillId)
            {
                FailNextAcknowledgmentFor = null;
                throw new IOException("Replay acknowledgment unavailable.");
            }

            Pending.RemoveAll(item => item.TradeEvent.FillId == fillId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static RetainedTradeFillHandoffFailure CreateFailure(TradeExecutedEvent tradeEvent, string failure)
            => new(tradeEvent, DateTimeOffset.UtcNow, 1, failure, DateTimeOffset.UtcNow);
    }
}
