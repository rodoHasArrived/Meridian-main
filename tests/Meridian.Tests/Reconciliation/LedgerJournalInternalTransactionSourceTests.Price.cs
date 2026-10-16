using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Tests.Reconciliation;

public sealed partial class LedgerJournalInternalTransactionSourceTests
{
    [Theory]
    [InlineData("19.75", "5", true)]
    [InlineData("19.75", "0", false)]
    [InlineData(null, "5", false)]
    [InlineData("missing", "5", false)]
    [InlineData("0", "5", false)]
    [InlineData("-19.75", "5", false)]
    public async Task GetTransactionsAsync_PreservesOnlyValidRetainedTradePrice(string? price, string quantity, bool hasPrice)
    {
        var trade = PricedTrade(price, quantity);

        var transactions = await Source(trade).GetTransactionsAsync(Query());

        var projected = transactions.Should().ContainSingle().Subject;
        projected.Price.Should().Be(hasPrice ? 19.75m : null);
        projected.NetAmount.Should().Be(-100m, "net cash includes commission and must not become the unit price");
    }

    [Fact]
    public async Task GetTransactionsAsync_CommissionAuditTagsCannotBecomeTradePrice()
    {
        var commission = Journal(Timestamp(2026, 5, 28), "Fill commission",
            new JournalEntryMetadata(ActivityType: "commission", Symbol: "MSFT", FinancialAccountId: AccountLabel,
                Tags: new Dictionary<string, string> { ["tradeFill.price"] = "19.75", ["quantity"] = "5" }),
            (LedgerAccounts.CommissionExpenseFor(AccountLabel), 1.25m, 0m),
            (LedgerAccounts.CashAccount(AccountLabel), 0m, 1.25m));

        var transactions = await Source(commission).GetTransactionsAsync(Query());

        var projected = transactions.Should().ContainSingle().Subject;
        projected.TransactionType.Should().Be("fee");
        projected.Price.Should().BeNull();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task CreateAsync_AppliesPriceToleranceToRealJournalProjection(int priceTolerance, bool matches)
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-journal-price-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "statement.csv");
            await File.WriteAllLinesAsync(sourcePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId",
                "EXT-1,MSFT,5,20.25,-100,trade,2026-05-28,2026-05-28,USD,,EXT-9"
            ]);
            var profile = StatementToleranceProfile.Default with
            {
                TransactionRules = [new("journal-retained-price", 0m, TimeSpan.Zero, priceTolerance)]
            };
            var importStore = new JsonCanonicalStatementStore(root, new AtomicFileWriterAdapter());
            var artifacts = new InMemoryStatementRunMatchArtifactStore();
            var workflow = new StatementRunWorkflowService(importStore,
                new JsonReconciliationCaseStore(root, new AtomicFileWriterAdapter()),
                new JsonReconciliationBreakStore(root, new AtomicFileWriterAdapter()),
                new CsvBrokerStatementService(importStore),
                new StatementReconciliationContextAdapter(new StatementReconciliationService()),
                new JournalPricePopulationProvider(Source(PricedTrade("19.75", "5"))),
                toleranceProfileProvider: new InMemoryStatementToleranceProfileProvider([profile]),
                recoveryRepository: new InMemoryStatementRunRecoveryRepository(),
                matchArtifactStore: artifacts,
                caseworkCommitStore: EmptyEphemeralStatementCaseworkCommitStore.Instance);

            var result = await workflow.CreateAsync(new StatementRunRequest(
                "custodian", "Sample Custodian", "FUND-1", AccountLabel, PeriodStart, PeriodEnd,
                sourcePath, "statement.csv", "canonical-csv-v1", profile.ProfileId, "ops-user", string.Empty));
            var retained = await artifacts.GetAsync(result.Import.ImportId);

            if (matches)
            {
                result.Breaks.Should().BeEmpty();
                var group = retained!.MatchGroups.Should().ContainSingle().Subject;
                group.MatchTier.Should().Be("Tolerance", "equal net cash and transaction IDs cannot hide a retained price difference");
                group.RuleIds.Should().Equal("journal-retained-price");
            }
            else
            {
                retained!.MatchGroups.Should().BeEmpty();
                result.Breaks.Should().NotBeEmpty("the real journal source must pass its execution price to the matcher");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JournalEntry PricedTrade(string? price, string quantity)
    {
        var tags = new Dictionary<string, string> { ["quantity"] = quantity };
        if (price is not null)
            tags["tradeFill.price"] = price;
        return Journal(Timestamp(2026, 5, 28), "Buy 5 MSFT plus commission",
            new JournalEntryMetadata(ActivityType: "trade-fill", Symbol: "MSFT", FinancialAccountId: AccountLabel,
                SettlementReference: "EXT-9", Tags: tags),
            (LedgerAccounts.Securities("MSFT", AccountLabel), 98.75m, 0m),
            (LedgerAccounts.CommissionExpenseFor(AccountLabel), 1.25m, 0m),
            (LedgerAccounts.CashAccount(AccountLabel), 0m, 100m));
    }

    private sealed class JournalPricePopulationProvider(LedgerJournalInternalTransactionSource source)
        : IInternalReconciliationPopulationProvider
    {
        public async Task<InternalReconciliationPopulations> GetPopulationsAsync(
            InternalReconciliationPopulationContext context, CancellationToken ct = default) =>
            new([], [], await source.GetTransactionsAsync(new InternalLedgerTransactionQuery(
                context.ExternalAccountId, [context.ExternalAccountId], context.StatementPeriodStart,
                context.StatementPeriodEnd, context.BaseCurrency), ct));
    }
}
