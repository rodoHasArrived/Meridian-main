using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Coverage for the durable fill delivery boundary: the fill must reach the accounting handoff
/// through the async publisher entry point (never the blocking bridge, which can starve the
/// consumer it waits on), and a durability store that is unavailable must delay delivery rather
/// than silently abandon it.
/// </summary>
public sealed class DurableFillDeliveryBoundaryTests
{
    [Fact]
    public async Task FillPublication_UsesTheAsyncPublisherEntryPoint()
    {
        // The blocking Publish bridge runs PublishAsync().GetAwaiter().GetResult(). Acceptance
        // applies storage backpressure and can wait for the posting consumer to free channel
        // capacity, so taking that bridge from the OMS's async fill path parks a pool thread
        // against the consumer that has to drain it.
        var gateway = new SingleFillGateway();
        var publisher = new EntryPointRecordingPublisher();

        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: publisher);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            ClientOrderId = "fill-delivery-order"
        });

        result.Success.Should().BeTrue();
        publisher.AsyncPublishCount.Should().Be(1,
            "the fill path must await PublishAsync so backpressure suspends the caller instead of blocking a thread");
        publisher.BlockingPublishCount.Should().Be(0,
            "the synchronous bridge must not be taken from an async fill path");
    }

    [Fact]
    public async Task RetainedHandoffReplay_WhenTheStoreIsBrieflyUnavailable_StillDeliversTheFill()
    {
        // A fill the accounting layer never accepted lives only in the handoff store. Giving up
        // on the first load error leaves it undelivered with nothing scheduled to retry, while
        // the OMS keeps trading as though the backlog were empty.
        var retainedFill = CreateFill();
        var store = new TransientlyUnavailableHandoffStore(retainedFill, failuresBeforeSuccess: 1);
        var publisher = new EntryPointRecordingPublisher();
        var gateway = new SingleFillGateway();

        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: publisher,
            tradeFillHandoffFailureStore: store);

        // First load throws; the retry after backoff succeeds and the retained fill is replayed.
        var replayed = await WaitUntilAsync(
            () => publisher.PublishedFillIds.Contains(retainedFill.FillId),
            TimeSpan.FromSeconds(20));

        replayed.Should().BeTrue(
            "an unavailable handoff store must delay replay, never cancel it — the retained fill has no other copy");
        store.LoadAttempts.Should().BeGreaterThanOrEqualTo(2,
            "the load must be retried rather than abandoned after its first failure");
        store.ReplayedFillIds.Should().Contain(retainedFill.FillId,
            "a replayed fill must be marked so it is not delivered again on the next restart");
    }

    private static TradeExecutedEvent CreateFill() => new(
        FillId: Guid.NewGuid(),
        OrderId: "retained-order",
        Symbol: "AAPL",
        Side: OrderSide.Buy,
        FilledQuantity: 10m,
        FillPrice: 150m,
        Commission: 0m,
        RealizedPnl: 0m,
        NewCash: 0m,
        OccurredAt: DateTimeOffset.UtcNow);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(25);
        }

        return condition();
    }

    /// <summary>
    /// Records which publisher entry point the caller took. <see cref="PublishAsync"/> is
    /// overridden so the interface default (which forwards to the blocking bridge) cannot mask
    /// a synchronous call.
    /// </summary>
    private sealed class EntryPointRecordingPublisher : IScopedTradeEventPublisher
    {
        private int _blockingPublishCount;
        private int _asyncPublishCount;

        public string PostingScope => "test-ledger/open-period";

        public ConcurrentBag<Guid> PublishedFillIds { get; } = new();

        public int BlockingPublishCount => Volatile.Read(ref _blockingPublishCount);

        public int AsyncPublishCount => Volatile.Read(ref _asyncPublishCount);

        public void Publish(TradeExecutedEvent tradeEvent)
        {
            Interlocked.Increment(ref _blockingPublishCount);
            PublishedFillIds.Add(tradeEvent.FillId);
        }

        public Task PublishAsync(TradeExecutedEvent tradeEvent)
        {
            Interlocked.Increment(ref _asyncPublishCount);
            PublishedFillIds.Add(tradeEvent.FillId);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Handoff store whose first <see cref="LoadPendingAsync"/> calls fail, standing in for a
    /// store that is briefly unreachable at startup.
    /// </summary>
    private sealed class TransientlyUnavailableHandoffStore : ITradeFillHandoffFailureStore
    {
        private readonly RetainedTradeFillHandoffFailure _retained;
        private int _failuresRemaining;
        private int _loadAttempts;

        public TransientlyUnavailableHandoffStore(TradeExecutedEvent retained, int failuresBeforeSuccess)
        {
            _retained = new RetainedTradeFillHandoffFailure(
                retained,
                DateTimeOffset.UtcNow,
                FailureCount: 1,
                LastFailure: "accounting publisher unavailable",
                LastAttemptAtUtc: DateTimeOffset.UtcNow);
            _failuresRemaining = failuresBeforeSuccess;
        }

        public string PostingScope => "test-ledger/open-period";

        public int LoadAttempts => Volatile.Read(ref _loadAttempts);

        public ConcurrentBag<Guid> ReplayedFillIds { get; } = new();

        public Task RetainAsync(TradeExecutedEvent tradeEvent, string failure, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> LoadPendingAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _loadAttempts);
            if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
                throw new IOException("Injected handoff store outage");

            IReadOnlyList<RetainedTradeFillHandoffFailure> pending =
                ReplayedFillIds.Contains(_retained.TradeEvent.FillId)
                    ? Array.Empty<RetainedTradeFillHandoffFailure>()
                    : new[] { _retained };
            return Task.FromResult(pending);
        }

        public Task MarkReplayedAsync(Guid fillId, CancellationToken ct = default)
        {
            ReplayedFillIds.Add(fillId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Gateway that acknowledges a submit as an immediate complete fill.</summary>
    private sealed class SingleFillGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        public string GatewayId => "test-gateway";

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId,
            OrderModification modification,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
            => Task.FromResult(new ExecutionReport
            {
                OrderId = request.ClientOrderId ?? "test-order",
                ClientOrderId = request.ClientOrderId ?? "test-order",
                ReportType = ExecutionReportType.Fill,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = request.Quantity,
                FillPrice = 150m,
                Commission = 0m,
                Timestamp = DateTimeOffset.UtcNow
            });

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default)
            => AsyncEnumerable.Empty<ExecutionReport>();
    }
}
