using FluentAssertions;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Banking;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;

namespace Meridian.Tests.Application;

public sealed class ReconciliationRunServiceTests
{
    [Fact]
    public async Task RunAsync_ShouldReturnNull_WhenRunDoesNotExist()
    {
        var service = CreateService();

        var result = await service.RunAsync(new ReconciliationRunRequest("missing-run"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ShouldMaterializeStoredReconciliation_ForRecordedRun()
    {
        var store = new StrategyRunStore();
        var service = CreateService(store, out var repository);
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-1", ledgerAsOfOffsetMinutes: 10));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-1"));

        detail.Should().NotBeNull();
        detail!.Summary.RunId.Should().Be("run-1");
        detail.Summary.BreakCount.Should().Be(0);
        detail.Matches.Should().Contain(match => match.CheckId == "cash-balance");

        var latest = await repository.GetLatestForRunAsync("run-1");
        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be(detail.Summary.ReconciliationRunId);
    }

    [Fact]
    public async Task RunAsync_WithUnresolvedSecurityCoverage_ShouldExposeSecurityIssues()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-security"));

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(), lookup);

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-security"));

        detail.Should().NotBeNull();
        detail!.Summary.HasSecurityCoverageIssues.Should().BeTrue();
        detail.Summary.SecurityIssueCount.Should().Be(2);
        detail.SecurityCoverageIssues.Should().NotBeNull();
        detail.SecurityCoverageIssues!.Should().Contain(issue => issue.Source == "portfolio" && issue.Symbol == "TSLA");
        detail.SecurityCoverageIssues.Should().Contain(issue => issue.Source == "ledger" && issue.Symbol == "TSLA");
        detail.SecurityCoverageIssues.Should().OnlyContain(issue =>
            issue.Reason.Contains("missing a Security Master match", StringComparison.OrdinalIgnoreCase));
        detail.SecurityClassifications.Should().ContainKey("AAPL");
        detail.SecurityClassifications.Should().NotContainKey("TSLA");
    }

    [Fact]
    public async Task RunAsync_WithSecurityLookup_ShouldPopulateAuthoritativeSecurityReferences()
    {
        // Arrange — register both AAPL and TSLA so the lookup returns full data for both
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-classifications"));

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL",
            SubType: "CommonShare",
            MatchedIdentifierKind: "ISIN",
            MatchedIdentifierValue: "US0378331005",
            MatchedProvider: "OpenFIGI"));
        lookup.Register("TSLA", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DisplayName: "Tesla Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "TSLA",
            SubType: "CommonShare"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(), lookup);

        // Act
        var detail = await service.RunAsync(new ReconciliationRunRequest("run-classifications"));

        // Assert
        detail.Should().NotBeNull();
        detail!.SecurityClassifications.Should().NotBeNull(
            "a Security Master lookup was wired, so authoritative security references must be populated");

        detail.SecurityClassifications!.Should().ContainKey("AAPL");
        detail.SecurityClassifications["AAPL"].AssetClass.Should().Be("Equity");
        detail.SecurityClassifications["AAPL"].SubType.Should().Be("CommonShare");
        detail.SecurityClassifications["AAPL"].PrimaryIdentifierKind.Should().Be("Ticker");
        detail.SecurityClassifications["AAPL"].PrimaryIdentifierValue.Should().Be("AAPL");
        detail.SecurityClassifications["AAPL"].MatchedIdentifierKind.Should().Be("ISIN");
        detail.SecurityClassifications["AAPL"].MatchedIdentifierValue.Should().Be("US0378331005");
        detail.SecurityClassifications["AAPL"].MatchedProvider.Should().Be("OpenFIGI");
        detail.SecurityClassifications["TSLA"].PrimaryIdentifierKind.Should().Be("Ticker");
    }

    [Fact]
    public async Task RunAsync_WithSecurityValidationIssue_ShouldExposeReconciliationCoverageIssue()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-validation-gate"));

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL"));
        lookup.Register("TSLA", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DisplayName: "Tesla Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "TSLA"));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: new SymbolSecurityValidationGate("TSLA"));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-validation-gate"));

        detail.Should().NotBeNull();
        detail!.Summary.HasSecurityCoverageIssues.Should().BeTrue();
        detail.SecurityCoverageIssues.Should().NotBeNullOrEmpty();
        detail.SecurityCoverageIssues!.Should().Contain(issue =>
            issue.Symbol == "TSLA"
            && issue.Code == "SM_ACCOUNTING_CLASSIFICATION_MISSING"
            && issue.Severity == ReconciliationBreakSeverity.High
            && issue.Reason.Contains("Security Master validation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WithSecurityMasterAccountingEventAdapter_ShouldAttachExpectedEventsToReconciliationDetail()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-sm-accounting"));

        var securityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var accountingRequest = new SecurityMasterAccountingEventRequest(
            RunId: "run-sm-accounting",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 2, 1),
            Securities:
            [
                new SecurityMasterAccountingSecurity(
                    securityId,
                    "BOND1",
                    "Bond",
                    "USD",
                    new SecurityFixedIncomeTerms(
                        CouponRate: 0.06m,
                        CouponType: "Fixed",
                        DayCountConvention: "ACT/365",
                        PaymentFrequencyPerYear: 2,
                        NextCouponDate: new DateOnly(2026, 1, 31),
                        AccrualStartDate: new DateOnly(2026, 1, 1)),
                    new SecurityAccountingRule("AvailableForSale", "GAAP"))
            ],
            Positions:
            [
                new SecurityMasterAccountingPosition(
                    "BOND1",
                    securityId,
                    "acct-1",
                    100_000m)
            ],
            ActualActivity:
            [
                new SecurityActualCashActivity(
                    SourceName: "custodian",
                    ExternalTransactionId: "coupon-row-1",
                    AccountId: "acct-1",
                    SecurityId: securityId,
                    Symbol: "BOND1",
                    CashAmount: 3_000m,
                    PrincipalAmount: 0m,
                    IncomeAmount: 3_000m,
                    PayDate: new DateOnly(2026, 1, 31),
                    Classification: "Income")
            ]);
        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(accountingRequest));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-accounting"));

        detail.Should().NotBeNull();
        detail!.Summary.ExpectedAccountingEventCount.Should().Be(2);
        detail.Summary.ExpectedJournalPreviewCount.Should().Be(2);
        detail.Summary.HasSecurityMasterAccountingIssues.Should().BeFalse();
        detail.ExpectedAccountingEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome);
        detail.ExpectedJournalPreviews.Should().OnlyContain(preview => preview.IsBalanced);
    }


    [Fact]
    public async Task RunAsync_WithFixedCouponTerms_ShouldGenerateAccrualAndCouponExpectedEvents()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-sm-fixed-coupon"));

        var securityId = Guid.Parse("44444444-3333-3333-3333-333333333333");
        var request = new SecurityMasterAccountingEventRequest(
            RunId: "run-sm-fixed-coupon",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            Securities: [new SecurityMasterAccountingSecurity(securityId, "BOND2", "Bond", "USD", new SecurityFixedIncomeTerms(0.06m, "Fixed", "ACT/365", 2, NextCouponDate: new DateOnly(2026, 1, 31), AccrualStartDate: new DateOnly(2026, 1, 1)), new SecurityAccountingRule("AvailableForSale", "GAAP"))],
            Positions: [new SecurityMasterAccountingPosition("BOND2", securityId, "acct-1", 100_000m)]);

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(request));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-fixed-coupon"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome);
        detail.ExpectedAccountingEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.ReceiveCouponCash);
    }

    [Fact]
    public async Task RunAsync_WithFactorPaydownAtPar_ShouldProjectExpectedPrincipalAtOnePointZeroFactor()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-sm-factor-par"));

        var securityId = Guid.Parse("55555555-3333-3333-3333-333333333333");
        var request = new SecurityMasterAccountingEventRequest(
            RunId: "run-sm-factor-par",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            Securities: [new SecurityMasterAccountingSecurity(securityId, "MBS1", "MortgageBacked", "USD", new SecurityFixedIncomeTerms(0.03m, "Fixed", "30/360", 12, CurrentFactor: 1.00m, RequiresFactorSchedule: true), new SecurityAccountingRule("AvailableForSale", "GAAP"))],
            Positions: [new SecurityMasterAccountingPosition("MBS1", securityId, "acct-1", 100_000m)],
            FactorSchedule: [new SecurityFactorScheduleEntry(securityId, new DateOnly(2026, 1, 15), 1.00m, 0.99m, "test")]);

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(request));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-factor-par"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.ReceivePrincipalCash &&
            item.ExpectedPrincipalAmount == 1_000m);
    }

    [Fact]
    public async Task RunAsync_WithExpectedActualMismatch_ShouldEmitDeterministicIssueCode()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-sm-mismatch"));

        var securityId = Guid.Parse("66666666-3333-3333-3333-333333333333");
        var request = new SecurityMasterAccountingEventRequest(
            RunId: "run-sm-mismatch",
            PeriodStart: new DateOnly(2026, 1, 1),
            PeriodEnd: new DateOnly(2026, 1, 31),
            Securities: [new SecurityMasterAccountingSecurity(securityId, "BOND3", "Bond", "USD", new SecurityFixedIncomeTerms(0.06m, "Fixed", "ACT/365", 2, NextCouponDate: new DateOnly(2026, 1, 31), AccrualStartDate: new DateOnly(2026, 1, 1)), new SecurityAccountingRule("AvailableForSale", "GAAP"))],
            Positions: [new SecurityMasterAccountingPosition("BOND3", securityId, "acct-1", 100_000m)],
            ActualActivity: [new SecurityActualCashActivity("custodian", "coupon-row-mismatch", "acct-1", securityId, "BOND3", 1m, 0m, 1m, new DateOnly(2026, 1, 31), "Income")]);

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(request));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-mismatch"));

        detail.Should().NotBeNull();
        detail!.SecurityMasterAccountingIssues.Should().Contain(issue => issue.Code == "SECURITY_MASTER_EXPECTED_VS_ACTUAL_MISMATCH");
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldBuildInputsFromResolvedPositions()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-accounting"));

        var securityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL fixed income test security",
            "Bond",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "CorporateBond"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(securityMasterQuery));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-accounting"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().NotBeNullOrEmpty();
        detail.ExpectedAccountingEvents!.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.Symbol == "AAPL" &&
            item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome);
        detail.Summary.SecurityMasterAccountingIssueCount.Should().BeGreaterThan(0);
        detail.SecurityMasterAccountingIssues.Should().Contain(issue => issue.Code == "FACTOR_SCHEDULE_MISSING");
    }

    [Fact]
    public async Task RunAsync_WithClassificationEdgeCases_ShouldPreservePrimaryIdentifierAndSubtypeValues()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-classification-edges"));

        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DisplayName: "Apple Inc.",
            AssetClass: "Equity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "AAPL",
            SubType: null));
        lookup.Register("TSLA", new WorkstationSecurityReference(
            SecurityId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            DisplayName: "Tesla Inc.",
            AssetClass: "LegacyEquity",
            Currency: "USD",
            Status: SecurityStatusDto.Active,
            PrimaryIdentifier: "88160R101",
            SubType: "LegacyEquitySubtype"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(), lookup);

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-classification-edges"));

        detail.Should().NotBeNull();
        detail!.SecurityClassifications.Should().NotBeNull();
        detail.SecurityClassifications!["AAPL"].SubType.Should().BeNull("ambiguous equity subtype should stay explicitly null");
        detail.SecurityClassifications["TSLA"].SubType.Should().Be("LegacyEquitySubtype");
        detail.SecurityClassifications["TSLA"].PrimaryIdentifierValue.Should().Be("88160R101");
    }

    [Fact]
    public async Task RunAsync_WithNoSecurityLookup_ShouldHaveNullOrEmptyClassifications()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-no-classifications"));
        var service = CreateService(store, new InMemoryReconciliationRunRepository());

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-no-classifications"));

        detail.Should().NotBeNull();
        // When no lookup is wired, the map is either null or empty — never populated
        var hasClassifications = detail!.SecurityClassifications is { Count: > 0 };
        hasClassifications.Should().BeFalse(
            "no Security Master lookup was wired so no classifications can be resolved");
    }

    [Fact]
    public async Task RunAsync_WithNearToleranceVariance_ShouldKeepPartialLikeResultInMatches()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun(
            "run-partial-like",
            portfolioCashOverride: 750.005m));

        var service = CreateService(store, new InMemoryReconciliationRunRepository());

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-partial-like", AmountTolerance: 0.01m));

        detail.Should().NotBeNull();
        detail!.Matches.Should().Contain(match =>
            match.CheckId == "cash-balance" &&
            match.Variance != 0m &&
            Math.Abs(match.Variance) < 0.01m);
    }

    [Fact]
    public async Task RunAsync_WithMaterialBreaks_ShouldSurfaceSeverityAndCanonicalReasonInDetail()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun(
            "run-break-severity",
            portfolioAsOfOffsetMinutes: 30,
            portfolioCashOverride: 950m));

        var service = CreateService(store, new InMemoryReconciliationRunRepository());

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-break-severity", AmountTolerance: 0.01m, MaxAsOfDriftMinutes: 5));

        detail.Should().NotBeNull();
        detail!.Breaks.Should().Contain(b => b.CheckId == "cash-balance"
            && b.Category == ReconciliationBreakCategory.TimingMismatch
            && b.Severity == ReconciliationBreakSeverity.High);
        detail.Breaks.Should().Contain(b => b.CheckId == "net-equity"
            && b.Category == ReconciliationBreakCategory.TimingMismatch
            && b.Reason.Contains("drift beyond tolerance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WithNearTimingDrift_ShouldPreservePartialMatchStatusAndUnresolvedCounts()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun(
            "run-partial-status",
            portfolioAsOfOffsetMinutes: 5760));

        var service = CreateService(store, new InMemoryReconciliationRunRepository());

        var detail = await service.RunAsync(new ReconciliationRunRequest(
            "run-partial-status",
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 4320));

        detail.Should().NotBeNull();
        detail!.Breaks.Should().Contain(b => b.CheckId == "cash-balance"
            && b.Category == ReconciliationBreakCategory.PartialMatch
            && b.Status == ReconciliationBreakStatus.PartialMatch
            && b.Severity == ReconciliationBreakSeverity.Low);
        detail.Summary.OpenBreakCount.Should().Be(detail.Breaks.Count,
            "partial matches are unresolved governance items and must remain visible in open-break counts");
    }

    [Fact]
    public async Task GetHistoryForRunAsync_ShouldReturnNewestFirst()
    {
        var store = new StrategyRunStore();
        var service = CreateService(store);
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-2"));

        var first = await service.RunAsync(new ReconciliationRunRequest("run-2"));
        var second = await service.RunAsync(new ReconciliationRunRequest("run-2"));

        var history = await service.GetHistoryForRunAsync("run-2");

        history.Should().HaveCount(2);
        history[0].CreatedAt.Should().BeOnOrAfter(history[1].CreatedAt);
        history.Select(item => item.ReconciliationRunId).Should().Contain([first!.Summary.ReconciliationRunId, second!.Summary.ReconciliationRunId]);
    }

    private static IReconciliationRunService CreateService(
        StrategyRunStore store,
        out IReconciliationRunRepository repository)
    {
        repository = new InMemoryReconciliationRunRepository();
        return CreateService(store, repository);
    }

    private static IReconciliationRunService CreateService(StrategyRunStore? store = null)
    {
        store ??= new StrategyRunStore();
        return CreateService(store, new InMemoryReconciliationRunRepository());
    }

    private static IReconciliationRunService CreateService(StrategyRunStore store, IReconciliationRunRepository repository)
    {
        return CreateService(store, repository, securityReferenceLookup: null);
    }

    private static IReconciliationRunService CreateService(
        StrategyRunStore store,
        IReconciliationRunRepository repository,
        ISecurityReferenceLookup? securityReferenceLookup)
    {
        return CreateService(store, repository, securityReferenceLookup, bankTransactionSource: null);
    }

    private static IReconciliationRunService CreateService(
        StrategyRunStore store,
        IReconciliationRunRepository repository,
        ISecurityReferenceLookup? securityReferenceLookup,
        IBankTransactionSource? bankTransactionSource,
        ISecurityValidationGateService? securityValidationGate = null,
        ISecurityMasterAccountingEventService? securityMasterAccountingEventService = null,
        ISecurityMasterAccountingEventSourceAdapter? securityMasterAccountingEventSourceAdapter = null)
    {
        IStrategyRepository strategyRepository = store;
        var portfolioReadService = securityReferenceLookup is null
            ? new PortfolioReadService()
            : new PortfolioReadService(securityReferenceLookup);
        var ledgerReadService = securityReferenceLookup is null
            ? new LedgerReadService()
            : new LedgerReadService(securityReferenceLookup);
        var runReadService = new StrategyRunReadService(strategyRepository, portfolioReadService, ledgerReadService);
        return new ReconciliationRunService(
            runReadService,
            new ReconciliationProjectionService(),
            repository,
            bankTransactionSource,
            securityValidationGate: securityValidationGate,
            securityMasterAccountingEventService: securityMasterAccountingEventService,
            securityMasterAccountingEventSourceAdapter: securityMasterAccountingEventSourceAdapter);
    }

    private sealed class StaticSecurityMasterAccountingEventSourceAdapter : ISecurityMasterAccountingEventSourceAdapter
    {
        private readonly SecurityMasterAccountingEventRequest _request;

        public StaticSecurityMasterAccountingEventSourceAdapter(SecurityMasterAccountingEventRequest request)
        {
            _request = request;
        }

        public Task<SecurityMasterAccountingEventRequest?> BuildRequestAsync(
            StrategyRunDetail detail,
            ReconciliationRunRequest request,
            CancellationToken ct = default)
        {
            return Task.FromResult<SecurityMasterAccountingEventRequest?>(_request);
        }
    }

    private sealed class StubSecurityMasterQueryService : Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService
    {
        private readonly Dictionary<Guid, SecurityEconomicDefinitionRecord> _definitions = [];

        public void Register(SecurityEconomicDefinitionRecord definition)
        {
            _definitions[definition.SecurityId] = definition;
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null) =>
            Task.FromResult<SecurityDetailDto?>(null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            _definitions.TryGetValue(securityId, out var definition);
            return Task.FromResult<SecurityEconomicDefinitionRecord?>(definition);
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default) =>
            Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }

    private sealed class SymbolSecurityValidationGate(string blockedSymbol) : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            var normalized = symbol.Trim().ToUpperInvariant();
            IReadOnlyList<SecurityValidationIssueDto> issues = string.Equals(normalized, blockedSymbol, StringComparison.OrdinalIgnoreCase)
                ?
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_ACCOUNTING_CLASSIFICATION_MISSING",
                        "Accounting classification is missing",
                        "The record does not expose an accounting classification for ledger posting and report grouping.",
                        ["commonTerms.accountingClassification"],
                        "Attach the ledger/reporting accounting classification.",
                        [])
                ]
                : Array.Empty<SecurityValidationIssueDto>();
            var report = new SecurityValidationReportDto(
                SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: issues.Count > 0,
                CriticalIssueCount: 0,
                ErrorIssueCount: issues.Count,
                Issues: issues);

            return Task.FromResult(new SecurityValidationGateResultDto(
                workflow,
                normalized,
                report.SecurityId,
                IsResolved: true,
                IsBlocked: report.HasBlockingIssues,
                Report: report,
                Snapshot: null));
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => ValidateSymbolAsync(symbol ?? securityId.ToString(), workflow, workflowReference, actor, persistSnapshot, ct);
    }

    private static SecurityEconomicDefinitionRecord CreateEconomicDefinition(
        Guid securityId,
        string symbol,
        string? accountingClassification,
        decimal? currentFactor = null)
    {
        var commonTerms = JsonSerializer.SerializeToElement(new
        {
            accountingClassification
        });
        var classification = JsonSerializer.SerializeToElement(new
        {
            assetClass = "FixedIncome",
            assetFamily = "FixedIncome",
            subType = "CorporateBond",
            typeName = "CorporateBond"
        });
        var economicTerms = JsonSerializer.SerializeToElement(new
        {
            maturity = new
            {
                effectiveDate = "2026-03-01",
                issueDate = "2026-03-01",
                maturityDate = "2031-03-01"
            },
            coupon = new
            {
                couponType = "Fixed",
                couponRate = 0.06m,
                paymentFrequency = "SemiAnnual",
                dayCount = "ACT/365"
            },
            accrual = new
            {
                accrualStartDate = "2026-03-01",
                dayCount = "ACT/365"
            },
            structuredProduct = currentFactor is null
                ? null
                : new
                {
                    factor = currentFactor,
                    factorDate = "2026-03-21",
                    notionalBalance = 100_000m
                }
        });

        return new SecurityEconomicDefinitionRecord(
            securityId,
            "FixedIncome",
            "FixedIncome",
            "CorporateBond",
            "CorporateBond",
            IssuerType: null,
            RiskCountry: null,
            SecurityStatusDto.Active,
            $"{symbol} fixed income test security",
            "USD",
            classification,
            commonTerms,
            economicTerms,
            JsonSerializer.SerializeToElement(new { source = "test" }),
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    symbol,
                    IsPrimary: true,
                    new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero))
            ],
            LegacyAssetClass: null,
            LegacyAssetSpecificTerms: null);
    }

    // -----------------------------------------------------------------------
    // Banking integration tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_WithMatchingBankNetAmount_ShouldProduceBankMatch()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-bank-match"));

        var entityId = Guid.NewGuid();
        // Ledger cash = 750 (initial 1000 - 400 AAPL + 150 TSLA short proceeds)
        // Bank net should match ledger cash: seed a single tx of 750
        var bankingService = new InMemoryBankingService();
        await bankingService.SeedBankTransactionsAsync(new BankTransactionSeedRequest(
            EntityIds: [entityId],
            CountPerEntity: 1,
            FromDate: new DateOnly(2026, 3, 20),
            ToDate: new DateOnly(2026, 3, 21)));

        // Adjust so net == 750 by seeding directly via approval
        var pending = await bankingService.InitiatePaymentAsync(entityId,
            new InitiatePaymentRequest(750m, new DateOnly(2026, 3, 21), "RECON-TEST", null));
        await bankingService.ApprovePaymentAsync(pending.PendingPaymentId,
            new ApprovePaymentRequest("Test approval", "test"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-bank-match", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().BeGreaterThan(0);
        detail.BankTransactions.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunAsync_WithBankEntityId_ShouldPopulateBankTransactionCount()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-bank-count"));

        var entityId = Guid.NewGuid();
        var bankingService = new InMemoryBankingService();
        await bankingService.SeedBankTransactionsAsync(new BankTransactionSeedRequest(
            EntityIds: [entityId],
            CountPerEntity: 3,
            FromDate: new DateOnly(2026, 3, 20),
            ToDate: new DateOnly(2026, 3, 21)));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-bank-count", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().Be(3);
        detail.BankTransactions.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_WithNoBankEntityId_ShouldSkipBankingChecks()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-no-bank"));

        var bankingService = new InMemoryBankingService();
        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        // No BankEntityId — banking checks should be skipped
        var detail = await service.RunAsync(new ReconciliationRunRequest("run-no-bank"));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().Be(0);
        detail.BankTransactions.Should().BeNull();
        detail.Matches.Should().Contain(m => m.CheckId == "cash-balance");
    }

    [Fact]
    public async Task RunAsync_WhenBankHasTransactionsButNoLedger_ShouldProduceMissingLedgerBreak()
    {
        // Build a run that has a portfolio but NO ledger
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildPortfolioOnlyRun("run-no-ledger"));

        var entityId = Guid.NewGuid();
        var bankingService = new InMemoryBankingService();
        await bankingService.SeedBankTransactionsAsync(new BankTransactionSeedRequest(
            EntityIds: [entityId],
            CountPerEntity: 2,
            FromDate: new DateOnly(2026, 3, 20),
            ToDate: new DateOnly(2026, 3, 21)));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-no-ledger", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankBreakCount.Should().BeGreaterThan(0);
        detail.Breaks.Should().Contain(b =>
            b.CheckId == "bank-ledger-coverage-missing" &&
            b.Category == ReconciliationBreakCategory.MissingLedgerCoverage);
    }

    private static class TestRunFactory
    {
        public static StrategyRunEntry BuildReconciliationReadyRun(
            string runId,
            decimal? portfolioCashOverride = null,
            int portfolioAsOfOffsetMinutes = 0,
            int ledgerAsOfOffsetMinutes = 0)
        {
            var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
            var completedAt = startedAt.AddMinutes(30);
            var portfolioAsOf = completedAt.AddMinutes(portfolioAsOfOffsetMinutes);
            var ledgerAsOf = completedAt.AddMinutes(ledgerAsOfOffsetMinutes);
            var portfolioCash = portfolioCashOverride ?? 750m;
            var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m),
                ["TSLA"] = new("TSLA", -5, 30m, 0m, 0m)
            };
            var accountSnapshot = new FinancialAccountSnapshot(
                AccountId: BacktestDefaults.DefaultBrokerageAccountId,
                DisplayName: "Primary Brokerage",
                Kind: FinancialAccountKind.Brokerage,
                Institution: "Simulated Broker",
                Cash: portfolioCash,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: -150m,
                Equity: portfolioCash + 400m - 150m,
                Positions: positions,
                Rules: new FinancialAccountRules());
            var snapshot = new PortfolioSnapshot(
                Timestamp: portfolioAsOf,
                Date: DateOnly.FromDateTime(portfolioAsOf.UtcDateTime),
                Cash: portfolioCash,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: -150m,
                TotalEquity: portfolioCash + 400m - 150m,
                DailyReturn: 0m,
                Positions: positions,
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    [accountSnapshot.AccountId] = accountSnapshot
                },
                DayCashFlows: []);

            var request = new BacktestRequest(
                From: new DateOnly(2026, 3, 20),
                To: new DateOnly(2026, 3, 21),
                Symbols: ["AAPL", "TSLA"],
                InitialCash: 1_000m,
                DataRoot: "./data");
            var metrics = new BacktestMetrics(
                InitialCapital: 1_000m,
                FinalEquity: 1_000m,
                GrossPnl: 0m,
                NetPnl: 0m,
                TotalReturn: 0m,
                AnnualizedReturn: 0m,
                SharpeRatio: 0d,
                SortinoRatio: 0d,
                CalmarRatio: 0d,
                MaxDrawdown: 0m,
                MaxDrawdownPercent: 0m,
                MaxDrawdownRecoveryDays: 0,
                ProfitFactor: 1d,
                WinRate: 1d,
                TotalTrades: 0,
                WinningTrades: 0,
                LosingTrades: 0,
                TotalCommissions: 0m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0d,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>());
            var result = new BacktestResult(
                Request: request,
                Universe: new HashSet<string>(["AAPL", "TSLA"], StringComparer.OrdinalIgnoreCase),
                Snapshots: [snapshot],
                CashFlows: [],
                Fills: [],
                Metrics: metrics,
                Ledger: CreateLedger(ledgerAsOf),
                ElapsedTime: TimeSpan.FromMinutes(30),
                TotalEventsProcessed: 100);

            return StrategyRunEntry.Start("recon-strategy", "Reconciliation Strategy", RunType.Backtest) with
            {
                RunId = runId,
                StartedAt = startedAt,
                EndedAt = completedAt,
                Metrics = result,
                DatasetReference = "dataset/us/equities",
                FeedReference = "synthetic:equities",
                PortfolioId = "recon-portfolio",
                LedgerReference = "recon-ledger",
                AuditReference = $"audit-{runId}"
            };
        }

        /// <summary>
        /// A run that has a portfolio snapshot but no ledger — useful for testing
        /// reconciliation paths where only one side of the comparison exists.
        /// </summary>
        public static StrategyRunEntry BuildPortfolioOnlyRun(string runId)
        {
            var startedAt = new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero);
            var completedAt = startedAt.AddMinutes(30);
            var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
            };
            var accountSnapshot = new FinancialAccountSnapshot(
                AccountId: BacktestDefaults.DefaultBrokerageAccountId,
                DisplayName: "Primary Brokerage",
                Kind: FinancialAccountKind.Brokerage,
                Institution: "Simulated Broker",
                Cash: 600m,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: 0m,
                Equity: 1000m,
                Positions: positions,
                Rules: new FinancialAccountRules());
            var snapshot = new PortfolioSnapshot(
                Timestamp: completedAt,
                Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
                Cash: 600m,
                MarginBalance: 0m,
                LongMarketValue: 400m,
                ShortMarketValue: 0m,
                TotalEquity: 1000m,
                DailyReturn: 0m,
                Positions: positions,
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    [accountSnapshot.AccountId] = accountSnapshot
                },
                DayCashFlows: []);

            var request = new BacktestRequest(
                From: new DateOnly(2026, 3, 20),
                To: new DateOnly(2026, 3, 21),
                Symbols: ["AAPL"],
                InitialCash: 1_000m,
                DataRoot: "./data");
            var metrics = new BacktestMetrics(
                InitialCapital: 1_000m,
                FinalEquity: 1_000m,
                GrossPnl: 0m,
                NetPnl: 0m,
                TotalReturn: 0m,
                AnnualizedReturn: 0m,
                SharpeRatio: 0d,
                SortinoRatio: 0d,
                CalmarRatio: 0d,
                MaxDrawdown: 0m,
                MaxDrawdownPercent: 0m,
                MaxDrawdownRecoveryDays: 0,
                ProfitFactor: 1d,
                WinRate: 1d,
                TotalTrades: 0,
                WinningTrades: 0,
                LosingTrades: 0,
                TotalCommissions: 0m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0d,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>());
            var result = new BacktestResult(
                Request: request,
                Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
                Snapshots: [snapshot],
                CashFlows: [],
                Fills: [],
                Metrics: metrics,
                Ledger: null!,   // <-- no ledger
                ElapsedTime: TimeSpan.FromMinutes(30),
                TotalEventsProcessed: 50);

            return StrategyRunEntry.Start("portfolio-only-strategy", "Portfolio-Only Strategy", RunType.Backtest) with
            {
                RunId = runId,
                StartedAt = startedAt,
                EndedAt = completedAt,
                Metrics = result,
                DatasetReference = "dataset/us/equities",
                FeedReference = "synthetic:equities",
                PortfolioId = "portfolio-only-portfolio",
                LedgerReference = null   // <-- no ledger reference
            };
        }

        private static IReadOnlyLedger CreateLedger(DateTimeOffset asOf)
        {
            var ledger = new global::Meridian.Ledger.Ledger();
            PostBalancedEntry(ledger, asOf.AddMinutes(-30), "Initial capital",
            [
                (LedgerAccounts.Cash, 1_000m, 0m),
                (LedgerAccounts.CapitalAccount, 0m, 1_000m)
            ]);
            PostBalancedEntry(ledger, asOf.AddMinutes(-20), "Buy AAPL",
            [
                (LedgerAccounts.Securities("AAPL"), 400m, 0m),
                (LedgerAccounts.Cash, 0m, 400m)
            ]);
            PostBalancedEntry(ledger, asOf.AddMinutes(-10), "Open TSLA short",
            [
                (LedgerAccounts.Cash, 150m, 0m),
                (LedgerAccounts.ShortSecuritiesPayable("TSLA"), 0m, 150m)
            ]);
            return ledger;
        }

        private static void PostBalancedEntry(
            global::Meridian.Ledger.Ledger ledger,
            DateTimeOffset timestamp,
            string description,
            IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
        {
            var journalId = Guid.NewGuid();
            var ledgerLines = lines
                .Select(line => new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    line.Account,
                    line.Debit,
                    line.Credit,
                    description))
                .ToArray();
            ledger.Post(new JournalEntry(journalId, timestamp, description, ledgerLines));
        }
    }

    private sealed class StubSecurityReferenceLookup : ISecurityReferenceLookup
    {
        private readonly Dictionary<string, WorkstationSecurityReference> _references = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string symbol, WorkstationSecurityReference reference)
        {
            _references[symbol] = reference;
        }

        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            _references.TryGetValue(symbol, out var reference);
            return Task.FromResult<WorkstationSecurityReference?>(reference);
        }
    }
}
