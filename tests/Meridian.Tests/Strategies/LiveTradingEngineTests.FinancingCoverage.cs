using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.Operations;
using Meridian.Strategies.Live.Strategies;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Contracts.FundStructure;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    [Fact]
    public async Task LaunchedRuns_WithActualSharedAccountFinancing_RetainUnavailableAllocationThroughReadApis()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-run-financing", Guid.NewGuid().ToString("N"));
        try
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            var scope = new TradeFillLedgerPostingContext("engine-financing", Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), ExpectedPeriodVersion: 1);
            var period = new LedgerAccountingPeriod(scope.PeriodId, scope.LedgerBookId, date.Year, date.Month,
                "Run financing test", date, date, "Open", DateTimeOffset.UtcNow, null, 1);
            var target = new AccountFinancingTarget();
            var book = new LedgerBookRecord(scope.LedgerBookId, "test-fund", Guid.NewGuid(),
                FundStructureNodeKindDto.Account, "Primary account book", "USD", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, AccountingPolicyId: scope.AccountingPolicyId,
                AccountingPolicyVersion: scope.AccountingPolicyVersion);
            await using var financing = new PaperFinancingPostingService(root, scope, target,
                (_, _) => Task.FromResult<LedgerAccountingPeriod?>(period),
                (_, _) => Task.FromResult<LedgerBookRecord?>(book));
            var sessionStore = new JsonlFilePaperSessionStore(Path.Combine(root, "paper-sessions"),
                NullLogger<JsonlFilePaperSessionStore>.Instance);
            await using var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
                sessionStore, financing: financing);
            var accountSession = await sessions.CreateSessionAsync(new("account-strategy", null, 100_000m));
            await sessions.RecordFinancingAsync(new(accountSession.SessionId, PaperTradingPortfolio.DefaultAccountId,
                date, 12.50m, "actual-account-financing", "Assessed account charge without run allocation", "operator"));
            target.Records.Should().ContainSingle();
            target.Records.Single().Entry.Metadata.Tags.Should().NotContainKey("strategy.runId");
            sessions.GetLedger(accountSession.SessionId)!.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
            var sharedPortfolio = sessions.GetActivePortfolio(accountSession.SessionId)!;
            sharedPortfolio.Cash.Should().Be(99_987.50m);

            var repository = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(root, "runs")));
            var hub = new Meridian.Execution.Live.LiveMarketEventHub();
            var cache = new LiveMarketDataCache();
            var orders = new RecordingOrderManager(100m);
            await using var engine = CreateEngine(repository, hub, cache, orders, portfolio: sharedPortfolio);
            var first = CreatePaperRun(BuyAndHoldLiveStrategy.CatalogId,
                new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "1" });
            var second = CreatePaperRun(BuyAndHoldLiveStrategy.CatalogId,
                new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "1" });
            await repository.RecordRunAsync(first);
            await repository.RecordRunAsync(second);
            var firstLaunch = await engine.TryLaunchAsync(first);
            firstLaunch.Launched.Should().BeTrue(firstLaunch.Reason);
            await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => orders.Requests.Count >= 1);
            (await engine.StopRunAsync(first.RunId)).Should().BeTrue();
            // The lifecycle permits one active run of a strategy. Complete the first run
            // before exercising a second run against the same charged account population.
            var secondLaunch = await engine.TryLaunchAsync(second);
            secondLaunch.Launched.Should().BeTrue(secondLaunch.Reason);
            await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => orders.Requests.Count >= 2);
            (await engine.StopRunAsync(second.RunId)).Should().BeTrue();

            var replay = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(root, "runs")));
            var reads = new StrategyRunReadService(replay, new PortfolioReadService(), new LedgerReadService());
            foreach (var run in new[] { first, second })
            {
                var retained = (await replay.GetRunByIdAsync(run.RunId))!;
                retained.Metrics!.EngineMetadata!.FinancingCoverageStatus.Should().Be(BacktestFinancingCoverageStatuses.Unavailable);
                retained.Metrics.EngineMetadata.RunAttributedMarginInterest.Should().BeNull();
                retained.Metrics.Metrics.TotalMarginInterest.Should().Be(0m, "the compatibility field cannot allocate one account fee to every run");
                retained.Metrics.EngineMetadata.CoverageWarnings.Should().Contain(warning => warning.Contains("financing decomposition", StringComparison.Ordinal));
                var detail = (await reads.GetRunDetailAsync(run.RunId))!;
                detail.Execution!.FinancingCoverageStatus.Should().Be(BacktestFinancingCoverageStatuses.Unavailable);
                detail.Execution.RunAttributedMarginInterest.Should().BeNull();
                detail.Portfolio!.FinancingCoverageStatus.Should().Be(BacktestFinancingCoverageStatuses.Unavailable);
                var json = JsonSerializer.SerializeToElement(detail.Execution, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                json.GetProperty(nameof(detail.Execution.RunAttributedMarginInterest)).ValueKind.Should().Be(JsonValueKind.Null);
            }
            var comparison = await reads.GetRunComparisonDtosAsync([first.RunId, second.RunId]);
            comparison.Should().HaveCount(2).And.OnlyContain(row =>
                row.FinancingCoverageStatus == BacktestFinancingCoverageStatuses.Unavailable
                && row.RunAttributedMarginInterest == null && row.CashFlowHealth == "Incomplete");
            comparison.Should().OnlyContain(row => row.CompatibilityWarnings!.Any(warning =>
                warning.Contains("account-charge allocation", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AccountFinancingTarget : ITradeFillLedgerPostingTarget
    {
        public List<LedgerJournalEntryRecord> Records { get; } = [];

        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            var retained = Records.SingleOrDefault(record => record.Entry.JournalEntryId == write.Entry.JournalEntryId);
            if (retained is not null)
                return Task.FromResult(new TradeFillLedgerPostingConfirmation(retained, false));
            retained = new(write.Entry, write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId,
                Records.Count + 1, DateTimeOffset.UtcNow, write.AccountingBasis, write.AccountingPolicyId,
                write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId,
                write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval);
            Records.Add(retained);
            return Task.FromResult(new TradeFillLedgerPostingConfirmation(retained, true));
        }
    }
}
