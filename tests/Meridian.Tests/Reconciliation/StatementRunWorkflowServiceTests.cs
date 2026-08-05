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
            "EXT-1,SPY,10,500,5000,position,2026-05-28,,USD,,",
            "EXT-1,,0,0,2500.25,cash,2026-05-31,,USD,,",
            "EXT-1,MSFT,5,20,100,trade,2026-05-28,,USD,,EXT-9");
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
            "EXT-1,SPY,10,500,5000,position,2026-05-28,,USD,,",
            "EXT-1,,0,0,2500.25,cash,2026-05-31,,USD,,",
            "EXT-1,MSFT,5,20,-100,trade,2026-05-28,2026-05-30,USD,,EXT-9");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [new InternalPortfolioPosition("i-spy", "EXT-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy")],
            [new InternalCashBalance("i-cash", "EXT-1", "USD", 2500.25m, "internal:cash", new DateOnly(2026, 5, 31))],
            [new InternalLedgerTransaction("i-tx", "EXT-9", "EXT-1", "MSFT", "USD", new DateOnly(2026, 5, 28), new DateOnly(2026, 5, 30), "trade", 5m, -100m, "internal:tx")])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().BeEmpty();
        result.Cases.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenStatementRowAccountDiffersFromRunAccount_FailsBeforePersistingOrMatching()
    {
        var path = await WriteStatementAsync(
            "wrong-account.csv",
            "OTHER-ACCOUNT,SPY,10,500,5000,position,2026-05-28,,USD,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [new InternalPortfolioPosition("i-spy", "EXT-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy")],
            [],
            [])));

        var act = async () => await workflow.CreateAsync(Request(path), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*external account*");
        var imports = await new JsonCanonicalStatementStore(_root).ListImportsAsync();
        imports.Should().BeEmpty("an account-mismatched statement must not be retained as a reconcilable run");
    }

    [Fact]
    public async Task CreateAsync_WhenCashBalanceDateDiffersFromInternal_DoesNotMatchOnAmountAlone()
    {
        // The statement closing balance and a faulty internal source both carry 30 Apr, despite the run
        // closing on 31 May. Date equality between the two sources alone is insufficient: a cash balance
        // must be dated at the run's statement period end before it can be reconciled.
        var path = await WriteStatementAsync(
            "wrong-period-cash.csv",
            "EXT-1,,0,0,2500.25,cash,2026-04-30,,USD,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [],
            [new InternalCashBalance("i-cash", "EXT-1", "USD", 2500.25m, "internal:cash", new DateOnly(2026, 4, 30))],
            [])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        // Both sides are unmatched instead of producing a fabricated exact match solely because the
        // stale statement and stale internal snapshot agree on account, currency, date, and amount.
        result.Breaks.Should().HaveCount(2);
        result.Breaks.Should().Contain(breakRecord => breakRecord.SourceReference == "internal:cash",
            "the stale internal balance must remain visible as a one-sided internal break");
    }

    [Fact]
    public async Task CreateAsync_ConvertsForeignCashAmountsWithoutErasingCurrencyIdentity()
    {
        // Both EUR balances convert to 1,085 in the USD reporting base while retaining EUR as their
        // matching identity. A USD reporting-currency balance must not substitute for this EUR balance.
        var path = await WriteStatementAsync(
            "fx.csv",
            "EXT-1,,0,0,1000,cash,2026-05-31,,EUR,,");
        var workflow = CreateWorkflow(
            Populations(new InternalReconciliationPopulations(
                [],
                [new InternalCashBalance("i-cash", "EXT-1", "EUR", 1000m, "internal:cash", new DateOnly(2026, 5, 31))],
                [])),
            new TableReconciliationFxRateProvider([new ReconciliationFxQuote("EUR", "USD", 1.085m, new DateOnly(2026, 5, 1))]));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().BeEmpty("matching EUR balances reconcile after their amounts convert to the USD reporting base");
    }

    [Fact]
    public async Task CreateAsync_WhenPerCurrencyCashBalancesAreSwappedAfterFxConversion_SurfacesCandidateBreaks()
    {
        // A corrupt statement swaps USD and EUR cash balances. FX conversion makes the swapped amounts
        // numerically equal to the opposite internal balance, but source-currency identity must still
        // prevent an exact match and leave both discrepancies for operator review.
        var path = await WriteStatementAsync(
            "swapped-per-currency-cash.csv",
            "EXT-1,,0,0,200,cash,2026-05-31,,USD,,",
            "EXT-1,,0,0,120,cash,2026-05-31,,EUR,,");
        var workflow = CreateWorkflow(
            Populations(new InternalReconciliationPopulations(
                [],
                [
                    new InternalCashBalance("i-usd", "EXT-1", "USD", 240m, "internal:cash:usd", new DateOnly(2026, 5, 31)),
                    new InternalCashBalance("i-eur", "EXT-1", "EUR", 100m, "internal:cash:eur", new DateOnly(2026, 5, 31)),
                ],
                [])),
            new TableReconciliationFxRateProvider([new ReconciliationFxQuote("EUR", "USD", 2m, new DateOnly(2026, 5, 1))]));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().HaveCount(2);
        result.Breaks.Should().OnlyContain(breakRecord => breakRecord.BreakCode == "CASH_CANDIDATE");
        result.Breaks.Should().OnlyContain(breakRecord => breakRecord.ToleranceBreached);
        result.Breaks.Should().Contain(breakRecord => breakRecord.SourceReference.EndsWith(":2", StringComparison.Ordinal));
        result.Breaks.Should().Contain(breakRecord => breakRecord.SourceReference.EndsWith(":3", StringComparison.Ordinal));
        result.Cases.Should().OnlyContain(reconciliationCase => reconciliationCase.Priority == "High");
        result.Cases.Should().OnlyContain(reconciliationCase =>
            reconciliationCase.BreakExplanation != null
            && reconciliationCase.BreakExplanation.RequiredSignoffRole == "Fund accounting");
    }

    [Fact]
    public async Task CreateAsync_WithoutFxRate_LeavesForeignCashAsABreak()
    {
        var path = await WriteStatementAsync(
            "fx-missing.csv",
            "EXT-1,,0,0,1000,cash,2026-05-31,,EUR,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [],
            [new InternalCashBalance("i-cash", "EXT-1", "USD", 1085m, "internal:cash", new DateOnly(2026, 5, 31))],
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
            "EXT-1,SPY,10,500,5000,position,2026-05-28,,USD,,");
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

        // The case must record internal provenance, not a fabricated broker statement row, so an
        // operator gets true attribution and a link to the retained internal record.
        var internalCase = result.Cases.Should().ContainSingle().Subject;
        var attachment = internalCase.Attachments.Should().ContainSingle().Subject;
        attachment.EvidenceKind.Should().Be("InternalReconciliationRecord");
        attachment.SourceSystem.Should().Be("meridian-internal-book");
        attachment.SourceReference.Should().Be("internal:pos:qqq");
        internalCase.EvidenceReferences.Should().Contain(reference => reference.Contains("internal-record:internal:pos:qqq", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_WithDefaultToleranceProfile_BreaksOnDeltaBeyondDefault()
    {
        // The statement cash is 0.50 off the internal book — beyond the default 0.01 cash tolerance —
        // so it surfaces as a break. Paired with the loose-profile test below to show the run's selected
        // profile, not a hard-coded default, drives the thresholds.
        var path = await WriteStatementAsync(
            "tolerance-default.csv",
            "EXT-1,,0,0,1000.50,cash,2026-05-31,,USD,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [],
            [new InternalCashBalance("i-cash", "EXT-1", "USD", 1000m, "internal:cash", new DateOnly(2026, 5, 31))],
            [])));

        var result = await workflow.CreateAsync(Request(path), CancellationToken.None);

        result.Breaks.Should().ContainSingle("the 0.50 delta exceeds the default 0.01 cash tolerance");
    }

    [Fact]
    public async Task CreateAsync_HonorsSelectedToleranceProfile()
    {
        // Same 0.50 delta as the default-profile test, but this run selects a loose profile whose 1.00
        // cash tolerance absorbs it, so it matches instead of breaking. Proves the run's selected
        // ToleranceProfileId threads into the matcher instead of always using the default thresholds.
        var path = await WriteStatementAsync(
            "tolerance-loose.csv",
            "EXT-1,,0,0,1000.50,cash,2026-05-31,,USD,,");
        var looseProfile = new StatementToleranceProfile(
            "statement-loose",
            1,
            [new CashToleranceRule("cash-loose-v1", 1.00m, null, TimeSpan.FromDays(5))],
            [new PositionToleranceRule("position-loose-v1", 0.0001m, 0m, 0m)],
            [new TransactionToleranceRule("transaction-loose-v1", 1.00m, TimeSpan.FromDays(5), 0m)]);
        var workflow = CreateWorkflow(
            Populations(new InternalReconciliationPopulations(
                [],
                [new InternalCashBalance("i-cash", "EXT-1", "USD", 1000m, "internal:cash", new DateOnly(2026, 5, 31))],
                [])),
            toleranceProfileProvider: new InMemoryStatementToleranceProfileProvider(
                [StatementToleranceProfile.Default, looseProfile]));

        var result = await workflow.CreateAsync(
            Request(path, toleranceProfileId: "statement-loose"),
            CancellationToken.None);

        result.Breaks.Should().BeEmpty("the 0.50 delta is within the selected loose profile's 1.00 cash tolerance");
    }

    [Fact]
    public async Task CreateAsync_WithUnknownToleranceProfile_FailsClosed()
    {
        // The default provider knows only "statement-default". A run that names an unregistered profile
        // must fail rather than silently reconcile with default thresholds while recording the requested
        // id, so the persisted profile is always the profile actually applied.
        var path = await WriteStatementAsync(
            "unknown-tolerance.csv",
            "EXT-1,,0,0,1000,cash,2026-05-28,,USD,,");
        var workflow = CreateWorkflow();

        var act = async () => await workflow.CreateAsync(
            Request(path, toleranceProfileId: "nonexistent-profile"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        // The import must not be committed when tolerance resolution fails, so a corrected retry of the
        // same statement is not blocked by the duplicate-source guard.
        var imports = await new JsonCanonicalStatementStore(_root).ListImportsAsync();
        imports.Should().BeEmpty("the import must not be persisted when the run fails before matching");
    }

    [Fact]
    public async Task CreateAsync_WhenStatementAccountDiffersFromRequestedExternalAccount_FailsClosed()
    {
        // The internal book belongs to EXT-1 and would exactly match this row if the matcher rewrote
        // its retained source account. The run must instead reject a populated account B before that
        // normalization can turn this into a false reconciliation for account A.
        var path = await WriteStatementAsync(
            "wrong-account.csv",
            "EXT-B,SPY,10,500,5000,position,2026-05-28,,USD,,");
        var workflow = CreateWorkflow(Populations(new InternalReconciliationPopulations(
            [new InternalPortfolioPosition("i-spy", "EXT-1", "SPY", new DateOnly(2026, 5, 28), 10m, 5000m, "internal:pos:spy")],
            [],
            [])));

        var act = async () => await workflow.CreateAsync(Request(path), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*account 'EXT-B'*requested external account 'EXT-1'*");
    }

    [Fact]
    public async Task CreateAsync_WithMalformedSettlementDate_FailsClosed()
    {
        // A nonblank but unparsable optional settlement date must be rejected, not silently dropped to
        // null: the matcher substitutes the trade date for a null settlement date, so a bad source date
        // could exact-match a same-day ledger transaction instead of blocking the import.
        var path = await WriteStatementAsync(
            "bad-settlement.csv",
            "EXT-1,MSFT,5,20,-100,trade,2026-05-28,not-a-date,USD,,EXT-9");
        var workflow = CreateWorkflow();

        var act = async () => await workflow.CreateAsync(Request(path), CancellationToken.None);

        await act.Should().ThrowAsync<System.IO.InvalidDataException>();
    }

    private StatementRunWorkflowService CreateWorkflow(
        IInternalReconciliationPopulationProvider? populations = null,
        IReconciliationFxRateProvider? fxRateProvider = null,
        IStatementToleranceProfileProvider? toleranceProfileProvider = null)
    {
        var importStore = new JsonCanonicalStatementStore(_root);
        return new StatementRunWorkflowService(
            importStore,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(importStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            populations,
            fxRateProvider,
            toleranceProfileProvider);
    }

    private async Task<string> WriteStatementAsync(string fileName, params string[] rows)
    {
        var path = Path.Combine(_root, fileName);
        var lines = new List<string> { CanonicalHeader };
        lines.AddRange(rows);
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    private static StatementRunRequest Request(string path, string toleranceProfileId = "statement-default") => new(
        Broker: "custodian",
        SourceInstitution: "Sample Custodian",
        FundAccountId: "FUND-1",
        ExternalAccountId: "EXT-1",
        StatementPeriodStart: new DateOnly(2026, 5, 1),
        StatementPeriodEnd: new DateOnly(2026, 5, 31),
        SourcePath: path,
        OriginalFileName: Path.GetFileName(path),
        MappingProfileId: "canonical-csv-v1",
        ToleranceProfileId: toleranceProfileId,
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
