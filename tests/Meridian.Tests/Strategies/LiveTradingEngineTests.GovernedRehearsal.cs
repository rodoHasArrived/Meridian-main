using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Events;
using Meridian.Execution.Live;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Risk;
using Meridian.Risk.Rules;
using Meridian.Storage;
using Meridian.Storage.Operations;
using Meridian.Storage.Services;
using Meridian.Strategies.Artifacts;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Strategies;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    [Fact]
    public async Task BuyAndHold_GovernedExecutionRehearsal()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var root = Path.Combine(Path.GetTempPath(), "meridian-governed-rehearsal", Guid.NewGuid().ToString("N"));
        const string symbol = "AAPL";
        const decimal openingCash = 10_000m;
        var account = Guid.Parse("bec00700-f1a1-4000-8000-000000000001");
        decimal[] prices = [100m, 102m, 105m, 109m, 110m, 114m];
        var firstDay = new DateOnly(2025, 1, 6);
        var bars = Path.Combine(root, symbol);
        Directory.CreateDirectory(bars);
        try
        {
            var observations = prices.Select((price, index) =>
            {
                var bar = new HistoricalBar(symbol, firstDay.AddDays(index), price, price, price, price,
                    1_000_000L, "synthetic-rehearsal", index + 1L);
                return JsonSerializer.Serialize(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), symbol, bar,
                    "synthetic-rehearsal", index + 1L), MarketDataJsonContext.HighPerformanceOptions);
            });
            await File.WriteAllLinesAsync(Path.Combine(bars, $"{symbol}_bars_{firstDay:yyyy-MM-dd}.jsonl"), observations, ct);
            var data = new StorageCatalogService(root, new StorageOptions());
            var backtest = new BacktestEngine(NullLogger<BacktestEngine>.Instance, data);
            var backtestRequest = new BacktestRequest(firstDay, firstDay.AddDays(prices.Length - 1),
                Symbols: [symbol], InitialCash: openingCash, DataRoot: root, CommissionKind: BacktestCommissionKind.Free,
                SlippageBasisPoints: 0m, FillTiming: FillTiming.SameBar, RiskFreeRate: 0, AdjustForCorporateActions: false);
            var strategy = new BuyAndHoldLiveStrategy(quantityPerSymbol: 10);
            var parameters = new Dictionary<string, string> { ["symbols"] = symbol, ["quantity"] = "10" };
            var artifacts = new StrategyArtifactRegistry(root);
            var artifactId = await artifacts.RetainAsync(StrategyArtifact.FromCode(strategy.StrategyId, strategy,
                parameters, backtestRequest, "synthetic-rehearsal/daily-bars-v1"), ct);
            var runs = new StrategyRunStore(new FileOperationalCaseHistoryStore(root));
            var history = new JsonlPromotionRecordStore(Path.Combine(root, "promotions"), NullLogger<JsonlPromotionRecordStore>.Instance);
            var research = StrategyRunEntry.StartForFundAccount(strategy.StrategyId, strategy.Name, RunType.Backtest,
                account, parameterSet: parameters, datasetReference: "synthetic-rehearsal/daily-bars-v1",
                retainedEvidenceReferences: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)
                    .Select(RehearsalEvidenceReference).ToArray(),
                executionRealism: backtestRequest.ToRealismDescriptor()).WithStrategyArtifact(artifactId);
            await runs.RecordRunAsync(research, ct);
            var result = await backtest.RunAsync(backtestRequest, strategy, ct: ct);
            result.Fills.Should().ContainSingle();
            result.Metrics.FinalEquity.Should().Be(openingCash + 10m * (prices[^1] - prices[0]));
            await runs.RecordRunAsync(research.Complete(result), ct);

            var controls = new ExecutionOperatorControlService(
                new ExecutionOperatorControlOptions(Path.Combine(root, "controls")), NullLogger<ExecutionOperatorControlService>.Instance);
            await using var audit = new ExecutionAuditTrailService(new ExecutionAuditTrailOptions(Path.Combine(root, "audit")),
                NullLogger<ExecutionAuditTrailService>.Instance);
            var risk = new CompositeRiskValidator(
                [new OrderRateThrottle(20, NullLogger<OrderRateThrottle>.Instance)], NullLogger<CompositeRiskValidator>.Instance);
            var paperCache = new LiveMarketDataCache();
            var paperFeed = new RehearsalFeed();
            var paperBook = CreateRehearsalBook(account, openingCash);
            var paperGateway = new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance, liveFeed: paperCache);
            await paperGateway.ConnectAsync(ct);
            await using var paperOms = new OrderManagementSystem(paperGateway, NullLogger<OrderManagementSystem>.Instance,
                riskValidator: risk, operatorControls: controls, portfolioState: paperBook);
            await paperOms.StartAsync(ct);
            await using var paperEngine = new LiveTradingEngine(LiveStrategyCatalog.CreateDefault(), paperFeed, paperCache,
                new Meridian.Execution.Adapters.OmsGovernedExecutionOrderGateway(paperGateway), paperOms, paperBook, runs,
                artifacts: artifacts);
            var paperPromotion = new PromotionService(runs, new BacktestToLivePromoter(), history,
                NullLogger<PromotionService>.Instance, controls, audit, runLauncher: paperEngine);
            var paperApproval = await paperPromotion.ApproveAsync(RehearsalApproval(research.RunId, RunType.Paper), ct);
            paperApproval.Success.Should().BeTrue(paperApproval.Reason);
            paperEngine.ActiveRunIds.Should().Contain(paperApproval.NewRunId!);
            var paperDay = DateTimeOffset.UtcNow;
            await SendRehearsalPriceAsync(paperFeed, paperCache, symbol, prices[0], paperDay, 1, ct);
            paperOms.GetCompletedOrders().Should().ContainSingle(order => order.Status == ExecutionSdk.OrderStatus.Filled);
            for (var index = 1; index < prices.Length; index++)
            {
                paperBook.UpdateMarketPrice(symbol, prices[index]);
                await SendRehearsalPriceAsync(paperFeed, paperCache, symbol, prices[index], paperDay.AddDays(index), index + 1, ct);
            }
            (await paperEngine.StopRunAsync(paperApproval.NewRunId!, ct)).Should().BeTrue();
            var paper = (await runs.GetRunByIdAsync(paperApproval.NewRunId!, ct))!;
            paper.StrategyArtifactId.Should().Be(artifactId);
            paper.Metrics!.Metrics.SharpeRatio.Should().BeGreaterThan(0.5);
            var inSample = await backtest.RunAsync(backtestRequest with { To = firstDay.AddDays(2) }, new BuyAndHoldLiveStrategy(), ct: ct);
            var outOfSample = await backtest.RunAsync(backtestRequest with { From = firstDay.AddDays(3) }, new BuyAndHoldLiveStrategy(), ct: ct);
            var degradation = outOfSample.Metrics.SharpeRatio / inSample.Metrics.SharpeRatio;
            await paperPromotion.RecordWalkForwardEvidenceAsync(paper.RunId,
                new(outOfSample.Metrics.SharpeRatio, outOfSample.Metrics.MaxDrawdownPercent, degradation, 2,
                    DateTimeOffset.UtcNow, "synthetic-rehearsal/chronological-windows"), ct);

            await using var broker = new RehearsalBroker();
            var config = CreateLiveBrokerageConfiguration();
            var clock = new RehearsalClock(DateTimeOffset.UtcNow);
            var authorization = new StrategyLiveAuthorizationService(root, runs, history, artifacts, broker, clock,
                (fundAccount, _) => Task.FromResult(fundAccount == account));
            var allow = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
                ExecutionManualOverrideKinds.AllowLivePromotion, "Reviewed synthetic execution", "synthetic-reviewer",
                StrategyId: paper.StrategyId, RunId: paper.RunId), ct);
            var grant = new StrategyLiveAuthorizationRequest(artifactId, [symbol], 1_200m, "USD", clock.GetUtcNow().AddHours(1));
            var livePromotion = new PromotionService(runs, new BacktestToLivePromoter(), history, NullLogger<PromotionService>.Instance,
                controls, audit, config, trustEvidenceProvider: new PromotionTrustEvidenceStub(), liveAuthorizations: authorization);
            var approved = await livePromotion.ApproveAsync(RehearsalApproval(paper.RunId, RunType.Live, allow.OverrideId)
                with
            { LiveAuthorization = grant }, ct);
            approved.Success.Should().BeTrue(approved.Reason);
            var live = (await runs.GetRunByIdAsync(approved.NewRunId!, ct))!;
            live.ParentRunId.Should().Be(paper.RunId);
            live.StrategyArtifactId.Should().Be(artifactId);

            var liveCache = new LiveMarketDataCache();
            var liveFeed = new RehearsalFeed();
            var liveBook = CreateRehearsalBook(account, openingCash);
            var quote = new RehearsalMarketReference();
            var scope = new TradeFillPostingStoreOptions(Path.Combine(root, "handoff"), "synthetic-governed-handoff");
            await using var posting = new WalTradeFillPostingStore(scope, NullLogger<WalTradeFillPostingStore>.Instance);
            var reconciliation = new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance);
            await using var journal = new ExecutionJournal(Path.Combine(root, "execution-journal"));
            await using var liveOms = new OrderManagementSystem(broker, NullLogger<OrderManagementSystem>.Instance,
                riskValidator: risk, operatorControls: controls, auditTrail: audit, portfolioState: liveBook,
                brokerageConfiguration: config, liveOrderReadinessGate: new TradingOperatorLiveOrderReadinessGate(
                    new AccountReadinessProvider(live.RunId, account),
                    NullLogger<TradingOperatorLiveOrderReadinessGate>.Instance),
                strategyRunAuthority: new StrategyRunAuthoritySource(runs, history),
                recoveryStore: new FileBrokerageOrderRecoveryStore(Path.Combine(root, "broker-recovery.json"), broker.GatewayId),
                journal: journal, tradeEventPublisher: new RehearsalFillPublisher(posting),
                marketReferences: quote, instrumentCoverage: quote, liveStrategyAuthorization: authorization);
            await liveOms.StartAsync(ct);
            await using var liveEngine = new LiveTradingEngine(LiveStrategyCatalog.CreateDefault(), liveFeed, liveCache,
                new Meridian.Execution.Adapters.OmsGovernedExecutionOrderGateway(broker), liveOms, liveBook, runs,
                new LiveTradingEngineOptions { AllowLiveRuns = true }, brokerageConfiguration: config,
                artifacts: artifacts, liveAuthorizations: authorization);
            var launch = await liveEngine.TryLaunchAsync(live, ct);
            launch.Launched.Should().BeTrue(launch.Reason);
            await SendRehearsalPriceAsync(liveFeed, liveCache, symbol, 100m, DateTimeOffset.UtcNow, 1, ct);
            var order = broker.Requests.Should().ContainSingle().Subject;
            var filled = BuildFill(order) with { BrokerAccountId = RehearsalBroker.ExternalAccount, FillPrice = 100m, Commission = 1m };
            var partial = filled with
            {
                ReportType = ExecutionSdk.ExecutionReportType.PartialFill,
                OrderStatus = ExecutionSdk.OrderStatus.PartiallyFilled,
                FilledQuantity = 4m,
                Commission = 0.40m,
                BrokerActivityId = "rehearsal-fill-1"
            };
            filled = filled with { Commission = 0.60m, BrokerActivityId = "rehearsal-fill-2" };
            await broker.PublishAsync(partial);
            await broker.PublishAsync(filled);
            const int replaySeed = 1729;
            var random = new Random(replaySeed);
            var replays = Enumerable.Repeat(new[] { partial, filled }, 12).SelectMany(reports => reports).ToArray();
            random.Shuffle(replays);
            foreach (var replay in replays)
                await broker.PublishAsync(replay);
            await WaitUntilAsync(() => liveOms.GetOrder(order.ClientOrderId!)?.Status == ExecutionSdk.OrderStatus.Filled);
            await WaitUntilAsync(() => liveBook.Cash == 8_999m);
            var receipts = await WaitForRehearsalReceiptsAsync(posting, ct);
            receipts.Select(receipt => receipt.TradeEvent.FillId).Should().OnlyHaveUniqueItems();
            receipts.Sum(receipt => receipt.TradeEvent.FilledQuantity).Should().Be(10m);
            receipts.Sum(receipt => receipt.TradeEvent.Commission).Should().Be(1m);
            var reconciled = await reconciliation.ReconcileOpenOrdersAsync(broker, liveOms, account, ct);
            reconciled.Breaks.Should().BeEmpty();
            liveBook.GetPositions().Should().ContainSingle().Which.ExactQuantity.Should().Be(
                (await broker.GetPositionsAsync(ct)).Single().Quantity);
            liveBook.Cash.Should().Be((await broker.GetAccountInfoAsync(ct)).Cash);
            (await liveEngine.StopRunAsync(live.RunId, ct)).Should().BeTrue();
            (await posting.LoadPendingAsync(ct)).Select(receipt => receipt.TradeEvent.FillId)
                .Should().Equal(receipts.Select(receipt => receipt.TradeEvent.FillId));

            var records = await journal.ReadAsync(account, ct);
            await RetainRehearsalEvidenceAsync(root, new
            {
                Evidence = "synthetic-verified",
                Commit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                Symbol = symbol,
                Account = account,
                OpeningCash = openingCash,
                Prices = prices,
                StrategyArtifactId = artifactId,
                ReplaySeed = replaySeed,
                Backtest = research.RunId,
                Paper = paper.RunId,
                Live = live.RunId,
                PaperDecision = paperApproval,
                LiveDecision = approved,
                Authorization = grant,
                Reports = new[] { partial, filled },
                ExecutionJournal = records,
                Reconciliation = reconciled,
                ExpectedCash = 8_999m,
                ExpectedShares = 10m,
                Accounting = receipts.Select(receipt => receipt.TradeEvent)
            }, ct);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static PromotionApprovalRequest RehearsalApproval(string runId, RunType mode, string? overrideId = null)
    {
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(mode);
        return new(runId, ApprovedBy: "synthetic-reviewer", ApprovalReason: "Reviewed deterministic rehearsal evidence",
            ApprovalChecklist: checklist, EvidenceReferences: checklist.Select(item => item switch
            {
                PromotionApprovalChecklist.LiveOverrideReviewed => $"{item}:manual-override/{overrideId}",
                PromotionApprovalChecklist.PaperExecutionModelReviewed => $"{item}:paper-match/1+paper-cost/1",
                _ => $"{item}:{RehearsalEvidenceReference(item)}"
            }).ToArray(), ManualOverrideId: overrideId);
    }

    private static string RehearsalEvidenceReference(string item) => $"retained/synthetic-rehearsal/{item}";

    private static PaperTradingPortfolio CreateRehearsalBook(Guid account, decimal cash) =>
        new([new AccountDefinition(account.ToString("D"), "Synthetic equity account", AccountKind.Brokerage, cash)]);

    private static async Task SendRehearsalPriceAsync(RehearsalFeed feed, LiveMarketDataCache cache, string symbol,
        decimal price, DateTimeOffset timestamp, long sequence, CancellationToken ct)
    {
        var trade = new Trade(timestamp, symbol, price, 100, AggressorSide.Buy, SequenceNumber: sequence);
        cache.RecordTrade(symbol, trade);
        await feed.PublishAsync(new LiveMarketEvent(timestamp, symbol, trade), ct);
    }

    private static async Task<IReadOnlyList<PendingTradeFillPosting>> WaitForRehearsalReceiptsAsync(
        WalTradeFillPostingStore posting, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var receipts = await posting.LoadPendingAsync(ct);
            if (receipts.Count >= 2)
                return receipts;
            await Task.Delay(25, ct);
        }
        throw new InvalidOperationException("The rehearsal did not retain both accounting handoff receipts.");
    }

    private static async Task RetainRehearsalEvidenceAsync(string root, object evidence, CancellationToken ct)
    {
        var directory = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") is { Length: > 0 } workspace
            ? Path.Combine(workspace, "artifacts", "test-results", "dotnet", "governed-execution")
            : root;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "buy-and-hold.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    private sealed class RehearsalFeed : ILiveMarketEventFeed
    {
        private readonly Channel<(LiveMarketEvent Event, TaskCompletionSource Consumed)> _events =
            Channel.CreateUnbounded<(LiveMarketEvent, TaskCompletionSource)>();

        public async Task PublishAsync(LiveMarketEvent evt, CancellationToken ct)
        {
            var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _events.Writer.WriteAsync((evt, consumed), ct);
            await consumed.Task.WaitAsync(ct);
        }

        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(IReadOnlyCollection<string> symbols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var (evt, consumed) in _events.Reader.ReadAllAsync(ct))
            {
                yield return evt;
                consumed.TrySetResult();
            }
        }
    }

    private sealed class RehearsalClock(DateTimeOffset instant) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = instant;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RehearsalMarketReference : ExecutionSdk.IOrderMarketReferenceSource, ExecutionSdk.IOrderInstrumentCoverageSource
    {
        public ExecutionSdk.OrderMarketReference GetReference(string symbol, ExecutionSdk.OrderSide side) =>
            new(100m, DateTimeOffset.UtcNow, "synthetic-rehearsal");
        public Task<bool> IsUsdEquityAsync(ExecutionSdk.OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(request.Symbol == "AAPL");
    }

    private sealed class RehearsalFillPublisher(WalTradeFillPostingStore store) : ITradeEventPublisher
    {
        public void Publish(TradeExecutedEvent tradeEvent) => PublishAsync(tradeEvent).GetAwaiter().GetResult();
        public async Task PublishAsync(TradeExecutedEvent tradeEvent) => await store.AcceptAsync(tradeEvent);
    }

    private sealed class RehearsalBroker : ExecutionSdk.IBrokerageGateway, ExecutionSdk.IExecutionGatewayModeProvider,
        ExecutionSdk.IBrokerageConnectionState
    {
        public const string ExternalAccount = "synthetic-equity-broker-account";
        private readonly ControllableExecutionGateway _inner = new(live: true);
        public IReadOnlyCollection<ExecutionSdk.OrderRequest> Requests => _inner.Requests;
        public string GatewayId => _inner.GatewayId;
        public string BrokerDisplayName => "Synthetic broker";
        public bool IsConnected => true;
        public long ConnectionGeneration => 1;
        public bool IsExecutionStreamHealthy => true;
        public string ScopeIdentity => $"synthetic:{ExternalAccount}";
        public ExecutionSdk.ExecutionMode ExecutionMode => ExecutionSdk.ExecutionMode.Live;
        public ExecutionSdk.BrokerageCapabilities BrokerageCapabilities => ExecutionSdk.BrokerageCapabilities.UsEquity();
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutionSdk.ExecutionReport> SubmitOrderAsync(ExecutionSdk.OrderRequest request, CancellationToken ct = default) =>
            _inner.SubmitOrderAsync(request, ct);
        public Task<ExecutionSdk.ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            _inner.CancelOrderAsync(orderId, ct);
        public Task<ExecutionSdk.ExecutionReport> ModifyOrderAsync(string orderId, ExecutionSdk.OrderModification modification,
            CancellationToken ct = default) => _inner.ModifyOrderAsync(orderId, modification, ct);
        public IAsyncEnumerable<ExecutionSdk.ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _inner.StreamExecutionReportsAsync(ct);
        public ValueTask PublishAsync(ExecutionSdk.ExecutionReport report) => _inner.PublishAsync(report);
        public Task<ExecutionSdk.BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new ExecutionSdk.BrokerHealthStatus { IsHealthy = true });
        public Task<IReadOnlyList<ExecutionSdk.BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionSdk.BrokerOrder>>([]);
        public Task<IReadOnlyList<ExecutionSdk.BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionSdk.BrokerPosition>>([new() { Symbol = "AAPL", Quantity = 10m }]);
        public Task<ExecutionSdk.AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new ExecutionSdk.AccountInfo { AccountId = ExternalAccount, Cash = 8_999m });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
