using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Verifies that the live statement-run matcher compares broker/custodian statement rows against a
/// real internal book (positions, cash, ledger transactions) rather than self-checking each row.
/// </summary>
public sealed class StatementRunMatchingServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 15);

    private static CanonicalStatementImport Import(string importId = "import-1")
        => new(importId, "custodian", AsOf, DateTimeOffset.UtcNow, "path", "checksum", 0, 0);

    private static CanonicalStatementRow Row(
        int rowNumber,
        string symbol,
        decimal quantity,
        decimal price,
        decimal cashAmount,
        string activityType,
        string importId = "import-1")
        => new(importId, rowNumber, "A1", symbol, quantity, price, cashAmount, activityType, AsOf, $"hash-{rowNumber}");

    [Fact]
    public void Match_ExactPositionAgainstInternalBook_ProducesMatchedOutcomeAndNoBreak()
    {
        var import = Import();
        var rows = new[] { Row(1, "SPY", 10m, 500m, 5_000m, "position") };
        var book = new InternalReconciliationBook(
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", AsOf, 10m, 5_000m, "internal:pos:1")],
            [],
            []);

        var result = StatementRunMatchingService.Match(import, rows, book, StatementToleranceProfile.Default);

        result.Breaks.Should().BeEmpty();
        result.Outcomes.Should().ContainSingle();
        result.Outcomes[0].OutcomeType.Should().Be("matched");
        result.Outcomes[0].RowChecksum.Should().Be("hash-1");
        result.Outcomes[0].LinkedEntityId.Should().Be("internal:pos:1");
        result.Outcomes[0].Confidence.Should().Be(1.00m);
    }

    [Fact]
    public void Match_PositionQuantityMismatch_ProducesBreakWithComputedVarianceAndBreachFlag()
    {
        var import = Import();
        var rows = new[] { Row(1, "SPY", 10m, 500m, 5_000m, "position") };
        // Same account + security + as-of date but a quantity variance the tolerance cannot absorb,
        // and no internal market value so the reported delta is the quantity variance.
        var book = new InternalReconciliationBook(
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", AsOf, 8m, null, "internal:pos:1")],
            [],
            []);

        var result = StatementRunMatchingService.Match(import, rows, book, StatementToleranceProfile.Default);

        result.Breaks.Should().ContainSingle();
        var breakRecord = result.Breaks[0];
        breakRecord.BreakCode.Should().Be("POSITION_CANDIDATE_REVIEW");
        breakRecord.Category.Should().Be("Position");
        breakRecord.Delta.Should().Be(2m);
        breakRecord.ToleranceBreached.Should().BeTrue();
        breakRecord.SourceReference.Should().Be("import-1:1");
        result.Outcomes[0].OutcomeType.Should().NotBe("matched");
    }

    [Fact]
    public void Match_PositionWithNoInternalCounterpart_ProducesUnmatchedBreak()
    {
        var import = Import();
        var rows = new[] { Row(1, "SPY", 10m, 500m, 5_000m, "position") };

        var result = StatementRunMatchingService.Match(import, rows, InternalReconciliationBook.Empty, StatementToleranceProfile.Default);

        result.Breaks.Should().ContainSingle();
        result.Breaks[0].BreakCode.Should().Be("POSITION_UNMATCHED");
        result.Breaks[0].ToleranceBreached.Should().BeTrue();
        result.Outcomes[0].OutcomeType.Should().Be("POSITION_UNMATCHED");
        result.Outcomes[0].Confidence.Should().Be(0m);
    }

    [Fact]
    public void Match_InternalRecordMissingFromStatement_ProducesMissingOnStatementBreak()
    {
        var import = Import();
        var book = new InternalReconciliationBook(
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", AsOf, 10m, 5_000m, "internal:pos:1")],
            [],
            []);

        var result = StatementRunMatchingService.Match(import, [], book, StatementToleranceProfile.Default);

        result.Outcomes.Should().BeEmpty();
        result.Breaks.Should().ContainSingle();
        result.Breaks[0].BreakCode.Should().Be("POSITION_MISSING_ON_STATEMENT");
        result.Breaks[0].SourceReference.Should().Be("internal:pos:1");
    }

    [Fact]
    public void Match_CashWithinTolerance_Matches_AndBeyondTolerance_Breaks()
    {
        var import = Import();
        var withinRows = new[] { Row(1, string.Empty, 0m, 0m, 100.00m, "cash") };
        var withinBook = new InternalReconciliationBook(
            [],
            [new InternalCashBalance("int-cash-1", "A1", "", 100.005m, "internal:cash:1")],
            []);

        var withinResult = StatementRunMatchingService.Match(import, withinRows, withinBook, StatementToleranceProfile.Default);
        withinResult.Breaks.Should().BeEmpty();
        withinResult.Outcomes[0].OutcomeType.Should().Be("matched");

        var beyondBook = new InternalReconciliationBook(
            [],
            [new InternalCashBalance("int-cash-1", "A1", "", 200m, "internal:cash:1")],
            []);
        var beyondResult = StatementRunMatchingService.Match(import, withinRows, beyondBook, StatementToleranceProfile.Default);
        beyondResult.Breaks.Should().ContainSingle();
        beyondResult.Breaks[0].BreakCode.Should().Be("CASH_CANDIDATE_REVIEW");
        beyondResult.Breaks[0].Delta.Should().Be(100m);
        beyondResult.Breaks[0].ToleranceBreached.Should().BeTrue();
    }

    [Fact]
    public void Match_TransactionMatchingInternalLedger_ProducesNoBreak()
    {
        var import = Import();
        var rows = new[] { Row(1, "SPY", 10m, 500m, -5_000m, "BUY") };
        var book = new InternalReconciliationBook(
            [],
            [],
            [new InternalLedgerTransaction("int-tx-1", null, "A1", "SPY", null, AsOf, AsOf, "BUY", 10m, -5_000m, "internal:tx:1")]);

        var result = StatementRunMatchingService.Match(import, rows, book, StatementToleranceProfile.Default);

        result.Breaks.Should().BeEmpty();
        result.Outcomes[0].OutcomeType.Should().Be("matched");
    }

    [Fact]
    public void Match_EmptyInternalBook_TreatsEveryRowAsHonestUnmatchedBreak()
    {
        var import = Import();
        var rows = new[]
        {
            Row(1, "SPY", 10m, 500m, 5_000m, "position"),
            Row(2, string.Empty, 0m, 0m, 250m, "cash"),
            Row(3, "MSFT", 5m, 400m, -2_000m, "BUY"),
        };

        var result = StatementRunMatchingService.Match(import, rows, InternalReconciliationBook.Empty, StatementToleranceProfile.Default);

        result.Breaks.Should().HaveCount(3);
        result.Outcomes.Should().HaveCount(3);
        result.Outcomes.Should().OnlyContain(outcome => outcome.OutcomeType != "matched");
        result.Breaks.Select(b => b.BreakCode).Should().BeEquivalentTo(
            new[] { "POSITION_UNMATCHED", "CASH_UNMATCHED", "TRANSACTION_UNMATCHED" });
    }
}
