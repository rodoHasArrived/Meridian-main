using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Adapters.Alpaca;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// End-to-end guard for the Alpaca fill-streaming loop: raw <c>trade_updates</c> payloads flow
/// through <see cref="AlpacaTradeUpdatesClient"/> normalization and its durable inbox, are consumed
/// as the execution gateway's report stream by a real <see cref="OrderManagementSystem"/>, and drive
/// order lifecycle state, the paper portfolio, and the trade-fill accounting handoff — including
/// duplicate, out-of-order, cancel, and reject delivery, and reconnect REST replay — without any
/// polling between the broker event and the posted increment.
/// </summary>
public sealed class AlpacaStreamedFillLoopTests
{
    [Fact]
    public async Task StreamedTradeUpdates_DriveOrderLifecycle_AndPostExactlyOnceToAccountingHandoff()
    {
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);
        var gateway = new AlpacaReportsGateway(client);
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        var filled = await PlaceAcceptedOrderAsync(oms, "AAPL", quantity: 10m);
        var cancelled = await PlaceAcceptedOrderAsync(oms, "MSFT", quantity: 5m);
        var rejected = await PlaceAcceptedOrderAsync(oms, "NVDA", quantity: 3m);

        // Live delivery of a partial, a duplicate of the same broker event, a stale out-of-order
        // replay with a lower cumulative quantity, and the completion.
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-partial", "alpaca-a", filled.OrderId, "AAPL", "partial_fill", "partially_filled",
            qty: "10", filledQty: "4", price: "101", timestamp: "2026-08-07T14:30:01Z"));
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-partial", "alpaca-a", filled.OrderId, "AAPL", "partial_fill", "partially_filled",
            qty: "10", filledQty: "4", price: "101", timestamp: "2026-08-07T14:30:01Z"));
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-stale", "alpaca-a", filled.OrderId, "AAPL", "partial_fill", "partially_filled",
            qty: "10", filledQty: "2", price: "100.5", timestamp: "2026-08-07T14:29:59Z"));
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-fill", "alpaca-a", filled.OrderId, "AAPL", "fill", "filled",
            qty: "10", filledQty: "10", price: "102", timestamp: "2026-08-07T14:30:05Z"));

        // Lifecycle events that carry no fill must still reach tracked order state.
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-cancel", "alpaca-b", cancelled.OrderId, "MSFT", "canceled", "canceled",
            qty: "5", filledQty: "0", price: null, timestamp: "2026-08-07T14:30:06Z"));
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-reject", "alpaca-c", rejected.OrderId, "NVDA", "rejected", "rejected",
            qty: "3", filledQty: "0", price: null, timestamp: "2026-08-07T14:30:07Z",
            reason: "insufficient buying power"));

        var increments = await ReadFillIncrementsAsync(oms, filled.OrderId, count: 2);
        increments.Select(static report => report.FilledQuantity).Should().Equal(new[] { 4m, 6m },
            "cumulative broker quantities must reach consumers as deduplicated increments");
        increments.Select(static report => report.FillPrice).Should().Equal(101m, 102m);

        publisher.AcceptedEvents.Select(static tradeEvent => tradeEvent.FilledQuantity).Should().Equal(new[] { 4m, 6m },
            "each genuine increment posts exactly once to the accounting handoff");
        publisher.AcceptedEvents.Select(static tradeEvent => tradeEvent.FillId).Distinct().Should().HaveCount(2,
            because: "every posted increment carries its own durable fill identity");

        await WaitUntilAsync(() => oms.GetOrder(cancelled.OrderId)!.Status == OrderStatus.Cancelled,
            "a streamed cancel event must drive tracked order state");
        await WaitUntilAsync(() => oms.GetOrder(rejected.OrderId)!.Status == OrderStatus.Rejected,
            "a streamed reject event must drive tracked order state");

        var order = oms.GetOrder(filled.OrderId)!;
        order.Status.Should().Be(OrderStatus.Filled);
        order.FilledQuantity.Should().Be(10m);
        portfolio.Positions["AAPL"].Quantity.Should().Be(10L);
        portfolio.Positions.Should().NotContainKey("MSFT");
        portfolio.Positions.Should().NotContainKey("NVDA");
    }

    [Fact]
    public async Task ReconnectRestReplay_CrossSourceDuplicateAndMissedFill_PostsOnlyTheMissedIncrement()
    {
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);
        var gateway = new AlpacaReportsGateway(client);
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        var order = await PlaceAcceptedOrderAsync(oms, "AAPL", quantity: 10m);
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-live-partial", "alpaca-r", order.OrderId, "AAPL", "partial_fill", "partially_filled",
            qty: "10", filledQty: "4", price: "101", timestamp: "2026-08-07T14:30:01Z"));
        (await ReadFillIncrementsAsync(oms, order.OrderId, count: 1))
            .Single().FilledQuantity.Should().Be(4m);

        // After a disconnect, REST reconciliation replays the already-streamed partial under its
        // FILL-activity identity and backfills the completion the socket never delivered.
        client.ConfigureReconciliation((_, _) => Task.FromResult<IReadOnlyList<AlpacaReconciliationReport>>(
        [
            new AlpacaReconciliationReport(
                "rest-fill-activity",
                "activity-replayed-partial",
                ReconciledReport(order.OrderId, cumulativeQuantity: 4m, fillPrice: 101m,
                    OrderStatus.PartiallyFilled, "2026-08-07T14:30:01Z")),
            new AlpacaReconciliationReport(
                "rest-fill-activity",
                "activity-missed-completion",
                ReconciledReport(order.OrderId, cumulativeQuantity: 10m, fillPrice: 102m,
                    OrderStatus.Filled, "2026-08-07T14:31:00Z"))
        ]));
        await client.ReconcileAfterConnectAsync();

        var backfilled = await ReadFillIncrementsAsync(oms, order.OrderId, count: 1);
        backfilled.Single().FilledQuantity.Should().Be(6m,
            because: "the replayed partial is absorbed as a zero increment and only the missed completion posts");

        publisher.AcceptedEvents.Select(static tradeEvent => tradeEvent.FilledQuantity).Should().Equal(new[] { 4m, 6m },
            "reconnect recovery must not double-post fills that already reached the ledger handoff");
        oms.GetOrder(order.OrderId)!.Status.Should().Be(OrderStatus.Filled);
        portfolio.Positions["AAPL"].Quantity.Should().Be(10L);
    }

    private static AlpacaTradeUpdatesClient CreateClient(IAlpacaTradeUpdateCursorStore store)
    {
        var client = new AlpacaTradeUpdatesClient(
            new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true),
            NullLogger<AlpacaTradeUpdatesClient>.Instance,
            cursorStore: store);
        client.ConfigureDurableStateScope("paper-account-e2e", AlpacaCredentialEnvironment.PaperEnvironment);
        return client;
    }

    private static async Task<OrderResult> PlaceAcceptedOrderAsync(
        OrderManagementSystem oms,
        string symbol,
        decimal quantity)
    {
        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = quantity,
            LimitPrice = 100m
        });
        result.Success.Should().BeTrue();
        return result;
    }

    private static async Task<IReadOnlyList<ExecutionReport>> ReadFillIncrementsAsync(
        OrderManagementSystem oms,
        string orderId,
        int count)
    {
        var increments = new List<ExecutionReport>(count);
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (increments.Count < count)
        {
            var report = await oms.ExecutionReports.ReadAsync(readCts.Token);
            if (report.ClientOrderId == orderId || report.OrderId == orderId)
                increments.Add(report);
        }

        return increments;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        condition().Should().BeTrue(because);
    }

    private static string TradeUpdateJson(
        string eventId,
        string alpacaOrderId,
        string clientOrderId,
        string symbol,
        string eventName,
        string status,
        string qty,
        string filledQty,
        string? price,
        string timestamp,
        string? reason = null)
    {
        var order = new Dictionary<string, object?>
        {
            ["id"] = alpacaOrderId,
            ["client_order_id"] = clientOrderId,
            ["symbol"] = symbol,
            ["qty"] = qty,
            ["filled_qty"] = filledQty,
            ["side"] = "buy",
            ["status"] = status,
            ["updated_at"] = timestamp
        };
        var data = new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["event"] = eventName,
            ["timestamp"] = timestamp,
            ["order"] = order
        };
        if (price is not null)
            data["price"] = price;
        if (reason is not null)
            data["reason"] = reason;

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["stream"] = "trade_updates",
            ["data"] = data
        });
    }

    private static ExecutionReport ReconciledReport(
        string clientOrderId,
        decimal cumulativeQuantity,
        decimal fillPrice,
        OrderStatus status,
        string timestamp) => new()
        {
            OrderId = "alpaca-r",
            GatewayOrderId = "alpaca-r",
            ClientOrderId = clientOrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderQuantity = 10m,
            FilledQuantity = cumulativeQuantity,
            FillPrice = fillPrice,
            OrderStatus = status,
            ReportType = status == OrderStatus.Filled
            ? ExecutionReportType.Fill
            : ExecutionReportType.PartialFill,
            Timestamp = DateTimeOffset.Parse(timestamp),
            Diagnostics = new ExecutionDiagnostics
            {
                BrokerStatus = status == OrderStatus.Filled ? "fill" : "partial_fill",
                Category = "alpaca-rest-fill-reconciliation"
            }
        };

    /// <summary>
    /// Minimal gateway seam mirroring <see cref="AlpacaBrokerageGateway"/> report streaming: submit
    /// acknowledges synchronously, and every asynchronous lifecycle transition arrives through the
    /// trade-updates client's durable report stream.
    /// </summary>
    private sealed class AlpacaReportsGateway(AlpacaTradeUpdatesClient client)
        : IExecutionGateway, IExecutionGatewayModeProvider
    {
        public string GatewayId => "alpaca-e2e-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionReport
            {
                OrderId = request.ClientOrderId!,
                ClientOrderId = request.ClientOrderId,
                ReportType = ExecutionReportType.New,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity,
                FilledQuantity = 0m,
                Timestamp = DateTimeOffset.UtcNow
            });

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var report in client.Reports.WithCancellation(ct).ConfigureAwait(false))
                yield return report;
        }
    }

    private sealed class InMemoryCursorStore : IAlpacaTradeUpdateCursorStore
    {
        private readonly object _gate = new();
        private AlpacaTradeUpdateCursorState _state = AlpacaTradeUpdateCursorState.Empty;

        public DateTimeOffset? Load() => LoadState().Watermark;

        public IReadOnlyList<string> LoadRecentEventIds() => LoadState().EventIds;

        public void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds) =>
            SaveState(new AlpacaTradeUpdateCursorState(
                AlpacaTradeUpdateCursorState.CurrentVersion,
                watermark,
                recentEventIds.ToArray(),
                new Dictionary<string, string>(StringComparer.Ordinal),
                []));

        public AlpacaTradeUpdateCursorState LoadState()
        {
            lock (_gate)
                return _state;
        }

        public void SaveState(AlpacaTradeUpdateCursorState state)
        {
            lock (_gate)
                _state = state;
        }
    }

    private sealed class RecordingTradeEventPublisher : ITradeEventPublisher
    {
        public ConcurrentQueue<TradeExecutedEvent> AcceptedEvents { get; } = new();

        public void Publish(TradeExecutedEvent tradeEvent) => AcceptedEvents.Enqueue(tradeEvent);
    }
}
