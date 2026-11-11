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

    private readonly HashSet<int> _closedMonths = [];

    [Fact]
    public async Task ManagementFee_DraftsDayWeightedFeeOnOpeningNetAssets_OncePerWindow()
    {
        Capitalize(10_000_000m);
        await SeedAsync(ManagementFeeAccrualCalculation.CalculationKind,
            new() { ["annualRate"] = 0.02m, ["yearBasisDays"] = 365m });
        var fixture = await CreateFixtureAsync();

        var results = await fixture.Runner.RunDueAsync(Due(JanuaryEnd));

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
        await fixture.Drafts.SaveAsync(january with { Status = ManualJournalEntryStatusDto.Posted, PostedAtUtc = Registered, PostedBy = "controller" });
        january = (await fixture.Drafts.ListAsync(Fund, Book)).Single();
        await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd));
        await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1));

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

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("yearBasisDays");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownCalculationKind_BlocksWithoutDraft()
    {
        await SeedAsync("not-a-calculation", new());
        var fixture = await CreateFixtureAsync();

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

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

        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));

        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        january.TotalDebits.Should().Be(200_000m, "20% of the 1,000,000 above the high-water mark");

        // January's fee is posted and payable. A further 500,000 gain in February must accrue
        // 100,000, not the cumulative 300,000.
        Post(JanuaryEnd, (LedgerAccounts.PerformanceFeeExpenseFor(Fund), 200_000m, 0m),
            (LedgerAccounts.PerformanceFeePayableFor(Fund), 0m, 200_000m));
        await fixture.Drafts.SaveAsync(january with { Status = ManualJournalEntryStatusDto.Posted, PostedAtUtc = Registered, PostedBy = "controller" });
        Post(new(2026, 2, 10), (LedgerAccounts.Cash, 500_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 500_000m));
        await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd));

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

        var first = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;
        // A gain recorded afterwards belongs to a later occurrence, not to the settled one.
        Post(new(2026, 1, 25), (LedgerAccounts.Cash, 1_000_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 1_000_000m));
        var retry = (await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(JanuaryEnd).AddHours(2))).Should().ContainSingle().Subject;

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

        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));

        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.TotalDebits.Should().Be(3_100m);
        draft.Memo.Should().Contain("audit-2026");
    }

    [Fact]
    public async Task Depreciation_DraftsEachRegisteredAssetsMonthlyChargeOnce_AndStopsAtRetirement()
    {
        var register = new FileFixedAssetRegister(Path.Combine(_root, "registers"));
        var server = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
        var fitOut = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
        // 36,000 over 36 months = 1,000 a month; 12,000 over 24 months = 500 a month, retired mid-February.
        await register.UpsertAsync(Asset(server, "Server", 36_000m, 36, new(2026, 1, 1)));
        await register.UpsertAsync(Asset(fitOut, "Office fit-out", 12_000m, 24, new(2026, 1, 1)) with { RetiredOn = new DateOnly(2026, 2, 15) });
        await SeedAsync(FixedAssetDepreciationCalculation.CalculationKind, new());
        var fixture = await CreateFixtureAsync(assets: register);

        await fixture.Runner.RunDueAsync(Due(FebruaryEnd));
        await (await CreateFixtureAsync(assets: register)).Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1));

        var drafts = await fixture.Drafts.ListAsync(Fund, Book);
        drafts.Should().HaveCount(2);
        var january = drafts.Single(draft => draft.AccountingDate == JanuaryEnd);
        var february = drafts.Single(draft => draft.AccountingDate == FebruaryEnd);
        january.TotalDebits.Should().Be(1_500m);
        january.Lines.Should().HaveCount(4);
        january.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit)
            .Should().OnlyContain(line => line.AccountPath == "Expenses:Depreciation Expense");
        january.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit)
            .Should().OnlyContain(line => line.AccountPath == "Assets:Accumulated Depreciation");
        february.TotalDebits.Should().Be(1_000m, "the fit-out was retired before its February period end");
        february.Memo.Should().Contain("1 of 2 registered asset(s)");
    }

    [Fact]
    public async Task Depreciation_AssetRegisteredToAnotherBook_IsNotDepreciatedHere()
    {
        var register = new FileFixedAssetRegister(Path.Combine(_root, "registers"));
        await register.UpsertAsync(Asset(Guid.NewGuid(), "Other book asset", 36_000m, 36, new(2026, 1, 1)) with { LedgerBookId = Guid.NewGuid() });
        await SeedAsync(FixedAssetDepreciationCalculation.CalculationKind, new(), endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(assets: register);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
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
        // A EUR security bought at par and marked up in USD: a price move, not an FX exposure.
        var security = LedgerAccounts.Securities("SAP");
        var buy = Guid.NewGuid();
        _ledger.Post(new JournalEntry(buy, At(new(2026, 1, 12)), "Buy SAP",
        [
            new(Guid.NewGuid(), buy, At(new(2026, 1, 12)), security, 100m, 0m, "Buy SAP",
                currency: new LedgerEntryCurrency("EUR", "USD", 100m, 0m, 1m)),
            new(Guid.NewGuid(), buy, At(new(2026, 1, 12)), LedgerAccounts.Cash, 0m, 100m, "Buy SAP")
        ]));
        Post(new(2026, 1, 20), (security, 20m, 0m), (LedgerAccounts.RealizedGain, 0m, 20m));
        await SeedAsync(FxRevaluationCalculation.CalculationKind, new() { ["maxRateAgeDays"] = 3m });
        // The January quote is 28 days old by the end of February, so it cannot stand in for one.
        var quotes = new List<ReconciliationFxQuote> { new("EUR", "USD", 1.10m, JanuaryEnd) };
        var fixture = await CreateFixtureAsync(Rates(quotes));

        var results = await fixture.Runner.RunDueAsync(Due(FebruaryEnd));

        // February is not measured while January's revaluation is still an open draft.
        results.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .Blockers.Should().ContainSingle().Which.Should().Contain("post or reject it first");
        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        january.TotalDebits.Should().Be(2_000m, "100,000 EUR at 1.10 is 110,000 against 108,000 carried");
        january.Memo.Should().Contain("EUR/USD 1.1 dated 2026-01-31 (direct, test-rates rev-1)");
        january.Lines.Select(line => (line.AccountPath, line.Side, line.Amount)).Should().BeEquivalentTo(new[]
        {
            ("Assets:Cash EUR", AccountingTemplateLineSideDto.Debit, 2_000m),
            ("Revenue:Unrealized FX Gain", AccountingTemplateLineSideDto.Credit, 2_000m)
        });

        // Once January's revaluation is posted the account carries 110,000. February at 1.09
        // must draft a 1,000 loss. Workbench posts retain calculation provenance in their tags.
        _ledger.PostLines(At(JanuaryEnd), "January FX revaluation",
            [(EuroCash, 2_000m, 0m), (LedgerAccounts.UnrealizedFxGain, 0m, 2_000m)],
            new JournalEntryMetadata(ActivityType: "ManualJournalEntry", Tags: new Dictionary<string, string>
            {
                [RecurringJournalEvidenceGuard.EvidenceTag] = january.RecurringJournalEvidenceJson!,
                [RecurringJournalEvidenceGuard.DigestTag] = january.RecurringJournalEvidenceDigest!
            }));
        await fixture.Drafts.SaveAsync(january with { Status = ManualJournalEntryStatusDto.Posted, PostedAtUtc = Registered, PostedBy = "controller" });
        Post(new(2026, 2, 5), (EuroCash, 50m, 0m), (LedgerAccounts.Cash, 0m, 50m));
        var stale = await (await CreateFixtureAsync(Rates(quotes))).Runner.RunDueAsync(Due(FebruaryEnd).AddMinutes(30));
        stale.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .Blockers.Should().ContainSingle().Which.Should().Contain("dated 2026-01-31, 28 day(s)");
        quotes.Add(new("EUR", "USD", 1.09m, FebruaryEnd.AddDays(-1)));
        await (await CreateFixtureAsync(Rates(quotes))).Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1));

        var february = (await fixture.Drafts.ListAsync(Fund, Book)).Single(draft => draft.AccountingDate == FebruaryEnd);
        february.Lines.Select(line => (line.AccountPath, line.Side, line.Amount)).Should().BeEquivalentTo(new[]
        {
            ("Expenses:Unrealized FX Loss", AccountingTemplateLineSideDto.Debit, 1_000m),
            ("Assets:Cash EUR", AccountingTemplateLineSideDto.Credit, 1_000m)
        });
    }

    [Fact]
    public async Task FxRevaluation_CurrencyScopedCashAccount_IsRevalued()
    {
        // Cash (EUR) carries its currency as the account symbol; it is cash, not an instrument.
        var euroCash = LedgerAccounts.CashInCurrency("EUR");
        _ledger.PostLines(At(new(2026, 1, 10)), "Buy EUR",
        [
            (euroCash, 108_000m, 0m, null, new LedgerEntryCurrency("EUR", "USD", 100_000m, 0m, 1.08m)),
            (LedgerAccounts.InvestorCapitalFor("lp-1"), 0m, 108_000m, null, null)
        ]);
        await SeedAsync(FxRevaluationCalculation.CalculationKind, new() { ["maxRateAgeDays"] = 0m }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(Rates([new("EUR", "USD", 1.10m, JanuaryEnd)]));

        (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle()
            .Which.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Which.TotalDebits.Should().Be(2_000m);
    }

    [Fact]
    public async Task CalculatedLines_CarryTheScheduleDimensions()
    {
        await SeedAsync(FundExpenseAccrualCalculation.CalculationKind,
            new() { ["annualAmount"] = 36_500m, ["yearBasisDays"] = 365m }, new() { ["expenseId"] = "audit" }, endsOn: JanuaryEnd,
            dimensions: new LedgerLineDimensionSet(CostCenterId: "cc-ops"));
        var fixture = await CreateFixtureAsync();

        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));

        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.Lines.Should().OnlyContain(line => line.Dimensions != null && line.Dimensions.CostCenterId == "cc-ops");
    }

    [Fact]
    public async Task PerformanceFee_PreviousPeriodReversed_WaitsForTheReversalToPost()
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
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        // January was posted and then marked reversed; its reversal is still only a draft.
        await fixture.Drafts.SaveAsync(january with { Status = ManualJournalEntryStatusDto.Reversed });
        var reversal = january with
        {
            JournalEntryId = Guid.NewGuid(),
            Status = ManualJournalEntryStatusDto.Draft,
            ReversalOfJournalEntryId = january.JournalEntryId
        };
        await fixture.Drafts.SaveAsync(reversal);

        var waiting = await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd));
        waiting.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .Blockers.Should().ContainSingle().Which.Should().Contain("correction journal is not posted yet");

        await fixture.Drafts.SaveAsync(reversal with { Status = ManualJournalEntryStatusDto.Posted });
        var settled = await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1));
        settled.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
    }

    [Fact]
    public async Task ReversedOccurrence_IsBlockedInTheQueueUntilItsReversalIsPosted()
    {
        await SeedAsync(FundExpenseAccrualCalculation.CalculationKind,
            new() { ["annualAmount"] = 36_500m, ["yearBasisDays"] = 365m }, new() { ["expenseId"] = "audit" }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync();
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        await fixture.Drafts.SaveAsync(draft with { Status = ManualJournalEntryStatusDto.Reversed });
        var reversal = draft with
        {
            JournalEntryId = Guid.NewGuid(),
            Status = ManualJournalEntryStatusDto.Draft,
            ReversalOfJournalEntryId = draft.JournalEntryId
        };
        await fixture.Drafts.SaveAsync(reversal);

        var pending = (await fixture.Runner.GetQueueAsync(Fund, Book, Entity, tenantId: Tenant, companyId: Company)).Occurrences.Single();
        pending.State.Should().Be("Blocked");
        pending.Blockers.Should().ContainSingle().Which.Should().Contain("correction journal is not posted yet");

        await fixture.Drafts.SaveAsync(reversal with { Status = ManualJournalEntryStatusDto.Posted });
        var settled = (await fixture.Runner.GetQueueAsync(Fund, Book, Entity, tenantId: Tenant, companyId: Company)).Occurrences.Single();
        settled.State.Should().Be(nameof(RecurringOccurrenceState.Drafted));
        settled.ApprovalStatus.Should().Be(nameof(ManualJournalEntryStatusDto.Reversed));
        settled.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task FxRevaluation_FunctionalCashInSameAccount_DoesNotDraftAtAnUnchangedRate()
    {
        _ledger.PostLines(At(new(2026, 1, 10)), "Mixed-currency contribution",
        [
            (EuroCash, 110m, 0m, null, new LedgerEntryCurrency("EUR", "USD", 100m, 0m, 1.10m)),
            (EuroCash, 50m, 0m, null, new LedgerEntryCurrency("USD", "USD", 50m, 0m, 1m)),
            (LedgerAccounts.InvestorCapitalFor("lp-1"), 0m, 160m, null, null)
        ]);
        await SeedAsync(FxRevaluationCalculation.CalculationKind, new() { ["maxRateAgeDays"] = 0m }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(Rates([new("EUR", "USD", 1.10m, JanuaryEnd)]));

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FxRevaluation_RateWithoutQuoteDateOrSource_Blocks()
    {
        Capitalize(10_000_000m);
        var purchase = Guid.NewGuid();
        _ledger.Post(new JournalEntry(purchase, At(new(2026, 1, 10)), "Buy EUR",
        [
            new(Guid.NewGuid(), purchase, At(new(2026, 1, 10)), EuroCash, 108_000m, 0m, "Buy EUR",
                currency: new LedgerEntryCurrency("EUR", "USD", 100_000m, 0m, 1.08m)),
            new(Guid.NewGuid(), purchase, At(new(2026, 1, 10)), LedgerAccounts.Cash, 0m, 108_000m, "Buy EUR")
        ]));
        await SeedAsync(FxRevaluationCalculation.CalculationKind, new() { ["maxRateAgeDays"] = 3m }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(new UndatedRates());

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("quote date and source");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
        // The blocked occurrence still names its period, so close readiness for that period sees it.
        (await fixture.Runner.GetQueueAsync(Fund, Book, Entity, tenantId: Tenant, companyId: Company)).Occurrences
            .Should().ContainSingle().Which.PeriodId.Should().Be(January.ToString("D"));
    }

    [Fact]
    public async Task FixedIncome_YesterdayWithoutBookPosition_RefusesToUseTodaysOpenLots()
    {
        var today = JanuaryEnd.AddDays(1);
        var account = LedgerAccounts.Securities(Ticker);
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(value => value.ListOpenTaxLotsAsync(Book, account, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LedgerTaxLotRecord>());
        var inputs = new LedgerRecurringJournalCalculationInputs(store.Object, new FixedClock(At(today)));

        var backdatedRead = () => inputs.ListOpenLotsAsync(Scope, account, SecurityId, null, JanuaryEnd, default);

        await backdatedRead.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bookPositionId*");
        store.Verify(value => value.ListOpenTaxLotsAsync(Book, account, It.IsAny<CancellationToken>()), Times.Never);
        (await inputs.ListOpenLotsAsync(Scope, account, SecurityId, null, today, default)).Should().BeEmpty();
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

        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        await (await CreateFixtureAsync(lots: [lot], cashFlows: MonthlyCouponCashFlows())).Runner.RunDueAsync(Due(FebruaryEnd));

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
    public async Task FixedIncome_AccruesOnlyTheShareOfTheBondTheFundHolds_AndNothingOnceItIsSold()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker });
        // 250,000 of the 1,000,000 issue, bought at par on 16 January: a quarter of 5,000 for 15 of 31 days.
        var quarter = new LedgerTaxLot("lot-q", new(2026, 1, 16), 2_500m, 100m, SecurityId)
        { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = 100m };
        var held = await CreateFixtureAsync(lots: [quarter], cashFlows: MonthlyCouponCashFlows());

        await held.Runner.RunDueAsync(Due(JanuaryEnd));

        var january = (await held.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        january.Lines.Single(line => line.AccountPath == "Assets:Accrued Interest Receivable UST30").Amount.Should().Be(604.84m);

        // The position is sold before February: no lot, no entitlement, no draft.
        var sold = await CreateFixtureAsync(lots: [], cashFlows: MonthlyCouponCashFlows());
        var february = (await sold.Runner.RunDueAsync(Due(FebruaryEnd)))
            .Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd));
        february.State.Should().Be(AutomatedJournalScheduleStateDto.NoDraftRequired);
        (await sold.Drafts.ListAsync(Fund, Book)).Should().ContainSingle();
    }

    [Fact]
    public async Task PerformanceFee_TwoPeriodsDueTogether_SecondWaitsForTheFirstToPost()
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

        var results = await fixture.Runner.RunDueAsync(Due(FebruaryEnd));

        // Without the wait both months would draft the same 200,000.
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.AccountingDate.Should().Be(JanuaryEnd);
        results.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);

        // A rejected January fee was never owed, so February may then accrue the whole amount.
        await fixture.Drafts.SaveAsync(draft with { Status = ManualJournalEntryStatusDto.Rejected });
        await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd).AddHours(1));
        (await fixture.Drafts.ListAsync(Fund, Book)).Single(item => item.AccountingDate == FebruaryEnd).TotalDebits.Should().Be(200_000m);
    }

    [Fact]
    public async Task PerformanceFee_WaitsForTheEffectiveDayToEnd_SoSameDayGainsAreCounted()
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

        var early = (await fixture.Runner.RunDueAsync(At(JanuaryEnd))).Should().ContainSingle().Subject;
        early.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        early.Blockers.Should().ContainSingle().Which.Should().Contain("once that day has ended");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();

        // A gain posted later on the effective date is in the amount drafted the next morning.
        Post(JanuaryEnd, (LedgerAccounts.Cash, 1_000_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 1_000_000m));
        await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(JanuaryEnd));
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Which.TotalDebits.Should().Be(200_000m);
    }

    [Fact]
    public async Task PerformanceFee_PreviousPeriodPostedThenClosed_NextPeriodStillAccrues()
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
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
        var january = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        await fixture.Drafts.SaveAsync(january with { Status = ManualJournalEntryStatusDto.Posted });
        Post(JanuaryEnd, (new LedgerAccount("Performance Fee Expense", LedgerAccountType.Expense), 200_000m, 0m),
            (new LedgerAccount("Performance Fee Payable", LedgerAccountType.Liability), 0m, 200_000m));
        Post(new(2026, 2, 10), (LedgerAccounts.Cash, 500_000m, 0m), (LedgerAccounts.RealizedGain, 0m, 500_000m));

        // Closing January marks its occurrence blocked; its posted draft still settles it.
        _closedMonths.Add(1);
        var results = await (await CreateFixtureAsync()).Runner.RunDueAsync(Due(FebruaryEnd));

        results.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, JanuaryEnd))
            .State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        results.Single(result => result.RunKey == RecurringJournalIdentity.OccurrenceKey(ScheduleId, FebruaryEnd))
            .State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().Contain(item => item.AccountingDate == FebruaryEnd);
    }

    [Fact]
    public async Task Depreciation_CorruptRegister_BlocksThatOccurrenceWithoutStoppingTheRun()
    {
        Directory.CreateDirectory(Path.Combine(_root, "registers"));
        await File.WriteAllTextAsync(Path.Combine(_root, "registers", "fixed-asset-register.json"), "{ not json");
        await SeedAsync(FixedAssetDepreciationCalculation.CalculationKind, new(), endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync(assets: new FileFixedAssetRegister(Path.Combine(_root, "registers")));

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("could not complete");
        File.ReadAllText(Path.Combine(_root, "registers", "fixed-asset-register.json")).Should().Be("{ not json");
    }

    [Fact]
    public async Task FixedIncome_StaleProjection_BlocksWithTheGateReason()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker });
        var fixture = await CreateFixtureAsync(cashFlows: MonthlyCouponCashFlows(StructuredCashFlowStaleness.Stale));

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("cannot be posted");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FixedIncome_PositionSoldDuringTheWindow_AccruesForTheDaysItWasHeld()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Guid.NewGuid().ToString("D") },
            endsOn: JanuaryEnd);
        // The whole issue at par: half is sold on 11 January and the rest on 21 January.
        LedgerTaxLot Lot(decimal quantity) => new("lot-1", new(2025, 12, 31), quantity, 100m, SecurityId)
        { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = 100m };
        var fixture = await CreateFixtureAsync(cashFlows: MonthlyCouponCashFlows(), lotsOn: date =>
            date < new DateOnly(2026, 1, 11) ? [Lot(10_000m)] : date < new DateOnly(2026, 1, 21) ? [Lot(5_000m)] : []);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        // 5,000 a month: 10 days on the whole issue, then 10 days on half, then nothing.
        result.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        draft.TotalDebits.Should().Be(1_612.90m + 806.45m);
        draft.Memo.Should().Contain("holding changed during the window");
    }

    [Fact]
    public async Task FixedIncome_BondInAnotherCurrency_BlocksInsteadOfDraftingUnconvertedAmounts()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker });
        var lot = new LedgerTaxLot("lot-1", new(2025, 12, 31), 10_000m, 100m, SecurityId) { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = 100m };
        var fixture = await CreateFixtureAsync(lots: [lot], bondCurrency: "EUR");

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("denominated in EUR");
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task FixedIncome_CouponWithoutAnAccrualInterval_BlocksInsteadOfRecognizingItWhole()
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker });
        var service = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        service.Setup(value => value.GetProjectionAsOfAsync(SecurityId, StructuredCashFlowScenario.Base,
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken _) =>
                new StructuredCashFlowProjectionDto(securityId, StructuredCashFlowSourceKind.CalculatedBullet, scenario, asOf,
                    [new StructuredCashFlowScheduleEntry(new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero), 0m, 30_000m, 1m)],
                    StructuredCashFlowStaleness.Fresh, asOf,
                    TermsUsed: StructuredCashFlowTerms.Empty with
                    { MaturityDate = new DateOnly(2030, 12, 31), DayCountConvention = "ACT/365", PrincipalFace = 1_000_000m }));
        var lot = new LedgerTaxLot("lot-1", new(2025, 12, 31), 10_000m, 100m, SecurityId) { UnitKind = SecurityLotUnitKind.FaceValue, Multiplier = 100m };
        var fixture = await CreateFixtureAsync(lots: [lot], cashFlows: service.Object);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.Blocked);
        result.Blockers.Should().ContainSingle().Which.Should().Contain("no accrual interval");
    }

    [Fact]
    public async Task CalculatedDraft_PassesTheExistingSubmitAndApproveFlow_WithoutSelfApproval()
    {
        Capitalize(10_000_000m);
        await SeedAsync(ManagementFeeAccrualCalculation.CalculationKind,
            new() { ["annualRate"] = 0.02m, ["yearBasisDays"] = 365m }, endsOn: JanuaryEnd);
        var fixture = await CreateFixtureAsync();
        await fixture.Runner.RunDueAsync(Due(JanuaryEnd));
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

    // The worker's first run after the effective day has ended.
    private static DateTimeOffset Due(DateOnly date) => new(date.AddDays(1).ToDateTime(new TimeOnly(0, 30)), TimeSpan.Zero);

    private static string Evidence(string intent, Guid journalEntryId)
        => $"evidence://{intent}/journal/{journalEntryId:D}/ledger-book/{Book:D}/tenant/{Tenant}/company/{Company}";

    private async Task SeedAsync(string kind, Dictionary<string, decimal> terms,
        Dictionary<string, string>? identifiers = null, DateOnly? endsOn = null, LedgerLineDimensionSet? dimensions = null)
    {
        var store = new FileRecurringJournalStore(Path.Combine(_root, "recurring"));
        await store.InitializeAsync();
        await using var session = await store.OpenSessionAsync();
        await session.RegisterTemplateAsync(new JournalTemplate(TemplateId, "Calculated journal", "Calculated each period", [],
            calculation: new JournalTemplateCalculation(kind, identifiers)), "controller", 0, Registered);
        await session.RegisterScheduleAsync(new RecurringJournalSchedule(ScheduleId, TemplateId, new LedgerBookKey(Fund, "Fund"),
            RecurringJournalCadence.Monthly, JanuaryEnd, "controller", Registered, terms, endsOn: endsOn ?? FebruaryEnd,
                dimensions: dimensions),
            Scope, [Source()], "controller", 0, Registered);
    }

    private static FixedAssetRegisterEntry Asset(Guid id, string name, decimal cost, int months, DateOnly inService)
        => new(new FixedAssetRecord(id, name, "USD", cost, 0m, inService, months, DepreciationMethod.StraightLine),
            Tenant, Company, Fund, Book, "controller", Registered, "evidence://fixed-asset-invoice");

    private static ISecurityMasterQueryService Securities(string currency)
    {
        var service = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        service.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityDetailDto(SecurityId, "Bond", SecurityStatusDto.Active, Ticker, currency, default, default, [], [], 1,
                Registered, null));
        return service.Object;
    }

    private async Task<Fixture> CreateFixtureAsync(IReconciliationFxRateProvider? rates = null,
        IReadOnlyList<LedgerTaxLot>? lots = null, ISecurityMasterCashFlowService? cashFlows = null,
        IFixedAssetRegister? assets = null, string bondCurrency = "USD",
        Func<DateOnly, IReadOnlyList<LedgerTaxLot>>? lotsOn = null)
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
             new("eur-cash-scoped", "Assets:Cash (EUR)", LedgerAccounts.CashInCurrency("EUR").Name, "Asset", Symbol: "EUR"),
             new("fx-gain", "Revenue:Unrealized FX Gain", LedgerAccounts.UnrealizedFxGain.Name, "Revenue"),
             new("fx-loss", "Expenses:Unrealized FX Loss", LedgerAccounts.UnrealizedFxLoss.Name, "Expense"),
             new("bond", "Assets:Securities UST30", LedgerAccounts.Securities(Ticker).Name, "Asset", Symbol: Ticker),
             new("accrued-interest", "Assets:Accrued Interest Receivable UST30", LedgerAccounts.AccruedInterestReceivable(Ticker).Name, "Asset", Symbol: Ticker),
             new("coupon", "Revenue:Coupon Income", LedgerAccounts.CouponIncome.Name, "Revenue"),
             new("depreciation", "Expenses:Depreciation Expense", "Depreciation Expense", "Expense"),
             new("accumulated-depreciation", "Assets:Accumulated Depreciation", "Accumulated Depreciation", "Asset")],
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
        var inputs = new LedgerInputs(_ledger, lots ?? [], () => _ledgerReads++, lotsOn);
        var cashFlowService = cashFlows ?? MonthlyCouponCashFlows();
        var runner = new RecurringJournalRunner(store, intake, drafts, new Periods(_closedMonths),
        [
            new ManagementFeeAccrualCalculation(inputs),
            new PerformanceFeeAccrualCalculation(inputs),
            new FundExpenseAccrualCalculation(),
            new FixedAssetDepreciationCalculation(assets),
            new FixedIncomeAccrualCalculation(inputs,
                new SecurityMasterAmortizationLedgerBridge(cashFlowService, NullLogger<SecurityMasterAmortizationLedgerBridge>.Instance),
                cashFlowService, Securities(bondCurrency)),
            new FxRevaluationCalculation(inputs, rates ?? Rates([]))
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

    private sealed class Periods(HashSet<int> closedMonths) : IRecurringJournalPeriodAuthority
    {
        public Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
        {
            var id = date.Month == 1 ? January : February;
            var start = new DateOnly(date.Year, date.Month, 1);
            var closed = closedMonths.Contains(date.Month);
            return Task.FromResult(new RecurringJournalPeriodState(id.ToString("D"), 1, !closed, null, null, closed ? "The period is closed." : null,
                start, start.AddMonths(1).AddDays(-1)));
        }
    }

    private sealed class LedgerInputs(DomainLedger ledger, IReadOnlyList<LedgerTaxLot> lots, Action onLedgerRead,
        Func<DateOnly, IReadOnlyList<LedgerTaxLot>>? lotsOn = null) : IRecurringJournalCalculationInputs
    {
        public Task<IReadOnlyLedger> HydrateLedgerAsOfAsync(RecurringJournalScope scope, DateOnly asOf, CancellationToken ct)
        {
            onLedgerRead();
            return Task.FromResult<IReadOnlyLedger>(ledger);
        }

        public Task<IReadOnlyList<LedgerTaxLot>> ListOpenLotsAsync(RecurringJournalScope scope, LedgerAccount account, Guid securityId,
            Guid? bookPositionId, DateOnly asOf, CancellationToken ct)
            => Task.FromResult(lotsOn?.Invoke(asOf) ?? lots);
    }

    private static TableReconciliationFxRateProvider Rates(IEnumerable<ReconciliationFxQuote> quotes)
        => new(quotes.ToArray(), source: "test-rates rev-1");

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A rate source that can quote a number but not when or where it came from.</summary>
    private sealed class UndatedRates : IReconciliationFxRateProvider
    {
        public bool TryGetRate(string fromCurrency, string toCurrency, DateOnly asOf, out decimal rate)
        {
            rate = 1.10m;
            return true;
        }
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
                    TermsUsed: StructuredCashFlowTerms.Empty with
                    {
                        MaturityDate = new DateOnly(2026, 12, 31),
                        DayCountConvention = "ACT/365",
                        PrincipalFace = 1_000_000m
                    });
            });
        return service.Object;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
