using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// End-to-end proof that the live statement-run workflow reconciles imported rows against the
/// internal book supplied by <see cref="IInternalReconciliationBookSource"/>: matched rows produce
/// no breaks, while rows with no internal counterpart surface as genuine breaks.
/// </summary>
public sealed class StatementRunWorkflowServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-stmt-workflow-{Guid.NewGuid():N}");

    public StatementRunWorkflowServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_MatchesRowsAgainstInternalBook_OnlyUnmatchedRowsBecomeBreaks()
    {
        var csvPath = Path.Combine(_root, "statement.csv");
        await File.WriteAllTextAsync(
            csvPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
            "A1,SPY,10,500,5000,position,2026-01-15\n" +
            "A1,,0,0,250,cash,2026-01-15\n");

        var store = new JsonCanonicalStatementStore(_root);
        var book = new InternalReconciliationBook(
            [new InternalPortfolioPosition("int-pos-1", "A1", "SPY", new DateOnly(2026, 1, 15), 10m, 5_000m, "internal:pos:1")],
            [],
            []);
        var workflow = new StatementRunWorkflowService(
            store,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(store),
            new PassthroughValidation(),
            new StubInternalBook(book));

        var request = new StatementRunRequest(
            Broker: "custodian",
            SourceInstitution: "Sample Custodian",
            FundAccountId: "fund-1",
            ExternalAccountId: "external-1",
            StatementPeriodStart: new DateOnly(2026, 1, 1),
            StatementPeriodEnd: new DateOnly(2026, 1, 31),
            SourcePath: csvPath,
            OriginalFileName: "statement.csv",
            MappingProfileId: "statement-default",
            ToleranceProfileId: "statement-default",
            ImportedBy: "operator@example.test",
            SourceFileHash: string.Empty);

        var result = await workflow.CreateAsync(request);

        // The SPY position matches the internal book exactly → no break; only the unmatched cash
        // row surfaces as a reconciliation break and case.
        result.Breaks.Should().ContainSingle();
        result.Breaks[0].BreakCode.Should().Be("CASH_UNMATCHED");
        // The CSV header is source row 1, so the position row is row 2 and the cash row is row 3.
        result.Breaks[0].SourceReference.Should().EndWith(":3");
        result.Cases.Should().ContainSingle();
    }

    private sealed class PassthroughValidation : IStatementReconciliationValidationService
    {
        public Task<string> ValidateAsync(
            StatementReconciliationValidationRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult("ok");
    }

    private sealed class StubInternalBook(InternalReconciliationBook book) : IInternalReconciliationBookSource
    {
        public Task<InternalReconciliationBook> GetBookAsync(
            StatementRunRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(book);
    }
}
