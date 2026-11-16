using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.PaperMatching;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Storage.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    private static readonly Guid AaplSecurityId = Guid.Parse("5ec0a9f1-0000-4000-8000-000000000aa1");
    // ---- Promotion to paper books the run's fills ----

    /// <summary>
    /// End to end over the supported paper path, with no fake order manager: an approved
    /// backtest -> paper promotion launches the target run, its strategy order goes through the
    /// real OMS and paper gateway, the fill is priced from the observed market and charged by
    /// the paper cost model, and the durable ledger consumer posts balanced journals carrying
    /// the promoted run's identity — using the same lot projection the backtest engine uses.
    /// </summary>
    [Fact]
    public async Task PromotedPaperRun_FillsPostBalancedJournalsTaggedWithTheTargetRun()
    {
        const decimal observedPrice = 100m;
        const decimal quantity = 1m; // RecordingOrderLiveStrategy buys one share on its first trade
        const decimal fixedCommission = 1.25m;
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-promotion-ledger", Guid.NewGuid().ToString("N"));
        try
        {
            var postingContext = new TradeFillLedgerPostingContext(
                "promotion-ledger/open-period", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var target = new AccountFinancingTarget();
            await using var postingStore = new WalTradeFillPostingStore(
                new TradeFillPostingStoreOptions(Path.Combine(dataRoot, "fill-postings"), postingContext),
                NullLogger<WalTradeFillPostingStore>.Instance);
            // Posting is fail-closed on Security Master identity: an unresolved fill stays
            // pending for replay. The harness resolves AAPL so the posting can complete.
            await using var consumer = new LedgerPostingConsumer(
                target,
                NullLogger<LedgerPostingConsumer>.Instance,
                postingStore,
                postingContext,
                securityValidationGate: new ResolvedSecurityValidationGate(AaplSecurityId));

            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(dataRoot, "runs")));
            var hub = new Meridian.Execution.Live.LiveMarketEventHub();
            var cache = new LiveMarketDataCache();
            // The engine reads the same paper book the OMS applies fills to, as in the host.
            var portfolio = new PaperTradingPortfolio(100_000m);
            var paperGateway = new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
                liveFeed: cache,
                costOptions: new PaperTradingCostOptions
                {
                    CommissionKind = PaperCommissionKind.FixedPerOrder,
                    CommissionRate = fixedCommission,
                    CommissionMinimum = 0m
                });
            await using var oms = new OrderManagementSystem(
                paperGateway,
                NullLogger<OrderManagementSystem>.Instance,
                portfolioState: portfolio,
                tradeEventPublisher: consumer);
            await oms.StartAsync();

            // The strategy signals when its fill reaches the run session, so the run is only
            // stopped after the fill has been delivered to it (stopping mid-delivery fails the run).
            var strategy = new RecordingOrderLiveStrategy("promotion-ledger-strategy");
            var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
            var source = StrategyRunEntry.StartWithEvidence(
                strategy.StrategyId, "Recording strategy", RunType.Backtest, "promotion-ledger-source",
                engine: "MeridianNative",
                parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL" },
                retainedEvidenceReferences: [CanonicalPromotionVaultReference]).Complete(BuildPassingResult());
            await store.RecordRunAsync(source);

            string promotedRunId;
            await using (var engine = CreateEngine(store, hub, cache, oms, catalog, portfolio: portfolio))
            {
                var promotions = new PromotionService(store, new BacktestToLivePromoter(),
                    new InMemoryPromotionRecordStore(), NullLogger<PromotionService>.Instance, runLauncher: engine);
                var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
                var promotion = await promotions.ApproveAsync(new PromotionApprovalRequest(source.RunId,
                    ApprovedBy: "ops", ApprovalReason: "Ready for paper.",
                    ApprovalChecklist: checklist,
                    EvidenceReferences: checklist.Select(item => $"{item}:{CanonicalPromotionVaultReference}").ToArray()));

                promotion.Success.Should().BeTrue(promotion.Reason);
                promotedRunId = promotion.NewRunId!;
                promotedRunId.Should().NotBe(source.RunId);
                await WaitForLifecycleAsync(store, promotedRunId, StrategyRunLifecycleEventType.Activated);

                // The OMS delivers a fill only after its accounting handoff is durably accepted,
                // so once the strategy sees the fill the posting is retained for the consumer.
                await PublishTradesUntilAsync(hub, cache, "AAPL", observedPrice,
                    () => strategy.FillReceived.Task.IsCompleted);
                (await strategy.FillReceived.Task.WaitAsync(WaitBudget)).FilledQuantity.Should().Be((long)quantity);
                (await engine.StopRunAsync(promotedRunId)).Should().BeTrue();
            }

            // Drain every accepted fill into the posting target before reading the journal.
            await consumer.DisposeAsync();

            var filled = oms.GetCompletedOrders().Should()
                .ContainSingle(order => order.Status == ExecutionSdk.OrderStatus.Filled).Subject;
            filled.RunId.Should().Be(promotedRunId);
            filled.FilledQuantity.Should().Be(quantity);
            filled.AverageFillPrice.Should().Be(observedPrice,
                "a paper market order fills at the observed trade, never at a zero or scaffold price");

            var journals = target.Records.Select(record => record.Entry).ToArray();
            journals.Should().NotBeEmpty("the promoted run's paper fill must reach the ledger");
            journals.Should().OnlyContain(entry =>
                entry.Metadata.Tags != null
                && entry.Metadata.Tags.ContainsKey(TradeFillCashFlowReplayCompatibility.StrategyRunIdTag)
                && entry.Metadata.Tags[TradeFillCashFlowReplayCompatibility.StrategyRunIdTag] == promotedRunId,
                "every journal must link back to the promoted paper run, not the backtest source");
            journals.Should().OnlyContain(entry => entry.Metadata.StrategyId == strategy.StrategyId);
            journals.Should().OnlyContain(entry => entry.Metadata.SecurityId == AaplSecurityId);
            journals.Should().OnlyContain(entry =>
                entry.Lines.Sum(line => line.Debit) == entry.Lines.Sum(line => line.Credit),
                "every posted journal must balance");

            var tradeJournal = journals.Should()
                .ContainSingle(entry => entry.Metadata.ActivityType == "trade-fill").Subject;
            var accountId = tradeJournal.Metadata.FinancialAccountId;
            var backtestProjection = SecurityTradePostingProjector.Project(
                new SecurityTradePosting(SecurityTradePostingKind.OpenLong, quantity, quantity * observedPrice),
                "AAPL",
                accountId);
            tradeJournal.Lines.Select(line => (line.Account, line.Debit, line.Credit)).Should()
                .BeEquivalentTo(backtestProjection,
                    "paper fills must post the same lot economics the backtest engine posts (ADR-015)");

            var commissionJournal = journals.Should()
                .ContainSingle(entry => entry.Metadata.ActivityType == "trade-commission").Subject;
            commissionJournal.Lines.Select(line => (line.Account, line.Debit, line.Credit)).Should()
                .BeEquivalentTo(SecurityTradePostingProjector.Commission(fixedCommission, accountId),
                    "the paper cost model's commission must be booked against cash");

            var retainedRun = (await new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(dataRoot, "runs")))
                .GetRunByIdAsync(promotedRunId))!;
            retainedRun.RunType.Should().Be(RunType.Paper);
            retainedRun.ParentRunId.Should().Be(source.RunId);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    /// <summary>Resolves every symbol to one Security Master identity with no blocking issues.</summary>
    private sealed class ResolvedSecurityValidationGate(Guid securityId) : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid requestedSecurityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? requestedSecurityId.ToString(), workflow));

        private SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                securityId,
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: false,
                CriticalIssueCount: 0,
                ErrorIssueCount: 0,
                Issues: []);
            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                securityId,
                IsResolved: true,
                IsBlocked: false,
                Report: report,
                Snapshot: null);
        }
    }
}
