using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Storage;

/// <summary>
/// Two accounting periods of calculated journals against the PostgreSQL ledger: every draft the
/// runner produces is compared with the golden file, approved and posted through the workbench,
/// and the second period is calculated from the first period's posted journals.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RecurringJournalCalculationPostgresTests
{
    private const string Fund = "golden-fund";
    private const string Tenant = "golden-tenant";
    private const string Company = "golden-company";
    private const string Entity = "golden-entity";
    private static readonly DateOnly JanuaryEnd = new(2026, 1, 31);
    private static readonly DateOnly FebruaryEnd = new(2026, 2, 28);
    private static readonly DateTimeOffset Registered = new(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly LedgerAccount EuroCash = LedgerAccounts.CashAccount("EUR-OPERATING");

    [LedgerDatabaseFact]
    public async Task TwoPeriods_DraftApproveAndPost_MatchTheGoldenFileWithoutPostingAnythingTwice()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), "meridian-recurring-golden-" + Guid.NewGuid().ToString("N"));
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        try
        {
            var store = database.JournalStore;
            var book = new LedgerBookRecord(Guid.NewGuid(), Fund, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Golden fund book", "USD", Registered, Registered);
            var scope = new RecurringJournalScope(Fund, book.LedgerBookId, Entity, "USD", Tenant, Company);
            LedgerAccountingPeriod december, january, february;
            using (FundScopeTenantAuthority.Enter(Tenant, "golden-fixture"))
            {
                await store.SaveLedgerBookAsync(book, ct);
                december = await SavePeriodAsync(store, book, 2025, 12, ct);
                january = await SavePeriodAsync(store, book, 2026, 1, ct);
                february = await SavePeriodAsync(store, book, 2026, 2, ct);
                // Capital in, 100,000 EUR bought at 1.08, and a 1,000,000 realized gain.
                await AppendAsync(store, book, december, new(2025, 12, 31), "Capital contribution",
                    (LedgerAccounts.Cash, 10_000_000m, 0m, null), (LedgerAccounts.InvestorCapitalFor("lp-1"), 0m, 10_000_000m, null));
                await AppendAsync(store, book, january, new(2026, 1, 10), "Buy EUR",
                    (EuroCash, 108_000m, 0m, new LedgerEntryCurrency("EUR", "USD", 100_000m, 0m, 1.08m)),
                    (LedgerAccounts.Cash, 0m, 108_000m, null));
                await AppendAsync(store, book, january, new(2026, 1, 20), "Realized gain",
                    (LedgerAccounts.Cash, 1_000_000m, 0m, null), (LedgerAccounts.RealizedGain, 0m, 1_000_000m, null));
            }

            var recurring = new FileRecurringJournalStore(Path.Combine(root, "recurring"));
            await recurring.InitializeAsync(ct);
            await using (var session = await recurring.OpenSessionAsync(ct))
            {
                await RegisterAsync(session, scope, "management-fee", ManagementFeeAccrualCalculation.CalculationKind,
                    new() { ["annualRate"] = 0.02m, ["yearBasisDays"] = 365m }, ct);
                await RegisterAsync(session, scope, "performance-fee", PerformanceFeeAccrualCalculation.CalculationKind,
                    new() { ["performanceFeeRate"] = 0.20m, ["highWaterMark"] = 10_000_000m, ["hurdleAmount"] = 0m, ["crystallize"] = 0m }, ct);
                await RegisterAsync(session, scope, "fx-revaluation", FxRevaluationCalculation.CalculationKind,
                    new() { ["maxRateAgeDays"] = 0m }, ct);
            }

            var fixture = await CreateFixtureAsync(root, database, book, recurring);
            var golden = LoadGolden();

            await fixture.Runner.RunDueAsync(Due(JanuaryEnd), ct: ct);
            var januaryDrafts = (await fixture.Drafts.ListAsync(Fund, book.LedgerBookId, ct)).ToArray();
            Project(januaryDrafts).Should().BeEquivalentTo(golden["2026-01-31"], options => options.WithStrictOrdering());
            (await store.GetByPeriodAsync(january.PeriodId, ct)).Should().HaveCount(2, "drafting never posts");

            foreach (var draft in januaryDrafts)
                await ApproveAndPostAsync(fixture.Workbench, draft, book.LedgerBookId, ct);
            var posted = await store.GetByPeriodAsync(january.PeriodId, ct);
            posted.Should().HaveCount(5);
            posted.Select(record => record.Entry.JournalEntryId).Should().Contain(januaryDrafts.Select(draft => draft.JournalEntryId));

            // A restarted host calculates February from the posted January journals, and
            // repeating the run creates nothing further.
            var restarted = await CreateFixtureAsync(root, database, book, recurring);
            await restarted.Runner.RunDueAsync(Due(FebruaryEnd), ct: ct);
            await restarted.Runner.RunDueAsync(Due(FebruaryEnd).AddHours(6), ct: ct);

            var all = await restarted.Drafts.ListAsync(Fund, book.LedgerBookId, ct);
            all.Should().HaveCount(6);
            Project(all.Where(draft => draft.AccountingDate == FebruaryEnd))
                .Should().BeEquivalentTo(golden["2026-02-28"], options => options.WithStrictOrdering());
            (await store.GetByPeriodAsync(january.PeriodId, ct)).Should().HaveCount(5);
            (await store.GetByPeriodAsync(february.PeriodId, ct)).Should().BeEmpty("February drafts still await approval");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [LedgerDatabaseFact]
    public async Task FixedIncomeAccrual_ReadsOpenFaceValueLotsFromPostgres_AndDraftsTheMonthsCouponAndAmortization()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), "meridian-recurring-bond-" + Guid.NewGuid().ToString("N"));
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        try
        {
            var store = database.JournalStore;
            var securityId = Guid.NewGuid();
            var book = new LedgerBookRecord(Guid.NewGuid(), Fund, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Golden bond book", "USD", Registered, Registered);
            var scope = new RecurringJournalScope(Fund, book.LedgerBookId, Entity, "USD", Tenant, Company);
            var carrying = LedgerAccounts.Securities("UST30");
            using (FundScopeTenantAuthority.Enter(Tenant, "golden-fixture"))
            {
                await store.SaveLedgerBookAsync(book, ct);
                await SavePeriodAsync(store, book, 2026, 1, ct);
                // 1,000,000 face at 101.20, maturing 2026-12-31: 12,000 of premium over 365 days.
                await store.SaveTaxLotAsync(new LedgerTaxLotRecord(Guid.NewGuid(), book.LedgerBookId, carrying, "lot-1",
                    new DateOnly(2025, 12, 31), 10_000m, 10_000m, 101.20m, "USD", Registered, Registered,
                    SecurityId: securityId, BookPositionId: Guid.NewGuid(), OriginalFace: 1_000_000m, BookedFactor: 1m, ParBasis: 100m), ct);
            }

            var recurring = new FileRecurringJournalStore(Path.Combine(root, "recurring"));
            await recurring.InitializeAsync(ct);
            await using (var session = await recurring.OpenSessionAsync(ct))
            {
                await session.RegisterTemplateAsync(new JournalTemplate("bond-template", "Bond accrual", "Calculated each period", [],
                    calculation: new JournalTemplateCalculation(FixedIncomeAccrualCalculation.CalculationKind,
                        new Dictionary<string, string> { ["securityId"] = securityId.ToString("D"), ["ticker"] = "UST30" })),
                    "controller", 0, Registered, ct);
                await session.RegisterScheduleAsync(new RecurringJournalSchedule("bond-accrual", "bond-template",
                    new LedgerBookKey(Fund, "Fund"), RecurringJournalCadence.Monthly, JanuaryEnd, "controller", Registered,
                    endsOn: JanuaryEnd), scope,
                    [new JournalEvidenceReference("bond-terms", "evidence://bond-terms", "security-terms", "document-vault",
                        Registered.AddDays(-1), "records-controller", ContentHash: new string('d', 64), EvidenceVersion: 1)],
                    "controller", 0, Registered, ct);
            }

            // One semi-annual coupon of 18,100 accruing 100 a day from 2025-12-31 to 2026-06-30.
            var cashFlows = new Mock<Meridian.Contracts.SecurityMaster.ISecurityMasterCashFlowService>(MockBehavior.Strict);
            cashFlows.Setup(service => service.GetProjectionAsOfAsync(securityId, Meridian.Contracts.SecurityMaster.StructuredCashFlowScenario.Base,
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Meridian.Contracts.SecurityMaster.StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken _) =>
                    new Meridian.Contracts.SecurityMaster.StructuredCashFlowProjectionDto(id,
                        Meridian.Contracts.SecurityMaster.StructuredCashFlowSourceKind.CalculatedBullet, scenario, asOf,
                        [new Meridian.Contracts.SecurityMaster.StructuredCashFlowScheduleEntry(
                            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), 0m, 18_100m, 1m)
                        { AccrualStart = new DateOnly(2025, 12, 31), AccrualEnd = new DateOnly(2026, 6, 30) }],
                        Meridian.Contracts.SecurityMaster.StructuredCashFlowStaleness.Fresh, asOf,
                        TermsUsed: Meridian.Contracts.SecurityMaster.StructuredCashFlowTerms.Empty with
                        { MaturityDate = new DateOnly(2026, 12, 31), DayCountConvention = "ACT/365", PrincipalFace = 1_000_000m }));

            var securities = new Mock<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(MockBehavior.Strict);
            securities.Setup(service => service.GetByIdAsync(securityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Meridian.Contracts.SecurityMaster.SecurityDetailDto(securityId, "Bond",
                    Meridian.Contracts.SecurityMaster.SecurityStatusDto.Active, "UST30", "USD", default, default, [], [], 1,
                    DateTimeOffset.UnixEpoch, null));

            var fixture = await CreateFixtureAsync(root, database, book, recurring, inputs => new FixedIncomeAccrualCalculation(inputs,
                new Meridian.Application.SecurityMaster.SecurityMasterAmortizationLedgerBridge(cashFlows.Object,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<Meridian.Application.SecurityMaster.SecurityMasterAmortizationLedgerBridge>.Instance),
                cashFlows.Object, securities.Object));

            await fixture.Runner.RunDueAsync(Due(JanuaryEnd), ct: ct);

            var draft = (await fixture.Drafts.ListAsync(Fund, book.LedgerBookId, ct)).Should().ContainSingle().Subject;
            draft.Lines.Select(line => (line.AccountPath, line.Side.ToString(), line.Amount)).Should().BeEquivalentTo(new[]
            {
                // 31 days of coupon at 100 a day; 12,000 x 31/365 of premium written down against income.
                ("Assets:Accrued Interest Receivable UST30", "Debit", 3_100m),
                ("Revenue:Coupon Income", "Credit", 3_100m),
                ("Revenue:Coupon Income", "Debit", 1_019.18m),
                ("Assets:Securities UST30", "Credit", 1_019.18m)
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ApproveAndPostAsync(ManualJournalEntryWorkbenchService workbench,
        ManualJournalEntryDraftDto draft, Guid bookId, CancellationToken ct)
    {
        string Evidence(string intent) =>
            $"evidence://{intent}/journal/{draft.JournalEntryId:D}/ledger-book/{bookId:D}/tenant/{Tenant}/company/{Company}";
        var submitted = await workbench.ApplyLifecycleActionAsync(new(draft.JournalEntryId, Fund,
            JournalEntryLifecycleActionDto.Submit, "fund-accountant", draft.Version,
            Notes: "Submit the calculated period journal for independent review.",
            EvidenceLinks: [Evidence("review")], LedgerBookId: bookId, TenantId: Tenant, CompanyId: Company), ct);
        var approved = await workbench.ApplyLifecycleActionAsync(new(draft.JournalEntryId, Fund,
            JournalEntryLifecycleActionDto.Approve, "independent-controller", submitted.JournalEntry.Version,
            Notes: "Approve after agreeing the calculation inputs to the ledger.",
            EvidenceLinks: [Evidence("approval")], LedgerBookId: bookId, TenantId: Tenant, CompanyId: Company), ct);
        var posted = await workbench.ApplyLifecycleActionAsync(new(draft.JournalEntryId, Fund,
            JournalEntryLifecycleActionDto.Post, "independent-controller", approved.JournalEntry.Version,
            Notes: "Post the approved calculated journal.",
            EvidenceLinks: [Evidence("posting")], LedgerBookId: bookId, TenantId: Tenant, CompanyId: Company), ct);
        posted.JournalEntry.Status.Should().Be(ManualJournalEntryStatusDto.Posted);
    }

    private static IReadOnlyList<GoldenDraft> Project(IEnumerable<ManualJournalEntryDraftDto> drafts)
        => drafts.OrderBy(draft => draft.Memo, StringComparer.Ordinal)
            .Select(draft => new GoldenDraft(draft.Memo ?? "", draft.Status.ToString(), draft.Lines
                .Select(line => new GoldenLine(line.AccountPath, line.Side.ToString(), line.Amount)).ToArray()))
            .ToArray();

    private static Dictionary<string, GoldenDraft[]> LoadGolden()
    {
        const string name = "calculated-period-drafts.json";
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "recurring-journal", "golden", name);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); !File.Exists(path) && directory is not null; directory = directory.Parent)
            path = Path.Combine(directory.FullName, "tests", "fixtures", "recurring-journal", "golden", name);
        return JsonSerializer.Deserialize<Dictionary<string, GoldenDraft[]>>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task<Fixture> CreateFixtureAsync(string root, LedgerPostgresTestDatabase database,
        LedgerBookRecord book, FileRecurringJournalStore recurring,
        Func<IRecurringJournalCalculationInputs, IRecurringJournalCalculation>? extraCalculation = null)
    {
        var configuration = new InMemoryAccountingConfigurationStore();
        await configuration.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, book.LedgerBookId,
            AccountingConfigurationStatusDto.Draft, "golden", Registered, [],
            [new("mgmt-expense", "Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
             new("mgmt-payable", "Liabilities:Management Fee Payable", "Management Fee Payable", "Liability"),
             new("perf-expense", "Expenses:Performance Fee Expense", "Performance Fee Expense", "Expense"),
             new("perf-payable", "Liabilities:Performance Fee Payable", "Performance Fee Payable", "Liability"),
             new("eur-cash", "Assets:Cash EUR", EuroCash.Name, "Asset", FinancialAccountId: EuroCash.FinancialAccountId),
             new("fx-gain", "Revenue:Unrealized FX Gain", LedgerAccounts.UnrealizedFxGain.Name, "Revenue"),
             new("fx-loss", "Expenses:Unrealized FX Loss", LedgerAccounts.UnrealizedFxLoss.Name, "Expense"),
             new("bond", "Assets:Securities UST30", LedgerAccounts.Securities("UST30").Name, "Asset", Symbol: "UST30"),
             new("accrued-interest", "Assets:Accrued Interest Receivable UST30", LedgerAccounts.AccruedInterestReceivable("UST30").Name, "Asset", Symbol: "UST30"),
             new("coupon", "Revenue:Coupon Income", LedgerAccounts.CouponIncome.Name, "Revenue")],
            [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var audit = new FileAccountingConfigurationStore(Path.Combine(root, "audit.json"));
        var configurationService = new AccountingConfigurationService(configuration, audit);
        var drafts = new FileManualJournalEntryDraftStore(Path.Combine(root, "drafts.json"));
        // A new store instance per fixture stands in for a restarted host.
        var journalStore = new PostgresLedgerJournalStore(database.Options);
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configurationService, audit,
            journalStore: journalStore, recurringStore: recurring);
        var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configurationService);
        var tenancy = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        tenancy.Setup(registry => registry.ResolveAsync(Fund, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(Fund, Tenant, Company));
        // The lot read runs "on" the accrual date; a later run without a book position is refused.
        var inputs = new LedgerRecurringJournalCalculationInputs(journalStore, new FixedClock(At(JanuaryEnd)));
        var runner = new RecurringJournalRunner(recurring, intake, drafts,
            new RecurringJournalPeriodAuthority(journalStore, tenancy.Object, new VerifiedSubject()),
        [
            .. extraCalculation is null ? Array.Empty<IRecurringJournalCalculation>() : [extraCalculation(inputs)],
            new ManagementFeeAccrualCalculation(inputs),
            new PerformanceFeeAccrualCalculation(inputs),
            new FxRevaluationCalculation(inputs, new TableReconciliationFxRateProvider(
            [
                new ReconciliationFxQuote("EUR", "USD", 1.10m, JanuaryEnd),
                new ReconciliationFxQuote("EUR", "USD", 1.09m, FebruaryEnd)
            ], source: "golden-rates rev-1"))
        ]);
        return new(runner, drafts, workbench);
    }

    private static async Task RegisterAsync(IRecurringJournalSession session, RecurringJournalScope scope, string id,
        string kind, Dictionary<string, decimal> terms, CancellationToken ct)
    {
        await session.RegisterTemplateAsync(new JournalTemplate($"{id}-template", id, "Calculated each period", [],
            calculation: new JournalTemplateCalculation(kind)), "controller", 0, Registered, ct);
        await session.RegisterScheduleAsync(new RecurringJournalSchedule(id, $"{id}-template", new LedgerBookKey(Fund, "Fund"),
            RecurringJournalCadence.Monthly, JanuaryEnd, "controller", Registered, terms, endsOn: FebruaryEnd), scope,
            [new JournalEvidenceReference("fund-terms-2026", "evidence://fund-terms-2026", "fund-terms", "document-vault",
                Registered.AddDays(-1), "records-controller", ContentHash: new string('c', 64), EvidenceVersion: 1)],
            "controller", 0, Registered, ct);
    }

    private static Task<LedgerAccountingPeriod> SavePeriodAsync(PostgresLedgerJournalStore store, LedgerBookRecord book,
        int year, int month, CancellationToken ct)
    {
        var start = new DateOnly(year, month, 1);
        // Fixed period identities keep the golden memos stable; each run has its own schema.
        return store.SavePeriodAsync(new LedgerAccountingPeriod(Guid.Parse($"00000000-0000-4000-8000-0000{year:D4}{month:D4}"), book.LedgerBookId, year, month,
            start.ToString("MMMM yyyy"), start, start.AddMonths(1).AddDays(-1), "Open", Registered, null, 0)
        { MutationActor = "period-controller" }, 0, ct: ct);
    }

    private static Task AppendAsync(PostgresLedgerJournalStore store, LedgerBookRecord book, LedgerAccountingPeriod period,
        DateOnly date, string description, params (LedgerAccount account, decimal debit, decimal credit, LedgerEntryCurrency? currency)[] lines)
    {
        var id = Guid.NewGuid();
        var at = At(date);
        return store.AppendAsync(new LedgerJournalEntryWrite(new JournalEntry(id, at, description,
            lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, at, line.account, line.debit, line.credit, description,
                currency: line.currency)).ToArray()), book.LedgerBookId, period.PeriodId, LedgerBookId: book.LedgerBookId));
    }

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    private static DateTimeOffset Due(DateOnly date) => new(date.AddDays(1).ToDateTime(new TimeOnly(0, 30)), TimeSpan.Zero);

    private sealed record GoldenLine(string AccountPath, string Side, decimal Amount);

    private sealed record GoldenDraft(string Memo, string Status, GoldenLine[] Lines);

    private sealed record Fixture(RecurringJournalRunner Runner, FileManualJournalEntryDraftStore Drafts,
        ManualJournalEntryWorkbenchService Workbench);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class VerifiedSubject : IRecurringJournalSubjectAuthority
    {
        public Task VerifyAsync(RecurringJournalScope scope, LedgerBookRecord book, DateOnly date, CancellationToken ct)
            => Task.CompletedTask;
    }
}
