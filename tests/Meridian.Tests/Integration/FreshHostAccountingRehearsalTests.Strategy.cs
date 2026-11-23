using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Domain.Enums;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Live;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Strategies.Artifacts;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Integration;

public sealed partial class FreshHostAccountingRehearsalTests
{
    private sealed record ReviewedRun(string RunId, string ArtifactId, string StrategyId);

    private static async Task<ReviewedRun> RetainReviewedRunAsync(Host host, Scope scope, CancellationToken ct)
    {
        // Named synthetic market: a small monotone rise after a $102 entry. Expectations are
        // independent arithmetic, not SecurityTradePostingProjector or another production adapter.
        decimal[] prices = [102m, 102.4m, 102.8m, 103.1m, 103.6m, 104m];
        var first = new DateOnly(2025, 1, 6);
        var barsRoot = Path.Combine(scope.Root, "market");
        var directory = Path.Combine(barsRoot, Symbol);
        Directory.CreateDirectory(directory);
        var bars = prices.Select((price, index) =>
        {
            var bar = new HistoricalBar(Symbol, first.AddDays(index), price, price, price, price,
                1_000_000L, "synthetic-fresh-host", index + 1L);
            return JsonSerializer.Serialize(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), Symbol, bar,
                "synthetic-fresh-host", index + 1L), MarketDataJsonContext.HighPerformanceOptions);
        });
        await File.WriteAllLinesAsync(Path.Combine(directory, $"{Symbol}_bars_{first:yyyy-MM-dd}.jsonl"), bars, ct);
        var request = new BacktestRequest(first, first.AddDays(prices.Length - 1), Symbols: [Symbol],
            InitialCash: 100000m, DataRoot: barsRoot, CommissionKind: BacktestCommissionKind.PerShare,
            CommissionRate: 0.1m, CommissionMinimum: 0m, SlippageBasisPoints: 0m,
            FillTiming: FillTiming.SameBar, RiskFreeRate: 0, AdjustForCorporateActions: false);
        var strategy = new SyntheticPriceStrategy(quantity: 10);
        var parameters = new Dictionary<string, string> { ["symbols"] = Symbol, ["quantity"] = "10" };
        const string dataset = "synthetic-fresh-host/daily-equity-rise-v1";
        var artifact = await host.Artifacts.RetainAsync(StrategyArtifact.FromCode(strategy.StrategyId, strategy,
            parameters, request, dataset), ct);
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
        string Evidence(string item) => $"retained/synthetic-fresh-host/{item}";
        StrategyRunEntry CreateSourceRun() => StrategyRunEntry.StartForFundAccount(strategy.StrategyId, strategy.Name, RunType.Backtest,
            scope.AccountId, parameterSet: parameters, datasetReference: dataset,
            retainedEvidenceReferences: checklist.Select(Evidence).ToArray(), executionRealism: request.ToRealismDescriptor())
            .WithStrategyArtifact(artifact);
        // Matches LiveTradingEngineTests.GovernedRehearsal's legacy native test scaffolding.
        // Dataset/provider/evidence stay synthetic. This null token is not permission to admit
        // explicitly Simulated promotion evidence; the refusal below preserves that boundary.
        var source = CreateSourceRun();
        source.DataProvenanceToken.Should().BeNull();
        await host.Runs.RecordRunAsync(source, ct);
        var engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, new StorageCatalogService(barsRoot, new StorageOptions()));
        var result = await engine.RunAsync(request, strategy, ct: ct);
        result.Fills.Should().ContainSingle();
        result.Metrics.FinalEquity.Should().Be(100019m, "$100,000 - $1,020 - $1 + 10 × $104 = $100,019");
        await host.Runs.RecordRunAsync(source.Complete(result), ct);
        var evidenceDirectory = Path.Combine(scope.Root, "retained", "synthetic-fresh-host");
        Directory.CreateDirectory(evidenceDirectory);
        foreach (var item in checklist)
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, item), JsonSerializer.Serialize(new
            {
                Scenario = dataset, SourceRun = source.RunId, Artifact = artifact, Prices = prices,
                InitialCash = 100000m, Quantity = 10m, Entry = 102m, Commission = 1m,
                ExpectedCash = 98979m, Mark = 104m, ExpectedEquity = 100019m,
                ActualEquity = result.Metrics.FinalEquity
            }), ct);
        var promotion = new PromotionService(host.Runs, new BacktestToLivePromoter(), host.Promotions,
            NullLogger<PromotionService>.Instance, runLauncher: host.Engine);
        PromotionApprovalRequest Approval(string runId) => new(runId, ApprovedBy: "synthetic-paper-reviewer",
            ApprovalReason: "Reviewed the retained synthetic market and independently calculated economics",
            ApprovalChecklist: checklist, EvidenceReferences: checklist.Select(item => $"{item}:{Evidence(item)}").ToArray());
        // A distinct retained run uses the same artifact, inputs and measured result with an
        // explicit marker. Real approval must reject it without a target run or OMS dispatch.
        var marked = CreateSourceRun() with { DataProvenanceToken = "simulated" };
        await host.Runs.RecordRunAsync(marked, ct);
        await host.Runs.RecordRunAsync(marked.Complete(result), ct);
        var blocked = await promotion.ApproveAsync(Approval(marked.RunId), ct);
        blocked.Success.Should().BeFalse();
        blocked.NewRunId.Should().BeNull();
        blocked.Reason.Should().Contain("simulated").And.Contain("cannot enter promotion evidence");
        (await host.Promotions.LoadAllAsync(ct)).Should().BeEmpty();
        host.Engine.ActiveRunIds.Should().BeEmpty();
        host.Oms.GetOpenOrders().Should().BeEmpty();
        host.Oms.GetCompletedOrders().Should().BeEmpty();
        (await host.Journals.GetByPeriodAsync(scope.Period.PeriodId, ct)).Should().HaveCount(1);

        var approved = await promotion.ApproveAsync(Approval(source.RunId), ct);
        approved.Success.Should().BeTrue(approved.Reason);
        host.Engine.ActiveRunIds.Should().Contain(approved.NewRunId!);
        return new(approved.NewRunId!, artifact, strategy.StrategyId);
    }

    private static OrderSubmitCommand Command(Scope scope, ReviewedRun run, string orderId = "fresh-host-buy")
    {
        const string actor = "synthetic-strategy-runner";
        var grant = new ExecutionAccountGrant(scope.AccountId, actor, ExecutionAccountGrantSources.StrategyRun, DateTimeOffset.UtcNow);
        return new(new Meridian.Execution.Sdk.OrderRequest
        {
            ClientOrderId = orderId, FundAccountId = scope.AccountId, StrategyId = run.StrategyId,
            Symbol = Symbol, Side = Meridian.Execution.Sdk.OrderSide.Buy,
            Type = Meridian.Execution.Sdk.OrderType.Market, Quantity = 10m, LimitPrice = 102m
        }, OrderSubmissionAuthority.ForStrategyRun(actor, grant, run.RunId, run.ArtifactId));
    }

    private static async Task PublishPriceAsync(Host host, decimal price, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new Trade(now, Symbol, price, 100, AggressorSide.Buy, SequenceNumber: 1);
        host.Market.RecordTrade(Symbol, trade);
        await host.Feed.PublishAsync(new LiveMarketEvent(now, Symbol, trade), ct);
    }

    // The canonical Synthetic profile requires an explicit simulated price. The ordinary
    // BuyAndHold strategy omits that field, so use a native reference strategy whose exact
    // executable and quantity are retained in the artifact. No production gate is relaxed.
    private sealed class SyntheticPriceStrategy(long quantity) : LiveStrategyBase
    {
        public const string Id = "synthetic-priced-buy-and-hold";
        private bool _entered;
        public override string StrategyId => Id;
        public override string Name => "Synthetic priced buy and hold";
        public override void OnTrade(Trade trade, IBacktestContext ctx) => Enter(trade.Symbol, trade.Price, ctx);
        public override void OnBar(HistoricalBar bar, IBacktestContext ctx) => Enter(bar.Symbol, bar.Close, ctx);

        private void Enter(string symbol, decimal price, IBacktestContext ctx)
        {
            if (_entered || !ctx.Universe.Contains(symbol) ||
                ctx.Positions.TryGetValue(symbol, out var position) && position.Quantity != 0)
                return;
            _entered = true;
            ctx.PlaceOrder(new Meridian.Backtesting.Sdk.OrderRequest(symbol, quantity,
                Meridian.Backtesting.Sdk.OrderType.Market, LimitPrice: price));
        }
    }

    // Deterministic feed acknowledgement occurs only when the run has processed the event.
    // Unlike polling/delays this cannot race subscription setup or a slow posting dependency.
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
}
