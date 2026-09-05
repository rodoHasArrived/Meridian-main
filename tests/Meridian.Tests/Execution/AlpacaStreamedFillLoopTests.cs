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

    /// <summary>
    /// The restart handoff gap: a fill durably admitted into the inbox but not yet acknowledged
    /// when the host stopped is replayed into a fresh OMS that never registered its order. It
    /// used to be acknowledged without reaching the accounting handoff. It is now adopted and
    /// booked as exactly the quantity the broker event executed -- never the cumulative, part of
    /// which the previous host already posted.
    /// </summary>
    [Fact]
    public async Task RestartReplay_FillForAnOrderTheNewHostNeverTracked_ReachesAccountingAsTheEventIncrementOnly()
    {
        var store = new InMemoryCursorStore();
        string orderId;

        // First incarnation: places the order and books the partial.
        {
            await using var client = CreateClient(store);
            var gateway = new AlpacaReportsGateway(client);
            var publisher = new RecordingTradeEventPublisher();
            using var oms = new OrderManagementSystem(
                gateway,
                NullLogger<OrderManagementSystem>.Instance,
                portfolioState: new PaperTradingPortfolio(100_000m),
                tradeEventPublisher: publisher);

            var order = await PlaceAcceptedOrderAsync(oms, "AAPL", quantity: 10m);
            orderId = order.OrderId;
            await client.ProcessMessageAsync(TradeUpdateJson(
                "evt-partial", "alpaca-restart", orderId, "AAPL", "partial_fill", "partially_filled",
                qty: "10", filledQty: "4", price: "101", timestamp: "2026-08-07T14:30:01Z", fillQty: "4"));
            (await ReadFillIncrementsAsync(oms, orderId, count: 1)).Single().FilledQuantity.Should().Be(4m);
            await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 1, "the partial reaches accounting before the host stops");
        }

        // The completion lands in the durable inbox while no OMS is consuming it, then the host
        // restarts with an empty in-memory book.
        await using var restartedClient = CreateClient(store);
        await restartedClient.ProcessMessageAsync(TradeUpdateJson(
            "evt-fill", "alpaca-restart", orderId, "AAPL", "fill", "filled",
            qty: "10", filledQty: "10", price: "102", timestamp: "2026-08-07T14:30:05Z", fillQty: "6"));

        var restartedPortfolio = new PaperTradingPortfolio(100_000m);
        var restartedPublisher = new RecordingTradeEventPublisher();
        using var restartedOms = new OrderManagementSystem(
            new AlpacaReportsGateway(restartedClient),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: restartedPortfolio,
            tradeEventPublisher: restartedPublisher);

        var booked = await ReadFillIncrementsAsync(restartedOms, orderId, count: 1);
        booked.Single().FilledQuantity.Should().Be(6m,
            "only the completion's own executed quantity may post; the 4 already booked before the restart must not be re-posted");
        await WaitUntilAsync(() => restartedPublisher.AcceptedEvents.Count == 1, "the adopted fill reaches the accounting handoff");
        var tradeEvent = restartedPublisher.AcceptedEvents.Single();
        tradeEvent.FilledQuantity.Should().Be(6m);
        tradeEvent.FillPrice.Should().Be(102m);
        tradeEvent.FinancialAccountId.Should().BeNull("the original fund attribution cannot be recovered after a restart");

        var adopted = restartedOms.GetOrder(orderId);
        adopted.Should().NotBeNull("the fill's order is adopted into tracked state");
        adopted!.Status.Should().Be(OrderStatus.Filled);
        adopted.FilledQuantity.Should().Be(10m, "tracked state reflects the broker's cumulative");
        restartedPortfolio.Positions["AAPL"].Quantity.Should().Be(6L);
    }

    /// <summary>
    /// An untracked option fill cannot be booked from the report alone: the contract multiplier
    /// the submission carried is gone with the previous host, and booking at share semantics
    /// would post a hundredth of the exposure. It must be refused, not approximated.
    /// </summary>
    [Fact]
    public async Task RestartReplay_UntrackedOptionFill_IsRefusedRatherThanBookedAtShareSemantics()
    {
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new AlpacaReportsGateway(client),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-option-fill", "alpaca-opt", "MDN-20260807-000001", "AAPL260918C00200000", "fill", "filled",
            qty: "2", filledQty: "2", price: "5.10", timestamp: "2026-08-07T14:30:05Z", fillQty: "2",
            assetClass: "us_option"));

        // The pump is sequential, so once a later adoptable equity fill has been observed the
        // option fill has been fully processed -- and it must have gone nowhere.
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-equity-fill", "alpaca-eq", "MDN-20260807-000002", "MSFT", "fill", "filled",
            qty: "3", filledQty: "3", price: "400", timestamp: "2026-08-07T14:30:06Z", fillQty: "3"));
        (await ReadFillIncrementsAsync(oms, "MDN-20260807-000002", count: 1)).Single().FilledQuantity.Should().Be(3m);
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 1, "the equity fill is adopted and booked");

        publisher.AcceptedEvents.Should().OnlyContain(tradeEvent => tradeEvent.Symbol == "MSFT",
            "an option fill without its multiplier must not reach accounting at share semantics");
        oms.GetOrder("MDN-20260807-000001").Should().BeNull("the option order is not adopted");
        portfolio.Positions.Should().NotContainKey("AAPL260918C00200000");
    }

    /// <summary>
    /// A fill re-read from the REST activity history cannot be told from one the previous host
    /// already booked under its stream event id, which is exactly what the reconnect overlap
    /// window re-reads. Snapshot-derived fills for untracked orders are therefore never adopted.
    /// </summary>
    [Fact]
    public async Task RestartReplay_SnapshotDerivedFillForAnUntrackedOrder_IsNotAdopted()
    {
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new AlpacaReportsGateway(client),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        client.ConfigureReconciliation((_, _) => Task.FromResult<IReadOnlyList<AlpacaReconciliationReport>>(
        [
            new AlpacaReconciliationReport(
                "rest-fill-activity",
                "activity-rest-1",
                ReconciledReport("MDN-20260807-000009", cumulativeQuantity: 4m, fillPrice: 101m,
                    OrderStatus.Filled, "2026-08-07T14:30:01Z") with { LastFillQuantity = 4m, AssetClass = "us_equity" })
        ]));
        await client.ReconcileAfterConnectAsync();

        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-equity-fill-2", "alpaca-eq-2", "MDN-20260807-000010", "MSFT", "fill", "filled",
            qty: "3", filledQty: "3", price: "400", timestamp: "2026-08-07T14:30:06Z", fillQty: "3"));
        (await ReadFillIncrementsAsync(oms, "MDN-20260807-000010", count: 1)).Single().FilledQuantity.Should().Be(3m);
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 1, "the streamed equity fill is adopted and booked");

        publisher.AcceptedEvents.Should().OnlyContain(tradeEvent => tradeEvent.Symbol == "MSFT",
            "a REST-derived fill for an untracked order may already have been booked before the restart");
        oms.GetOrder("MDN-20260807-000009").Should().BeNull();
    }

    /// <summary>
    /// The durable inbox guarantees admission, not booking, and may deliver out of order. When
    /// the completion of an order is replayed before an earlier partial of it, adoption from the
    /// completion books only that event's own quantity; the partial that arrives afterwards must
    /// still be booked as its own quantity rather than suppressed by the cumulative the order
    /// already carries.
    /// </summary>
    [Fact]
    public async Task RestartReplay_EarlierIncrementDeliveredAfterTheCompletionThatAdoptedTheOrder_IsStillBooked()
    {
        const string orderId = "MDN-20260807-000011";
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);

        // Both events are pending in the inbox while no host consumes them, and the completion
        // is delivered first.
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-late-fill", "alpaca-late", orderId, "AAPL", "fill", "filled",
            qty: "10", filledQty: "10", price: "102", timestamp: "2026-08-07T14:30:05Z", fillQty: "6"));
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-late-partial", "alpaca-late", orderId, "AAPL", "partial_fill", "partially_filled",
            qty: "10", filledQty: "4", price: "101", timestamp: "2026-08-07T14:30:01Z", fillQty: "4"));

        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new AlpacaReportsGateway(client),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        var booked = await ReadFillIncrementsAsync(oms, orderId, count: 2);
        booked.Select(static increment => increment.FilledQuantity).Should().Equal(new[] { 6m, 4m },
            "the completion adopts the order as its own 6 and the earlier partial then books its own 4");
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 2, "both increments reach the accounting handoff");
        publisher.AcceptedEvents.Select(static tradeEvent => tradeEvent.FilledQuantity).Should().BeEquivalentTo([6m, 4m]);
        publisher.AcceptedEvents.Sum(static tradeEvent => tradeEvent.FilledQuantity * tradeEvent.FillPrice)
            .Should().Be(6m * 102m + 4m * 101m, "each increment posts at its own event's price");

        portfolio.Positions["AAPL"].Quantity.Should().Be(10L, "nothing of the order's quantity is omitted from the book");
        oms.GetOrder(orderId)!.FilledQuantity.Should().Be(10m);
    }

    /// <summary>
    /// A sell for an untracked order into a book that holds no long in the symbol cannot be
    /// told from the close of a position lost with the previous host. Booking it would open a
    /// phantom short and post the proceeds as a zero-gain reduction, so it is retained for
    /// reconciliation instead.
    /// </summary>
    [Fact]
    public async Task RestartReplay_UntrackedSellWithNoKnownLong_IsRetainedRatherThanBookedAsAShort()
    {
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new AlpacaReportsGateway(client),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-orphan-sell", "alpaca-orphan-sell", "MDN-20260807-000012", "AAPL", "fill", "filled",
            qty: "4", filledQty: "4", price: "110", timestamp: "2026-08-07T14:30:05Z", fillQty: "4", side: "sell"));

        // The pump is sequential: once the later adoptable buy has been observed, the sell has
        // been fully processed.
        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-sentinel-buy", "alpaca-sentinel", "MDN-20260807-000013", "MSFT", "fill", "filled",
            qty: "3", filledQty: "3", price: "400", timestamp: "2026-08-07T14:30:06Z", fillQty: "3"));
        (await ReadFillIncrementsAsync(oms, "MDN-20260807-000013", count: 1)).Single().FilledQuantity.Should().Be(3m);
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 1, "the buy that opens a long is adopted and booked");

        publisher.AcceptedEvents.Should().OnlyContain(tradeEvent => tradeEvent.Symbol == "MSFT",
            "a sell against no known long must not reach accounting as a short-open");
        oms.GetOrder("MDN-20260807-000012").Should().BeNull("the sell's order is not adopted");
        portfolio.Positions.Should().NotContainKey("AAPL", "no phantom short is opened");
    }

    /// <summary>
    /// The same sell against a long the book does hold is bookable: the lot it reduces supplies
    /// the cost basis, so the close posts with its realised gain rather than as a short-open.
    /// </summary>
    [Fact]
    public async Task RestartReplay_UntrackedSellAgainstAKnownLong_BooksTheCloseAgainstItsLot()
    {
        var store = new InMemoryCursorStore();
        await using var client = CreateClient(store);
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "seed-long",
            ClientOrderId = "seed-long",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 100m,
            OrderStatus = OrderStatus.Filled,
            ReportType = ExecutionReportType.Fill,
            Timestamp = DateTimeOffset.Parse("2026-08-07T13:00:00Z")
        });
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new AlpacaReportsGateway(client),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        await client.ProcessMessageAsync(TradeUpdateJson(
            "evt-close-sell", "alpaca-close-sell", "MDN-20260807-000014", "AAPL", "fill", "filled",
            qty: "4", filledQty: "4", price: "110", timestamp: "2026-08-07T14:30:05Z", fillQty: "4", side: "sell"));

        (await ReadFillIncrementsAsync(oms, "MDN-20260807-000014", count: 1)).Single().FilledQuantity.Should().Be(4m);
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 1, "the close is adopted and reaches accounting");
        publisher.AcceptedEvents.Single().Side.Should().Be(OrderSide.Sell);

        portfolio.Positions["AAPL"].Quantity.Should().Be(6L, "the sell reduces the known long");
        portfolio.RealisedPnl.Should().Be(4m * (110m - 100m), "the close realises against the lot's basis");
        oms.GetOrder("MDN-20260807-000014")!.Status.Should().Be(OrderStatus.Filled);
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
        string? reason = null,
        string? fillQty = null,
        string assetClass = "us_equity",
        string side = "buy")
    {
        var order = new Dictionary<string, object?>
        {
            ["id"] = alpacaOrderId,
            ["client_order_id"] = clientOrderId,
            ["symbol"] = symbol,
            ["qty"] = qty,
            ["filled_qty"] = filledQty,
            ["side"] = side,
            ["status"] = status,
            ["asset_class"] = assetClass,
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
        if (fillQty is not null)
            data["qty"] = fillQty;
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
