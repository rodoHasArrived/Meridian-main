using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Storage.Operations;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Strategies;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    [Fact]
    public async Task ResumePendingRunsAsync_StopAfterBrokerDispatchRetainsAcceptedSubmissionInReopenedAuditWal()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-accepted-order-audit-", Guid.NewGuid().ToString("N"));
        var auditOptions = new ExecutionAuditTrailOptions(Path.Combine(dataRoot, "audit"));
        try
        {
            var accountId = Guid.NewGuid();
            var run = StrategyRunEntry.StartForFundAccount(BuyAndHoldLiveStrategy.CatalogId, "Buy and hold", RunType.Live,
                accountId, parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "1" });
            await new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot)).RecordRunAsync(run);
            var repository = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var feed = new AuditCancellationMarketFeed();
            var cache = new LiveMarketDataCache();
            var gateway = new AcceptedAcknowledgementBarrierGateway();
            var brokerage = CreateLiveBrokerageConfiguration();
            var provider = new AccountReadinessProvider(run.RunId, accountId);
            var gate = new TradingOperatorLiveOrderReadinessGate(provider,
                NullLogger<TradingOperatorLiveOrderReadinessGate>.Instance);
            ExecutionSdk.OrderRequest dispatched;
            await using (var audit = new ExecutionAuditTrailService(auditOptions, NullLogger<ExecutionAuditTrailService>.Instance))
            await using (var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                brokerageConfiguration: brokerage, liveOrderReadinessGate: gate, auditTrail: audit))
            {
                await oms.StartAsync();
                await using var engine = CreateEngine(repository, feed, cache, oms,
                    options: new LiveTradingEngineOptions { AllowLiveRuns = true }, brokerageConfiguration: brokerage);
                try
                {
                    (await engine.ResumePendingRunsAsync()).Should().Be(1);
                    await feed.Subscribed.Task.WaitAsync(WaitBudget);
                    await feed.PublishTradeAsync(cache);
                    dispatched = await gateway.Dispatched.Task.WaitAsync(WaitBudget);
                    dispatched.FundAccountId.Should().Be(accountId);
                    dispatched.Metadata!["runId"].Should().Be(run.RunId);
                    (await provider.Called.Task.WaitAsync(WaitBudget)).Should().Be(accountId);

                    var stop = engine.StopRunAsync(run.RunId);
                    await gateway.CallerCancelled.Task.WaitAsync(WaitBudget);
                    gateway.CallerToken.IsCancellationRequested.Should().BeTrue();
                    stop.IsCompleted.Should().BeFalse("the broker acknowledgement is still held at the network boundary");
                    gateway.ReleaseAcknowledgement();
                    (await stop.WaitAsync(WaitBudget)).Should().BeTrue();
                }
                finally
                {
                    gateway.ReleaseAcknowledgement();
                }
                gateway.Requests.Should().ContainSingle();
                gateway.SubmitCalls.Should().Be(1);
            }

            // Reopening proves the accepted submission survived in the real WAL, rather than
            // appearing only in the audit service's in-memory projection before cancellation.
            await using var reopened = new ExecutionAuditTrailService(auditOptions, NullLogger<ExecutionAuditTrailService>.Instance);
            var records = await reopened.GetAllAsync();
            var submitted = records.Should().ContainSingle(entry => entry.Action == "OrderSubmitted" && entry.RunId == run.RunId).Subject;
            submitted.OrderId.Should().Be(dispatched.ClientOrderId);
            submitted.Outcome.Should().Be(ExecutionSdk.OrderStatus.Accepted.ToString());
            submitted.Scope.Should().Contain($"run:{run.RunId}");
            submitted.Metadata!["liveReadinessDecision"].Should().Be("approved");
            submitted.Metadata["liveReadinessEvidenceReference"].Should().Be("live-readiness:AUDIT-LIVE-001;snapshot:snapshot-live-001");
            var retained = (await new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot)).GetRunByIdAsync(run.RunId))!;
            retained.FundAccountId.Should().Be(accountId);
            retained.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
            retained.InputHashSha256.Should().Be(StrategyRunEntry.ComputeAccountBoundInputHash(retained));
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PlaceOrderAsync_AlreadyCancelledCallerCannotDispatchOrCreateSubmissionAudit()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-cancelled-order-audit-", Guid.NewGuid().ToString("N"));
        try
        {
            var accountId = Guid.NewGuid();
            const string runId = "pre-dispatch-cancelled-run";
            var gateway = new AcceptedAcknowledgementBarrierGateway();
            gateway.ReleaseAcknowledgement();
            var provider = new AccountReadinessProvider(runId, accountId);
            var gate = new TradingOperatorLiveOrderReadinessGate(provider,
                NullLogger<TradingOperatorLiveOrderReadinessGate>.Instance);
            await using var audit = new ExecutionAuditTrailService(new ExecutionAuditTrailOptions(dataRoot),
                NullLogger<ExecutionAuditTrailService>.Instance);
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                brokerageConfiguration: CreateLiveBrokerageConfiguration(), liveOrderReadinessGate: gate, auditTrail: audit);
            await oms.StartAsync();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var request = new ExecutionSdk.OrderRequest
            {
                Symbol = "AAPL",
                Side = ExecutionSdk.OrderSide.Buy,
                Type = ExecutionSdk.OrderType.Market,
                Quantity = 1m,
                ClientOrderId = "pre-dispatch-cancelled-order",
                StrategyId = BuyAndHoldLiveStrategy.CatalogId,
                FundAccountId = accountId,
                Metadata = new Dictionary<string, string> { ["runId"] = runId }
            };

            var place = () => oms.PlaceOrderAsync(request, cancelled.Token);

            await place.Should().ThrowAsync<OperationCanceledException>();
            gateway.Requests.Should().BeEmpty();
            gateway.SubmitCalls.Should().Be(0, "cancellation must be refused before the OMS enters the broker adapter");
            gateway.Dispatched.Task.IsCompleted.Should().BeFalse();
            (await audit.GetAllAsync()).Should().NotContain(entry => entry.Action == "OrderSubmitted");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    private sealed class AuditCancellationMarketFeed : ILiveMarketEventFeed
    {
        private readonly Channel<LiveMarketEvent> _events = Channel.CreateUnbounded<LiveMarketEvent>();
        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(IReadOnlyCollection<string> symbols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Subscribed.TrySetResult();
            await foreach (var item in _events.Reader.ReadAllAsync(ct))
                yield return item;
        }

        public ValueTask PublishTradeAsync(LiveMarketDataCache cache)
        {
            var trade = new Trade(DateTimeOffset.UtcNow, "AAPL", 101m, Size: 100,
                Aggressor: AggressorSide.Buy, SequenceNumber: 1);
            cache.RecordTrade("AAPL", trade);
            return _events.Writer.WriteAsync(new LiveMarketEvent(trade.Timestamp, "AAPL", trade));
        }
    }

    private sealed class AcceptedAcknowledgementBarrierGateway : ExecutionSdk.IExecutionGateway, ExecutionSdk.IExecutionGatewayModeProvider
    {
        private readonly ControllableExecutionGateway _inner = new(live: true);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _submitCalls;
        public TaskCompletionSource<ExecutionSdk.OrderRequest> Dispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CallerCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CallerToken { get; private set; }
        public int SubmitCalls => Volatile.Read(ref _submitCalls);
        public IReadOnlyCollection<ExecutionSdk.OrderRequest> Requests => _inner.Requests;
        public string GatewayId => _inner.GatewayId;
        public bool IsConnected => _inner.IsConnected;
        public ExecutionSdk.ExecutionMode ExecutionMode => _inner.ExecutionMode;
        public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
        public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);

        public async Task<ExecutionSdk.ExecutionReport> SubmitOrderAsync(ExecutionSdk.OrderRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _submitCalls);
            ct.ThrowIfCancellationRequested();
            var accepted = await _inner.SubmitOrderAsync(request, ct);
            CallerToken = ct;
            using var registration = ct.Register(() => CallerCancelled.TrySetResult());
            Dispatched.TrySetResult(request);
            // The broker has already accepted this request. Caller cancellation controls the
            // run's wait, while the broker acknowledgement still has to be observed and retained.
            await _release.Task;
            return accepted;
        }

        public void ReleaseAcknowledgement() => _release.TrySetResult();
        public Task<ExecutionSdk.ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
            => _inner.CancelOrderAsync(orderId, ct);
        public Task<ExecutionSdk.ExecutionReport> ModifyOrderAsync(string orderId, ExecutionSdk.OrderModification modification,
            CancellationToken ct = default) => _inner.ModifyOrderAsync(orderId, modification, ct);
        public IAsyncEnumerable<ExecutionSdk.ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default)
            => _inner.StreamExecutionReportsAsync(ct);
    }
}
