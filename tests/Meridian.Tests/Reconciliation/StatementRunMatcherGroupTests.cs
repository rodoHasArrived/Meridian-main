using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Live-path evidence that the statement-run matcher retains group-aware match records and that
/// its outcome — matched groups and break identities alike — is invariant under permutation of the
/// internal populations a provider happens to enumerate in a different order.
/// </summary>
public sealed class StatementRunMatcherGroupTests
{
    private static readonly DateOnly TradeDate = new(2026, 5, 27);
    private static readonly DateTimeOffset CreatedAt = new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Match_RetainsGroupRecordsForPairAndSplitMatches()
    {
        var result = StatementRunMatcher.Match(
            Import(),
            Rows(),
            Populations(),
            StatementToleranceProfile.Default,
            IdentityReconciliationFxRateProvider.Instance,
            "USD",
            CreatedAt);

        result.MatchCount.Should().Be(2);
        result.MatchGroups.Should().HaveCount(result.MatchCount);
        var split = result.MatchGroups.Should()
            .ContainSingle(group => group.RuleIds.Contains("statement-transaction-split-v1"))
            .Subject;
        split.Kind.Should().Be("Transaction");
        split.StatementEvidenceReferences.Should().Equal("IMP-1:1");
        split.InternalEvidenceReferences.Should().Equal("internal:journal:leg-a", "internal:journal:leg-b");
        var allocations = result.GroupAllocations[split.MatchGroupId];
        allocations.LedgerAllocations.Select(item => item.Amount).Should().Equal("-3000", "-2000");
        allocations.Residuals.Should().ContainSingle(item => item.Measure == "Net amount" && item.Unit == "USD"
            && item.SourceTotal == "-5000" && item.LedgerTotal == "-5000" && item.Residual == "0");
        var pair = result.MatchGroups.Should()
            .ContainSingle(group => group.RuleIds.Contains("statement-transaction-exact-v1"))
            .Subject;
        pair.StatementEvidenceReferences.Should().Equal("IMP-1:2");
        pair.InternalEvidenceReferences.Should().Equal("internal:journal:pair");
        result.Breaks.Should().ContainSingle("only the row with no internal counterpart breaks")
            .Which.Record.SourceReference.Should().Be("IMP-1:3");
    }

    [Fact]
    public void Match_PreservesExactSignedGroupContributionsBeyondBinaryFloatPrecision()
    {
        var rows = Rows().Take(1).Select(item => item with { CashAmount = -9_007_199_254_740_992.123456m }).ToArray();
        var ledger = Populations().LedgerTransactions.Take(2).ToArray();
        ledger[0] = ledger[0] with { NetAmount = -9_007_199_254_740_991.123455m, EvidenceReference = "shared-journal" };
        ledger[1] = ledger[1] with { NetAmount = -1.000001m, EvidenceReference = "shared-journal" };
        var result = StatementRunMatcher.Match(Import(), rows, new([], [], ledger), StatementToleranceProfile.Default,
            IdentityReconciliationFxRateProvider.Instance, "USD", CreatedAt);

        var group = result.MatchGroups.Should().ContainSingle().Subject;
        var evidence = result.GroupAllocations[group.MatchGroupId];
        evidence.LedgerAllocations.Select(item => item.RecordId).Should().Equal(ledger.Select(item => item.TransactionId));
        evidence.LedgerAllocations.Select(item => item.Amount).Should().Equal("-9007199254740991.123455", "-1.000001");
        var amount = evidence.Residuals.Single(item => item.Measure == "Net amount");
        amount.SourceTotal.Should().Be("-9007199254740992.123456");
        amount.LedgerTotal.Should().Be(amount.SourceTotal);
        decimal.Parse(amount.Residual, System.Globalization.CultureInfo.InvariantCulture).Should().Be(0m);
        result.Breaks.Should().BeEmpty();
    }

    [Fact]
    public void Match_ProducesIdenticalGroupsAndBreakIds_UnderInternalPopulationPermutation()
    {
        var populations = Populations();
        var permuted = new InternalReconciliationPopulations(
            populations.Positions,
            populations.CashBalances,
            populations.LedgerTransactions.Reverse().ToArray());

        var forward = StatementRunMatcher.Match(
            Import(), Rows(), populations, StatementToleranceProfile.Default,
            IdentityReconciliationFxRateProvider.Instance, "USD", CreatedAt);
        var reversed = StatementRunMatcher.Match(
            Import(), Rows(), permuted, StatementToleranceProfile.Default,
            IdentityReconciliationFxRateProvider.Instance, "USD", CreatedAt);

        ProjectGroups(forward).Should().Equal(ProjectGroups(reversed));
        forward.Breaks.Select(static item => item.Record.BreakId)
            .Should().Equal(reversed.Breaks.Select(static item => item.Record.BreakId));
    }

    private static IEnumerable<string> ProjectGroups(StatementRunMatchResult result)
        => result.MatchGroups.Select(static group => string.Join(
            '|',
            group.MatchGroupId,
            group.Kind,
            group.MatchTier,
            string.Join(',', group.RuleIds),
            string.Join(',', group.StatementEvidenceReferences),
            string.Join(',', group.InternalEvidenceReferences)));

    private static CanonicalStatementImport Import() => new(
        "IMP-1",
        "custodian",
        TradeDate,
        CreatedAt,
        "statements/sample.xml",
        "checksum",
        3,
        3)
    {
        FundAccountId = "FUND-1",
        ExternalAccountId = "EXT-1",
        StatementPeriodStart = new DateOnly(2026, 5, 1),
        StatementPeriodEnd = new DateOnly(2026, 5, 31)
    };

    private static IReadOnlyList<CanonicalStatementRow> Rows() =>
    [
        // Settles internally as two ledger legs (6 + 4 shares, -3000 + -2000).
        new("IMP-1", 1, "EXT-1", "SPY", 10m, 0m, -5_000m, "trade", TradeDate, "raw-1"),
        // Pairs one-to-one with a single internal transaction.
        new("IMP-1", 2, "EXT-1", "MSFT", 5m, 0m, -1_000m, "trade", TradeDate, "raw-2"),
        // No internal counterpart: must stay an honest break.
        new("IMP-1", 3, "EXT-1", "XYZ", 1m, 0m, -42m, "trade", TradeDate, "raw-3")
    ];

    private static InternalReconciliationPopulations Populations() => new(
        [],
        [],
        [
            new InternalLedgerTransaction(
                "internal-txn:EXT-1:leg-a", null, "EXT-1", "SPY", "USD",
                TradeDate, TradeDate, "trade", 6m, -3_000m, "internal:journal:leg-a"),
            new InternalLedgerTransaction(
                "internal-txn:EXT-1:leg-b", null, "EXT-1", "SPY", "USD",
                TradeDate, TradeDate, "trade", 4m, -2_000m, "internal:journal:leg-b"),
            new InternalLedgerTransaction(
                "internal-txn:EXT-1:pair", null, "EXT-1", "MSFT", "USD",
                TradeDate, TradeDate, "trade", 5m, -1_000m, "internal:journal:pair")
        ]);
}
