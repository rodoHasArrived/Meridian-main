using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
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

        var management = production.Events.Single(e => e.Kind == AutomatedJournalEventKind.ManagementFeeAccrued);
        management.Amount.Should().Be(20_000m, "management fee is the period rate applied to beginning NAV");
        management.Symbol.Should().Be("FUND-ALPHA");
        management.IdempotencyKey.Should().Be("mgmt-fee|fund-alpha|2026-Q2");

        var performance = production.Events.Single(e => e.Kind == AutomatedJournalEventKind.PerformanceFeeAccrued);
        performance.Amount.Should().Be(6_000m,
            "performance fee applies to the high-water excess net of the management fee: 20% × (1,100,000 − 1,050,000 − 20,000)");
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
            new DateOnly(2026, 07, 01),
            new DateOnly(2026, 07, 31),
            AsOf));

        production.Skipped.Should().ContainSingle().Subject.Subject.Should().Be("UNKNOWN");
        production.Events.Should().ContainSingle("one unresolved ticker must not block the rest of the batch");
    }

    // -------------------------------------------------------------------------
    // AutomatedJournalIntakeRunner — producers land drafts in the queue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Runner_FeeAccrual_LandsDraftsInWorkbenchQueue()
    {
        var fixture = CreateIntakeFixture();
        var runner = new AutomatedJournalIntakeRunner(fixture.Intake, new FeeScheduleAccrualEventProducer());

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
            EntityId: "entity-alpha"));

        result.ProducerSkips.Should().BeEmpty();
        result.Intake.Created.Should().HaveCount(2);
        result.Intake.Created.Should().OnlyContain(draft => draft.Status == ManualJournalEntryStatusDto.Draft);

        var workbench = await fixture.Workbench.GetWorkbenchAsync("fund-alpha", BookId);
        workbench.Drafts.Should().HaveCount(2, "fee accrual drafts must be visible in the close cockpit's queue");
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

    private sealed record IntakeFixture(
        AutomatedJournalDraftIntakeService Intake,
        IManualJournalEntryWorkbenchService Workbench);

    private static IntakeFixture CreateIntakeFixture()
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
                Node("Liabilities:Performance Fee Payable", "Performance Fee Payable", "Liability")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [])).GetAwaiter().GetResult();

        var configurationService = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            new InMemoryAccountingActionAuditStore());
        return new IntakeFixture(
            new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService),
            workbench);
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
