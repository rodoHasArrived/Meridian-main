using FluentAssertions;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.FinancialOperations.Banking;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Banking;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
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
            // Period end is EXCLUSIVE (the adapter supplies the next month's first day), so the
            // 2026-01-31 coupon below is IN this period.
            PeriodEnd: new DateOnly(2026, 2, 1),
            Securities: [new SecurityMasterAccountingSecurity(securityId, "BOND2", "Bond", "USD", new SecurityFixedIncomeTerms(0.06m, "Fixed", "ACT/365", 2, NextCouponDate: new DateOnly(2026, 1, 31), AccrualStartDate: new DateOnly(2026, 1, 1)), new SecurityAccountingRule("AvailableForSale", "GAAP"))],
            Positions: [new SecurityMasterAccountingPosition("BOND2", securityId, "acct-1", 100_000m)]);

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null,
            bankTransactionSource: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(request));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-fixed-coupon"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.AccrueInterestIncome);
        detail.ExpectedAccountingEvents.Should().Contain(item => item.EventKind == ExpectedAccountingEventKindDto.ReceiveCashInterest);
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
            Positions: [new SecurityMasterAccountingPosition("MBS1", securityId, "acct-1", 100_000m, PositionId: Guid.Parse("55555555-3333-4333-8333-333333333334"))],
            FactorSchedule: [new SecurityFactorScheduleEntry(
                securityId,
                new DateOnly(2026, 1, 15),
                1.00m,
                0.99m,
                "test",
                "evidence://factor/test",
                "sha256:test-factor")]);

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null,
            bankTransactionSource: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(request));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-factor-par"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 1_000m);
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
            // Period end is EXCLUSIVE (the adapter supplies the next month's first day), so the
            // 2026-01-31 coupon below is IN this period.
            PeriodEnd: new DateOnly(2026, 2, 1),
            Securities: [new SecurityMasterAccountingSecurity(securityId, "BOND3", "Bond", "USD", new SecurityFixedIncomeTerms(0.06m, "Fixed", "ACT/365", 2, NextCouponDate: new DateOnly(2026, 1, 31), AccrualStartDate: new DateOnly(2026, 1, 1)), new SecurityAccountingRule("AvailableForSale", "GAAP"))],
            Positions: [new SecurityMasterAccountingPosition("BOND3", securityId, "acct-1", 100_000m)],
            ActualActivity: [new SecurityActualCashActivity("custodian", "coupon-row-mismatch", "acct-1", securityId, "BOND3", 1m, 0m, 1m, new DateOnly(2026, 1, 31), "Income")]);

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null,
            bankTransactionSource: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new StaticSecurityMasterAccountingEventSourceAdapter(request));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-mismatch"));

        detail.Should().NotBeNull();
        detail!.SecurityMasterAccountingIssues.Should().Contain(issue => issue.Code == "ACCRUAL_AMOUNT_MISMATCH");
    }

    [Fact]
    public async Task RunAsync_WithMissingEconomicDefinition_ShouldSurfaceRuleMissingBreak()
    {
        // A held security whose Security Master definition lookup MISSES must still reach the
        // event service so it records a High-severity SECURITY_ACCOUNTING_RULE_MISSING
        // completeness break. Filtering the position out (or suppressing the whole accounting
        // result when every lookup misses) would report a clean reconciliation with no expected
        // events and no break for a position the Security Master cannot account for.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-sm-missing-definition"));

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

        // No economic definition is registered: every lookup for the held security misses.
        var securityMasterQuery = new StubSecurityMasterQueryService();

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-missing-definition"));

        detail.Should().NotBeNull();
        detail!.Summary.HasSecurityMasterAccountingIssues.Should().BeTrue();
        (detail.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Should().Contain(issue =>
                issue.Code == "SECURITY_ACCOUNTING_RULE_MISSING" &&
                issue.Severity == ReconciliationBreakSeverity.High);
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
        var positionId = Guid.Parse("44444444-4444-4444-8444-444444444445");
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
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

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
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldUseSecurityMasterFactorScheduleForPaydown()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-factor-schedule"));

        var securityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL mortgage-backed test security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("44444444-4444-4444-8444-444444444446");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 21),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-factor-schedule"));

        detail.Should().NotBeNull();
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().NotContain("FACTOR_SCHEDULE_MISSING");
        detail.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 0.30m &&
            item.Provenance.Contains("factor-source:custodian-factor-file", StringComparison.Ordinal) &&
            item.EconomicEvent!.BookPositionId == positionId);
        detail.ExpectedJournalPreviews.Should().Contain(preview =>
            preview.IsBalanced &&
            preview.Lines.Any(line => line.AccountName == "Cash" && line.Debit == 0.30m));
    }

    [Fact]
    public async Task RunAsync_WithOnlyPrePeriodFactorRows_ShouldReportFactorStaleInsteadOfMissing()
    {
        // A factor-driven security whose retained observations ALL predate the period has
        // evidence that needs REFRESHING, not absent evidence. The adapter retains the latest
        // pre-period row so the coverage classifier reports FACTOR_STALE instead of the
        // indistinguishable FACTOR_SCHEDULE_MISSING — while the paydown generator, which filters
        // to in-period rows itself, still projects no paydown from the stale observation.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-sm-stale-factor"));

        var securityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL mortgage-backed test security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("44444444-4444-4444-8444-444444444447");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 1, 20),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.99m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-01"),
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 2, 18),
                    PriorFactor: 0.99m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-02")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-sm-stale-factor"));

        detail.Should().NotBeNull();
        var issueCodes = (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .ToArray();
        issueCodes.Should().Contain("FACTOR_STALE",
            "evidence exists but its latest observation predates the period");
        issueCodes.Should().NotContain("FACTOR_SCHEDULE_MISSING");
        (detail.ExpectedAccountingEvents ?? Array.Empty<ExpectedAccountingEventDto>())
            .Should().NotContain(static item => item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown,
                "the retained pre-period observation classifies coverage but never projects a paydown");
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldDerivePaydownFromTypedFactorScheduleEntries()
    {
        // A canonical StructuredCredit can persist its factor history solely in the typed
        // factorScheduleEntries array ({asOfDate, factor} rows, no per-row priorFactor). The
        // adapter derives each prior from the ordered preceding entry — here the February 1.00
        // pairs with the in-period March 0.97 — so the record produces the same paydown event a
        // legacy factorSchedule row would.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-typed-factor-entries"));

        var securityId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL mortgage-backed typed-entry security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("66666666-6666-4666-8666-666666666667");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 2, 21), 1.00m),
                (new DateOnly(2026, 3, 21), 0.97m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-typed-factor-entries"));

        detail.Should().NotBeNull();
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().NotContain("FACTOR_SCHEDULE_MISSING");
        detail.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 0.30m &&
            item.EconomicEvent!.BookPositionId == positionId);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldResolvePositionEndingOnLastInPeriodDay()
    {
        // The accounting period end is EXCLUSIVE (April 1 for a March run), so durable positions
        // must resolve as of the LAST in-period day (March 31). A position whose EffectiveTo is
        // March 31 held the paydown through the whole period; resolving at the exclusive boundary
        // itself would filter it out and drop the durable-position linkage from the event.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-period-final-day"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-777777777771");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL period-final-day security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-777777777772");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 21),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(
                    securityId,
                    positionId,
                    positionEffectiveTo: new DateOnly(2026, 3, 31))));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-period-final-day"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.EconomicEvent!.BookPositionId == positionId);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldResolveOwnershipOnObservationDate()
    {
        // Ownership changes mid-month: the original position closes March 15 and a successor in
        // the same account opens March 16. The factor observation is dated March 10, so the
        // paydown belongs to the ORIGINAL position - resolving durable ownership at the period's
        // end would wrongly attach it to the successor that never held the security on the
        // observation date.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-observation-ownership"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL observation-ownership security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-777777777778");
        var successorId = Guid.Parse("77777777-7777-4777-8777-777777777779");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 10),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(
                    securityId,
                    positionId,
                    positionEffectiveTo: new DateOnly(2026, 3, 15),
                    successorPosition: (successorId, new DateOnly(2026, 3, 16)))));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-observation-ownership"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.EconomicEvent!.BookPositionId == positionId);
        detail.ExpectedAccountingEvents.Should().NotContain(item =>
            item.EconomicEvent != null &&
            item.EconomicEvent.BookPositionId == successorId);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldNotAttributeSplitOwnershipObservations()
    {
        // Observations on March 10 and March 20 span an ownership change (original position ends
        // March 15, successor opens March 16): no single durable position held the security for
        // both observation dates, so NO paydown may be attributed to either position - the
        // generator instead fails closed with FACTOR_PAYDOWN_POSITION_REQUIRED so an operator
        // resolves the ownership question rather than a posting candidate carrying the wrong ID.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-split-ownership"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-77777777777a");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL split-ownership security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-77777777777b");
        var successorId = Guid.Parse("77777777-7777-4777-8777-77777777777c");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 10),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.98m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03a"),
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 20),
                    PriorFactor: 0.98m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03b")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(
                    securityId,
                    positionId,
                    positionEffectiveTo: new DateOnly(2026, 3, 15),
                    successorPosition: (successorId, new DateOnly(2026, 3, 16)))));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-split-ownership"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().NotContain(item =>
            item.EconomicEvent != null &&
            (item.EconomicEvent.BookPositionId == positionId || item.EconomicEvent.BookPositionId == successorId));
        (detail.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().Contain("FACTOR_PAYDOWN_POSITION_REQUIRED");
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldSupportCanonicalStructuredCreditClassification()
    {
        // A canonical StructuredCredit record classifies as class/subtype "StructuredCredit" and
        // family "StructuredCash" - none of which the accounting slice previously recognized, so
        // the record failed the fixed-income gate as SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT before
        // any factor coverage or paydown generation could run. The adapter must map it into the
        // supported asset-backed accounting class.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-canonical-structured-credit"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-77777777777d");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL canonical structured-credit security",
            "StructuredCredit",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "StructuredCredit"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-77777777777e");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 21),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "vendor-trustee",
                    EvidenceLink: "factor-evidence-2026-03")
            ],
            assetClass: "StructuredCredit",
            assetFamily: "StructuredCash",
            subType: "StructuredCredit",
            typeName: "StructuredCredit"));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-canonical-structured-credit"));

        detail.Should().NotBeNull();
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().NotContain("SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT");
        detail.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldPreferGovernedEvidencePointerForTypedRows()
    {
        // The governed profileFields.factorSchedule trustee-report pointer must supply the typed
        // rows' retained evidence, not the outer pass-through copy: the paydown projector treats
        // any nonblank link as sufficient evidence, so an outer-first read would stamp the wrong
        // lineage on the expected event and its posting candidate.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-governed-evidence"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-77777777777f");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL governed-evidence security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-777777777780");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 2, 21), 1.00m),
                (new DateOnly(2026, 3, 21), 0.97m)
            ],
            nestedFactorScheduleEvidence: "governed-trustee-report-2026-03"));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-governed-evidence"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.Provenance.Contains("factor-evidence:governed-trustee-report-2026-03", StringComparison.Ordinal));
        detail.ExpectedAccountingEvents.Should().NotContain(item =>
            item.SecurityId == securityId &&
            item.Provenance.Contains("factor-evidence:trustee-report-2026-03", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldFailClosedWhenOwnershipLookupFails()
    {
        // The security HAS in-period factor observations but the Asset Operations read throws:
        // ownership cannot be verified, so no paydown may be attributed to any position - the
        // generator fails closed with FACTOR_PAYDOWN_POSITION_REQUIRED instead of posting against
        // an unverified (possibly supplied and stale) identity during the outage.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-ownership-outage"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-777777777781");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL ownership-outage security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 21),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                new ThrowingAssetOperationsQueryService()));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-ownership-outage"));

        detail.Should().NotBeNull();
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().Contain("FACTOR_PAYDOWN_POSITION_REQUIRED");
        (detail.ExpectedAccountingEvents ?? Array.Empty<ExpectedAccountingEventDto>())
            .Should().NotContain(item =>
                item.SecurityId == securityId &&
                item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldComputePaydownFromDurableOriginalFace()
    {
        // Factor paydowns are relative to ORIGINAL face. The run position's quantity (10) may
        // already be the factor-adjusted current face, so when the durable book position records
        // its original face (20), the paydown must be 20 x 0.03 = 0.60 - not 10 x 0.03 = 0.30.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-original-face"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-777777777773");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL original-face security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-777777777774");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 21),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03")
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(
                    securityId,
                    positionId,
                    originalFaceAmount: 20m)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-original-face"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 0.60m &&
            item.EconomicEvent!.BookPositionId == positionId);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldTreatUnchangedOpeningObservationAsCoverage()
    {
        // A period whose only typed observation is the unchanged 1.00 opening still constitutes
        // factor COVERAGE evidence: the trustee reported, the factor just did not move. The
        // adapter must emit the observation (no FACTOR_SCHEDULE_MISSING) while the event
        // generator skips the zero-change row (no paydown event).
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-unchanged-opening"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-777777777775");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL unchanged-opening security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-777777777776");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 1.00m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 3, 21), 1.00m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-unchanged-opening"));

        detail.Should().NotBeNull();
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().NotContain("FACTOR_SCHEDULE_MISSING");
        (detail.ExpectedAccountingEvents ?? Array.Empty<ExpectedAccountingEventDto>())
            .Should().NotContain(item =>
                item.SecurityId == securityId &&
                item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldPairFirstTypedFactorObservationAgainstParBaseline()
    {
        // A typed schedule whose FIRST retained observation is already below one records a real
        // first paydown: factors are relative to original face, so the 0.97 opening pairs against
        // the implicit 1.00 baseline (canonical validation does not require an explicit 1.00 row).
        // Before the fix the first observation was skipped outright and the paydown never posted.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-first-typed-factor"));

        var securityId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL mortgage-backed first-observation security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("77777777-7777-4777-8777-777777777778");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 3, 21), 0.97m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-first-typed-factor"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 0.30m &&
            // Typed rows carry no per-row source: the canonical provenance's sourceSystem — the
            // vendor that asserted the schedule — is the factor-source lineage, not the generic
            // security-master fallback.
            item.Provenance.Contains("factor-source:vendor-trustee", StringComparison.Ordinal) &&
            item.EconomicEvent!.BookPositionId == positionId);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldGateFactorCoverageFromTypedSchedulePresence()
    {
        // A canonical StructuredCredit may carry its whole factor history in typed
        // factorScheduleEntries with NO scalar factor. In a later month with no in-period
        // observation the request contains no factor rows — the typed schedule's PRESENCE must
        // still mark the security factor-driven so the missing-coverage gate fires instead of
        // silently skipping the amortizing security.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-typed-presence-coverage"));

        var securityId = Guid.Parse("aaaaaaaa-9999-4999-8999-aaaaaaaaaaaa");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL typed-history security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("aaaaaaaa-9999-4999-8999-aaaaaaaaaaab");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: null,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 1, 15), 0.97m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-typed-presence-coverage"));

        detail.Should().NotBeNull();
        // The typed schedule's presence marks the security factor-driven even without a scalar
        // factor, and its only observation predates the period — so the retained pre-period row
        // classifies coverage as STALE (evidence needs refreshing), the sharper break than the
        // absent-evidence FACTOR_SCHEDULE_MISSING it previously collapsed into.
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Should().Contain(issue => issue.Code == "FACTOR_STALE",
                "the typed schedule's presence marks the security factor-driven and its latest observation predates the period");
        detail.ExpectedAccountingEvents.Should().NotContain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldExcludeNextPeriodBoundaryObservations()
    {
        // ResolvePeriod supplies the FIRST DAY of the next month as the period end; that boundary
        // is exclusive, or an April 1 observation would surface identical paydown evidence in both
        // the March and April reconciliations.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-boundary-exclusive"));

        var securityId = Guid.Parse("bbbbbbbb-9999-4999-8999-bbbbbbbbbbbb");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL boundary security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("bbbbbbbb-9999-4999-8999-bbbbbbbbbbbc");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 2, 21), 1.00m),
                (new DateOnly(2026, 4, 1), 0.97m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-boundary-exclusive"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().NotContain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown,
            "the April 1 observation belongs to the April reconciliation only");
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldPreferGovernedProfileFieldsFactorEntries()
    {
        // A profile-backed record persists its governed typed schedule beneath
        // profileFields; an extra outer factorScheduleEntries array on the same envelope is
        // ungoverned pass-through. The governed rows must claim the paydown dates first — here
        // the profileFields 1.00→0.97 pair produces the 0.30 paydown, and the contradictory
        // outer 0.50 row for the same date is suppressed rather than tripling the paydown.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-governed-factor-entries"));

        var securityId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL profile-backed structured security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("88888888-8888-4888-8888-888888888889");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 3, 21), 0.50m)
            ],
            profileFieldsFactorEntries:
            [
                (new DateOnly(2026, 2, 21), 1.00m),
                (new DateOnly(2026, 3, 21), 0.97m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-governed-factor-entries"));

        detail.Should().NotBeNull();
        detail!.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 0.30m);
        detail.ExpectedAccountingEvents.Should().NotContain(item =>
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount != 0.30m);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldIgnoreOuterFactorRowsWhenGovernedRowsExist()
    {
        // Once the governed profileFields schedule exists, the ungoverned OUTER array is excluded
        // entirely — not merely deduplicated by date. Each enumerated array derives its own priors
        // (first observation against the 1.0 baseline), so an outer 0.50 row on a date the
        // governed schedule does not cover would otherwise synthesize a 5.00 paydown the governed
        // 0.97 history contradicts, and that false amount could become a posting candidate.
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-outer-rows-ignored"));

        var securityId = Guid.Parse("99999999-9999-4999-8999-999999999999");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL profile-backed structured security",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "MortgageBackedSecurity"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("99999999-9999-4999-8999-99999999999a");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule: null,
            typedFactorEntries:
            [
                (new DateOnly(2026, 3, 25), 0.50m)
            ],
            profileFieldsFactorEntries:
            [
                (new DateOnly(2026, 2, 21), 1.00m),
                (new DateOnly(2026, 3, 21), 0.97m)
            ]));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-outer-rows-ignored"));

        detail.Should().NotBeNull();
        var paydowns = detail!.ExpectedAccountingEvents
            .Where(item => item.SecurityId == securityId
                && item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown)
            .ToArray();
        paydowns.Should().ContainSingle(
            "only the governed schedule's in-period observation may produce a paydown").Subject
            .PrincipalAmount.Should().Be(0.30m);
    }

    [Fact]
    public async Task RunAsync_WithRealSecurityMasterAccountingAdapter_ShouldPreserveMortgageBackedAssetClassForFactorPaydown()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-real-sm-mbs-factor"));

        var securityId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var lookup = new StubSecurityReferenceLookup();
        lookup.Register("AAPL", new WorkstationSecurityReference(
            securityId,
            "AAPL agency mortgage pool",
            "MortgageBackedSecurity",
            "USD",
            SecurityStatusDto.Active,
            "AAPL",
            SubType: "Mbs"));

        var securityMasterQuery = new StubSecurityMasterQueryService();
        var positionId = Guid.Parse("55555555-5555-4555-8555-555555555556");
        securityMasterQuery.Register(CreateEconomicDefinition(
            securityId,
            "AAPL",
            accountingClassification: "AvailableForSale",
            currentFactor: 0.97m,
            factorSchedule:
            [
                new FactorScheduleSeed(
                    AsOfDate: new DateOnly(2026, 3, 21),
                    PriorFactor: 1.00m,
                    CurrentFactor: 0.97m,
                    Source: "custodian-factor-file",
                    EvidenceLink: "factor-evidence-2026-03")
            ],
            assetClass: "MortgageBackedSecurity",
            assetFamily: "SecuritizedProduct",
            subType: "Mbs",
            typeName: "AgencyMortgageBackedSecurity"));

        var service = CreateService(
            store,
            new InMemoryReconciliationRunRepository(),
            lookup,
            bankTransactionSource: null,
            securityValidationGate: null,
            securityMasterAccountingEventService: new SecurityMasterAccountingEventService(),
            securityMasterAccountingEventSourceAdapter: new SecurityMasterAccountingEventSourceAdapter(
                securityMasterQuery,
                CreateAssetOperationsQueryService(securityId, positionId)));

        var detail = await service.RunAsync(new ReconciliationRunRequest("run-real-sm-mbs-factor"));

        detail.Should().NotBeNull();
        (detail!.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(static issue => issue.Code)
            .Should().NotContain("SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT");
        detail.ExpectedAccountingEvents.Should().Contain(item =>
            item.SecurityId == securityId &&
            item.EventKind == ExpectedAccountingEventKindDto.RecognizePrincipalPaydown &&
            item.PrincipalAmount == 0.30m);
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
    public async Task RunAsync_WhenBreakIsObservedAgain_PreservesFirstObservedTimestamp()
    {
        var firstRunAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(firstRunAt);
        var store = new StrategyRunStore();
        var repository = new InMemoryReconciliationRunRepository();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun(
            "run-break-retry",
            portfolioCashOverride: 950m));
        var service = CreateService(
            store,
            repository,
            securityReferenceLookup: null,
            bankTransactionSource: null,
            timeProvider: clock);

        var first = await service.RunAsync(new ReconciliationRunRequest("run-break-retry"));
        var originalBreak = first!.Breaks.Single(item => item.CheckId == "cash-balance");
        clock.SetUtcNow(firstRunAt.AddHours(6));

        var updated = await service.RunAsync(new ReconciliationRunRequest("run-break-retry"));
        var updatedBreak = updated!.Breaks.Single(item => item.CheckId == "cash-balance");

        originalBreak.FirstObservedAt.Should().Be(firstRunAt);
        updatedBreak.FirstObservedAt.Should().Be(firstRunAt);
        updated.Summary.CreatedAt.Should().Be(firstRunAt.AddHours(6));
        updated.Summary.ReconciliationRunId.Should().NotBe(first.Summary.ReconciliationRunId);
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

    [Fact]
    public void PublicConstructor_PreservesLegacyClrSignatureAndOptionalDefaults()
    {
        var constructor = typeof(ReconciliationRunService).GetConstructor(
        [
            typeof(StrategyRunReadService),
            typeof(ReconciliationProjectionService),
            typeof(IReconciliationRunRepository),
            typeof(IBankTransactionSource),
            typeof(IStrategyLedgerReconciliationSourceAdapter),
            typeof(IStrategyPortfolioReconciliationSourceAdapter),
            typeof(IInternalCashReconciliationSourceAdapter),
            typeof(IExternalStatementReconciliationSourceAdapter),
            typeof(ISecurityValidationGateService),
            typeof(ISecurityMasterAccountingEventService),
            typeof(ISecurityMasterAccountingEventSourceAdapter)
        ]);

        constructor.Should().NotBeNull();
        constructor!.GetParameters().Skip(3).Should().OnlyContain(static parameter =>
            parameter.IsOptional && parameter.DefaultValue == null);

        var timeProviderConstructor = typeof(ReconciliationRunService).GetConstructor(
        [
            typeof(StrategyRunReadService),
            typeof(ReconciliationProjectionService),
            typeof(IReconciliationRunRepository),
            typeof(IBankTransactionSource),
            typeof(IStrategyLedgerReconciliationSourceAdapter),
            typeof(IStrategyPortfolioReconciliationSourceAdapter),
            typeof(IInternalCashReconciliationSourceAdapter),
            typeof(IExternalStatementReconciliationSourceAdapter),
            typeof(ISecurityValidationGateService),
            typeof(ISecurityMasterAccountingEventService),
            typeof(ISecurityMasterAccountingEventSourceAdapter),
            typeof(TimeProvider)
        ]);
        timeProviderConstructor.Should().NotBeNull();
        timeProviderConstructor!.GetParameters().Should().OnlyContain(static parameter =>
            !parameter.IsOptional);
    }

    [Fact]
    public async Task RepositoryDefaultContinuityWrite_FailsClosedForLegacyImplementation()
    {
        IReconciliationRunRepository repository = new LegacyReconciliationRunRepository();
        var detail = BuildRepositoryDetail(
            "reconciliation-legacy-repository",
            "run-legacy-repository",
            new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            repository.SaveWithFirstObservationContinuityAsync(detail));

        exception.Message.Should().Contain("atomic first-observation continuity");
    }

    [Fact]
    public async Task Repository_PreservesLegacyFirstObservationAcrossUnevaluatedGap()
    {
        var repository = new InMemoryReconciliationRunRepository();
        var firstObservedAt = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        var gapAt = firstObservedAt.AddHours(4);
        var repeatedAt = firstObservedAt.AddHours(8);

        await repository.SaveAsync(BuildRepositoryDetail(
            "reconciliation-gap-first",
            "run-gap",
            firstObservedAt,
            breakItem: BuildRepositoryBreak("Cash-Balance", firstObservedAt: null)));
        await repository.SaveAsync(BuildRepositoryDetail(
            "reconciliation-gap-unevaluated",
            "run-gap",
            gapAt));

        var repeated = await repository.SaveWithFirstObservationContinuityAsync(BuildRepositoryDetail(
            "reconciliation-gap-repeated",
            "run-gap",
            repeatedAt,
            breakItem: BuildRepositoryBreak("cash-balance", repeatedAt)));

        repeated.Breaks.Should().ContainSingle();
        repeated.Breaks[0].FirstObservedAt.Should().Be(firstObservedAt);
        repeated.Breaks[0].LogicalBreakIdentity.Should().NotBeNullOrWhiteSpace();
        repeated.Breaks[0].CorrelationKeys!.RunId.Should().Be("run-gap");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repository_ReopenedBreakStartsNewIncidentAfterExplicitMatchOrResolution(
        bool closeWithMatch)
    {
        var repository = new InMemoryReconciliationRunRepository();
        var firstObservedAt = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);
        var closedAt = firstObservedAt.AddHours(1);
        var reopenedAt = firstObservedAt.AddHours(2);
        await repository.SaveAsync(BuildRepositoryDetail(
            "reconciliation-close-first",
            "run-close",
            firstObservedAt,
            breakItem: BuildRepositoryBreak("cash-balance", firstObservedAt)));

        await repository.SaveAsync(closeWithMatch
            ? BuildRepositoryDetail(
                "reconciliation-close-explicit-match",
                "run-close",
                closedAt,
                match: BuildRepositoryMatch("cash-balance"))
            : BuildRepositoryDetail(
                "reconciliation-close-explicit-resolution",
                "run-close",
                closedAt,
                breakItem: BuildRepositoryBreak(
                    "cash-balance",
                    closedAt,
                    ReconciliationBreakStatus.Resolved)));

        var reopened = await repository.SaveWithFirstObservationContinuityAsync(BuildRepositoryDetail(
            "reconciliation-close-reopened",
            "run-close",
            reopenedAt,
            breakItem: BuildRepositoryBreak("cash-balance", reopenedAt)));

        reopened.Breaks.Should().ContainSingle();
        reopened.Breaks[0].FirstObservedAt.Should().Be(reopenedAt);
    }

    [Fact]
    public async Task Repository_UsesNormalizedBankEntityScopeForLogicalBreakIdentity()
    {
        var repository = new InMemoryReconciliationRunRepository();
        var entityA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var entityB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var firstAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);
        var secondAt = firstAt.AddHours(1);
        var repeatedAt = firstAt.AddHours(2);

        var entityAFirst = await repository.SaveWithFirstObservationContinuityAsync(BuildRepositoryDetail(
            "reconciliation-bank-a-first",
            "run-bank-scope",
            firstAt,
            breakItem: BuildRepositoryBreak(
                "BANK-NET-VS-LEDGER-CASH",
                firstAt,
                bankEntityId: entityA,
                sourceScope: " BANK "),
            bankEntityId: entityA));
        var entityBFirst = await repository.SaveWithFirstObservationContinuityAsync(BuildRepositoryDetail(
            "reconciliation-bank-b-first",
            "run-bank-scope",
            secondAt,
            breakItem: BuildRepositoryBreak(
                "bank-net-vs-ledger-cash",
                secondAt,
                bankEntityId: entityB,
                sourceScope: "bank"),
            bankEntityId: entityB));
        var entityARepeated = await repository.SaveWithFirstObservationContinuityAsync(BuildRepositoryDetail(
            "reconciliation-bank-a-repeated",
            "run-bank-scope",
            repeatedAt,
            breakItem: BuildRepositoryBreak(
                "bank-net-vs-ledger-cash",
                repeatedAt,
                bankEntityId: entityA,
                sourceScope: "bank"),
            bankEntityId: entityA));

        var entityABreak = entityAFirst.Breaks.Single();
        var entityBBreak = entityBFirst.Breaks.Single();
        var repeatedEntityABreak = entityARepeated.Breaks.Single();
        entityABreak.LogicalBreakIdentity.Should().Be(repeatedEntityABreak.LogicalBreakIdentity);
        entityABreak.LogicalBreakIdentity.Should().NotBe(entityBBreak.LogicalBreakIdentity);
        repeatedEntityABreak.FirstObservedAt.Should().Be(firstAt);
        entityBBreak.FirstObservedAt.Should().Be(secondAt);
        repeatedEntityABreak.SourceScope.Should().Be("bank");
    }

    [Fact]
    public async Task RunAsync_ExternalStatementSourceSetsUseUnambiguousLogicalIdentity()
    {
        var firstAt = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);
        var secondAt = firstAt.AddHours(1);
        var clock = new MutableTimeProvider(firstAt);
        var store = new StrategyRunStore();
        var repository = new InMemoryReconciliationRunRepository();
        await store.RecordRunAsync(TestRunFactory.BuildReconciliationReadyRun("run-source-scope"));

        var firstService = CreateService(
            store,
            repository,
            securityReferenceLookup: null,
            bankTransactionSource: null,
            externalStatementAdapter: new StaticExternalStatementAdapter(
            [
                new ReconciliationExternalStatementInput("row-1", 5m, firstAt, "a,b"),
                new ReconciliationExternalStatementInput("row-2", 5m, firstAt, "c")
            ]),
            timeProvider: clock);
        var first = await firstService.RunAsync(new ReconciliationRunRequest("run-source-scope"));
        var firstBreak = first!.Breaks.Single(item =>
            item.CheckId == "external-statement-vs-internal-cash");

        clock.SetUtcNow(secondAt);
        var secondService = CreateService(
            store,
            repository,
            securityReferenceLookup: null,
            bankTransactionSource: null,
            externalStatementAdapter: new StaticExternalStatementAdapter(
            [
                new ReconciliationExternalStatementInput("row-3", 5m, secondAt, "a"),
                new ReconciliationExternalStatementInput("row-4", 5m, secondAt, "b,c")
            ]),
            timeProvider: clock);
        var second = await secondService.RunAsync(new ReconciliationRunRequest("run-source-scope"));
        var secondBreak = second!.Breaks.Single(item =>
            item.CheckId == "external-statement-vs-internal-cash");

        firstBreak.SourceScope.Should().NotBe(secondBreak.SourceScope);
        firstBreak.LogicalBreakIdentity.Should().NotBe(secondBreak.LogicalBreakIdentity);
        secondBreak.FirstObservedAt.Should().Be(secondAt);
    }

    private static ReconciliationRunDetail BuildRepositoryDetail(
        string reconciliationRunId,
        string runId,
        DateTimeOffset createdAt,
        ReconciliationBreakDto? breakItem = null,
        ReconciliationMatchDto? match = null,
        Guid? bankEntityId = null)
    {
        var unresolvedBreakCount = breakItem is not null
            && breakItem.Status is not ReconciliationBreakStatus.Matched
                and not ReconciliationBreakStatus.Resolved
            ? 1
            : 0;
        var summary = new ReconciliationRunSummary(
            reconciliationRunId,
            runId,
            createdAt,
            null,
            null,
            match is null ? 0 : 1,
            breakItem is null ? 0 : 1,
            unresolvedBreakCount,
            false,
            0.01m,
            5)
        {
            BankEntityId = bankEntityId
        };
        return new ReconciliationRunDetail(
            summary,
            match is null ? [] : [match],
            breakItem is null ? [] : [breakItem]);
    }

    private static ReconciliationBreakDto BuildRepositoryBreak(
        string checkId,
        DateTimeOffset? firstObservedAt,
        ReconciliationBreakStatus status = ReconciliationBreakStatus.Open,
        Guid? bankEntityId = null,
        string? sourceScope = null) => new(
            checkId,
            "Repository continuity check",
            ReconciliationBreakCategory.AmountMismatch,
            status,
            "ledger",
            1m,
            2m,
            1m,
            ReconciliationBreakSeverity.High,
            "Repository continuity test break.",
            null,
            null)
        {
            FirstObservedAt = firstObservedAt,
            BankEntityId = bankEntityId,
            SourceScope = sourceScope
        };

    private static ReconciliationMatchDto BuildRepositoryMatch(string checkId) => new(
        checkId,
        "Repository continuity check",
        "portfolio",
        "ledger",
        1m,
        1m,
        0m,
        null,
        null);

    private sealed class LegacyReconciliationRunRepository : IReconciliationRunRepository
    {
        public Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ReconciliationRunDetail?> GetByIdAsync(
            string reconciliationRunId,
            CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<ReconciliationRunDetail?> GetLatestForRunAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult<ReconciliationRunDetail?>(null);

        public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([]);
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
        ISecurityMasterAccountingEventSourceAdapter? securityMasterAccountingEventSourceAdapter = null,
        IExternalStatementReconciliationSourceAdapter? externalStatementAdapter = null,
        TimeProvider? timeProvider = null)
    {
        IStrategyRepository strategyRepository = store;
        var portfolioReadService = securityReferenceLookup is null
            ? new PortfolioReadService()
            : new PortfolioReadService(securityReferenceLookup);
        var ledgerReadService = securityReferenceLookup is null
            ? new LedgerReadService()
            : new LedgerReadService(securityReferenceLookup);
        var runReadService = new StrategyRunReadService(strategyRepository, portfolioReadService, ledgerReadService);
        if (timeProvider is null)
        {
            return new ReconciliationRunService(
                runReadService: runReadService,
                projectionService: new ReconciliationProjectionService(),
                repository: repository,
                bankTransactionSource: bankTransactionSource,
                externalStatementAdapter: externalStatementAdapter,
                securityValidationGate: securityValidationGate,
                securityMasterAccountingEventService: securityMasterAccountingEventService,
                securityMasterAccountingEventSourceAdapter: securityMasterAccountingEventSourceAdapter);
        }

        return new ReconciliationRunService(
            runReadService: runReadService,
            projectionService: new ReconciliationProjectionService(),
            repository: repository,
            bankTransactionSource: bankTransactionSource,
            ledgerAdapter: null,
            portfolioAdapter: null,
            internalCashAdapter: null,
            externalStatementAdapter: externalStatementAdapter,
            securityValidationGate: securityValidationGate,
            securityMasterAccountingEventService: securityMasterAccountingEventService,
            securityMasterAccountingEventSourceAdapter: securityMasterAccountingEventSourceAdapter,
            timeProvider: timeProvider);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class StaticExternalStatementAdapter(
        IReadOnlyList<ReconciliationExternalStatementInput> rows)
        : IExternalStatementReconciliationSourceAdapter
    {
        public Task<IReadOnlyList<ReconciliationExternalStatementInput>> GetStatementRowsAsync(
            ReconciliationRunRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(rows);
        }
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

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

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
        decimal? currentFactor = null,
        IReadOnlyList<FactorScheduleSeed>? factorSchedule = null,
        string assetClass = "FixedIncome",
        string assetFamily = "FixedIncome",
        string subType = "CorporateBond",
        string typeName = "CorporateBond",
        IReadOnlyList<(DateOnly AsOfDate, decimal Factor)>? typedFactorEntries = null,
        IReadOnlyList<(DateOnly AsOfDate, decimal Factor)>? profileFieldsFactorEntries = null,
        string? nestedFactorScheduleEvidence = null)
    {
        var commonTerms = JsonSerializer.SerializeToElement(new
        {
            accountingClassification
        });
        var classification = JsonSerializer.SerializeToElement(new
        {
            assetClass,
            assetFamily,
            subType,
            typeName
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
                    notionalBalance = 100_000m,
                    factorSchedule = factorSchedule?.Select(static entry => new
                    {
                        asOfDate = entry.AsOfDate.ToString("yyyy-MM-dd"),
                        priorFactor = entry.PriorFactor,
                        currentFactor = entry.CurrentFactor,
                        source = entry.Source,
                        evidenceLink = entry.EvidenceLink
                    }).ToArray()
                }
        });

        return new SecurityEconomicDefinitionRecord(
            securityId,
            assetClass,
            assetFamily,
            subType,
            typeName,
            IssuerType: null,
            RiskCountry: null,
            SecurityStatusDto.Active,
            $"{symbol} fixed income test security",
            "USD",
            classification,
            commonTerms,
            economicTerms,
            JsonSerializer.SerializeToElement(new { source = "test", sourceSystem = "vendor-trustee" }),
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
            LegacyAssetSpecificTerms: typedFactorEntries is null && profileFieldsFactorEntries is null
                ? null
                : JsonSerializer.SerializeToElement(new
                {
                    factorSchedule = "trustee-report-2026-03",
                    factorScheduleEntries = typedFactorEntries?.Select(static entry => new
                    {
                        asOfDate = entry.AsOfDate.ToString("yyyy-MM-dd"),
                        factor = entry.Factor
                    }).ToArray(),
                    profileFields = profileFieldsFactorEntries is null && nestedFactorScheduleEvidence is null
                        ? null
                        : new
                        {
                            factorSchedule = nestedFactorScheduleEvidence,
                            factorScheduleEntries = profileFieldsFactorEntries?.Select(static entry => new
                            {
                                asOfDate = entry.AsOfDate.ToString("yyyy-MM-dd"),
                                factor = entry.Factor
                            }).ToArray()
                        }
                }));
    }

    private sealed record FactorScheduleSeed(
        DateOnly AsOfDate,
        decimal PriorFactor,
        decimal CurrentFactor,
        string Source,
        string? EvidenceLink);

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
            new InitiatePaymentRequest(750m, new DateOnly(2026, 3, 21), "RECON-TEST", null, "USD"));
        await bankingService.ApprovePaymentAsync(pending.PendingPaymentId,
            new ApprovePaymentRequest("Test approval", "test"));
        await bankingService.RecordPaymentBankEvidenceAsync(pending.PendingPaymentId,
            new RecordPaymentBankEvidenceRequest(
                "BankConfirmation",
                TransactionDate: new DateOnly(2026, 3, 21),
                SettlementDate: new DateOnly(2026, 3, 21),
                Amount: 750m,
                Currency: "USD",
                ExternalRef: "RECON-TEST",
                EvidenceId: "recon-test-bank-confirmation"));

        var service = CreateService(store, new InMemoryReconciliationRunRepository(),
            securityReferenceLookup: null, bankTransactionSource: bankingService);

        var detail = await service.RunAsync(
            new ReconciliationRunRequest("run-bank-match", BankEntityId: entityId));

        detail.Should().NotBeNull();
        detail!.Summary.BankTransactionCount.Should().BeGreaterThan(0);
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
        var bankBreak = detail.Breaks.Single(b => b.CheckId == "bank-ledger-coverage-missing");
        bankBreak.Category.Should().Be(ReconciliationBreakCategory.MissingLedgerCoverage);
        bankBreak.BankEntityId.Should().Be(entityId);
        bankBreak.SourceScope.Should().Be("bank");
        bankBreak.CorrelationKeys!.RunId.Should().Be("run-no-ledger");
        bankBreak.LogicalBreakIdentity.Should().StartWith("reconciliation-break:v1:");
    }

    private static IAssetOperationsQueryService CreateAssetOperationsQueryService(
        Guid securityId,
        Guid positionId,
        DateOnly? positionEffectiveTo = null,
        decimal? originalFaceAmount = null,
        (Guid PositionId, DateOnly EffectiveFrom)? successorPosition = null)
    {
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var ownerNodeId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var roleId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var bookContext = new AccountingBookContextDto(
            ledgerBookId,
            "fund-reconciliation",
            ownerNodeId,
            FundStructureNodeKindDto.Fund,
            "Reconciliation GAAP",
            "USD",
            AccountingBasisKindDto.Gaap,
            "gaap-reconciliation-v1",
            "v1");
        var subject = new AssetOperationSubjectDto(
            securityId,
            "MortgageBackedSecurity",
            "Reconciliation MBS",
            "AAPL",
            ["FactorProcessing"]);
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            "Ready",
            [],
            [],
            [],
            [],
            DateTimeOffset.Parse("2026-03-21T16:30:00Z"),
            "AssetOperations",
            positionId.ToString("D"));
        var position = new BookPositionDto(
            positionId,
            securityId,
            roleId,
            bookContext,
            BookPositionSides.Long,
            "Active",
            new DateOnly(2026, 1, 1),
            EffectiveTo: positionEffectiveTo,
            Version: 7,
            PrimaryAccountId: "unscoped-account",
            CurrentEconomicState: originalFaceAmount is decimal originalFace
                ? new PositionEconomicStateDto(
                    Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
                    positionId,
                    new DateOnly(2026, 3, 1),
                    "USD",
                    Version: 1,
                    OriginalFaceAmount: originalFace)
                : null);
        var bookPositions = new List<BookPositionDto> { position };
        if (successorPosition is (Guid successorId, DateOnly successorFrom))
        {
            bookPositions.Add(new BookPositionDto(
                successorId,
                securityId,
                roleId,
                bookContext,
                BookPositionSides.Long,
                "Active",
                successorFrom,
                Version: 1,
                PrimaryAccountId: "unscoped-account"));
        }

        var detail = new AssetOperationsDetailDto(subject, [], [], [], [], [], [], [], [], readiness, [])
        {
            BookPositions = bookPositions
        };
        return new StaticAssetOperationsQueryService(detail);
    }

    private sealed class ThrowingAssetOperationsQueryService : IAssetOperationsQueryService
    {
        public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
            => throw new InvalidOperationException("asset operations unavailable");

        public Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<AssetOperationsReadinessDto?>(null);
    }

    private sealed class StaticAssetOperationsQueryService(AssetOperationsDetailDto detail)
        : IAssetOperationsQueryService
    {
        public Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<AssetOperationsDetailDto?>(
                detail.Subject.SecurityId == securityId ? detail : null);
        }

        public Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<AssetOperationsReadinessDto?>(
                detail.Subject.SecurityId == securityId ? detail.Readiness : null);
        }
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
