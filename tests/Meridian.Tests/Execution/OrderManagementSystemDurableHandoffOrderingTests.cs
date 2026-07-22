using System.Collections.Concurrent;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemDurableHandoffOrderingTests
{
    [Fact]
    public async Task SynchronousBrokerFill_WhenSessionOrderWriteFails_RemainsFilledAndReachesAccounting()
    {
        var gateway = new ControllableExecutionGateway
        {
            SubmitAck = Report("pending", OrderStatus.Filled, ExecutionReportType.Fill, 10m, 150m)
        };
        var publisher = new RecordingTradeEventPublisher();
        var sessionPersistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            new FailingOrderUpdateStore());
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            sessionPersistence: sessionPersistence,
            tradeEventPublisher: publisher);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            Metadata = new Dictionary<string, string> { ["sessionId"] = "session-a" }
        });

        result.Success.Should().BeTrue(
            "non-authoritative session persistence cannot rewrite a broker-accepted fill as rejected");
        result.OrderState.Status.Should().Be(OrderStatus.Filled);
        oms.GetOrder(result.OrderId)!.Status.Should().Be(OrderStatus.Filled);
        publisher.AcceptedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task StreamedFill_ShutdownDuringSessionWrite_AlreadyReachedAccountingHandoff()
    {
        var gateway = new ControllableExecutionGateway
        {
            SubmitAck = Report("pending", OrderStatus.Accepted, ExecutionReportType.New, 0m, null)
        };
        var publisher = new RecordingTradeEventPublisher();
        var sessionStore = new BlockSecondOrderUpdateStore();
        var sessionPersistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            sessionStore);
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            sessionPersistence: sessionPersistence,
            tradeEventPublisher: publisher);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            Metadata = new Dictionary<string, string> { ["sessionId"] = "session-b" }
        });
        placed.Success.Should().BeTrue();

        await gateway.PublishAsync(
            Report(placed.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, 10m, 150m));
        await sessionStore.BlockedUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await oms.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        publisher.AcceptedEvents.Should().ContainSingle(
            "the report-pump cancellation may stop session bookkeeping only after durable accounting admission");
        publisher.AcceptedEvents.Single().OrderId.Should().Be(placed.OrderId);
        oms.GetOrder(placed.OrderId)!.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task BrokerFillWithoutPrice_FailsClosedAsFilledAndRetainsDurableReconciliationEvidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "oms-unresolved-fill-audit",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var gateway = new ControllableExecutionGateway
            {
                SubmitAck = Report("pending", OrderStatus.Filled, ExecutionReportType.Fill, 10m, null)
            };
            var publisher = new RecordingTradeEventPublisher();
            await using (var audit = new ExecutionAuditTrailService(
                             root,
                             NullLogger<ExecutionAuditTrailService>.Instance))
            await using (var oms = new OrderManagementSystem(
                             gateway,
                             NullLogger<OrderManagementSystem>.Instance,
                             auditTrail: audit,
                             tradeEventPublisher: publisher))
            {
                var result = await oms.PlaceOrderAsync(new OrderRequest
                {
                    Symbol = "AAPL",
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = 10m
                });

                result.Success.Should().BeFalse();
                result.OrderState.Status.Should().Be(OrderStatus.Filled,
                    "the broker truth cannot be rewritten to Rejected when accounting input is malformed");
                publisher.AcceptedEvents.Should().BeEmpty(
                    "a missing execution price must never be converted into fabricated ledger economics");
            }

            await using var reopened = new ExecutionAuditTrailService(
                root,
                NullLogger<ExecutionAuditTrailService>.Instance);
            var retained = await reopened.GetAllAsync();
            retained.Should().ContainSingle(entry =>
                entry.Action == "AccountingHandoffUnresolved" &&
                entry.Outcome == "AttentionRequired" &&
                entry.Metadata != null &&
                entry.Metadata.GetValueOrDefault("fillPrice") == "missing");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static ExecutionReport Report(
        string orderId,
        OrderStatus status,
        ExecutionReportType reportType,
        decimal filledQuantity,
        decimal? fillPrice) =>
        new()
        {
            OrderId = orderId,
            ClientOrderId = orderId,
            ReportType = reportType,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = status,
            OrderQuantity = 10m,
            FilledQuantity = filledQuantity,
            FillPrice = fillPrice,
            Commission = 0m,
            Timestamp = DateTimeOffset.UtcNow
        };

    private sealed class ControllableExecutionGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();

        public required ExecutionReport SubmitAck { get; init; }

        public string GatewayId => "durable-ordering-test";

        public bool IsConnected => true;

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            var orderId = request.ClientOrderId ?? SubmitAck.OrderId;
            return Task.FromResult(SubmitAck with
            {
                OrderId = orderId,
                ClientOrderId = orderId,
                Symbol = request.Symbol
            });
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId,
            OrderModification modification,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public ValueTask PublishAsync(ExecutionReport report) => _reports.Writer.WriteAsync(report);
    }

    private sealed class RecordingTradeEventPublisher : ITradeEventPublisher
    {
        public ConcurrentQueue<TradeExecutedEvent> AcceptedEvents { get; } = new();

        public void Publish(TradeExecutedEvent tradeEvent) => AcceptedEvents.Enqueue(tradeEvent);
    }

    private sealed class FailingOrderUpdateStore : PaperSessionStoreStub
    {
        public override Task AppendOrderUpdateAsync(
            string sessionId,
            OrderState order,
            CancellationToken ct = default) =>
            Task.FromException(new IOException("simulated session order-history outage"));
    }

    private sealed class BlockSecondOrderUpdateStore : PaperSessionStoreStub
    {
        private int _orderUpdateCount;

        public TaskCompletionSource BlockedUpdateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task AppendOrderUpdateAsync(
            string sessionId,
            OrderState order,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _orderUpdateCount) != 2)
                return;

            BlockedUpdateStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private abstract class PaperSessionStoreStub : IPaperSessionStore
    {
        public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default) =>
            Task.CompletedTask;

        public virtual Task AppendOrderUpdateAsync(
            string sessionId,
            OrderState order,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveLedgerJournalAsync(
            string sessionId,
            IReadOnlyList<PersistedJournalEntryDto> entries,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);

        public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionReport>>([]);

        public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderState>>([]);

        public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
    }
}
