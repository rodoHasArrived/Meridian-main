using System.Threading.Channels;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using ExecutionMode = Meridian.Execution.Sdk.ExecutionMode;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderFillProcessorTests
{
    [Fact]
    public async Task PortfolioFailure_KeepsPreciseExposureAndOwnershipUntilApplicationSucceeds()
    {
        var order = CreateOrder("partially-applied") with
        {
            Quantity = 7m,
            FilledQuantity = 7m,
            ContractMultiplier = 100m,
            StrategyId = "strategy-a",
            RunId = "run-a"
        };
        var report = CreateReport(order.OrderId);
        var portfolio = new FailFirstPortfolioRead();
        var publisherStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublisher = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new RecordingPublisher
        {
            OnPublishAsync = async _ =>
            {
                publisherStarted.TrySetResult();
                await releasePublisher.Task;
            }
        };
        var processor = CreateProcessor(order, portfolio, publisher, null, out _);
        processor.ReservePending(report);
        Assert.Equal(10m, Assert.Single(processor.GetExposureReservations()).Quantity);

        var failure = await Assert.ThrowsAsync<AccountingHandoffException>(
            () => processor.ProcessAsync(null, report, previousFilledQuantity: 3m));
        Assert.Same(portfolio.Failure, failure.InnerException);
        processor.ReleasePending(report);

        var reservation = Assert.Single(processor.GetExposureReservations());
        Assert.Equal(4m, reservation.Quantity);
        Assert.Equal(order.FundAccountId, reservation.FundAccountId);
        Assert.Equal(order.StrategyId, reservation.StrategyId);
        Assert.Equal(order.RunId, reservation.RunId);
        Assert.Equal(100m, reservation.ContractMultiplier);
        Assert.Contains(order.OrderId, processor.GetIncompleteOrderIds());
        Assert.Empty(publisher.Events);

        var retry = processor.ProcessAsync(null, report, previousFilledQuantity: 7m);
        try
        {
            await publisherStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(processor.GetExposureReservations());
            Assert.Contains(order.OrderId, processor.GetIncompleteOrderIds());
            Assert.Equal(4m, Assert.Single(publisher.Events).FilledQuantity);
        }
        finally
        {
            releasePublisher.TrySetResult();
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Empty(processor.GetIncompleteOrderIds());
    }

    [Fact]
    public async Task Retry_AfterSessionAndSubscriberFailures_DoesNotRepeatCompletedStages()
    {
        var order = CreateOrder("staged-retry");
        var report = CreateReport(order.OrderId);
        var portfolio = new PaperTradingPortfolio(10_000m);
        var publisher = new RecordingPublisher();
        var store = new RecordingSessionStore { FailFirstClaim = true };
        await using var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance, store);
        var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("retry", null, 10_000m));
        var processor = CreateProcessor(order, portfolio, publisher, persistence, out var hub);
        await using var subscriber = hub.Subscribe(1, "retry-subscriber",
            (_, _, _) => throw new IOException("Subscriber recovery is unavailable."));
        try
        {
            var occupying = CreateReport("occupying");
            await hub.PublishAsync(occupying, new ExecutionReportHub.DeliveryCheckpoint());
            Assert.True(hub.Observer.TryRead(out _));

            var sessionFailure = await Assert.ThrowsAsync<AccountingHandoffException>(
                () => processor.ProcessAsync(session.SessionId, report, 0m));

            Assert.Equal("Session claim is unavailable.", sessionFailure.InnerException!.Message);
            Assert.Equal(8_500m, portfolio.Cash);
            Assert.Single(publisher.Events);
            Assert.Equal(1, store.ClaimAttempts);
            Assert.Equal(0, persistence.GetSession(session.SessionId)!.FillCount);
            Assert.Contains(order.OrderId, processor.GetIncompleteOrderIds());
            Assert.False(hub.Observer.TryRead(out _));

            var deliveryFailure = await Assert.ThrowsAsync<AccountingHandoffException>(
                () => processor.ProcessAsync(session.SessionId, report, 10m));

            Assert.Equal("Subscriber recovery is unavailable.", deliveryFailure.InnerException!.Message);
            Assert.Equal(8_500m, portfolio.Cash);
            Assert.Single(publisher.Events);
            Assert.Equal(2, store.ClaimAttempts);
            Assert.Equal(1, store.Acknowledgments);
            Assert.Equal(1, persistence.GetSession(session.SessionId)!.FillCount);
            Assert.Contains(order.OrderId, processor.GetIncompleteOrderIds());
            Assert.Empty(processor.GetExposureReservations());
            Assert.False(hub.Observer.TryRead(out _));
            Assert.True(subscriber.Reports.TryRead(out var occupyingDelivery));
            Assert.Same(occupying, occupyingDelivery);

            await processor.ProcessAsync(session.SessionId, report, 10m);
            await processor.ProcessAsync(session.SessionId, report, 10m);

            Assert.Equal(8_500m, portfolio.Cash);
            var position = Assert.IsType<ExecutionPosition>(Assert.Single(portfolio.Positions).Value);
            Assert.Equal(10m, position.ExactQuantity);
            var account = portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!;
            var accountPosition = Assert.IsType<ExecutionPosition>(Assert.Single(account.Positions).Value);
            Assert.Equal(10m, accountPosition.OwnerQuantities[order.FundAccountId!.Value.ToString("D")]);
            Assert.Single(publisher.Events);
            Assert.Equal(2, store.ClaimAttempts);
            Assert.Equal(1, store.Acknowledgments);
            Assert.Equal(1, persistence.GetSession(session.SessionId)!.FillCount);
            Assert.Empty(processor.GetIncompleteOrderIds());
            Assert.True(subscriber.Reports.TryRead(out var delivery));
            Assert.Same(report, delivery);
            Assert.False(subscriber.Reports.TryRead(out _));
            Assert.True(hub.Observer.TryRead(out var observation));
            Assert.Same(report, observation);
            Assert.False(hub.Observer.TryRead(out _));
        }
        finally
        {
            // A failed assertion must survive cleanup: the deliberately failing recovery
            // handler would otherwise throw again while disposal abandons a buffered report.
            while (subscriber.Reports.TryRead(out _))
            {
            }
        }
    }

    [Fact]
    public async Task RetentionPressure_KeepsIncompleteFilledOrderAndItsOwnershipContext()
    {
        var fundAccountId = Guid.NewGuid();
        var gateway = new FillGateway();
        var publisher = new RecordingPublisher
        {
            OnPublishAsync = fill => fill.OrderId == "incomplete"
                ? Task.FromException(new IOException("Accounting is temporarily unavailable."))
                : Task.CompletedTask
        };
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: new PaperTradingPortfolio(100_000m),
            tradeEventPublisher: publisher,
            options: new OrderManagementSystemOptions { MaxRetainedOrders = 1 });
        await oms.StartAsync();

        var incomplete = await oms.PlaceOrderAsync(Request("incomplete", fundAccountId));
        Assert.False(incomplete.Success);
        Assert.Equal(OrderStatus.Filled, incomplete.OrderState.Status);
        Assert.True((await oms.PlaceOrderAsync(Request("completed-one", null))).Success);
        Assert.True((await oms.PlaceOrderAsync(Request("completed-two", null))).Success);

        var retained = Assert.IsType<OrderState>(oms.GetOrder("incomplete"));
        Assert.Equal(OrderStatus.Filled, retained.Status);
        Assert.Equal(fundAccountId, retained.FundAccountId);
        Assert.Equal("retention-strategy", retained.StrategyId);
        Assert.Null(oms.GetOrder("completed-one"));

        publisher.OnPublishAsync = null;
        await gateway.PublishAsync(gateway.Reports["incomplete"]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ExecutionReport delivered;
        do
        {
            delivered = await oms.ExecutionReports.ReadAsync(timeout.Token);
        }
        while (delivered.OrderId != "incomplete");

        var attempts = publisher.Events.Where(fill => fill.OrderId == "incomplete").ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Same(attempts[0], attempts[1]);
        Assert.Equal(fundAccountId.ToString("D"), attempts[1].FinancialAccountId);
    }

    private static OrderFillProcessor CreateProcessor(
        OrderState order,
        IPortfolioState portfolio,
        RecordingPublisher publisher,
        PaperSessionPersistenceService? persistence,
        out ExecutionReportHub hub)
    {
        var context = new OrderFillContext(
            new Dictionary<string, OrderState> { [order.OrderId] = order },
            new Dictionary<string, string> { [order.OrderId] = order.FundAccountId!.Value.ToString("D") },
            new Dictionary<string, decimal> { [order.OrderId] = order.ContractMultiplier },
            new Dictionary<string, bool>());
        hub = new ExecutionReportHub(new OrderManagementSystemOptions(), () => "fill-test", null,
            NullLogger.Instance, new ExecutionFailureState(NullLogger.Instance));
        return new OrderFillProcessor(context, portfolio, persistence,
            new AccountingHandoffCoordinator(publisher, null, NullLogger.Instance), hub,
            new OrderExecutionAudit(null, () => "fill-test", NullLogger.Instance), NullLogger.Instance);
    }

    private static OrderState CreateOrder(string orderId) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
        FilledQuantity = 10m,
        Status = OrderStatus.Filled,
        FundAccountId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ExecutionReport CreateReport(string orderId) => new()
    {
        OrderId = orderId,
        ClientOrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.Filled,
        ReportType = ExecutionReportType.Fill,
        OrderQuantity = 10m,
        FilledQuantity = 10m,
        FillPrice = 150m,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static OrderRequest Request(string orderId, Guid? accountId) => new()
    {
        ClientOrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
        FundAccountId = accountId,
        StrategyId = "retention-strategy"
    };

    private sealed class FailFirstPortfolioRead : IPortfolioState
    {
        private int _reads;
        public IOException Failure { get; } = new("Portfolio view is temporarily unavailable.");
        public decimal Cash => 10_000m;
        public decimal PortfolioValue => Cash;
        public decimal UnrealisedPnl => 0m;
        public decimal RealisedPnl => ++_reads == 1 ? throw Failure : 0m;
        public IReadOnlyDictionary<string, IPosition> Positions { get; } = new Dictionary<string, IPosition>();
    }

    private sealed class RecordingPublisher : ITradeEventPublisher
    {
        public List<TradeExecutedEvent> Events { get; } = [];
        public Func<TradeExecutedEvent, Task>? OnPublishAsync { get; set; }
        public void Publish(TradeExecutedEvent tradeEvent) => throw new NotSupportedException();
        public Task PublishAsync(TradeExecutedEvent tradeEvent)
        {
            Events.Add(tradeEvent);
            return OnPublishAsync?.Invoke(tradeEvent) ?? Task.CompletedTask;
        }
    }

    private sealed class FillGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _stream = Channel.CreateUnbounded<ExecutionReport>();
        public Dictionary<string, ExecutionReport> Reports { get; } = [];
        public string GatewayId => "fill-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            var report = CreateReport(request.ClientOrderId!);
            Reports[report.OrderId] = report;
            return Task.FromResult(report);
        }
        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default)
            => _stream.Reader.ReadAllAsync(ct);
        public ValueTask PublishAsync(ExecutionReport report) => _stream.Writer.WriteAsync(report);
    }

    private sealed class RecordingSessionStore : IPaperSessionStore
    {
        public bool FailFirstClaim { get; init; }
        public int ClaimAttempts { get; private set; }
        public int Acknowledgments { get; private set; }
        private PaperSessionFillRecord? _fill;

        public Task<PaperSessionFillAppendResult> TryAppendFillAsync(
            string sessionId, PaperSessionFillRecord record, CancellationToken ct = default)
        {
            ClaimAttempts++;
            if (FailFirstClaim && ClaimAttempts == 1)
                throw new IOException("Session claim is unavailable.");
            record.Validate();
            _fill = record;
            return Task.FromResult(new PaperSessionFillAppendResult(PaperSessionFillAppendStatus.Added));
        }

        public Task MarkFillAppliedAsync(string sessionId, Guid fillId, string canonicalHash, CancellationToken ct = default)
        {
            Assert.Equal(_fill!.FillId, fillId);
            Assert.Equal(_fill.CanonicalHash, canonicalHash);
            Acknowledgments++;
            return Task.CompletedTask;
        }

        public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveLedgerJournalAsync(string sessionId, IReadOnlyList<PersistedJournalEntryDto> entries, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);
        public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExecutionReport>>([]);
        public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrderState>>([]);
        public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
    }
}
