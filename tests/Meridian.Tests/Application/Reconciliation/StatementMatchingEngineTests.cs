using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class StatementMatchingEngineTests
{
    [Fact]
    public void Run_WhenExactPositionCashAndTransactionExist_EmitsExactEvidence()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [new NormalizedStatementPosition("stmt-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10m, 5_000m, "broker:pos:1")],
            [new NormalizedStatementCashBalance("stmt-cash-1", "A1", "USD", 125.25m, "broker:cash:1", new DateOnly(2026, 5, 28))],
            [new NormalizedStatementTransaction("stmt-tx-1", "EXT-123", "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "a1", "spy", new DateOnly(2026, 5, 28), 10m, 5_000m, "internal:pos:1")],
            [new InternalCashBalance("int-cash-1", "A1", "usd", 125.25m, "internal:cash:1", new DateOnly(2026, 5, 28))],
            [new InternalLedgerTransaction("int-tx-1", "EXT-123", "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "internal:tx:1")],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(3);
        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Exact && match.Confidence == 1.00m);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Position
            && match.RuleIds.Contains("statement-position-exact-v1")
            && match.BrokerEvidenceReference == "broker:pos:1"
            && match.InternalEvidenceReference == "internal:pos:1"
            && match.Variance.Quantity == 0m
            && match.Variance.MarketValue == 0m);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Cash
            && match.RuleIds.Contains("statement-cash-exact-v1")
            && match.Variance.Amount == 0m);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Transaction
            && match.RuleIds.Contains("statement-transaction-external-id-v1")
            && match.Variance.Quantity == 0m
            && match.Variance.Amount == 0m);
    }

    [Fact]
    public void Run_WhenValuesAreNear_EmitsToleranceMatchesWithVarianceAndTolerance()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [new NormalizedStatementPosition("stmt-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10.01m, 5_000.50m, "broker:pos:1")],
            [new NormalizedStatementCashBalance("stmt-cash-1", "A1", "USD", 125.30m, "broker:cash:1", new DateOnly(2026, 5, 28))],
            [new NormalizedStatementTransaction("stmt-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10.01m, -5_000.25m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10m, 5_000m, "internal:pos:1")],
            [new InternalCashBalance("int-cash-1", "A1", "USD", 125.25m, "internal:cash:1", new DateOnly(2026, 5, 28))],
            [new InternalLedgerTransaction("int-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "internal:tx:1")],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(3);
        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Tolerance);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Position
            && match.RuleIds.Contains("statement-position-tolerance-v1")
            && match.Variance.Quantity == 0.01m
            && match.Variance.MarketValue == 0.50m
            && match.Tolerance.Quantity == 0.05m
            && match.Tolerance.Amount == 1.00m);
        // A cash balance carries no quantity, so the engine reports the configured CashBalance
        // tolerance in Amount — the field the cash variance is measured against.
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Cash
            && match.RuleIds.Contains("statement-cash-tolerance-v1")
            && match.Variance.Amount == 0.05m
            && match.Tolerance.Amount == 0.10m
            && match.Tolerance.Quantity == null);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Transaction
            && match.RuleIds.Contains("statement-transaction-tolerance-v1")
            && match.Variance.Quantity == 0.01m
            && match.Variance.Amount == -0.25m);
    }

    [Fact]
    public void Run_WhenExternalTransactionIdMatchesButTransactionDetailsDiffer_EmitsUnmatchedBreaks()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [],
            [],
            [new NormalizedStatementTransaction("stmt-tx-1", "EXT-123", "EVIL-ACCT", "TSLA", null, new DateOnly(2026, 5, 29), new DateOnly(2026, 5, 30), "SELL", 999m, 999m, "broker:tx:1")],
            [],
            [],
            [new InternalLedgerTransaction("int-tx-1", "EXT-123", "A1", "MSFT", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 5m, -100m, "internal:tx:1")],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Unmatched);
        result.Results.Should().Contain(match => match.BrokerEvidenceReference == "broker:tx:1"
            && match.InternalEvidenceReference == null
            && match.RuleIds.Contains("statement-transaction-break-v1"));
        result.Results.Should().Contain(match => match.BrokerEvidenceReference == null
            && match.InternalEvidenceReference == "internal:tx:1"
            && match.RuleIds.Contains("statement-transaction-break-v1"));
    }

    [Fact]
    public void Run_BrokerFillIdentityRetainsAmountAndTimingBreakBeforeFallback()
    {
        var statement = Trade("broker", "FILL-12");
        var sameFill = Journal("retained", "FILL-12") with { NetAmount = -95m, SettlementDate = statement.SettlementDate.AddDays(1) };
        var coincidental = Journal("coincidence", null);

        var matches = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            [], [], [statement], [], [], [coincidental, sameFill], Tolerances())).Results;

        var review = matches.Should().ContainSingle(match => match.BreakCode == "TRANSACTION_AMOUNT_AND_TIMING_DIFFERENCE").Subject;
        review.MatchTier.Should().Be(StatementMatchTier.Candidate);
        review.BrokerEvidenceReference.Should().Be(statement.EvidenceReference);
        review.InternalEvidenceReference.Should().Be(sameFill.EvidenceReference);
        review.Variance.Amount.Should().Be(-5m);
        matches.Should().ContainSingle(match => match.MatchTier == StatementMatchTier.Unmatched
            && match.InternalEvidenceReference == coincidental.EvidenceReference);
    }

    [Fact]
    public void Run_DistinctKnownFillIdsCannotBeMatchedByAmount()
    {
        var result = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            [], [], [Trade("broker", "FILL-1")], [], [], [Journal("retained", "FILL-2")], Tolerances()));

        result.Results.Should().HaveCount(2).And.OnlyContain(match => match.MatchTier == StatementMatchTier.Unmatched);
    }

    [Fact]
    public void Run_RepeatedAccountFillIdRetainsAllEvidenceAsOneAmbiguity()
    {
        var result = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            [], [], [Trade("broker-a", "FILL-1"), Trade("broker-b", "FILL-1")],
            [], [], [Journal("retained", "FILL-1")], Tolerances()));

        var ambiguity = result.Results.Should().ContainSingle().Subject;
        ambiguity.BreakCode.Should().Be("TRANSACTION_AMBIGUOUS");
        ambiguity.AllBrokerEvidenceReferences.Should().Equal("broker-a", "broker-b");
        ambiguity.AllInternalEvidenceReferences.Should().Equal("retained");
    }

    [Fact]
    public void Run_EvenMatchingIdsCannotCertifyAnOutOfWindowTrade()
    {
        var result = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            [], [], [Trade("broker", "FILL-1")], [], [], [Journal("retained", "FILL-1")], Tolerances())
        {
            StatementPeriodStart = new(2026, 6, 1),
            StatementPeriodEnd = new(2026, 6, 30)
        });

        result.Results.Should().HaveCount(2).And.OnlyContain(match => match.BreakCode == "TRANSACTION_OUTSIDE_STATEMENT_WINDOW");
    }

    [Fact]
    public void Run_WhenOnlyLooseCandidatesExist_EmitsCandidateBeforeBreaks()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [new NormalizedStatementPosition("stmt-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10m, 5_000m, "broker:pos:1")],
            [],
            [new NormalizedStatementTransaction("stmt-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", new DateOnly(2026, 5, 29), 12m, 5_200m, "internal:pos:1")],
            [],
            [new InternalLedgerTransaction("int-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 28), new DateOnly(2026, 5, 30), "BUY", 11m, -5_100m, "internal:tx:1")],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Candidate);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Position
            && match.RuleIds.Contains("statement-position-candidate-v1")
            && match.BrokerEvidenceReference == "broker:pos:1"
            && match.InternalEvidenceReference == "internal:pos:1");
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Transaction
            && match.RuleIds.Contains("statement-transaction-candidate-v1"));
    }

    [Fact]
    public void Run_WhenNoCandidateExists_CreatesBrokerAndInternalBreaks()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [new NormalizedStatementPosition("stmt-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10m, null, "broker:pos:1")],
            [new NormalizedStatementCashBalance("stmt-cash-1", "A1", "USD", 100m, "broker:cash:1", new DateOnly(2026, 5, 28))],
            [new NormalizedStatementTransaction("stmt-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "A2", "QQQ", new DateOnly(2026, 5, 28), 10m, null, "internal:pos:1")],
            [new InternalCashBalance("int-cash-1", "A2", "USD", 100m, "internal:cash:1", new DateOnly(2026, 5, 28))],
            [new InternalLedgerTransaction("int-tx-1", null, "A2", "QQQ", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "SELL", 10m, 5_000m, "internal:tx:1")],
            Tolerances());

        var result = engine.Run(request);

        result.Results.Should().HaveCount(6);
        result.Results.Should().OnlyContain(match => match.MatchTier == StatementMatchTier.Unmatched && match.Confidence == 0m);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Position
            && match.RuleIds.Contains("statement-position-break-v1")
            && match.BrokerEvidenceReference == "broker:pos:1"
            && match.InternalEvidenceReference == null);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Transaction
            && match.RuleIds.Contains("statement-transaction-break-v1")
            && match.BrokerEvidenceReference == null
            && match.InternalEvidenceReference == "internal:tx:1");
    }

    [Fact]
    public void Run_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        var engine = new StatementMatchingEngine();

        var act = () => engine.Run(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static StatementMatchingToleranceProfile Tolerances() => new(
        PositionQuantity: 0.05m,
        PositionMarketValue: 1.00m,
        CashBalance: 0.10m,
        TransactionQuantity: 0.05m,
        TransactionNetAmount: 1.00m,
        CandidateDateWindowDays: 2);

    private static NormalizedStatementTransaction Trade(string id, string? externalId) =>
        new(id, externalId, "A1", "SPY", "USD", new(2026, 5, 26), new(2026, 5, 27), "trade", 1m, -100m, id);

    private static InternalLedgerTransaction Journal(string id, string? externalId) =>
        new(id, externalId, "A1", "SPY", "USD", new(2026, 5, 26), new(2026, 5, 27), "trade", 1m, -100m, id);
}
