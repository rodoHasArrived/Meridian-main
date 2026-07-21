using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// End-to-end coverage for the rewired statement-run workflow. These tests prove the live workflow
/// now reconciles imported statements against Meridian's internal book with the shared matching
/// engine (positions, cash, and transactions) and FX normalization — replacing the previous
/// self-referential matcher that fabricated matches and hard-coded every break as tolerance-breached.
/// </summary>
public sealed class StatementRunWorkflowServiceTests : IDisposable
{
    private const string CanonicalHeader =
        "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-run-wf-{Guid.NewGuid():N}");

    public StatementRunWorkflowServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the per-test temp directory.
        }
    }

    [Fact]
    public async Task CreateAsync_WithNoInternalBook_TurnsEveryRowIntoAToleranceBreachedBreak()
    {
        var path = await WriteStatementAsync(
            "empty-book.csv",
            "FUND-1,SPY,10,500,5000,position,2026-05-28,,USD,,",
            "FUND-1,,0,0,2500.25,cash,2026-05-28,,USD,,",
            "FUND-1,MSFT,5,20,100,trade,2026-05-28,,USD,,EXT-9");
        var workflow = CreateWorkflow();

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        // No internal populations means nothing to match against: every row is a real, unmatched
        // break. The old matcher would have fabricated a "matched" position and near-zero cash match.
        result.Breaks.Should().HaveCount(3);
        result.Breaks.Should().OnlyContain(breakRecord => breakRecord.ToleranceBreached);
        result.Cases.Should().HaveCount(3);
        result.Cases.Should().OnlyContain(reconciliationCase => reconciliationCase.Priority == "High");
    }

    [Fact]
    public async Task CreateAsync_WhenInternalBookMatches_ProducesNoBreaks()
    {
        var path = await WriteStatementAsync(
            "matched.csv",
            "FUND-1,SPY,10,500,5000,position,2026-05-28,,USD,,",
            "FUND-1,,0,0,2500.25,cash,2026-05-28,,USD,,",
            "FUND-1,MSFT,5,20,-100,trade,2026-05-28,2026-05-30,USD,,EXT-9");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [new InternalPortfolioPosition("i-spy", "EXT-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy")],
            [new InternalCashBalance("i-cash", "EXT-1", "USD", 2500.25m, "internal:cash")],
            [new InternalLedgerTransaction("i-tx", "EXT-9", "EXT-1", "MSFT", "USD", new DateOnly(2026, 5, 28), new DateOnly(2026, 5, 30), "trade", 5m, -100m, "internal:tx")])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().BeEmpty();
        result.Cases.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ConvertsForeignCashToBaseCurrency_UsingFxRates()
    {
        // 1,000 EUR converts to 1,085 USD at 1.085; the internal book holds 1,085 USD for the account.
        var path = await WriteStatementAsync(
            "fx.csv",
            "FUND-1,,0,0,1000,cash,2026-05-28,,EUR,,");
        var workflow = CreateWorkflow(
            Populations(new InternalReconciliationPopulations(
                [],
                [new InternalCashBalance("i-cash", "EXT-1", "USD", 1085m, "internal:cash")],
                [])),
            new TableReconciliationFxRateProvider([new ReconciliationFxQuote("EUR", "USD", 1.085m)]));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().BeEmpty("the foreign-currency cash line reconciles once converted to the USD base");
    }

    [Fact]
    public async Task CreateAsync_WithoutFxRate_LeavesForeignCashAsABreak()
    {
        var path = await WriteStatementAsync(
            "fx-missing.csv",
            "FUND-1,,0,0,1000,cash,2026-05-28,,EUR,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [],
            [new InternalCashBalance("i-cash", "FUND-1", "USD", 1085m, "internal:cash")],
            [])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        // Identity FX cannot convert EUR to USD, so the line fails closed to a break instead of
        // being matched across incompatible currencies.
        result.Breaks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenInternalPositionMissingFromStatement_CreatesInternalBreak()
    {
        var path = await WriteStatementAsync(
            "internal-only.csv",
            "FUND-1,SPY,10,500,5000,position,2026-05-28,,USD,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [
                new InternalPortfolioPosition("i-spy", "EXT-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy"),
                new InternalPortfolioPosition("i-qqq", "EXT-1", "QQQ", new DateOnly(2026, 5, 28), 3m, 900m, "internal:pos:qqq"),
            ],
            [],
            [])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        // SPY matches; the internal QQQ position has no statement counterpart, so it becomes a
        // one-sided internal break anchored to the internal evidence reference.
        var breakRecord = result.Breaks.Should().ContainSingle().Subject;
        breakRecord.SourceReference.Should().Be("internal:pos:qqq");
        breakRecord.ToleranceBreached.Should().BeTrue();
    }

    private StatementRunWorkflowService CreateWorkflow(
        IInternalReconciliationPopulationProvider? populations = null,
        IReconciliationFxRateProvider? fxRateProvider = null)
    {
        var importStore = new JsonCanonicalStatementStore(_root);
        return new StatementRunWorkflowService(
            importStore,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(importStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            populations,
            fxRateProvider);
    }

    private async Task<string> WriteStatementAsync(string fileName, params string[] rows)
    {
        var path = Path.Combine(_root, fileName);
        var lines = new List<string> { CanonicalHeader };
        lines.AddRange(rows);
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    private static StatementRunRequest Request(string path) => new(
        Broker: "custodian",
        SourceInstitution: "Sample Custodian",
        FundAccountId: "FUND-1",
        ExternalAccountId: "EXT-1",
        StatementPeriodStart: new DateOnly(2026, 5, 1),
        StatementPeriodEnd: new DateOnly(2026, 5, 31),
        SourcePath: path,
        OriginalFileName: Path.GetFileName(path),
        MappingProfileId: "canonical-csv-v1",
        ToleranceProfileId: "statement-default",
        ImportedBy: "ops-user",
        SourceFileHash: string.Empty);

    private static IInternalReconciliationPopulationProvider Populations(InternalReconciliationPopulations populations)
        => new StubPopulationProvider(populations);

    private sealed class StubPopulationProvider(InternalReconciliationPopulations populations)
        : IInternalReconciliationPopulationProvider
    {
        public Task<InternalReconciliationPopulations> GetPopulationsAsync(
            InternalReconciliationPopulationContext context,
            CancellationToken ct = default)
            => Task.FromResult(populations);
    }
}
