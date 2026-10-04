using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Live-path evidence for the W9-INGEST-009 sided-kernel wiring: deterministic pair assignment
/// under permuted inputs, side-qualified member keys, and bounded one-to-many / many-to-one
/// transaction splits with identity partitioning and aggregate-quantity validation.
/// </summary>
public sealed class StatementMatchingEngineSidedKernelTests
{
    private static readonly DateOnly TradeDate = new(2026, 5, 27);
    private static readonly DateOnly SettlementDate = new(2026, 5, 28);

    [Fact]
    public void Run_WhenStatementTransactionSettlesAsMultipleLedgerLegs_MatchesSplitGroup()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [],
            [],
            [StatementTransaction("stmt-tx-1", "broker:tx:1", quantity: 10m, netAmount: -5_000m)],
            [],
            [],
            [
                InternalTransaction("int-tx-a", "internal:tx:a", quantity: 6m, netAmount: -3_000m),
                InternalTransaction("int-tx-b", "internal:tx:b", quantity: 4m, netAmount: -2_000m)
            ],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(1);
        var match = result.Results[0];
        match.Kind.Should().Be(StatementMatchKind.Transaction);
        match.MatchTier.Should().Be(StatementMatchTier.Exact);
        match.RuleIds.Should().Contain("statement-transaction-split-v1");
        match.BrokerEvidenceReference.Should().Be("broker:tx:1");
        match.InternalEvidenceReferences.Should().BeEquivalentTo("internal:tx:a", "internal:tx:b");
        match.Variance.Quantity.Should().Be(0m);
        match.Variance.Amount.Should().Be(0m);
    }

    [Fact]
    public void Run_WhenInternalTransactionIsReportedAsMultipleStatementRows_MatchesManyToOneSplit()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [],
            [],
            [
                StatementTransaction("stmt-tx-a", "broker:tx:a", quantity: 6m, netAmount: -3_000m),
                StatementTransaction("stmt-tx-b", "broker:tx:b", quantity: 4m, netAmount: -2_000m)
            ],
            [],
            [],
            [InternalTransaction("int-tx-1", "internal:tx:1", quantity: 10m, netAmount: -5_000m)],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(1);
        var match = result.Results[0];
        match.MatchTier.Should().Be(StatementMatchTier.Exact);
        match.RuleIds.Should().Contain("statement-transaction-split-v1");
        match.InternalEvidenceReference.Should().Be("internal:tx:1");
        match.BrokerEvidenceReferences.Should().BeEquivalentTo("broker:tx:a", "broker:tx:b");
    }

    [Fact]
    public void Run_WhenSplitLegsComeFromDifferentIdentity_RefusesCoincidentalSum()
    {
        var engine = new StatementMatchingEngine();
        // The two internal legs sum exactly to the statement amount but belong to another account:
        // identity partitioning must keep them out of the split pool entirely.
        var request = new StatementMatchingRequest(
            [],
            [],
            [StatementTransaction("stmt-tx-1", "broker:tx:1", quantity: 0m, netAmount: -5_000m)],
            [],
            [],
            [
                InternalTransaction("int-tx-a", "internal:tx:a", quantity: 0m, netAmount: -3_000m, account: "OTHER-ACCT"),
                InternalTransaction("int-tx-b", "internal:tx:b", quantity: 0m, netAmount: -2_000m, account: "OTHER-ACCT")
            ],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Unmatched);
        result.Results.Should().HaveCount(3);
    }

    [Fact]
    public void Run_WhenSplitLegsHaveRightCashButWrongQuantity_RefusesTheSplit()
    {
        var engine = new StatementMatchingEngine();
        // Cash sums exactly, but the legs deliver five shares against a ten-share statement row.
        // The accept callback must refuse the subset; a silently absorbed quantity mismatch would
        // surface nowhere.
        var request = new StatementMatchingRequest(
            [],
            [],
            [StatementTransaction("stmt-tx-1", "broker:tx:1", quantity: 10m, netAmount: -5_000m)],
            [],
            [],
            [
                InternalTransaction("int-tx-a", "internal:tx:a", quantity: 3m, netAmount: -3_000m),
                InternalTransaction("int-tx-b", "internal:tx:b", quantity: 2m, netAmount: -2_000m)
            ],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().NotContain(match =>
            match.MatchTier == StatementMatchTier.Exact || match.MatchTier == StatementMatchTier.Tolerance);
        result.Results.Should().NotContain(match => match.RuleIds.Contains("statement-transaction-split-v1"));
    }

    [Fact]
    public void Run_WhenManyLargerCrossIdentityCandidatesExist_StillFindsTheGenuineSplit()
    {
        var engine = new StatementMatchingEngine();
        // More cross-identity same-sign legs than the kernel's 24-candidate cap, every one larger
        // than the genuine legs. Partitioning by identity before the bounded kernel is what keeps
        // the cap from truncating the genuine legs out of the pool; filtering inside the kernel's
        // accept callback alone would report a break here.
        var crossIdentity = Enumerable.Range(1, 30)
            .Select(index => InternalTransaction(
                $"int-noise-{index:D2}",
                $"internal:noise:{index:D2}",
                quantity: 0m,
                netAmount: -50_000m,
                account: "OTHER-ACCT"))
            .ToArray();
        var request = new StatementMatchingRequest(
            [],
            [],
            [StatementTransaction("stmt-tx-1", "broker:tx:1", quantity: 0m, netAmount: -5_000m)],
            [],
            [],
            [
                .. crossIdentity,
                InternalTransaction("int-tx-a", "internal:tx:a", quantity: 0m, netAmount: -3_000m),
                InternalTransaction("int-tx-b", "internal:tx:b", quantity: 0m, netAmount: -2_000m)
            ],
            Tolerances());

        var result = engine.Run(request);

        var split = result.Results.Should()
            .ContainSingle(match => match.RuleIds.Contains("statement-transaction-split-v1"))
            .Subject;
        split.MatchTier.Should().Be(StatementMatchTier.Exact);
        split.InternalEvidenceReferences.Should().BeEquivalentTo("internal:tx:a", "internal:tx:b");
        result.Results.Count(match => match.MatchTier == StatementMatchTier.Unmatched).Should().Be(30);
    }

    [Fact]
    public void Run_WhenRawIdsCollideAcrossSides_SideQualifiedKeysStillMatchBothPairs()
    {
        var engine = new StatementMatchingEngine();
        // The same raw id appears on both sides (a bank reference propagated into the ledger).
        // Statement "K" pairs with internal "Q" and statement "Q" pairs with internal "K": a
        // consumed set shared across sides without side qualification would block the second pair.
        var request = new StatementMatchingRequest(
            [],
            [],
            [
                StatementTransaction("K", "broker:tx:K", quantity: 10m, netAmount: -5_000m, symbol: "SPY"),
                StatementTransaction("Q", "broker:tx:Q", quantity: 5m, netAmount: -1_000m, symbol: "MSFT")
            ],
            [],
            [],
            [
                InternalTransaction("Q", "internal:tx:Q", quantity: 10m, netAmount: -5_000m, symbol: "SPY"),
                InternalTransaction("K", "internal:tx:K", quantity: 5m, netAmount: -1_000m, symbol: "MSFT")
            ],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Exact);
        result.Results.Should().Contain(match =>
            match.BrokerEvidenceReference == "broker:tx:K" && match.InternalEvidenceReference == "internal:tx:Q");
        result.Results.Should().Contain(match =>
            match.BrokerEvidenceReference == "broker:tx:Q" && match.InternalEvidenceReference == "internal:tx:K");
    }

    [Fact]
    public void Run_IsInvariantUnderInternalPopulationPermutation()
    {
        // Two statement rows, two equally-scored internal counterparts, plus an unmatched internal
        // extra: the first-admissible walk this engine used to run paired different records when the
        // internal population arrived in a different order.
        var statements = new[]
        {
            StatementTransaction("stmt-a", "broker:tx:a", quantity: 0m, netAmount: -100.00m),
            StatementTransaction("stmt-b", "broker:tx:b", quantity: 0m, netAmount: -100.40m)
        };
        var internals = new[]
        {
            InternalTransaction("int-a", "internal:tx:a", quantity: 0m, netAmount: -100.20m),
            InternalTransaction("int-b", "internal:tx:b", quantity: 0m, netAmount: -100.20m),
            InternalTransaction("int-c", "internal:tx:c", quantity: 0m, netAmount: -900m)
        };

        var forward = new StatementMatchingEngine().Run(Request(statements, internals));
        var reversed = new StatementMatchingEngine().Run(Request(statements, internals.Reverse().ToArray()));

        Project(forward).Should().Equal(Project(reversed));
        // Equal scores tie-break on ordinal member ids, so the lexicographically first pairs win.
        forward.Results.Should().Contain(match =>
            match.BrokerEvidenceReference == "broker:tx:a" && match.InternalEvidenceReference == "internal:tx:a");
        forward.Results.Should().Contain(match =>
            match.BrokerEvidenceReference == "broker:tx:b" && match.InternalEvidenceReference == "internal:tx:b");

        static StatementMatchingRequest Request(
            NormalizedStatementTransaction[] statements,
            InternalLedgerTransaction[] internals)
            => new([], [], statements, [], [], internals, Tolerances());

        static IEnumerable<string> Project(StatementMatchingResult result)
            => result.Results.Select(match =>
                $"{match.Kind}|{match.MatchTier}|{string.Join(',', match.RuleIds)}|{match.BrokerEvidenceReference}|{match.InternalEvidenceReference}");
    }

    private static NormalizedStatementTransaction StatementTransaction(
        string id,
        string evidence,
        decimal quantity,
        decimal netAmount,
        string account = "A1",
        string symbol = "SPY")
        => new(id, null, account, symbol, null, TradeDate, SettlementDate, "BUY", quantity, netAmount, evidence);

    private static InternalLedgerTransaction InternalTransaction(
        string id,
        string evidence,
        decimal quantity,
        decimal netAmount,
        string account = "A1",
        string symbol = "SPY")
        => new(id, null, account, symbol, null, TradeDate, SettlementDate, "BUY", quantity, netAmount, evidence);

    private static StatementMatchingToleranceProfile Tolerances() => new(
        PositionQuantity: 0.05m,
        PositionMarketValue: 1.00m,
        CashBalance: 0.10m,
        TransactionQuantity: 0.05m,
        TransactionNetAmount: 1.00m,
        CandidateDateWindowDays: 2);
}
