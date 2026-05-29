using FluentAssertions;
using Meridian.Application.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class StatementMatchingEngineTests
{
    [Fact]
    public void Run_WhenExactPositionCashAndTransactionExist_EmitsExactEvidence()
    {
        var engine = new StatementMatchingEngine();
        var request = new StatementMatchingRequest(
            [new NormalizedStatementPosition("stmt-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10m, 5_000m, "broker:pos:1")],
            [new NormalizedStatementCashBalance("stmt-cash-1", "A1", "USD", 125.25m, "broker:cash:1")],
            [new NormalizedStatementTransaction("stmt-tx-1", "EXT-123", "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "a1", "spy", new DateOnly(2026, 5, 28), 10m, 5_000m, "internal:pos:1")],
            [new InternalCashBalance("int-cash-1", "A1", "usd", 125.25m, "internal:cash:1")],
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
            [new NormalizedStatementCashBalance("stmt-cash-1", "A1", "USD", 125.30m, "broker:cash:1")],
            [new NormalizedStatementTransaction("stmt-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10.01m, -5_000.25m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", new DateOnly(2026, 5, 28), 10m, 5_000m, "internal:pos:1")],
            [new InternalCashBalance("int-cash-1", "A1", "USD", 125.25m, "internal:cash:1")],
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
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Cash
            && match.RuleIds.Contains("statement-cash-tolerance-v1")
            && match.Variance.Amount == 0.05m
            && match.Tolerance.Quantity == 0.10m);
        result.Results.Should().Contain(match => match.Kind == StatementMatchKind.Transaction
            && match.RuleIds.Contains("statement-transaction-tolerance-v1")
            && match.Variance.Quantity == 0.01m
            && match.Variance.Amount == -0.25m);
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
            [new NormalizedStatementCashBalance("stmt-cash-1", "A1", "USD", 100m, "broker:cash:1")],
            [new NormalizedStatementTransaction("stmt-tx-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 27), new DateOnly(2026, 5, 28), "BUY", 10m, -5_000m, "broker:tx:1")],
            [new InternalPortfolioPosition("int-pos-1", "A2", "QQQ", new DateOnly(2026, 5, 28), 10m, null, "internal:pos:1")],
            [new InternalCashBalance("int-cash-1", "A2", "USD", 100m, "internal:cash:1")],
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
}
