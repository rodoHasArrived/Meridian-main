using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Tests the automated journal event producers and the runner that lands their output
/// in the manual journal workbench queue: corporate-action dividends from the Security
/// Master and fee-schedule accruals from fund fee terms.
/// </summary>
public sealed class AutomatedJournalEventProducerTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 07, 05, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AaplSecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // -------------------------------------------------------------------------
    // FeeScheduleAccrualEventProducer
    // -------------------------------------------------------------------------

    [Fact]
    public void FeeProducer_MatchesPartnershipProjectorConventions()
    {
        var producer = new FeeScheduleAccrualEventProducer();

        var production = producer.Produce(new FeeScheduleAccrualRequest(
            FundId: "fund-alpha",
            PeriodId: "2026-Q2",
            AsOf,
            BeginningNav: 1_000_000m,
            EndingNavBeforeFees: 1_100_000m,
            HighWaterMark: 1_050_000m,
            ManagementFeeRate: 0.02m,
            PerformanceFeeRate: 0.20m));

        production.Skipped.Should().BeEmpty();
        production.Events.Should().HaveCount(2);
        var partnershipProjection = PartnershipInvestorAccountingProjector.Project(
            new PartnershipInvestorAllocationInput(
                "fund-alpha",
                "2026-Q2",
                AsOf,
                BeginningNav: 1_000_000m,
                EndingNavBeforeFees: 1_100_000m,
                HighWaterMark: 1_050_000m,
                ManagementFeeRate: 0.02m,
                PerformanceFeeRate: 0.20m,
                [new PartnershipInvestor("fund-alpha-investor", "Fund Alpha investor", 1m)]));

        var management = production.Events.Single(e => e.Kind == AutomatedJournalEventKind.ManagementFeeAccrued);
        management.Amount.Should().Be(partnershipProjection.ManagementFee,
            "the scheduler and partnership projector must use one management-fee convention");
        management.Symbol.Should().Be("fund-alpha", "the draft projector normalizes symbols downstream");
        management.IdempotencyKey.Should().Be("mgmt-fee|fund-alpha|2026-Q2");

        var performance = production.Events.Single(e => e.Kind == AutomatedJournalEventKind.PerformanceFeeAccrued);
        performance.Amount.Should().Be(partnershipProjection.PerformanceFee,
            "the scheduler and partnership projector must use the same high-water-mark excess net of management fees");
        performance.IdempotencyKey.Should().Be("perf-fee|fund-alpha|2026-Q2");
    }

    [Fact]
    public void FeeProducer_BelowHighWaterMark_AccruesNoPerformanceFee()
    {
        var producer = new FeeScheduleAccrualEventProducer();

        var production = producer.Produce(new FeeScheduleAccrualRequest(
            "fund-alpha", "2026-Q2", AsOf,
            BeginningNav: 1_000_000m,
            EndingNavBeforeFees: 1_020_000m,
            HighWaterMark: 1_200_000m,
            ManagementFeeRate: 0.02m,
            PerformanceFeeRate: 0.20m));

        production.Events.Should().ContainSingle()
            .Which.Kind.Should().Be(AutomatedJournalEventKind.ManagementFeeAccrued);
    }

    [Fact]
    public void FeeProducer_ZeroRates_ProducesNoEvents()
    {
        var producer = new FeeScheduleAccrualEventProducer();

        var production = producer.Produce(new FeeScheduleAccrualRequest(
            "fund-alpha", "2026-Q2", AsOf,
            BeginningNav: 1_000_000m,
            EndingNavBeforeFees: 1_100_000m,
            HighWaterMark: 0m,
            ManagementFeeRate: 0m,
            PerformanceFeeRate: 0m));

        production.Events.Should().BeEmpty("zero-rate schedules accrue nothing, which is correct, not a gap");
    }

    // -------------------------------------------------------------------------
    // CorporateActionDividendEventProducer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DividendProducer_EmitsEffectiveInWindowDividends()
    {
        var inWindow = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 02), 0.26m);
        var outOfWindow = DividendAction(AaplSecurityId, new DateOnly(2026, 05, 01), 0.25m);
        var cancelled = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 03), 0.10m) with
        {
            LifecycleState = CorporateActionLifecycleStates.Cancelled
        };
        var supersededOriginal = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 04), 0.20m);
        var supersedingTip = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 04), 0.25m) with
        {
            SupersedesCorpActId = supersededOriginal.CorpActId
        };

        var securityMaster = new FakeSecurityMasterQueryService(
            tickerToSecurityId: new Dictionary<string, Guid> { ["AAPL"] = AaplSecurityId },
            corporateActions: [inWindow, outOfWindow, cancelled, supersededOriginal, supersedingTip]);
        var producer = new CorporateActionDividendEventProducer(securityMaster);

        var production = await producer.ProduceAsync(new CorporateActionDividendRequest(
            [new DividendAccrualPosition("AAPL", Quantity: 400m)],
            Currency: "USD",
            WindowStart: new DateOnly(2026, 07, 01),
            WindowEnd: new DateOnly(2026, 07, 31),
            AsOf));

        production.Skipped.Should().BeEmpty();
        production.Events.Should().HaveCount(2, "out-of-window, cancelled, and superseded actions must not accrue");
        production.Events.Should().OnlyContain(e => e.Kind == AutomatedJournalEventKind.DividendDeclared);
        production.Events.Should().Contain(e =>
            e.Amount == 104.00m && e.SourceEventId == inWindow.CorpActId.ToString("N"));
        production.Events.Should().Contain(e =>
            e.Amount == 100.00m && e.SourceEventId == supersedingTip.CorpActId.ToString("N"),
            "the amendment chain must collapse to the superseding tip's terms");
        production.Events.Should().OnlyContain(e =>
            e.SecurityId == AaplSecurityId && e.IdempotencyKey!.StartsWith("corp-act-dividend|"));
    }

    [Fact]
    public async Task DividendProducer_UnresolvedTicker_SurfacesSkipAndContinues()
    {
        var dividend = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 02), 0.26m);
        var securityMaster = new FakeSecurityMasterQueryService(
            tickerToSecurityId: new Dictionary<string, Guid> { ["AAPL"] = AaplSecurityId },
            corporateActions: [dividend]);
        var producer = new CorporateActionDividendEventProducer(securityMaster);

        var production = await producer.ProduceAsync(new CorporateActionDividendRequest(
            [
                new DividendAccrualPosition("UNKNOWN", 10m),
                new DividendAccrualPosition("AAPL", 100m)
            ],
            "USD",
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            AsOf));

        production.Skipped.Should().ContainSingle().Subject.Subject.Should().Be("UNKNOWN");
        production.Events.Should().ContainSingle("one unresolved ticker must not block the rest of the batch");
    }

    [Fact]
    public async Task DividendProducer_WithholdingRate_AccruesPairedWithholdingTax()
    {
        var dividend = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 02), 0.26m);
        var securityMaster = new FakeSecurityMasterQueryService(
            tickerToSecurityId: new Dictionary<string, Guid> { ["AAPL"] = AaplSecurityId },
            corporateActions: [dividend]);
        var producer = new CorporateActionDividendEventProducer(securityMaster);

        var production = await producer.ProduceAsync(new CorporateActionDividendRequest(
            [new DividendAccrualPosition("AAPL", Quantity: 400m)],
            "USD",
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            AsOf,
            WithholdingTaxRate: 0.15m));

        production.Skipped.Should().BeEmpty();
        production.Events.Should().HaveCount(2);

        var declared = production.Events.Single(e => e.Kind == AutomatedJournalEventKind.DividendDeclared);
        declared.Amount.Should().Be(104.00m);

        var withholding = production.Events.Single(e => e.Kind == AutomatedJournalEventKind.WithholdingTaxAccrued);
        withholding.Amount.Should().Be(15.60m, "withholding is the rate applied to the declared dividend amount");
        withholding.SourceEventId.Should().Be(dividend.CorpActId.ToString("N"));
        withholding.EffectiveDate.Should().Be(declared.EffectiveDate);
        withholding.IdempotencyKey.Should().Be(
            FormattableString.Invariant($"corp-act-dividend-wht|{dividend.CorpActId:N}|-"),
            "the withholding idempotency key must differ from the dividend key so both drafts intake");
        withholding.EvidenceReferences.Should().NotBeEmpty("withholding inherits the corporate-action evidence");
    }

    [Fact]
    public async Task DividendProducer_ZeroWithholdingRate_ProducesNoWithholdingEvents()
    {
        var dividend = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 02), 0.26m);
        var securityMaster = new FakeSecurityMasterQueryService(
            tickerToSecurityId: new Dictionary<string, Guid> { ["AAPL"] = AaplSecurityId },
            corporateActions: [dividend]);
        var producer = new CorporateActionDividendEventProducer(securityMaster);

        var production = await producer.ProduceAsync(new CorporateActionDividendRequest(
            [new DividendAccrualPosition("AAPL", 400m)],
            "USD",
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            AsOf));

        production.Events.Should().OnlyContain(e => e.Kind == AutomatedJournalEventKind.DividendDeclared);
    }

    [Fact]
    public async Task DividendProducer_MismatchedCorporateActionCurrency_IsSkippedExactly()
    {
        var dividend = DividendAction(AaplSecurityId, new DateOnly(2026, 07, 02), 0.26m) with
        {
            Currency = "EUR"
        };
        var securityMaster = new FakeSecurityMasterQueryService(
            tickerToSecurityId: new Dictionary<string, Guid> { ["AAPL"] = AaplSecurityId },
            corporateActions: [dividend]);
        var producer = new CorporateActionDividendEventProducer(securityMaster);

        var production = await producer.ProduceAsync(new CorporateActionDividendRequest(
            [new DividendAccrualPosition("AAPL", 400m)],
            "USD",
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            AsOf));

        production.Events.Should().BeEmpty();
        production.Skipped.Should().ContainSingle().Which.Reason
            .Contains("currency", StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue();
    }

    [Fact]
    public async Task DividendProducer_InvalidWithholdingRate_Throws()
    {
        var securityMaster = new FakeSecurityMasterQueryService(
            tickerToSecurityId: new Dictionary<string, Guid>(),
            corporateActions: []);
        var producer = new CorporateActionDividendEventProducer(securityMaster);

        var act = () => producer.ProduceAsync(new CorporateActionDividendRequest(
            [new DividendAccrualPosition("AAPL", 400m)],
            "USD",
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            AsOf,
            WithholdingTaxRate: 1m));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // AutomatedJournalIntakeRunner — producers land drafts in the queue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Runner_FeeAccrual_LandsDraftsInWorkbenchQueue()
    {
        var fixture = CreateIntakeFixture(tenantId: "tenant-alpha", companyId: "company-alpha");
        var resolver = new StubCapitalAccountReconciliationResolver(FeeReconciliation());
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            capitalAccountReconciliationResolver: resolver,
            timeProvider: new FixedTimeProvider(AsOf));

        var result = await runner.RunFeeAccrualIntakeAsync(new RunFeeAccrualDraftIntakeRequest(
            FundProfileId: "fund-alpha",
            Currency: "USD",
            Actor: "automated-journal",
            PeriodId: "2026-Q2",
            BeginningNav: 1_000_000m,
            EndingNavBeforeFees: 1_100_000m,
            HighWaterMark: 1_050_000m,
            ManagementFeeRate: 0.02m,
            PerformanceFeeRate: 0.20m,
            LedgerBookId: BookId,
            EntityId: "entity-alpha",
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            EvidenceLinks: ["evidence://client/forged-ready-assertion"],
            EvidenceRetainedAtUtc: AsOf,
            CapitalAccountReconciliation: FeeReconciliation(confidence: 0.01m)));

        result.ProducerSkips.Should().BeEmpty();
        result.Intake.Created.Should().HaveCount(2);
        result.Intake.Created.Should().OnlyContain(draft => draft.Status == ManualJournalEntryStatusDto.Draft);
        result.Intake.Created.Should().OnlyContain(draft =>
            !draft.EvidenceLinks.Contains("evidence://client/forged-ready-assertion"));
        resolver.Scopes.Should().ContainSingle().Which.Should().Be(new AutomatedJournalCapitalAccountReconciliationScope(
            "tenant-alpha",
            "company-alpha",
            "fund-alpha",
            BookId,
            "entity-alpha",
            "2026-Q2",
            "USD",
            AsOf));
        result.EvidenceAssessments.Values.Should().OnlyContain(assessment =>
            assessment.ConfidenceScore == 0.98m &&
            assessment.EvidenceLinks.Contains("evidence://capital-accounts/fund-alpha/2026-Q2/v42"));

        var workbench = await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha");
        workbench.Drafts.Should().HaveCount(2, "fee accrual drafts must be visible in the close cockpit's queue");
    }

    [Fact]
    public async Task Runner_FeeAccrual_WithoutReviewedCapitalAccountEvidence_FailsClosed()
    {
        var fixture = CreateIntakeFixture(tenantId: "tenant-alpha", companyId: "company-alpha");
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            timeProvider: new FixedTimeProvider(AsOf));

        var result = await runner.RunFeeAccrualIntakeAsync(FeeIntakeRequest() with
        {
            CapitalAccountReconciliation = FeeReconciliation()
        });

        result.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        result.ReadinessBlockers.Should().Contain(item =>
            item.Contains("source is unavailable", StringComparison.OrdinalIgnoreCase));
        result.Intake.Created.Should().BeEmpty();
        (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Runner_FeeAccrual_ClientCannotLowerServerConfidenceOrVarianceBounds()
    {
        var fixture = CreateIntakeFixture(tenantId: "tenant-alpha", companyId: "company-alpha");
        var resolver = new StubCapitalAccountReconciliationResolver(FeeReconciliation(confidence: 0.80m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            capitalAccountReconciliationResolver: resolver,
            timeProvider: new FixedTimeProvider(AsOf));

        var lowConfidence = await runner.RunFeeAccrualIntakeAsync(FeeIntakeRequest() with
        {
            CapitalAccountReconciliation = FeeReconciliation(),
            MinimumCapitalAccountConfidence = 0m
        });
        resolver.Reconciliation = FeeReconciliation(
            maximumVarianceTolerance: 100m,
            capitalAccountOpeningBalance: 999_999.98m);
        var looseTolerance = await runner.RunFeeAccrualIntakeAsync(FeeIntakeRequest() with
        {
            CapitalAccountReconciliation = FeeReconciliation()
        });

        lowConfidence.Readiness.Should().Be(AutomatedJournalIntakeReadiness.NeedsInvestigation);
        lowConfidence.ReadinessBlockers.Should().Contain(item =>
            item.Contains("server-governed", StringComparison.OrdinalIgnoreCase) &&
            item.Contains("90", StringComparison.Ordinal) &&
            item.Contains("threshold", StringComparison.OrdinalIgnoreCase));
        looseTolerance.Readiness.Should().Be(AutomatedJournalIntakeReadiness.NeedsInvestigation);
        looseTolerance.ReadinessBlockers.Should().Contain(item =>
            item.Contains("server-governed tolerance 0.01", StringComparison.OrdinalIgnoreCase));
        (await fixture.Workbench.GetWorkbenchAsync(
            "fund-alpha", BookId, tenantId: "tenant-alpha", companyId: "company-alpha")).Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Runner_DividendIntake_WithoutSecurityMaster_FailsLoudly()
    {
        var fixture = CreateIntakeFixture();
        var runner = new AutomatedJournalIntakeRunner(fixture.Intake, new FeeScheduleAccrualEventProducer());

        var act = () => runner.RunDividendIntakeAsync(new RunDividendDraftIntakeRequest(
            "fund-alpha", "USD", "automated-journal",
            [new DividendAccrualPosition("AAPL", 100m)],
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Security Master query service*");
    }

    [Fact]
    public async Task Runner_DividendIntake_LandsDeclaredDividendDraft()
    {
        var fixture = CreateIntakeFixture();
        var securityMaster = new FakeSecurityMasterQueryService(
            new Dictionary<string, Guid> { ["AAPL"] = AaplSecurityId },
            [DividendAction(AaplSecurityId, new DateOnly(2026, 07, 02), 0.26m)]);
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            new CorporateActionDividendEventProducer(securityMaster));

        var result = await runner.RunDividendIntakeAsync(new RunDividendDraftIntakeRequest(
            "fund-alpha", "USD", "automated-journal",
            [new DividendAccrualPosition("AAPL", 400m)],
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            LedgerBookId: BookId,
            PeriodId: "2026-07",
            EntityId: "entity-alpha"));

        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.EntryType.Should().Be(ManualJournalEntryTypeDto.AccruedBalance);
        draft.TotalDebits.Should().Be(104.00m);
        draft.Lines.Should().Contain(line => line.AccountPath == "Assets:Dividend Receivable");
        draft.Lines.Should().Contain(line => line.AccountPath == "Income:Dividend Income");
    }

    // -------------------------------------------------------------------------
    // Fixture
    // -------------------------------------------------------------------------

    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed record IntakeFixture(
        AutomatedJournalDraftIntakeService Intake,
        IManualJournalEntryWorkbenchService Workbench,
        IManualJournalEntryDraftStore DraftStore);

    private static IntakeFixture CreateIntakeFixture(
        ILedgerJournalStore? journalStore = null,
        IManualJournalEntryDraftStore? retainedDraftStore = null,
        string? tenantId = null,
        string? companyId = null)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
            "fund-alpha",
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                Node("Assets:Cash", "Cash", "Asset"),
                Node("Assets:Dividend Receivable", "Dividend Receivable", "Asset"),
                Node("Income:Dividend Income", "Dividend Income", "Revenue"),
                Node("Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
                Node("Liabilities:Management Fee Payable", "Management Fee Payable", "Liability"),
                Node("Expenses:Performance Fee Expense", "Performance Fee Expense", "Expense"),
                Node("Liabilities:Performance Fee Payable", "Performance Fee Payable", "Liability"),
                Node("Equity:Retained Earnings", "Retained Earnings", "Equity")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [],
            TenantId: tenantId,
            CompanyId: companyId)).GetAwaiter().GetResult();

        var configurationService = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = retainedDraftStore ?? new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            new InMemoryAccountingActionAuditStore(),
            journalStore: journalStore);
        return new IntakeFixture(
            new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService),
            workbench,
            draftStore);
    }

    private sealed class FailOnceCorrectionDraftStore : IManualJournalEntryDraftStore
    {
        private readonly InMemoryManualJournalEntryDraftStore _inner = new();

        public bool FailNextBatch { get; set; }

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
            => _inner.ListFundProfileIdsAsync(ct);

        public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
            string fundProfileId,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => _inner.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);

        public Task<ManualJournalEntryDraftDto?> GetAsync(
            string fundProfileId,
            Guid journalEntryId,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => _inner.GetAsync(fundProfileId, journalEntryId, ct, tenantId, companyId);

        public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
            => _inner.SaveAsync(draft, ct);

        public Task SaveBatchAsync(
            IReadOnlyList<ManualJournalEntryDraftDto> drafts,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailNextBatch)
            {
                FailNextBatch = false;
                throw new IOException("Injected closing-reversal batch failure.");
            }

            return _inner.SaveBatchAsync(drafts, ct);
        }
    }

    // -------------------------------------------------------------------------
    // AutomatedJournalIntakeRunner — period-close closing entries
    // -------------------------------------------------------------------------

    private static readonly Guid ClosedPeriodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly PeriodEndDate = new(2026, 6, 30);

    private static ILedgerBookService LedgerBookServiceWithClosedPeriod(
        params LedgerPeriodTrialBalanceLineDto[] trialBalance)
        => LedgerBookServiceWithClosedPeriod(BookId, trialBalance);

    private static ILedgerBookService LedgerBookServiceWithClosedPeriod(
        Guid ledgerBookId,
        params LedgerPeriodTrialBalanceLineDto[] trialBalance)
    {
        var summary = new LedgerPeriodSummaryDto(
            PeriodId: ClosedPeriodId,
            LedgerBookId: ledgerBookId,
            FiscalYear: 2026,
            PeriodNo: 6,
            Label: "2026-06",
            TrialBalance: trialBalance,
            TotalDebits: trialBalance.Sum(line => line.DebitTotal),
            TotalCredits: trialBalance.Sum(line => line.CreditTotal),
            NetIncome: 0m,
            PeriodOnPeriodVariance: null,
            OpenBreakCount: 0,
            SignoffStatus: LedgerPeriodSignoffStatusDto.Pending,
            // A soft close reports the current time, not a persisted close date; the runner must
            // ignore this and date closing entries to the period end date instead.
            CompletedAt: DateTimeOffset.UtcNow);

        var period = new LedgerPeriodDto(
            PeriodId: ClosedPeriodId,
            LedgerBookId: ledgerBookId,
            FiscalYear: 2026,
            PeriodNo: 6,
            Label: "2026-06",
            StartDate: new DateOnly(2026, 6, 1),
            EndDate: PeriodEndDate,
            Status: LedgerPeriodStatusDto.SoftClosed,
            OpenedAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            ClosedAt: null,
            Version: 1);

        var service = Substitute.For<ILedgerBookService>();
        service.GetBookAsync(ledgerBookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(
            ledgerBookId,
            "fund-alpha",
            FundAccountId,
            FundStructureNodeKindDto.Account,
            "Fund Alpha primary ledger",
            "USD",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        service.GetPeriodSummaryAsync(ClosedPeriodId, Arg.Any<CancellationToken>()).Returns(summary);
        service.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { period });
        return service;
    }

    private static LedgerPeriodTrialBalanceLineDto TrialBalanceLine(
        string accountName, string accountType, decimal debits, decimal credits, decimal balance,
        LedgerDimensionSetDto? dimensions = null, string? symbol = null, string? financialAccountId = null)
        => new(accountName, accountType, Symbol: symbol, FinancialAccountId: financialAccountId,
            DebitTotal: debits, CreditTotal: credits, Balance: balance, EntryCount: 1,
            Dimensions: dimensions);

    private static ILedgerBookService LedgerBookServiceWithHardClosedPeriod(
        params LedgerPeriodTrialBalanceLineDto[] trialBalance)
    {
        var service = LedgerBookServiceWithClosedPeriod(trialBalance);
        service.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new LedgerPeriodDto(
                    ClosedPeriodId,
                    BookId,
                    2026,
                    6,
                    "2026-06",
                    new DateOnly(2026, 6, 1),
                    PeriodEndDate,
                    LedgerPeriodStatusDto.HardClosed,
                    new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    AsOf,
                    2)
            ]);
        return service;
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_PreservesAccountSymbolAndFinancialAccountScope()
    {
        var fixture = CreateIntakeFixture();
        // A financial-account-scoped revenue balance (broker-specific dividend income) must close to
        // the scoped account so the scoped trial-balance row is zeroed, not an unscoped aggregate.
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 250m, 250m,
                symbol: "AAPL", financialAccountId: "broker-1"));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        var result = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId, BookId));

        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Lines.Should().Contain(line =>
            line.LedgerAccountFinancialAccountId == "broker-1" &&
            line.LedgerAccountSymbol == "AAPL" &&
            line.Amount == 250m,
            "the scoped revenue account's identity must survive onto the closing draft line so posting zeroes the scoped balance");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_PreservesDimensionSplitClosingLines()
    {
        var fixture = CreateIntakeFixture();
        // The same revenue account under two entities must close to two dimension-scoped lines,
        // not a single aggregate, so entity-level P&L and retained earnings stay correct.
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m,
                new LedgerDimensionSetDto(EntityId: "entity-a")),
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 120m, 120m,
                new LedgerDimensionSetDto(EntityId: "entity-b")));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        var result = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId, BookId));

        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Lines.Should().HaveCount(4,
            "two dimension-scoped revenue closings plus two dimension-scoped retained-earnings rolls");
        draft.Lines.Should().Contain(line =>
            line.Dimensions != null && line.Dimensions.EntityId == "entity-a" && line.Amount == 300m);
        draft.Lines.Should().Contain(line =>
            line.Dimensions != null && line.Dimensions.EntityId == "entity-b" && line.Amount == 120m);
        draft.Lines.Where(line => line.Dimensions != null && line.Dimensions.EntityId == "entity-a")
            .Should().HaveCount(2, "the entity-a revenue close and its retained-earnings roll both carry the entity scope");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_LandsClosingEntryDraftInWorkbenchQueue()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Cash", "Asset", 500m, 0m, 500m),
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m),
            TrialBalanceLine("Management Fee Expense", "Expense", 200m, 0m, 200m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        var result = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            FundProfileId: "fund-alpha",
            Currency: "USD",
            Actor: "fund-controller",
            PeriodId: ClosedPeriodId,
            LedgerBookId: BookId));

        result.ProducerSkips.Should().BeEmpty();
        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft,
            "revenue, expense, and retained-earnings accounts must all map onto the chart");
        draft.Memo.Should().Contain("Period-close closing entries");
        draft.Lines.Should().HaveCount(3,
            "closing zeroes the revenue and expense accounts and rolls net income to retained earnings");
        draft.Lines.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m)
            .Should().Be(draft.Lines.Sum(line => line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m));
        draft.AccountingDate.Should().Be(PeriodEndDate,
            "closing entries are dated to the period end date, not the soft-close/run time");
        draft.EntryType.Should().Be(ManualJournalEntryTypeDto.ClosingEntry,
            "the ClosingEntry type drives the ClosingEntry posting kind so the close can post into the closed period");

        var workbench = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        workbench.Drafts.Should().ContainSingle(
            "the closing-entry draft must be visible in the close cockpit's queue");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_WithoutRequestBookId_BindsDraftToPeriodBook()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        // No LedgerBookId supplied on the request; the draft must still bind to the period's book.
        var result = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId));

        result.Intake.Created.Should().ContainSingle();
        var workbench = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        workbench.Drafts.Should().ContainSingle(
            "the closing-entry draft must land under the period's ledger book, not an unscoped queue");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_MismatchedBookId_FailsLoudly()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        var act = () => runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId,
            LedgerBookId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*belongs to book*");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_SecondRun_SkipsDuplicateInsteadOfDoublingTheClose()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);
        var request = new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId, BookId);

        var first = await runner.RunPeriodCloseIntakeAsync(request);
        var second = await runner.RunPeriodCloseIntakeAsync(request);

        first.Intake.Created.Should().ContainSingle();
        second.Intake.Created.Should().BeEmpty();
        second.Intake.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("already exists");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_OpenOrMissingPeriod_FailsLoudly()
    {
        var fixture = CreateIntakeFixture();
        var bookService = Substitute.For<ILedgerBookService>();
        bookService.GetPeriodSummaryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((LedgerPeriodSummaryDto?)null);
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        var act = () => runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*close the period before running closing entries*");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_WithoutLedgerBookService_FailsLoudly()
    {
        var fixture = CreateIntakeFixture();
        var runner = new AutomatedJournalIntakeRunner(fixture.Intake, new FeeScheduleAccrualEventProducer());

        var act = () => runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ledger book service*");
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_NoTemporaryBalances_ReturnsEmptyIntake()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Cash", "Asset", 500m, 0m, 500m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);

        var result = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "fund-controller", ClosedPeriodId, BookId));

        result.Intake.Created.Should().BeEmpty("a period with no temporary-account balances has nothing to close");
        result.Intake.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task Runner_PeriodCloseIntake_HardClosedPeriod_AllowsPreviewButRejectsDraftMutation()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithHardClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: bookService);
        var request = new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha",
            "USD",
            "fund-controller",
            ClosedPeriodId,
            BookId);

        var preview = await runner.PreviewPeriodCloseAsync(request);
        var mutate = () => runner.RunPeriodCloseIntakeAsync(request);

        preview.Draft.Should().NotBeNull("hard-closed periods remain available for read-only close review");
        await mutate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must be soft-closed*current status is HardClosed*");
        var workbench = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        workbench.Drafts.Should().BeEmpty("a hard-closed period must never acquire a new closing draft");
    }

    [Fact]
    public async Task ClosePostingBridge_FinalizeHardClose_RechecksGateAndLeavesNonReadyPeriodSoftClosed()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);
        var bridge = new AccountingClosePostingWorkbenchBridge(
            runner,
            fixture.Workbench,
            (IManualJournalEntryLifecycleService)fixture.Workbench,
            bookService);
        var context = new AccountingClosePostingContext(
            Guid.NewGuid(), FundAccountId, BookId, "2026-06", "USD");
        var command = new AccountingClosePostingCommand(
            "fund-controller",
            "Finalize the retained close package.",
            [$"evidence://period/{ClosedPeriodId:D}/book/{BookId:D}/approval/close"],
            OperationsActionOriginDto.HumanOperator,
            Role: "Fund Controller");

        var act = () => bridge.FinalizeHardCloseAsync(context, command);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot be hard-closed*closing-entry gate is Required*");
        await bookService.DidNotReceive().ClosePeriodAsync(
            Arg.Any<Guid>(),
            Arg.Any<CloseLedgerPeriodRequest>(),
            Arg.Any<CancellationToken>());
        var retained = await bookService.ListPeriodsAsync(
            new LedgerPeriodQuery(LedgerBookId: BookId));
        retained.Should().ContainSingle().Which.Status.Should().Be(LedgerPeriodStatusDto.SoftClosed);
    }

    [Fact]
    public async Task ClosePostingBridge_FinalizeHardClose_CloseLocksPostedClosingBatchAndRetryConverges()
    {
        var journalPeriod = new LedgerAccountingPeriod(
            ClosedPeriodId,
            BookId,
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            PeriodEndDate,
            "SoftClosed",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            1);
        var journalBook = new LedgerBookRecord(
            BookId,
            "fund-alpha",
            FundAccountId,
            FundStructureNodeKindDto.Account,
            "Fund Alpha primary ledger",
            "USD",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var journalStore = Substitute.For<ILedgerJournalStore>();
        journalStore.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>())
            .Returns(journalBook);
        journalStore.ListLedgerBooksAsync(
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<FundStructureNodeKindDto?>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { journalBook });
        journalStore.GetPeriodAsync(ClosedPeriodId, Arg.Any<CancellationToken>())
            .Returns(_ => journalPeriod);
        journalStore.ListPeriodsAsync(
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new[] { journalPeriod });
        journalStore.GetByPeriodAsync(ClosedPeriodId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerJournalEntryRecord>());
        var fixture = CreateIntakeFixture(journalStore);
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var currentPeriod = (await bookService.ListPeriodsAsync(
            new LedgerPeriodQuery(LedgerBookId: BookId))).Single();
        var currentSummary = (await bookService.GetPeriodSummaryAsync(ClosedPeriodId))!;
        bookService.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { currentPeriod });
        bookService.GetPeriodSummaryAsync(ClosedPeriodId, Arg.Any<CancellationToken>())
            .Returns(_ => currentSummary);
        bookService.ClosePeriodAsync(
                ClosedPeriodId,
                Arg.Any<CloseLedgerPeriodRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                currentPeriod = currentPeriod with
                {
                    Status = LedgerPeriodStatusDto.HardClosed,
                    ClosedAt = AsOf,
                    Version = currentPeriod.Version + 1
                };
                journalPeriod = journalPeriod with
                {
                    Status = "HardClosed",
                    ClosedAt = AsOf,
                    Version = journalPeriod.Version + 1
                };
                return new LedgerPeriodCloseResultDto(currentPeriod, currentSummary, null!);
            });
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: bookService);
        var intake = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha",
            "USD",
            "close-preparer",
            ClosedPeriodId,
            BookId,
            EntityId: "entity-alpha"));
        var created = intake.Intake.Created.Should().ContainSingle().Subject;
        var posted = created with
        {
            Status = ManualJournalEntryStatusDto.Posted,
            PostedAtUtc = AsOf,
            PostedBy = "posting-controller",
            UpdatedAtUtc = AsOf,
            Version = created.Version + 1,
            EvidenceLinks = [$"evidence://period/{ClosedPeriodId:D}/book/{BookId:D}/approval/close"]
        };
        await fixture.DraftStore.SaveAsync(posted);
        var zeroBalance = TrialBalanceLine("Cash", "Asset", 500m, 0m, 500m);
        currentSummary = currentSummary with
        {
            TrialBalance = [zeroBalance],
            TotalDebits = zeroBalance.DebitTotal,
            TotalCredits = zeroBalance.CreditTotal,
            NetIncome = 0m
        };
        var bridge = new AccountingClosePostingWorkbenchBridge(
            runner,
            fixture.Workbench,
            (IManualJournalEntryLifecycleService)fixture.Workbench,
            bookService);
        var context = new AccountingClosePostingContext(
            Guid.NewGuid(),
            FundAccountId,
            BookId,
            ClosedPeriodId.ToString("D"),
            "USD");
        var command = new AccountingClosePostingCommand(
            "fund-controller",
            "Finalize the retained close package and lock its posted closing batch.",
            [$"evidence://period/{ClosedPeriodId:D}/book/{BookId:D}/approval/close"],
            OperationsActionOriginDto.HumanOperator,
            Role: "Fund Controller",
            CorrelationId: "hard-close-2026-06");

        var closed = await bridge.FinalizeHardCloseAsync(context, command);
        var retry = await bridge.FinalizeHardCloseAsync(context, command);

        closed.Status.Should().Be(LedgerPeriodStatusDto.HardClosed);
        retry.Should().Be(closed);
        await bookService.Received(1).ClosePeriodAsync(
            ClosedPeriodId,
            Arg.Is<CloseLedgerPeriodRequest>(request =>
                request.CloseKind == LedgerPeriodCloseKindDto.HardClose &&
                request.ClosedBy == "fund-controller"),
            Arg.Any<CancellationToken>());
        var retained = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        var closeLocked = retained.Drafts.Should().ContainSingle(draft =>
            draft.JournalEntryId == posted.JournalEntryId).Subject;
        closeLocked.Status.Should().Be(ManualJournalEntryStatusDto.CloseLocked);
        closeLocked.CloseLockedBy.Should().Be("fund-controller");
        closeLocked.ClosedLockedAtUtc.Should().NotBeNull();
        closeLocked.LifecycleTransitions.Should().ContainSingle(transition =>
            transition.Action == JournalEntryLifecycleActionDto.LockAfterClose &&
            transition.FromStatus == ManualJournalEntryStatusDto.Posted &&
            transition.ToStatus == ManualJournalEntryStatusDto.CloseLocked &&
            transition.CorrelationId == "hard-close-2026-06" &&
            transition.EvidenceLinks.Any(link =>
                link.Contains("accounting-close/period-lock", StringComparison.OrdinalIgnoreCase) &&
                link.Contains(posted.JournalEntryId.ToString("D"), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ClosePostingBridge_CloseLockedReopenRetry_ReleasesThroughReceiptAndRejectsDifferentCorrelation()
    {
        var journalPeriod = new LedgerAccountingPeriod(
            ClosedPeriodId,
            BookId,
            2026,
            6,
            "2026-06",
            new DateOnly(2026, 6, 1),
            PeriodEndDate,
            "SoftClosed",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            1);
        var journalBook = new LedgerBookRecord(
            BookId,
            "fund-alpha",
            FundAccountId,
            FundStructureNodeKindDto.Account,
            "Fund Alpha primary ledger",
            "USD",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var journalStore = Substitute.For<ILedgerJournalStore>();
        journalStore.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>())
            .Returns(journalBook);
        journalStore.ListLedgerBooksAsync(
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<FundStructureNodeKindDto?>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { journalBook });
        journalStore.GetPeriodAsync(ClosedPeriodId, Arg.Any<CancellationToken>())
            .Returns(_ => journalPeriod);
        journalStore.ListPeriodsAsync(
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new[] { journalPeriod });
        journalStore.GetByPeriodAsync(ClosedPeriodId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerJournalEntryRecord>());
        var faultingDraftStore = new FailOnceCorrectionDraftStore();
        var fixture = CreateIntakeFixture(journalStore, faultingDraftStore);
        var bookService = LedgerBookServiceWithClosedPeriod(
            TrialBalanceLine("Dividend Income", "Revenue", 0m, 300m, 300m));
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake, new FeeScheduleAccrualEventProducer(), ledgerBookService: bookService);
        const string approvalReference = "reopen-approval-42";
        var evidence =
            $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{BookId:D}/{ClosedPeriodId:D}/{approvalReference}";
        var supportEvidence =
            $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{BookId:D}/{ClosedPeriodId:D}/support-package";
        var intake = await runner.RunPeriodCloseIntakeAsync(new RunPeriodCloseDraftIntakeRequest(
            "fund-alpha", "USD", "close-preparer", ClosedPeriodId, BookId, EntityId: "entity-alpha"));
        var created = intake.Intake.Created.Should().ContainSingle().Subject;
        var postedClosingBatch = created with
        {
            Status = ManualJournalEntryStatusDto.CloseLocked,
            UpdatedAtUtc = AsOf,
            Version = created.Version + 1,
            PostedAtUtc = AsOf,
            PostedBy = "fund-controller",
            ClosedLockedAtUtc = AsOf,
            CloseLockedBy = "close-controller",
            EvidenceLinks = [evidence, supportEvidence]
        };
        await fixture.DraftStore.SaveAsync(postedClosingBatch);

        var currentPeriod = (await bookService.ListPeriodsAsync(
            new LedgerPeriodQuery(LedgerBookId: BookId))).Single() with
        {
            Status = LedgerPeriodStatusDto.HardClosed,
            ClosedAt = AsOf
        };
        journalPeriod = journalPeriod with
        {
            Status = "HardClosed",
            ClosedAt = AsOf,
            Version = currentPeriod.Version
        };
        bookService.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { currentPeriod });
        bookService.ReopenPeriodAsync(
                ClosedPeriodId,
                Arg.Any<ReopenLedgerPeriodRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<ReopenLedgerPeriodRequest>();
                var priorStatus = currentPeriod.Status.ToString();
                currentPeriod = currentPeriod with
                {
                    Status = LedgerPeriodStatusDto.SoftClosed,
                    ClosedAt = null,
                    Version = currentPeriod.Version + 1
                };
                journalPeriod = journalPeriod with
                {
                    Status = "SoftClosed",
                    ClosedAt = null,
                    Version = currentPeriod.Version
                };
                return new LedgerPeriodReopenResultDto(
                    currentPeriod,
                    priorStatus,
                    request.ReopenedBy,
                    AsOf,
                    request.ApprovalReference,
                    request.EvidenceLinks);
            });
        var bridge = new AccountingClosePostingWorkbenchBridge(
            runner,
            fixture.Workbench,
            (IManualJournalEntryLifecycleService)fixture.Workbench,
            bookService);
        var context = new AccountingClosePostingContext(
            Guid.NewGuid(), FundAccountId, BookId, ClosedPeriodId.ToString("D"), "USD");
        var command = new AccountingClosePostingCommand(
            "fund-controller",
            "Reopen the period for a governed restatement.",
            [evidence, supportEvidence],
            OperationsActionOriginDto.HumanOperator,
            Role: "Fund Controller",
            ApprovalReference: approvalReference,
            CorrelationId: "reopen-correlation-42");

        faultingDraftStore.FailNextBatch = true;
        var interrupted = () => bridge.ReopenAndQueueClosingReversalsAsync(context, command);

        await interrupted.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*was reopened under a retained governed intent*Retry the exact reopen command*");
        currentPeriod.Status.Should().Be(LedgerPeriodStatusDto.SoftClosed);
        var afterInterruptedAttempt = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        afterInterruptedAttempt.Drafts.Should().ContainSingle(draft =>
            draft.JournalEntryId == postedClosingBatch.JournalEntryId &&
            draft.Status == ManualJournalEntryStatusDto.CloseLocked);
        afterInterruptedAttempt.Drafts.Should().NotContain(draft =>
            draft.ReversalOfJournalEntryId == postedClosingBatch.JournalEntryId);

        var first = await bridge.ReopenAndQueueClosingReversalsAsync(context, command);
        var retry = await bridge.ReopenAndQueueClosingReversalsAsync(context, command);
        var differentCorrelation = () => bridge.ReopenAndQueueClosingReversalsAsync(
            context,
            command with { CorrelationId = "reopen-correlation-different" });
        var reducedEvidenceReplay = () => bridge.ReopenAndQueueClosingReversalsAsync(
            context,
            command with { EvidenceLinks = [evidence] });

        first.State.Should().Be(ClosePostingGateStateDto.ReversalQueued);
        retry.ReversalDraftJournalEntryIds.Should().Equal(first.ReversalDraftJournalEntryIds);
        first.ClosingBatchJournalEntryIds.Should().ContainSingle()
            .Which.Should().Be(postedClosingBatch.JournalEntryId);
        first.ReversalDraftJournalEntryIds.Should().ContainSingle();
        await reducedEvidenceReplay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*do not match this reopen actor, correlation, reason, and evidence*");
        await differentCorrelation.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*do not match this reopen actor, correlation, reason, and evidence*");
        await bookService.Received(1).ReopenPeriodAsync(
            ClosedPeriodId,
            Arg.Any<ReopenLedgerPeriodRequest>(),
            Arg.Any<CancellationToken>());
        var retained = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        retained.Drafts.Count(draft =>
                draft.ReversalOfJournalEntryId == postedClosingBatch.JournalEntryId)
            .Should().Be(1, "retries and rejected correlations must reuse the retained reversal draft");
        retained.Drafts.Should().ContainSingle(draft =>
            draft.JournalEntryId == postedClosingBatch.JournalEntryId &&
            draft.Status == ManualJournalEntryStatusDto.Reversed &&
            draft.LifecycleTransitions.Any(transition =>
                transition.Action == JournalEntryLifecycleActionDto.Reverse &&
                transition.FromStatus == ManualJournalEntryStatusDto.CloseLocked));
    }

    [Fact]
    public async Task ClosePostingBridge_ZeroBalanceReopen_RetainsExactReceiptAcrossRetry()
    {
        var fixture = CreateIntakeFixture();
        var bookService = LedgerBookServiceWithHardClosedPeriod(
            TrialBalanceLine("Cash", "Asset", 500m, 0m, 500m));
        var currentPeriod = (await bookService.ListPeriodsAsync(
            new LedgerPeriodQuery(LedgerBookId: BookId))).Single();
        bookService.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { currentPeriod });
        bookService.ReopenPeriodAsync(
                ClosedPeriodId,
                Arg.Any<ReopenLedgerPeriodRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<ReopenLedgerPeriodRequest>();
                currentPeriod = currentPeriod with
                {
                    Status = LedgerPeriodStatusDto.SoftClosed,
                    ClosedAt = null,
                    Version = currentPeriod.Version + 1
                };
                return new LedgerPeriodReopenResultDto(
                    currentPeriod,
                    LedgerPeriodStatusDto.HardClosed.ToString(),
                    request.ReopenedBy,
                    AsOf,
                    request.ApprovalReference,
                    request.EvidenceLinks);
            });
        var runner = new AutomatedJournalIntakeRunner(
            fixture.Intake,
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: bookService);
        var bridge = new AccountingClosePostingWorkbenchBridge(
            runner,
            fixture.Workbench,
            (IManualJournalEntryLifecycleService)fixture.Workbench,
            bookService);
        var workflowId = Guid.NewGuid();
        var context = new AccountingClosePostingContext(
            workflowId,
            FundAccountId,
            BookId,
            ClosedPeriodId.ToString("D"),
            "USD");
        const string approvalReference = "zero-balance-reopen-approval";
        var evidence =
            $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{BookId:D}/{ClosedPeriodId:D}/{approvalReference}";
        var command = new AccountingClosePostingCommand(
            "fund-controller",
            "Reopen the zero-balance period for a governed restatement.",
            [evidence],
            OperationsActionOriginDto.HumanOperator,
            Role: "Fund Controller",
            ApprovalReference: approvalReference,
            CorrelationId: "zero-balance-reopen-correlation");

        var first = await bridge.ReopenAndQueueClosingReversalsAsync(context, command);
        var retry = await bridge.ReopenAndQueueClosingReversalsAsync(context, command);
        var changedReason = () => bridge.ReopenAndQueueClosingReversalsAsync(
            context,
            command with { Reason = "Changed reopen reason." });
        var changedEvidence = () => bridge.ReopenAndQueueClosingReversalsAsync(
            context,
            command with
            {
                EvidenceLinks =
                [
                    evidence,
                    $"/api/workstation/evidence/subjects/accounting-record/reversal/ledger-book/{BookId:D}/{ClosedPeriodId:D}/changed"
                ]
            });

        first.State.Should().Be(ClosePostingGateStateDto.NotRequired);
        retry.State.Should().Be(ClosePostingGateStateDto.NotRequired);
        await changedReason.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained governed reopen intent does not match*");
        await changedEvidence.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained governed reopen intent does not match*");
        await bookService.Received(1).ReopenPeriodAsync(
            ClosedPeriodId,
            Arg.Any<ReopenLedgerPeriodRequest>(),
            Arg.Any<CancellationToken>());
        var retained = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        retained.AuditTrail.Should().ContainSingle(item =>
            item.Action.StartsWith($"GovernedLedgerPeriodReopen:{ClosedPeriodId:D}:", StringComparison.Ordinal) &&
            item.CorrelationId == command.CorrelationId &&
            !string.IsNullOrWhiteSpace(item.AfterHash));
    }

    private static ChartOfAccountsNodeDto Node(string path, string name, string type)
        => new(NodeId: path, Path: path, AccountName: name, AccountType: type);

    private static CorporateActionDto DividendAction(Guid securityId, DateOnly exDate, decimal dividendPerShare)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: CorporateActionEventTypes.Dividend,
            ExDate: exDate,
            PayDate: exDate.AddDays(14),
            DividendPerShare: dividendPerShare,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    private static RunFeeAccrualDraftIntakeRequest FeeIntakeRequest()
        => new(
            FundProfileId: "fund-alpha",
            Currency: "USD",
            Actor: "automated-journal",
            PeriodId: "2026-Q2",
            BeginningNav: 1_000_000m,
            EndingNavBeforeFees: 1_100_000m,
            HighWaterMark: 1_050_000m,
            ManagementFeeRate: 0.02m,
            PerformanceFeeRate: 0.20m,
            LedgerBookId: BookId,
            EntityId: "entity-alpha",
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha",
            EvidenceRetainedAtUtc: AsOf);

    private static AutomatedJournalCapitalAccountReconciliationDto FeeReconciliation(
        decimal confidence = 0.98m,
        decimal maximumVarianceTolerance = 0m,
        decimal capitalAccountOpeningBalance = 1_000_000m)
        => new(
            ReconciliationId: "capital-tie-out-2026-q2",
            PeriodId: "2026-Q2",
            Currency: "USD",
            ReconciledBeginningNav: 1_000_000m,
            ReconciledEndingNavBeforeFees: 1_100_000m,
            ReconciledHighWaterMark: 1_050_000m,
            CapitalAccountOpeningBalance: capitalAccountOpeningBalance,
            CapitalAccountEndingBalanceBeforeFees: 1_100_000m,
            CapitalAccountHighWaterMark: 1_050_000m,
            MaximumVarianceTolerance: maximumVarianceTolerance,
            ConfidenceScore: confidence,
            IsReconciled: true,
            SourceVersion: "capital-ledger:v42",
            ReviewedBy: "fund-controller",
            ReviewedAtUtc: AsOf.AddMinutes(-5),
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    "capital-tie-out-2026-q2",
                    "Reviewed capital-account reconciliation",
                    "evidence://capital-accounts/fund-alpha/2026-Q2/v42",
                    "capital-account-subledger",
                    AsOf.AddMinutes(-5))
            ]);

    private sealed class StubCapitalAccountReconciliationResolver(
        AutomatedJournalCapitalAccountReconciliationDto? reconciliation)
        : IAutomatedJournalCapitalAccountReconciliationResolver
    {
        public AutomatedJournalCapitalAccountReconciliationDto? Reconciliation { get; set; } = reconciliation;

        public List<AutomatedJournalCapitalAccountReconciliationScope> Scopes { get; } = [];

        public Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
            AutomatedJournalCapitalAccountReconciliationScope scope,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Scopes.Add(scope);
            return Task.FromResult(Reconciliation);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeSecurityMasterQueryService : ISecurityMasterQueryService
    {
        private readonly IReadOnlyDictionary<string, Guid> _tickerToSecurityId;
        private readonly IReadOnlyList<CorporateActionDto> _corporateActions;

        public FakeSecurityMasterQueryService(
            IReadOnlyDictionary<string, Guid> tickerToSecurityId,
            IReadOnlyList<CorporateActionDto> corporateActions)
        {
            _tickerToSecurityId = tickerToSecurityId;
            _corporateActions = corporateActions;
        }

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
            => Task.FromResult(
                identifierKind == SecurityIdentifierKind.Ticker &&
                _tickerToSecurityId.TryGetValue(identifierValue, out var securityId)
                    ? Detail(securityId)
                    : null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>(
                _corporateActions.Where(action => action.SecurityId == securityId).ToArray());

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(Detail(securityId));

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => Task.FromResult(Detail(securityId));

        public Task<SecurityDetailDto?> GetRecordedByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => Task.FromResult(Detail(securityId));

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);

        private static SecurityDetailDto? Detail(Guid securityId)
        {
            var emptyTerms = JsonDocument.Parse("{}").RootElement;
            return new SecurityDetailDto(
                securityId,
                "Equity",
                SecurityStatusDto.Active,
                "Test Security",
                "USD",
                emptyTerms,
                emptyTerms,
                Identifiers: [],
                Aliases: [],
                Version: 1,
                EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
                EffectiveTo: null);
        }
    }
}
