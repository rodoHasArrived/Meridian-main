using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Ui;

/// <summary>
/// Calculated recurring journals through the real runner, journal intake and file-backed workbench:
/// each occurrence drafts its amount once from ledger inputs and consecutive occurrences never
/// draft the same economics twice.
/// </summary>
public sealed class RecurringJournalCalculationTests : IDisposable
{
    private const string Fund = "fund-calculated";
    private const string Tenant = "tenant-calculated";
    private const string Company = "company-calculated";
    private const string Entity = "entity-calculated";
    private const string ScheduleId = "calculated-schedule";
    private const string TemplateId = "calculated-template";
    private const string Ticker = "UST30";
    private static readonly Guid Book = Guid.Parse("5d0d6c0b-2a7e-4a0f-9a55-0f6f0a3a1c01");
    private static readonly Guid January = Guid.Parse("11111111-aaaa-4bbb-8ccc-000000000001");
    private static readonly Guid February = Guid.Parse("11111111-aaaa-4bbb-8ccc-000000000002");
    private static readonly Guid SecurityId = Guid.Parse("7c3f9f2e-91d4-4a3b-8f65-2f0c5a9d7e10");
    private static readonly DateOnly JanuaryEnd = new(2026, 1, 31);
    private static readonly DateOnly FebruaryEnd = new(2026, 2, 28);
    private static readonly DateTimeOffset Registered = new(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly RecurringJournalScope Scope = new(Fund, Book, Entity, "USD", Tenant, Company);
    private static readonly LedgerAccount EuroCash = LedgerAccounts.CashAccount("EUR-OPERATING");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-recurring-calculated-" + Guid.NewGuid().ToString("N"));
    private readonly DomainLedger _ledger = new();
    private int _ledgerReads;

    [Fact]
    public async Task ManagementFee_DraftsDayWeightedFeeOnOpeningNetAssets_OncePerWindow()
    {
        Capitalize(10_000_000m);
        await SeedAsync(ManagementFeeAccrualCalculation.CalculationKind,
            new() { ["annualRate"] = 0.02m, ["yearBasisDays"] = 365m });
        var fixture = await CreateFixtureAsync();

        var results = await fixture.Runner.RunDueAsync(At(JanuaryEnd));

        results.Should().ContainSingle().Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        // 10,000,000 x 2% x 31 / 365
        january.TotalDebits.Should().Be(16_986.30m);
        january.TotalCredits.Should().Be(16_986.30m);
        january.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        january.AccountingDate.Should().Be(JanuaryEnd);
        january.Memo.Should().Contain("10000000.00").And.Contain("2026-01-01 to 2026-01-31");
        january.EvidenceLinks.Should().Contain(Source().Uri);
        RecurringJournalEvidenceGuard.Validate(january).Should().BeNull();
        january.Lines.Select(line => (line.AccountPath, line.Side)).Should().BeEquivalentTo(new[]
        {
            ("Expenses:Management Fee Expense", AccountingTemplateLineSideDto.Debit),
            ("Liabilities:Management Fee Payable", AccountingTemplateLineSideDto.Credit)
        });

        // The posted January fee lowers February's opening net assets; a rerun changes nothing.
        Post(JanuaryEnd, (LedgerAccounts.ManagementFeeExpenseFor(Fund), 16_986.30m, 0m),
            (LedgerAccounts.ManagementFeePayableFor(Fund), 0m, 16_986.30m));
        await (await CreateFixtureAsync()).Runner.RunDueAsync(At(FebruaryEnd));
        await (await CreateFixtureAsync()).Runner.RunDueAsync(At(FebruaryEnd).AddHours(1));

        var drafts = await fixture.Drafts.ListAsync(Fund, Book);
        drafts.Should().HaveCount(2);
        drafts.Single(draft => draft.JournalEntryId == january.JournalEntryId).Should().BeEquivalentTo(january);
        var february = drafts.Single(draft => draft.AccountingDate == FebruaryEnd);
        // (10,000,000 - 16,986.30) x 2% x 28 / 365
        february.TotalDebits.Should().Be(15_316.40m);
        february.Memo.Should().Contain("2026-02-01 to 2026-02-28");
        _ledgerReads.Should().Be(2, "a retained draft is never recalculated");
    }

    [Fact]
    public async Task ManagementFee_MissingDayCountBasis_BlocksInsteadOfGuessing()
    {
        Capitalize(10_000_000m);
        await SeedAsync(ManagementFeeAccrualCalculation.CalculationKind, new() { ["annualRate"] = 0.02m });
        var fixture = await CreateFixtureAsync();

        var result = (await fixture.Runner.RunDueAsync(At(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("yearBasisDays");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownCalculationKind_BlocksWithoutDraft()
    {
        await SeedAsync("not-a-calculation", new());
        var fixture = await CreateFixtureAsync();

        var result = (await fixture.Runner.RunDueAsync(At(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("not-a-calculation");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task PerformanceFee_AccruesOnlyTheChangeFromWhatIsAlreadyPayable()
    {
        Capitalize(10_000_000m);
        Post(new(2026, 1, 20), (LedgerAccounts.Cash, 1_000_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 1_000_000m));
        await SeedAsync(PerformanceFeeAccrualCalculation.CalculationKind, new()
        {
            ["performanceFeeRate"] = 0.20m,
            ["highWaterMark"] = 10_000_000m,
            ["hurdleAmount"] = 0m,
            ["crystallize"] = 0m
        });
        var fixture = await CreateFixtureAsync();

        await fixture.Runner.RunDueAsync(At(JanuaryEnd));

        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        january.TotalDebits.Should().Be(200_000m, "20% of the 1,000,000 above the high-water mark");

        // January's fee is posted and payable. A further 500,000 gain in February must accrue
        // 100,000, not the cumulative 300,000.
        Post(JanuaryEnd, (LedgerAccounts.PerformanceFeeExpenseFor(Fund), 200_000m, 0m),
            (LedgerAccounts.PerformanceFeePayableFor(Fund), 0m, 200_000m));
        Post(new(2026, 2, 10), (LedgerAccounts.Cash, 500_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 500_000m));
        await (await CreateFixtureAsync()).Runner.RunDueAsync(At(FebruaryEnd));

        var february = (await fixture.Drafts.ListAsync(Fund, Book)).Single(draft => draft.AccountingDate == FebruaryEnd);
        february.TotalDebits.Should().Be(100_000m);
        february.Memo.Should().Contain("already accrued 200000.00");
        february.Lines.Single(line => line.Side == AccountingTemplateLineSideDto.Debit).AccountPath
            .Should().Be("Expenses:Performance Fee Expense");
    }

    [Fact]
    public async Task PerformanceFee_NoFurtherAccrual_RecordsNoDraftRequiredAndDoesNotRecalculate()
    {
        Capitalize(10_000_000m);
        await SeedAsync(PerformanceFeeAccrualCalculation.CalculationKind, new()
        {
            ["performanceFeeRate"] = 0.20m,
            ["highWaterMark"] = 10_000_000m,
            ["hurdleAmount"] = 0m,
            ["crystallize"] = 0m
        }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync();

        var first = (await fixture.Runner.RunDueAsync(At(JanuaryEnd))).Should().ContainSingle().Subject;
        // A gain recorded afterwards belongs to a later occurrence, not to the settled one.
        Post(new(2026, 1, 25), (LedgerAccounts.Cash, 1_000_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 1_000_000m));
        var retry = (await (await CreateFixtureAsync()).Runner.RunDueAsync(At(JanuaryEnd).AddHours(2))).Should().ContainSingle().Subject;

        first.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        retry.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        var queue = await fixture.Runner.GetQueueAsync(Fund, Book, Entity, tenantId: Tenant, companyId: Company);
        var row = queue.Occurrences.Should().ContainSingle().Subject;
        row.State.Should().Be(nameof(RecurringOccurrenceState.NoDraftRequired));
        row.JournalEntryId.Should().BeNull();
        row.Blockers.Should().ContainSingle().Which.Should().Contain("needs no further accrual");
    }

    [Fact]
    public async Task ExpenseAccrual_DraftsStraightLineAmountForTheWindow()
    {
        await SeedAsync(FundExpenseAccrualCalculation.CalculationKind,
            new() { ["annualAmount"] = 36_500m, ["yearBasisDays"] = 365m },
            new() { ["expenseId"] = "audit-2026" });
        var fixture = await CreateFixtureAsync();

        await fixture.Runner.RunDueAsync(At(JanuaryEnd));

        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.TotalDebits.Should().Be(3_100m);
        draft.Memo.Should().Contain("audit-2026");
    }

    [Fact]
    public async Task FxRevaluation_DraftsOnlyTheChangeInCarryingValue_AndBlocksWithoutARate()
    {
        Capitalize(10_000_000m);
        // 100,000 EUR bought at 1.08: carried at 108,000 USD.
        var purchase = Guid.NewGuid();
        _ledger.Post(new JournalEntry(purchase, At(new(2026, 1, 10)), "Buy EUR",
        [
            new(Guid.NewGuid(), purchase, At(new(2026, 1, 10)), EuroCash, 108_000m, 0m, "Buy EUR",
                currency: new LedgerEntryCurrency("EUR", "USD", 100_000m, 0m, 1.08m)),
            new(Guid.NewGuid(), purchase, At(new(2026, 1, 10)), LedgerAccounts.Cash, 0m, 108_000m, "Buy EUR")
        ]));
        await SeedAsync(FxRevaluationCalculation.CalculationKind, new());
        var rates = new Rates { [("EUR", JanuaryEnd)] = 1.10m };
        var fixture = await CreateFixtureAsync(rates);

        var results = await fixture.Runner.RunDueAsync(At(FebruaryEnd));

        results.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .Blockers.Should().ContainSingle().Which.Should().Contain("No EUR/USD FX rate");
        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        january.TotalDebits.Should().Be(2_000m, "100,000 EUR at 1.10 is 110,000 against 108,000 carried");
        january.Lines.Select(line => (line.AccountPath, line.Side, line.Amount)).Should().BeEquivalentTo(new[]
        {
            ("Assets:Cash EUR", AccountingTemplateLineSideDto.Debit, 2_000m),
            ("Revenue:Unrealized FX Gain", AccountingTemplateLineSideDto.Credit, 2_000m)
        });

        // Once January's revaluation is posted the account carries 110,000. February at 1.09
        // must draft a 1,000 loss, not a second gain against the original 108,000.
        Post(JanuaryEnd, (EuroCash, 2_000m, 0m), (LedgerAccounts.UnrealizedFxGain, 0m, 2_000m));
        rates[("EUR", FebruaryEnd)] = 1.09m;
        await (await CreateFixtureAsync(rates)).Runner.RunDueAsync(At(FebruaryEnd).AddHours(1));

        var february = (await fixture.Drafts.ListAsync(Fund, Book)).Single(draft => draft.AccountingDate == FebruaryEnd);
        february.Lines.Select(line => (line.AccountPath, line.Side, line.Amount)).Should().BeEquivalentTo(new[]
        {
            ("Expenses:Unrealized FX Loss", AccountingTemplateLineSideDto.Debit, 1_000m),
            ("Assets:Cash EUR", AccountingTemplateLineSideDto.Credit, 1_000m)
        });
    }

    [Fact]
    public async Task FixedIncome_ConsecutiveWindows_AmortizePremiumExactlyOnce()
    {
        // 1,000,000 face bought at 101.20 on 2025-12-31 and maturing 2026-12-31: 12,000 premium.
        var lot = new LedgerTaxLot("lot-1", new(2025, 12, 31), 10_000m, 101.20m, SecurityId)
        { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = 100m };
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker });
        var fixture = await CreateFixtureAsync(lots: [lot], cashFlows: MonthlyCouponCashFlows());

        await fixture.Runner.RunDueAsync(At(JanuaryEnd));
        await (await CreateFixtureAsync(lots: [lot], cashFlows: MonthlyCouponCashFlows())).Runner.RunDueAsync(At(FebruaryEnd));

        var drafts = await fixture.Drafts.ListAsync(Fund, Book);
        drafts.Should().HaveCount(2);
        var january = drafts.Single(draft => draft.AccountingDate == JanuaryEnd);
        var february = drafts.Single(draft => draft.AccountingDate == FebruaryEnd);
        decimal Amortized(ManualJournalEntryDraftDto draft) => draft.Lines
            .Single(line => line.AccountPath == "Assets:Securities UST30" && line.Side == AccountingTemplateLineSideDto.Credit).Amount;
        decimal Coupon(ManualJournalEntryDraftDto draft) => draft.Lines
            .Single(line => line.AccountPath == "Assets:Accrued Interest Receivable UST30").Amount;

        Coupon(january).Should().Be(5_000m);
        Coupon(february).Should().Be(5_000m);
        // Actual/365 share of the 12,000 premium: 31 days, then 28 days. Amortizing February
        // from acquisition again would book 1,939.73 instead.
        Amortized(january).Should().Be(1_019.18m);
        Amortized(february).Should().Be(920.55m);
        january.TotalDebits.Should().Be(january.TotalCredits);
        february.Memo.Should().Contain("2026-02-01 to 2026-02-28");
    }

    [Fact]
    public async Task FixedIncome_StaleProjection_BlocksWithTheGateReason()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker });
        var fixture = await CreateFixtureAsync(cashFlows: MonthlyCouponCashFlows(StructuredCashFlowStaleness.Stale));

        var result = (await fixture.Runner.RunDueAsync(At(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("cannot be posted");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task CalculatedDraft_PassesTheExistingSubmitAndApproveFlow_WithoutSelfApproval()
    {
        Capitalize(10_000_000m);
        await SeedAsync(ManagementFeeAccrualCalculation.CalculationKind,
            new() { ["annualRate"] = 0.02m, ["yearBasisDays"] = 365m }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync();
        await fixture.Runner.RunDueAsync(At(JanuaryEnd));
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        draft.ApprovalId.Should().BeNull();
        draft.PostedAtUtc.Should().BeNull();
        draft.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical).Should().BeEmpty();

        var submitted = await fixture.Workbench.ApplyLifecycleActionAsync(new(
            draft.JournalEntryId, Fund, JournalEntryLifecycleActionDto.Submit, "fund-accountant", draft.Version,
            Notes: "Submit the calculated January management fee for independent review.",
            EvidenceLinks: [Evidence("review", draft.JournalEntryId)], LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));
        var approved = await fixture.Workbench.ApplyLifecycleActionAsync(new(
            draft.JournalEntryId, Fund, JournalEntryLifecycleActionDto.Approve, "independent-controller", submitted.JournalEntry.Version,
            Notes: "Approve the fee after agreeing net assets and the day count to the ledger.",
            EvidenceLinks: [Evidence("approval", draft.JournalEntryId)], LedgerBookId: Book, TenantId: Tenant, CompanyId: Company));

        approved.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Approved);
        approved.JournalEntry.TotalDebits.Should().Be(16_986.30m);
        approved.JournalEntry.RecurringJournalEvidenceJson.Should().Be(draft.RecurringJournalEvidenceJson);
    }

    private void Capitalize(decimal amount)
        => Post(new(2025, 12, 31), (LedgerAccounts.Cash, amount, 0m), (LedgerAccounts.InvestorCapitalFor("lp-1"), 0m, amount));

    private void Post(DateOnly date, params (LedgerAccount account, decimal debit, decimal credit)[] lines)
        => _ledger.PostLines(At(date), $"Seeded {date:yyyy-MM-dd}", lines);

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static string Evidence(string intent, Guid journalEntryId)
        => $"evidence://{intent}/journal/{journalEntryId:D}/ledger-book/{Book:D}/tenant/{Tenant}/company/{Company}";

    private async Task SeedAsync(string kind, Dictionary<string, decimal> terms,
        Dictionary<string, string>? identifiers = null, DateOnly? endsOn = null)
    {
        var store = new FileRecurringJournalStore(Path.Combine(_root, "recurring"));
        await store.InitializeAsync();
        await using var session = await store.OpenSessionAsync();
        await session.RegisterTemplateAsync(new JournalTemplate(TemplateId, "Calculated journal", "Calculated each period", [],
            calculation: new JournalTemplateCalculation(kind, identifiers)), "controller", 0, Registered);
        await session.RegisterScheduleAsync(new RecurringJournalSchedule(ScheduleId, TemplateId, new LedgerBookKey(Fund, "Fund"),
            RecurringJournalCadence.Monthly, JanuaryEnd, "controller", Registered, terms, endsOn: endsOn ?? FebruaryEnd),
            Scope, [Source()], "controller", 0, Registered);
    }

    private async Task<Fixture> CreateFixtureAsync(IReconciliationFxRateProvider? rates = null,
        IReadOnlyList<LedgerTaxLot>? lots = null, ISecurityMasterCashFlowService? cashFlows = null)
    {
        var configuration = new InMemoryAccountingConfigurationStore();
        await configuration.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, Book,
            AccountingConfigurationStatusDto.Draft, "test", Registered, [],
            [new("mgmt-expense", "Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
             new("mgmt-payable", "Liabilities:Management Fee Payable", "Management Fee Payable", "Liability"),
             new("perf-expense", "Expenses:Performance Fee Expense", "Performance Fee Expense", "Expense"),
             new("perf-payable", "Liabilities:Performance Fee Payable", "Performance Fee Payable", "Liability"),
             new("opex", "Expenses:Fund Operating Expense", "Fund Operating Expense", "Expense"),
             new("accrued", "Liabilities:Accrued Expenses Payable", "Accrued Expenses Payable", "Liability"),
             new("eur-cash", "Assets:Cash EUR", EuroCash.Name, "Asset", FinancialAccountId: EuroCash.FinancialAccountId),
             new("fx-gain", "Revenue:Unrealized FX Gain", LedgerAccounts.UnrealizedFxGain.Name, "Revenue"),
             new("fx-loss", "Expenses:Unrealized FX Loss", LedgerAccounts.UnrealizedFxLoss.Name, "Expense"),
             new("bond", "Assets:Securities UST30", LedgerAccounts.Securities(Ticker).Name, "Asset", Symbol: Ticker),
             new("accrued-interest", "Assets:Accrued Interest Receivable UST30", LedgerAccounts.AccruedInterestReceivable(Ticker).Name, "Asset", Symbol: Ticker),
             new("coupon", "Revenue:Coupon Income", LedgerAccounts.CouponIncome.Name, "Revenue")],
            [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var audit = new FileAccountingConfigurationStore(Path.Combine(_root, "audit.json"));
        var configurationService = new AccountingConfigurationService(configuration, audit);
        var drafts = new FileManualJournalEntryDraftStore(Path.Combine(_root, "drafts.json"));
        var journal = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        journal.Setup(value => value.GetPeriodAsync(January, It.IsAny<CancellationToken>())).ReturnsAsync(Period(January, 1));
        journal.Setup(value => value.GetPeriodAsync(February, It.IsAny<CancellationToken>())).ReturnsAsync(Period(February, 2));
        var store = new FileRecurringJournalStore(Path.Combine(_root, "recurring"));
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configurationService, audit,
            journalStore: journal.Object, recurringStore: store);
        var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configurationService);
        var inputs = new LedgerInputs(_ledger, lots ?? [], () => _ledgerReads++);
        var cashFlowService = cashFlows ?? MonthlyCouponCashFlows();
        var runner = new RecurringJournalRunner(store, intake, drafts, new Periods(),
        [
            new ManagementFeeAccrualCalculation(inputs),
            new PerformanceFeeAccrualCalculation(inputs),
            new FundExpenseAccrualCalculation(),
            new FixedIncomeAccrualCalculation(inputs,
                new SecurityMasterAmortizationLedgerBridge(cashFlowService, NullLogger<SecurityMasterAmortizationLedgerBridge>.Instance),
                cashFlowService),
            new FxRevaluationCalculation(inputs, rates ?? new Rates())
        ]);
        return new(runner, drafts, workbench, inputs);
    }

    private static LedgerAccountingPeriod Period(Guid id, int month)
    {
        var start = new DateOnly(2026, month, 1);
        return new(id, Book, 2026, month, start.ToString("MMMM yyyy"), start, start.AddMonths(1).AddDays(-1), "Open", Registered, null, 1);
    }

    private static JournalEvidenceReference Source() => new("fund-terms-2026", "evidence://fund-terms-2026",
        "fund-terms", "document-vault", Registered.AddDays(-1), "records-controller", ContentHash: new string('b', 64), EvidenceVersion: 1);

    private sealed record Fixture(RecurringJournalRunner Runner, FileManualJournalEntryDraftStore Drafts,
        ManualJournalEntryWorkbenchService Workbench, LedgerInputs Inputs);

    private sealed class Periods : IRecurringJournalPeriodAuthority
    {
        public Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
        {
            var id = date.Month == 1 ? January : February;
            var start = new DateOnly(date.Year, date.Month, 1);
            return Task.FromResult(new RecurringJournalPeriodState(id.ToString("D"), 1, true, null, null, null,
                start, start.AddMonths(1).AddDays(-1)));
        }
    }

    private sealed class LedgerInputs(DomainLedger ledger, IReadOnlyList<LedgerTaxLot> lots, Action onLedgerRead) : IRecurringJournalCalculationInputs
    {
        public Task<IReadOnlyLedger> HydrateLedgerAsOfAsync(RecurringJournalScope scope, DateOnly asOf, CancellationToken ct)
        {
            onLedgerRead();
            return Task.FromResult<IReadOnlyLedger>(ledger);
        }

        public Task<IReadOnlyList<LedgerTaxLot>> ListOpenLotsAsync(RecurringJournalScope scope, LedgerAccount account, DateOnly asOf, CancellationToken ct)
            => Task.FromResult(lots);
    }

    private sealed class Rates : Dictionary<(string Currency, DateOnly Date), decimal>, IReconciliationFxRateProvider
    {
        public bool TryGetRate(string fromCurrency, string toCurrency, DateOnly asOf, out decimal rate)
            => TryGetValue((fromCurrency, asOf), out rate) && toCurrency == "USD";
    }

    /// <summary>A 6% annual bond paying 5,000 at each month-end, projected from the requested as-of date.</summary>
    private static ISecurityMasterCashFlowService MonthlyCouponCashFlows(
        StructuredCashFlowStaleness staleness = StructuredCashFlowStaleness.Fresh)
    {
        var service = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        service.Setup(value => value.GetProjectionAsOfAsync(SecurityId, StructuredCashFlowScenario.Base,
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken _) =>
            {
                var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);
                var schedule = Enumerable.Range(1, 12)
                    .Select(month => new DateOnly(2026, month, 1).AddMonths(1).AddDays(-1))
                    .Where(end => end >= asOfDate)
                    .Select(end => new StructuredCashFlowScheduleEntry(
                        new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), 0m, 5_000m, 1m)
                    {
                        AccrualStart = new DateOnly(end.Year, end.Month, 1).AddDays(-1),
                        AccrualEnd = end
                    })
                    .ToArray();
                return new StructuredCashFlowProjectionDto(securityId, StructuredCashFlowSourceKind.CalculatedBullet,
                    scenario, asOf, schedule, staleness, asOf,
                    TermsUsed: StructuredCashFlowTerms.Empty with { MaturityDate = new DateOnly(2026, 12, 31), DayCountConvention = "ACT/365" });
            });
        return service.Object;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
