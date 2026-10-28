using System.Threading.Channels;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemAmendmentHandoffTests
{
    [Fact]
    public async Task AmendmentFillHandoffFailure_RetainsPendingEvidenceUntilRetryCompletesWithoutRebooking()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"amendment-handoff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var gateway = new AmendmentGateway();
            var publisher = new FailFirstPublisher();
            var portfolio = new PaperTradingPortfolio(10_000m);
            var store = new FileBrokerageOrderRecoveryStore(Path.Combine(directory, "orders.json"), gateway.GatewayId);
            store.BindScope("paper:amendment-test");
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                portfolioState: portfolio, tradeEventPublisher: publisher, recoveryStore: store);
            await oms.StartAsync();
            Assert.True((await oms.PlaceOrderAsync(new OrderRequest
            {
                ClientOrderId = "client",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 10m,
                LimitPrice = 100m
            })).Success);

            var failure = await Assert.ThrowsAsync<AccountingHandoffException>(() =>
                oms.ModifyOrderAsync("client", new OrderModification { NewQuantity = 20m }));

            Assert.False(failure.WasRetained);
            Assert.Equal("Accounting is temporarily unavailable.", Assert.IsType<IOException>(failure.InnerException).Message);
            var pending = Assert.IsType<PendingOrderAmendment>(oms.GetPendingAmendmentForRecovery("client"));
            Assert.Equal(10m, pending.Original.Quantity);
            Assert.Equal(20m, pending.Proposed.Quantity);
            Assert.Equal("candidate-broker", pending.CandidateBrokerOrderId);
            var current = Assert.IsType<OrderState>(oms.GetOrder("client"));
            Assert.Equal(20m, current.Quantity);
            Assert.Equal(4m, current.FilledQuantity);
            Assert.Equal(9_600m, portfolio.Cash);
            Assert.Equal(1, publisher.Attempts.Count);
            var retained = Assert.Single(store.Load());
            Assert.True(retained.RequiresAmendmentRecovery);
            Assert.NotNull(retained.PendingAmendment);
            Assert.Equal(0m, retained.State.FilledQuantity);
            Assert.False(oms.ExecutionReports.TryRead(out _));

            await oms.ReconcileRecoveryOrderAsync(gateway.ModifiedReport);

            Assert.Null(oms.GetPendingAmendmentForRecovery("client"));
            Assert.Empty(oms.GetRecoveryOrders());
            Assert.Equal(9_600m, portfolio.Cash);
            Assert.Equal(2, publisher.Attempts.Count);
            Assert.Same(publisher.Attempts[0], publisher.Attempts[1]);
            Assert.Equal(4m, publisher.Attempts[1].FilledQuantity);
            retained = Assert.Single(store.Load());
            Assert.False(retained.RequiresAmendmentRecovery);
            Assert.False(retained.RequiresRecovery);
            Assert.Null(retained.PendingAmendment);
            Assert.Equal(20m, retained.State.Quantity);
            Assert.Equal(4m, retained.State.FilledQuantity);
            Assert.Equal("candidate-broker", retained.BrokerOrderId);
            Assert.True(oms.ExecutionReports.TryRead(out var delivered));
            Assert.Equal(4m, delivered!.FilledQuantity);
            Assert.Equal(ExecutionReportType.PartialFill, delivered.ReportType);

            // A later duplicate recovery report cannot repeat the completed fill or recreate
            // amendment uncertainty after its durable watermark has caught up.
            await oms.ReconcileRecoveryOrderAsync(gateway.ModifiedReport);
            Assert.Null(oms.GetPendingAmendmentForRecovery("client"));
            Assert.Empty(oms.GetRecoveryOrders());
            Assert.Equal(9_600m, portfolio.Cash);
            Assert.Equal(2, publisher.Attempts.Count);
            Assert.False(oms.ExecutionReports.TryRead(out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FailFirstPublisher : ITradeEventPublisher
    {
        public List<TradeExecutedEvent> Attempts { get; } = [];
        public void Publish(TradeExecutedEvent tradeEvent) => throw new NotSupportedException();
        public Task PublishAsync(TradeExecutedEvent tradeEvent)
        {
            Attempts.Add(tradeEvent);
            return Attempts.Count == 1
                ? Task.FromException(new IOException("Accounting is temporarily unavailable."))
                : Task.CompletedTask;
        }
    }

    private sealed class AmendmentGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        public string GatewayId => "amendment-handoff-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public ExecutionReport ModifiedReport { get; } = new()
        {
            OrderId = "candidate-broker",
            ClientOrderId = "client",
            GatewayOrderId = "candidate-broker",
            ReplacesOrderId = "original-broker",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Modified,
            OrderStatus = OrderStatus.PartiallyFilled,
            OrderQuantity = 20m,
            FilledQuantity = 4m,
            FillPrice = 100m,
            HasOrderTerms = true,
            OrderLimitPrice = 100m,
            Timestamp = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)
        };
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionReport
            {
                OrderId = "original-broker",
                ClientOrderId = request.ClientOrderId,
                GatewayOrderId = "original-broker",
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.New,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity,
                Timestamp = DateTimeOffset.UtcNow
            });
        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            Task.FromResult(ModifiedReport);
        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);
    }
}
