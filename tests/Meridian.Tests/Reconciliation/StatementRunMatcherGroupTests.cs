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
        var pair = result.MatchGroups.Should()
            .ContainSingle(group => group.RuleIds.Contains("statement-transaction-exact-v1"))
            .Subject;
        pair.StatementEvidenceReferences.Should().Equal("IMP-1:2");
        pair.InternalEvidenceReferences.Should().Equal("internal:journal:pair");
        result.Breaks.Should().ContainSingle("only the row with no internal counterpart breaks")
            .Which.Record.SourceReference.Should().Be("IMP-1:3");
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
