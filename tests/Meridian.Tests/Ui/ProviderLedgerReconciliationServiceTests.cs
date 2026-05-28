using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Accounts;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.ProviderSdk;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ProviderLedgerReconciliationServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_MatchesProviderProjectionToInternalSnapshot()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.Summary.BreakCount.Should().Be(0);
            detail.Checks.Should().Contain(check => check.CheckId == "cash-balance" && check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check => check.CheckId == "securities-market-value" && check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check => check.CheckId == "security-master:AAPL" && check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            var passport = detail.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.ProviderId.Should().Be("alpaca");
            passport.ExternalAccountId.Should().Be("PA-LEDGER");
            passport.SecurityId.Should().Be(Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D"));
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Resolved);
            passport.ConfidenceScore.Should().Be(100m);
            passport.ResolutionSource.Should().Be("provider-position");
            passport.ValidationIssueCodes.Should().BeEmpty();
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.AccountId.Should().Be(fixture.AccountId);
            detail.ShadowBookComparison.MatchedLineCount.Should().Be(5);
            detail.ShadowBookComparison.BreakLineCount.Should().Be(0);
            detail.ShadowBookComparison.UnavailableLineCount.Should().Be(2);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "account-cash" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 50_000m &&
                line.ProviderAmount == 50_000m);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "unrealized-pnl" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 3_750m &&
                line.ProviderAmount == 3_750m);
            File.Exists(detail.Summary.DetailPath).Should().BeTrue("latest reconciliation detail must be retained as evidence");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_AttachesSecurityMasterOverrideHistoryToPassport()
    {
        var root = CreateTempRoot();
        try
        {
            var securityId = Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D");
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                operatorOverridesStore: new StaticOperatorOverridesStore(securityId));

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var passport = detail.SecurityMasterPassports.Should().ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.OverrideHistory.Should().ContainSingle(history =>
                history.Contains("SecurityOverrideApproved", StringComparison.OrdinalIgnoreCase) &&
                history.Contains("Approved", StringComparison.OrdinalIgnoreCase) &&
                history.Contains("reviewer=security-steward", StringComparison.OrdinalIgnoreCase) &&
                history.Contains("reason=provider-symbol-confirmed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_DegradesPassportConfidenceWhenProviderEvidenceIsStale()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            await BackdateBrokerageProjectionAsync(fixture, TimeSpan.FromHours(2));

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(ProviderStaleAfterMinutes: 30, RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().Contain(breakRow => breakRow.Code == "PROVIDER_PROJECTION_STALE");
            var passport = detail.SecurityMasterPassports.Should().ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Resolved);
            passport.ProviderIsStale.Should().BeTrue();
            passport.FreshnessMinutes.Should().BeGreaterThanOrEqualTo(120);
            passport.ConfidenceScore.Should().Be(70m);
            passport.ValidationIssueCodes.Should().Contain("PROVIDER_EVIDENCE_STALE");
            passport.Reason.Should().Contain("Provider evidence is stale");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsStaleSecurityMasterMappingCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true);
            await BackdateBrokerageProjectionAsync(fixture, TimeSpan.FromHours(2));

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(ProviderStaleAfterMinutes: 30, RequestedBy: "ops-user"));

            var passport = detail.SecurityMasterPassports.Should().ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.ValidationIssueCodes.Should().Contain("PROVIDER_EVIDENCE_STALE");
            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var staleCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider Security Master passport" &&
                item.RoutingDetail == passport.SecurityId!.Value.ToString("D")).Subject;

            staleCase.BreakId.Should().StartWith($"provider-ledger-security-master-stale:{fixture.AccountId:N}:");
            staleCase.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            staleCase.Category.Should().Be(ReconciliationBreakCategory.ClassificationGap);
            staleCase.AssignedTo.Should().Be("security-master-steward");
            staleCase.Team.Should().Be("Security Master");
            staleCase.RequiredSignoffRole.Should().Be("Security Master steward");
            staleCase.SignoffStatus.Should().Be("pending-signoff");
            staleCase.ExceptionRoute.Should().Be("security-master/stale-provider-mappings");
            staleCase.ToleranceProfileId.Should().Be("security-master-provider-freshness");
            staleCase.ExplainabilitySummary.Should().Contain("symbol=AAPL");
            staleCase.ExplainabilitySummary.Should().Contain("validationIssues=PROVIDER_EVIDENCE_STALE");
            staleCase.RecommendedAction.Should().Contain("Refresh provider evidence");
            staleCase.BreakExplanation.Should().NotBeNull();
            staleCase.BreakExplanation!.SourceSystems.Should().Contain("Security Master");
            staleCase.BreakExplanation.LedgerImpact.Should().Contain("Ledger close");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_ComparesProviderPositionsWithCustodianStatementLines()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                recordCustodianPosition: true);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.Lines.Should().Contain(line =>
                line.Dimension == "position-quantity:AAPL" &&
                line.InternalSource == "custodian-statement" &&
                line.ProviderSource == "provider-sync" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 100m &&
                line.ProviderAmount == 100m);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "position-market-value:AAPL" &&
                line.InternalSource == "custodian-statement" &&
                line.ProviderSource == "provider-sync" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 18_750m &&
                line.ProviderAmount == 18_750m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsCustodianStatementPositionVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                recordCustodianPosition: true,
                custodianPositionMarketValue: 18_600m);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "SHADOW_BOOK_POSITION_MARKET_VALUE_AAPL_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.ExternalStatementMismatch &&
                breakRow.Symbol == "AAPL" &&
                breakRow.Owner == "fund-accounting" &&
                breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.BreakLineCount.Should().Be(1);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "position-market-value:AAPL" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.InternalAmount == 18_600m &&
                line.ProviderAmount == 18_750m &&
                line.Variance == 150m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_ComparesProviderCashAndIncomeWithBankStatementLines()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                activityAdapter: new IncomeActivityAdapter(),
                recordBankStatement: true,
                bankClosingBalance: 50_000m,
                bankIncomeAmount: 142.25m,
                internalAccruedInterest: null);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.Lines.Should().Contain(line =>
                line.Dimension == "bank-statement-cash" &&
                line.InternalSource == "internal-ledger" &&
                line.ProviderSource == "bank-statement" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 50_000m &&
                line.ProviderAmount == 50_000m &&
                line.Variance == 0m);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "bank-statement-income-cash-flow" &&
                line.InternalSource == "bank-statement" &&
                line.ProviderSource == "provider-activity" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 142.25m &&
                line.ProviderAmount == 142.25m &&
                line.Variance == 0m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsBankStatementCashAndIncomeVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                activityAdapter: new IncomeActivityAdapter(),
                recordBankStatement: true,
                bankClosingBalance: 49_900m,
                bankIncomeAmount: 100m,
                internalAccruedInterest: null);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SHADOW_BOOK_BANK_STATEMENT_CASH_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.CashMismatch &&
                breakRow.Owner == "fund-accounting" &&
                breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SHADOW_BOOK_BANK_STATEMENT_INCOME_CASH_FLOW_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.CashMismatch &&
                breakRow.Owner == "fund-accounting" &&
                breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.Lines.Should().Contain(line =>
                line.Dimension == "bank-statement-cash" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.InternalAmount == 50_000m &&
                line.ProviderAmount == 49_900m &&
                line.Variance == -100m);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "bank-statement-income-cash-flow" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.InternalAmount == 100m &&
                line.ProviderAmount == 142.25m &&
                line.Variance == 42.25m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_MatchesWhenProviderCapabilitiesAreRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountBalances" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountPositions" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:ReconciliationFeed" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:CorporateActions" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.CorporateActionReadiness.ProviderCorporateActionsRoutable.Should().BeTrue();
            detail.CorporateActionReadiness.EquityPositionCount.Should().Be(1);
            detail.CorporateActionReadiness.SecurityResolvedCount.Should().Be(1);
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("splits");
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("dividends");
            detail.CorporateActionReadiness.MissingFeeds.Should().BeEmpty();
            detail.CorporateActionReadiness.Lines.Should().Contain(line =>
                line.Dimension == "equity-corporate-actions" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_BlocksWhenProviderCapabilityIsNotRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: false));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Blocked);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "PROVIDER_CAPABILITY_UNROUTABLE" &&
                breakRow.CheckId == "provider-capability:ReconciliationFeed" &&
                breakRow.Category == ReconciliationBreakCategory.MissingPortfolioCoverage);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:ReconciliationFeed" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_DegradesWhenCorporateActionsAreNotRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.CorporateActions));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "PROVIDER_CORPORATE_ACTION_CAPABILITY_MISSING" &&
                breakRow.CheckId == "provider-capability:CorporateActions" &&
                breakRow.Severity == ReconciliationBreakSeverity.Medium);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountBalances" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:CorporateActions" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.ProviderCorporateActionsRoutable.Should().BeFalse();
            detail.CorporateActionReadiness.MissingFeeds.Should().Contain("provider-corporate-actions");
            detail.CorporateActionReadiness.Warnings.Should().Contain(warning =>
                warning.Contains("Provider corporate-action capability is not routable", StringComparison.OrdinalIgnoreCase));
            detail.CorporateActionReadiness.Lines.Should().Contain(line =>
                line.Dimension == "equity-corporate-actions" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "EquityCorporateActionCandidate" &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                candidate.RequiredFeed == "splits,dividends");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_DegradesWhenHeldAssetClassPositionsAreNotRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new AssetClassPositionCapabilityRouter("Equity"));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountPositions" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountPositions:equity" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                check.Label.Contains("Equity positions", StringComparison.OrdinalIgnoreCase));
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "PROVIDER_ASSET_CLASS_POSITION_CAPABILITY_MISSING" &&
                breakRow.CheckId == "provider-capability:AccountPositions:equity" &&
                breakRow.Severity == ReconciliationBreakSeverity.Medium);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_DegradesWhenQuoteHistoryIsNotRoutableForValuationMarks()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.HistoricalQuotes));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:HistoricalQuotes:equity" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                check.Label.Contains("Equity valuation marks", StringComparison.OrdinalIgnoreCase));
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "PROVIDER_QUOTE_HISTORY_CAPABILITY_MISSING" &&
                breakRow.CheckId == "provider-capability:HistoricalQuotes:equity" &&
                breakRow.Severity == ReconciliationBreakSeverity.Medium);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SurfacesFactorScheduleCandidatesForFixedIncomePositions()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter());

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:FactorSchedule" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.CorporateActionReadiness.FixedIncomeOrStructuredPositionCount.Should().Be(1);
            detail.CorporateActionReadiness.FactorScheduleCandidateCount.Should().Be(1);
            detail.CorporateActionReadiness.FactorScheduleRoutable.Should().BeTrue();
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("factor-schedule");
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("coupon-schedule");
            detail.CorporateActionReadiness.Lines.Should().Contain(line =>
                line.Dimension == "factor-schedule" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.Count == 1);
            var candidate = detail.CorporateActionReadiness.EvidenceCandidates.Should().ContainSingle(item =>
                item.CandidateType == "FactorScheduleCandidate" &&
                item.Symbol == "UST10Y").Subject;
            candidate.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
            candidate.RequiredFeed.Should().Be("factor-schedule,coupon-schedule");
            candidate.EvidenceSource.Should().Be("provider-position");
            candidate.SecurityId.Should().Be(Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D"));
            candidate.Amount.Should().Be(9_900m);
            candidate.Quantity.Should().Be(10m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_DegradesWhenFactorScheduleCapabilityIsNotRoutable()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.FactorSchedule),
                portfolioAdapter: new FixedIncomePortfolioAdapter());

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "PROVIDER_FACTOR_SCHEDULE_CAPABILITY_MISSING" &&
                breakRow.CheckId == "provider-capability:FactorSchedule" &&
                breakRow.Severity == ReconciliationBreakSeverity.Medium);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:CorporateActions" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:FactorSchedule" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.ProviderCorporateActionsRoutable.Should().BeTrue();
            detail.CorporateActionReadiness.FactorScheduleRoutable.Should().BeFalse();
            detail.CorporateActionReadiness.MissingFeeds.Should().Contain("factor-schedule");
            detail.CorporateActionReadiness.Warnings.Should().Contain(warning =>
                warning.Contains("Provider factor-schedule capability is not routable", StringComparison.OrdinalIgnoreCase));
            detail.CorporateActionReadiness.Lines.Should().Contain(line =>
                line.Dimension == "factor-schedule" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "FactorScheduleCandidate" &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                candidate.RequiredFeed == "factor-schedule,coupon-schedule" &&
                candidate.Reason.Contains("factor-schedule capability is not routable", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_RetainsProviderCorporateActionAndFactorEvents()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new CorporateActionActivityAdapter());

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.ProviderCorporateActionEventCount.Should().Be(1);
            detail.CorporateActionReadiness.FactorScheduleEventCount.Should().Be(1);
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("provider-corporate-actions");
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("factor-schedule");
            detail.CorporateActionReadiness.Lines.Should().Contain(line =>
                line.Dimension == "provider-corporate-action-events" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.Count == 1);
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "FactorScheduleEvent" &&
                candidate.ProviderEventId == "factor-ust10y-20260501" &&
                candidate.EvidenceSource == "provider-corporate-action" &&
                candidate.RequiredFeed == "factor-schedule" &&
                candidate.Amount == 0.9825m &&
                candidate.Symbol == "UST10Y" &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.CorporateActionReadiness.LedgerEffects.Should().Contain(effect =>
                effect.CandidateType == "FactorScheduleEvent" &&
                effect.ProviderEventId == "factor-ust10y-20260501" &&
                effect.LedgerEffectKind == "FactorScheduleValuationInput" &&
                effect.Factor == 0.9825m &&
                effect.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                effect.JournalLines.Count == 0);
            var factorFeed = detail.CorporateActionReadiness.SecurityMasterScheduleFeeds.Should()
                .ContainSingle(feed => feed.ProviderEventId == "factor-ust10y-20260501").Subject;
            factorFeed.FeedKind.Should().Be("SecurityMasterFactorHistory");
            factorFeed.RequiredFeed.Should().Be("factor-schedule");
            factorFeed.SecurityId.Should().NotBeNull();
            factorFeed.Factor.Should().Be(0.9825m);
            factorFeed.CanUpdateSecurityMaster.Should().BeTrue();
            factorFeed.CanSupportLedgerValuation.Should().BeTrue();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_RetainsProviderLoanScheduleEvents()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new LoanScheduleActivityAdapter());

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.ProviderCorporateActionEventCount.Should().Be(1);
            detail.CorporateActionReadiness.FactorScheduleEventCount.Should().Be(0);
            detail.CorporateActionReadiness.LoanScheduleEventCount.Should().Be(1);
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("loan-schedule");
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("factor-schedule");
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "LoanScheduleEvent" &&
                candidate.ProviderEventId == "loan-schedule-ust10y-20260501" &&
                candidate.RequiredFeed == "loan-schedule,factor-schedule" &&
                candidate.EvidenceSource == "provider-corporate-action" &&
                candidate.Amount == 250m &&
                candidate.Quantity == 0.9825m &&
                candidate.Symbol == "UST10Y" &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            var loanEffect = detail.CorporateActionReadiness.LedgerEffects.Should().ContainSingle(effect =>
                effect.CandidateType == "LoanScheduleEvent" &&
                effect.ProviderEventId == "loan-schedule-ust10y-20260501" &&
                effect.LedgerEffectKind == "LoanScheduleValuationInput").Subject;
            loanEffect.CashAmount.Should().Be(250m);
            loanEffect.PrincipalAmount.Should().Be(250m);
            loanEffect.Factor.Should().Be(0.9825m);
            loanEffect.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
            loanEffect.JournalLines.Should().BeEmpty();
            var loanFeed = detail.CorporateActionReadiness.SecurityMasterScheduleFeeds.Should()
                .ContainSingle(feed => feed.ProviderEventId == "loan-schedule-ust10y-20260501").Subject;
            loanFeed.FeedKind.Should().Be("SecurityMasterLoanSchedule");
            loanFeed.RequiredFeed.Should().Be("loan-schedule,factor-schedule");
            loanFeed.SecurityId.Should().NotBeNull();
            loanFeed.PrincipalAmount.Should().Be(250m);
            loanFeed.Factor.Should().Be(0.9825m);
            loanFeed.CanUpdateSecurityMaster.Should().BeTrue();
            loanFeed.CanSupportLedgerValuation.Should().BeTrue();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_RetainsIncomeCashActivityCandidates()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                activityAdapter: new IncomeActivityAdapter(),
                internalAccruedInterest: null);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.IncomeCashTransactionCount.Should().Be(2);
            detail.CorporateActionReadiness.DividendCashTransactionCount.Should().Be(1);
            detail.CorporateActionReadiness.InterestCashTransactionCount.Should().Be(1);
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("income-cash-activity");
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "DividendCashActivity" &&
                candidate.ProviderEventId == "cash-dividend-aapl" &&
                candidate.Symbol == "AAPL" &&
                candidate.Amount == 125m &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "InterestCashActivity" &&
                candidate.ProviderEventId == "cash-interest-aapl" &&
                candidate.Symbol == "AAPL" &&
                candidate.Amount == 17.25m &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            var dividendEffect = detail.CorporateActionReadiness.LedgerEffects.Should().ContainSingle(effect =>
                effect.CandidateType == "DividendCashActivity" &&
                effect.ProviderEventId == "cash-dividend-aapl" &&
                effect.LedgerEffectKind == "DividendIncomeRecognition").Subject;
            dividendEffect.IncomeAmount.Should().Be(125m);
            dividendEffect.JournalLines.Should().Contain(line =>
                line.AccountName == "Cash" &&
                line.Debit == 125m);
            dividendEffect.JournalLines.Should().Contain(line =>
                line.AccountName == "Dividend Income" &&
                line.Symbol == "AAPL" &&
                line.Credit == 125m);
            detail.CorporateActionReadiness.LedgerEffects.Should().Contain(effect =>
                effect.CandidateType == "InterestCashActivity" &&
                effect.ProviderEventId == "cash-interest-aapl" &&
                effect.LedgerEffectKind == "CashIncomeRecognition" &&
                effect.IncomeAmount == 17.25m &&
                effect.JournalLines.Count == 2);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_RetainsPrincipalPaydownCashActivityCandidates()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new PrincipalActivityAdapter());

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.PrincipalCashTransactionCount.Should().Be(1);
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("principal-cash-activity");
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("factor-schedule");
            detail.CorporateActionReadiness.Lines.Should().Contain(line =>
                line.Dimension == "principal-cash-activity" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.Count == 1);
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "PrincipalCashActivity" &&
                candidate.ProviderEventId == "principal-paydown-ust10y" &&
                candidate.RequiredFeed == "principal-cash-activity,factor-schedule" &&
                candidate.Amount == 250m &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            var principalEffect = detail.CorporateActionReadiness.LedgerEffects.Should().ContainSingle(effect =>
                effect.CandidateType == "PrincipalCashActivity" &&
                effect.ProviderEventId == "principal-paydown-ust10y" &&
                effect.LedgerEffectKind == "PrincipalReturnRecognition").Subject;
            principalEffect.PrincipalAmount.Should().Be(250m);
            principalEffect.IncomeAmount.Should().BeNull();
            principalEffect.JournalLines.Should().Contain(line =>
                line.AccountName == "Cash" &&
                line.Debit == 250m);
            principalEffect.JournalLines.Should().Contain(line =>
                line.AccountName == "Investment Principal" &&
                line.Symbol == "UST10Y" &&
                line.Credit == 250m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsCashVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalCash: 49_900m);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            var cashBreak = detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "CASH_BALANCE_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.CashMismatch &&
                breakRow.Variance == 100m).Subject;
            cashBreak.BreakKey.Should().NotBeNullOrWhiteSpace();
            cashBreak.Owner.Should().Be("fund-accounting");
            cashBreak.Tolerance.Should().Be(0.01m);
            cashBreak.SignOffState.Should().Be(ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            cashBreak.FirstObservedAt.Should().NotBeNull();
            cashBreak.LastObservedAt.Should().NotBeNull();
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.Lines.Should().Contain(line =>
                line.Dimension == "account-cash" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.Variance == 100m);
            detail.Summary.OpenBreakCount.Should().BeGreaterThan(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsShadowBookUnrealizedPnlVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalUnrealizedPnl: 3_100m);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "SHADOW_BOOK_UNREALIZED_PNL_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.AmountMismatch &&
                breakRow.Severity == ReconciliationBreakSeverity.High &&
                breakRow.Owner == "fund-accounting" &&
                breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.BreakLineCount.Should().Be(1);
            detail.ShadowBookComparison.UnavailableLineCount.Should().Be(2);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "unrealized-pnl" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.InternalAmount == 3_100m &&
                line.ProviderAmount == 3_750m &&
                line.Variance == 650m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_MatchesShadowBookRealizedPnlWhenProviderReportsIt()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalRealizedPnl: 925m,
                activityAdapter: new RealizedPnlActivityAdapter(925m));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.MatchedLineCount.Should().Be(6);
            detail.ShadowBookComparison.BreakLineCount.Should().Be(0);
            detail.ShadowBookComparison.UnavailableLineCount.Should().Be(1);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "realized-pnl" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 925m &&
                line.ProviderAmount == 925m &&
                line.Variance == 0m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsShadowBookRealizedPnlVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalRealizedPnl: 900m,
                activityAdapter: new RealizedPnlActivityAdapter(925m));

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "SHADOW_BOOK_REALIZED_PNL_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.AmountMismatch &&
                breakRow.Severity == ReconciliationBreakSeverity.High &&
                breakRow.Owner == "fund-accounting" &&
                breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.BreakLineCount.Should().Be(1);
            detail.ShadowBookComparison.UnavailableLineCount.Should().Be(1);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "realized-pnl" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.InternalAmount == 900m &&
                line.ProviderAmount == 925m &&
                line.Variance == 25m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsDurableBreakQueueCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true);

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var cashCase = cases.Should().ContainSingle(item =>
                item.Category == ReconciliationBreakCategory.CashMismatch &&
                item.FundAccountId == fixture.AccountId.ToString("D")).Subject;

            cashCase.BreakId.Should().StartWith($"provider-ledger:{fixture.AccountId:N}:");
            cashCase.RunId.Should().Be(detail.Summary.ReconciliationRunId.ToString("N"));
            cashCase.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            cashCase.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Open);
            cashCase.AssignedTo.Should().Be("fund-accounting");
            cashCase.ToleranceBand.Should().Be(0.01m);
            cashCase.RequiredSignoffRole.Should().Be("Fund accounting");
            cashCase.SignoffStatus.Should().Be("assigned");
            cashCase.ExceptionRoute.Should().Be("accounting/reconciliation/provider-ledger");
            cashCase.RoutingTarget.Should().Be($"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest");
            cashCase.UpstreamSyncCursor.Should().Contain("alpaca");
            cashCase.LastUpstreamSyncAt.Should().Be(detail.Summary.ProviderSyncedAt);
            cashCase.ExplainabilitySummary.Should().Contain("variance=100");
            cashCase.BreakExplanation.Should().NotBeNull();
            cashCase.BreakExplanation!.SourceSystems.Should().Contain("alpaca");
            cashCase.BreakExplanation.SourceSystems.Should().Contain("Meridian ledger");
            cashCase.BreakExplanation.SourceSystems.Should().Contain("Security Master");
            cashCase.BreakExplanation.ProbableCause.ToLowerInvariant().Should().Contain("cash");
            cashCase.BreakExplanation.LedgerImpact.ToLowerInvariant().Should().Contain("variance 100");
            cashCase.BreakExplanation.SuggestedNextAction.ToLowerInvariant().Should().Contain("provider evidence");
            cashCase.BreakExplanation.EvidenceLinks.Should().Contain(cashCase.RoutingTarget!);
            cashCase.BreakExplanation.EvidenceLinks.Should().Contain(cashCase.UpstreamSyncCursor!);

            var audit = await repository.GetAuditHistoryAsync(cashCase.BreakId);
            audit.Should().ContainSingle(entry =>
                entry.EventType == "Seeded" &&
                entry.BreakId == cashCase.BreakId &&
                entry.SignoffStatus == "assigned");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsCorporateActionCandidateCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.CorporateActions),
                includeBreakQueue: true);

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var candidate = detail.CorporateActionReadiness!.EvidenceCandidates.Should().ContainSingle(item =>
                item.CandidateType == "EquityCorporateActionCandidate" &&
                item.Status == ProviderLedgerReconciliationCheckStatusDto.Break).Subject;
            var candidateCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider corporate-action evidence" &&
                item.RoutingDetail == candidate.CandidateId).Subject;

            candidateCase.BreakId.Should().StartWith($"provider-ledger-corporate-action:{fixture.AccountId:N}:");
            candidateCase.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            candidateCase.Category.Should().Be(ReconciliationBreakCategory.MissingPortfolioCoverage);
            candidateCase.Severity.Should().Be(ReconciliationBreakSeverity.Medium);
            candidateCase.AssignedTo.Should().Be("security-master-steward");
            candidateCase.RequiredSignoffRole.Should().Be("Security Master steward");
            candidateCase.SignoffStatus.Should().Be("pending-signoff");
            candidateCase.ExceptionRoute.Should().Be("accounting/reconciliation/provider-ledger/corporate-actions");
            candidateCase.RoutingTarget.Should().Be($"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest");
            candidateCase.ExplainabilitySummary.Should().Contain("candidate=EquityCorporateActionCandidate");
            candidateCase.ExplainabilitySummary.Should().Contain("requiredFeed=splits,dividends");
            candidateCase.ExplainabilitySummary.Should().Contain("ledgerEffect=CorporateActionCoverageInput");
            candidateCase.ExplainabilitySummary.Should().Contain("journalLines=0");
            candidateCase.RecommendedAction.Should().Contain("Security Master attribution");
            candidateCase.BreakExplanation.Should().NotBeNull();
            candidateCase.BreakExplanation!.SourceSystems.Should().Contain("Provider corporate-action/factor evidence");
            candidateCase.BreakExplanation.ProbableCause.Should().Contain(candidate.Reason);
            candidateCase.BreakExplanation.LedgerImpact.Should().Contain("ledger accrual");
            candidateCase.BreakExplanation.SuggestedNextAction.Should().Contain("Security Master attribution");
            candidateCase.BreakExplanation.EvidenceLinks.Should().Contain(candidate.CandidateId);

            var audit = await repository.GetAuditHistoryAsync(candidateCase.BreakId);
            audit.Should().ContainSingle(entry =>
                entry.EventType == "Seeded" &&
                entry.BreakId == candidateCase.BreakId &&
                entry.RequiredSignoffRole == "Security Master steward");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsIncomeCaseworkWithLedgerEffectMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.CorporateActions),
                activityAdapter: new IncomeActivityAdapter(),
                includeBreakQueue: true);

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var dividendCandidate = detail.CorporateActionReadiness!.EvidenceCandidates.Should().ContainSingle(item =>
                item.CandidateType == "DividendCashActivity" &&
                item.ProviderEventId == "cash-dividend-aapl" &&
                item.Status == ProviderLedgerReconciliationCheckStatusDto.Break).Subject;
            var dividendCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider corporate-action evidence" &&
                item.RoutingDetail == dividendCandidate.CandidateId).Subject;

            dividendCase.ExplainabilitySummary.Should().Contain("candidate=DividendCashActivity");
            dividendCase.ExplainabilitySummary.Should().Contain("providerEventId=cash-dividend-aapl");
            dividendCase.ExplainabilitySummary.Should().Contain("evidenceSource=provider-activity");
            dividendCase.ExplainabilitySummary.Should().Contain("ledgerEffect=DividendIncomeRecognition");
            dividendCase.ExplainabilitySummary.Should().Contain("incomeAmount=125");
            dividendCase.ExplainabilitySummary.Should().Contain("journalLines=0");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsPrincipalCaseworkWithLedgerEffectMetadata()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.FactorSchedule),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new PrincipalActivityAdapter(),
                includeBreakQueue: true);

            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var principalCandidate = detail.CorporateActionReadiness!.EvidenceCandidates.Should().ContainSingle(item =>
                item.CandidateType == "PrincipalCashActivity" &&
                item.ProviderEventId == "principal-paydown-ust10y" &&
                item.Status == ProviderLedgerReconciliationCheckStatusDto.Break).Subject;
            var principalCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider corporate-action evidence" &&
                item.RoutingDetail == principalCandidate.CandidateId).Subject;

            principalCase.ExplainabilitySummary.Should().Contain("candidate=PrincipalCashActivity");
            principalCase.ExplainabilitySummary.Should().Contain("providerEventId=principal-paydown-ust10y");
            principalCase.ExplainabilitySummary.Should().Contain("requiredFeed=principal-cash-activity,factor-schedule");
            principalCase.ExplainabilitySummary.Should().Contain("ledgerEffect=PrincipalReturnRecognition");
            principalCase.ExplainabilitySummary.Should().Contain("principalAmount=250");
            principalCase.ExplainabilitySummary.Should().Contain("journalLines=0");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SignedOffBreakUpdatesDurableCase()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true);

            var firstDetail = await fixture.Reconciliation.RunAsync(fixture.AccountId);
            var firstCashBreak = firstDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(
                    RequestedBy: "ops-user",
                    SignedOffBreakKeys: [firstCashBreak.BreakKey!],
                    SignedOffBy: "controller"));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var caseId = $"provider-ledger:{fixture.AccountId:N}:{firstCashBreak.BreakKey!.Replace(':', '-').ToLowerInvariant()}";
            var updated = await repository.GetByIdAsync(caseId);

            updated.Should().NotBeNull();
            updated!.Status.Should().Be(ReconciliationBreakQueueStatus.Resolved);
            updated.SignoffStatus.Should().Be("signed-off");
            updated.ResolvedBy.Should().Be("controller");
            updated.SignoffHistory.Should().ContainSingle(record =>
                record.Actor == "controller" &&
                record.Role == "Fund accounting");

            var audit = await repository.GetAuditHistoryAsync(caseId);
            audit.Select(entry => entry.EventType).Should().Contain("Seeded");
            audit.Select(entry => entry.EventType).Should().Contain("ReviewStarted");
            audit.Select(entry => entry.EventType).Should().Contain("Resolved");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_PreservesBreakAgingAndSignOffState()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalCash: 49_900m);

            var firstDetail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(DefaultBreakOwner: "fund-controller"));
            var firstCashBreak = firstDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            var signedOffDetail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(
                    DefaultBreakOwner: "fund-controller",
                    SignedOffBreakKeys: [firstCashBreak.BreakKey!],
                    SignedOffBy: "controller"));
            var signedOffCashBreak = signedOffDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            signedOffCashBreak.BreakKey.Should().Be(firstCashBreak.BreakKey);
            signedOffCashBreak.Owner.Should().Be("fund-controller");
            signedOffCashBreak.FirstObservedAt.Should().Be(firstCashBreak.FirstObservedAt);
            signedOffCashBreak.SignOffState.Should().Be(ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff);
            signedOffCashBreak.SignedOffBy.Should().Be("controller");
            signedOffCashBreak.SignedOffAt.Should().NotBeNull();
            signedOffDetail.Summary.SignedOffBreakCount.Should().BeGreaterThan(0);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsSecuritiesMarketValueVariance()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalSecuritiesMarketValue: 18_000m);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SECURITIES_MARKET_VALUE_MISMATCH" &&
                breakRow.Category == ReconciliationBreakCategory.AmountMismatch &&
                breakRow.Variance == 750m);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_BlocksWhenRequiredSourcesAreMissing()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, runProviderSync: false, recordInternalSnapshot: false);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Blocked);
            detail.Breaks.Should().Contain(breakRow => breakRow.Code == "PROVIDER_PROJECTION_MISSING");
            detail.Breaks.Should().Contain(breakRow => breakRow.Code == "INTERNAL_LEDGER_SNAPSHOT_MISSING");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsSecurityMasterCoverageGap()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: false);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Summary.SecurityIssueCount.Should().Be(1);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SM_PROVIDER_POSITION_SECURITY_UNRESOLVED" &&
                breakRow.Symbol == "AAPL" &&
                breakRow.Category == ReconciliationBreakCategory.ClassificationGap);
            var passport = detail.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Unresolved);
            passport.ConfidenceScore.Should().Be(0m);
            passport.ResolutionSource.Should().Be("unresolved");
            passport.SecurityId.Should().BeNull();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsSecurityMasterCaseworkForUnresolvedProviderSymbols()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: false,
                includeBreakQueue: true);

            var detail = await fixture.Reconciliation.RunAsync(fixture.AccountId);

            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SM_PROVIDER_POSITION_SECURITY_UNRESOLVED" &&
                breakRow.Symbol == "AAPL");
            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var securityCase = cases.Should().ContainSingle(item =>
                item.Category == ReconciliationBreakCategory.ClassificationGap &&
                item.ExplainabilitySummary != null &&
                item.ExplainabilitySummary.Contains(
                    "code=SM_PROVIDER_POSITION_SECURITY_UNRESOLVED",
                    StringComparison.OrdinalIgnoreCase) == true).Subject;

            securityCase.RequiredSignoffRole.Should().Be("Security Master steward");
            securityCase.AssignedTo.Should().Be("security-master-steward");
            securityCase.Team.Should().Be("Security Master");
            securityCase.ExceptionRoute.Should().Be("security-master/unresolved-provider-symbols");
            securityCase.ToleranceProfileId.Should().Be("security-master-identity");
            securityCase.SignoffStatus.Should().Be("assigned");
            securityCase.LifecycleRationale.Should().Contain("unresolved provider Security Master identity");
            securityCase.RecommendedAction.Should().Contain("Security Master identity");
            securityCase.BreakExplanation.Should().NotBeNull();
            securityCase.BreakExplanation!.SourceSystems.Should().Contain("Security Master");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ProviderLedgerReconciliation_PostCreatesDetailAndGetReturnsLatest()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            await using var app = await CreateEndpointAppAsync(fixture);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await response.Content.ReadFromJsonAsync<ProviderLedgerReconciliationDetailDto>(JsonOptions);
            created.Should().NotBeNull();
            created!.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);

            var latest = await client.GetFromJsonAsync<ProviderLedgerReconciliationDetailDto>(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest",
                JsonOptions);
            latest.Should().NotBeNull();
            latest!.Summary.ReconciliationRunId.Should().Be(created.Summary.ReconciliationRunId);
            latest.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item =>
                item.Symbol == "AAPL" &&
                item.Status == ProviderSecurityMasterPassportStatusDto.Resolved &&
                item.ResolutionSource == "provider-position");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ProviderLedgerReconciliation_RequiresBrokerageSyncPermissions()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            await using var app = await CreateEndpointAppAsync(fixture, UserPermission.ManageDirectLending);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_IsReadyWhenProviderLedgerAndApprovalsClear()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().HaveCount(6);
            readiness.Components.Should().OnlyContain(component => component.Status == FundAccountCloseReadinessStatusDto.Ready);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Score == 10 &&
                component.Weight == 10);
            readiness.Blockers.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_BlocksWhenHeldSecurityHasOpenSecurityMasterCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));
            var detail = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));
            var securityId = detail.SecurityMasterPassports.Should().ContainSingle(passport => passport.Symbol == "AAPL").Subject.SecurityId;
            securityId.Should().NotBeNull();

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var detectedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
            await repository.CreateIfMissingAsync(new ReconciliationBreakQueueItem(
                BreakId: $"security-master:override:{securityId!.Value:N}",
                RunId: "security-master-overrides",
                StrategyName: "Security Master exception casework",
                Category: ReconciliationBreakCategory.ClassificationGap,
                Status: ReconciliationBreakQueueStatus.Open,
                Variance: 0m,
                Reason: "Security Master operator override requires approval.",
                AssignedTo: "security-master-steward",
                DetectedAt: detectedAt,
                LastUpdatedAt: detectedAt,
                Severity: ReconciliationBreakSeverity.High,
                ExceptionRoute: "security-master/operator-overrides",
                ToleranceProfileId: "security-master-override",
                RequiredSignoffRole: "Security Master steward",
                SignoffStatus: "pending-signoff",
                RoutingTarget: $"/api/security-master/{securityId.Value:D}/operator-overrides",
                RoutingDetail: securityId.Value.ToString("D"),
                RecommendedAction: "Approve or reject the operator override before close.",
                Team: "Security Master",
                UpstreamSyncCursor: $"security-master-override:{securityId.Value:N}:Pending"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().Be(88);
            readiness.Components.Should().Contain(component =>
                component.Key == "approvals-casework" &&
                component.Status == FundAccountCloseReadinessStatusDto.Blocked &&
                component.Score == 0);
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.approvals.critical_pending" &&
                blocker.Category == "Approvals");
            readiness.NextActions.Should().Contain(action => action.Code == "close.approvals.critical_pending");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_BlocksWhenProviderLedgerEvidenceMissing()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                runProviderSync: false,
                recordInternalSnapshot: true);

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().BeLessThan(100);
            readiness.Blockers.Should().Contain(blocker => blocker.Code == "close.reconciliation.missing");
            readiness.Components.Should().Contain(component =>
                component.Key == "provider-ledger-reconciliation" &&
                component.Status == FundAccountCloseReadinessStatusDto.Blocked);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_RequiresReviewWhenShadowBookComparisonBreaks()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                recordCustodianPosition: true,
                custodianPositionMarketValue: 18_600m);
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.ReviewRequired);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().Be(82);
            readiness.Components.Should().Contain(component =>
                component.Key == "provider-ledger-reconciliation" &&
                component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired &&
                component.Score == 12);
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.reconciliation.breaks_open" &&
                blocker.Category == "Reconciliation");
            readiness.NextActions.Should().Contain(action => action.Code == "close.reconciliation.breaks_open");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_RequiresCorporateActionReviewWhenCapabilityIsDegraded()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.CorporateActions),
                includeBreakQueue: true);
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.ReviewRequired);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().BeLessThan(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired &&
                component.Score == 5);
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.corporate_actions.review" &&
                blocker.Category == "CorporateActions");
            readiness.NextActions.Should().Contain(action => action.Code == "close.corporate_actions.review");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_RequiresDirectFactorEvidenceForFixedIncomeClose()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter());
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.ReviewRequired);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().Be(95);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired &&
                component.Score == 5 &&
                component.Reason.Contains("direct provider factor-schedule, loan-schedule, or principal/paydown evidence", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.corporate_actions.factor_evidence_missing" &&
                blocker.Category == "CorporateActions");
            readiness.NextActions.Should().Contain(action => action.Code == "close.corporate_actions.factor_evidence_missing");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_IsReadyWhenFixedIncomeFactorEvidenceIsRetained()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new CorporateActionActivityAdapter());
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Ready &&
                component.Reason.Contains("direct provider factor-schedule, loan-schedule, or principal/paydown evidence", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_IsReadyWhenFixedIncomeLoanScheduleEvidenceIsRetained()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new LoanScheduleActivityAdapter());
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Ready &&
                component.Reason.Contains("factor-schedule", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_IsReadyWhenFixedIncomePrincipalPaydownEvidenceIsRetained()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new PrincipalActivityAdapter());
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Ready &&
                component.Reason.Contains("principal/paydown evidence", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_BlocksWhenProviderLedgerBreakNeedsSignOff()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                internalCash: 49_000m);
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Blockers.Should().Contain(blocker => blocker.Code == "close.reconciliation.breaks_open");
            readiness.Blockers.Should().Contain(blocker => blocker.Code == "close.approvals.critical_pending");
            readiness.NextActions.Should().Contain(action => action.Code == "close.approvals.critical_pending");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_FundAccountCloseReadiness_ReturnsControllerScore()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));
            await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));
            await using var app = await CreateEndpointAppAsync(fixture);

            var response = await app.GetTestClient().GetAsync(
                $"/api/fund-accounts/{fixture.AccountId}/close-readiness");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var readiness = await response.Content.ReadFromJsonAsync<FundAccountCloseReadinessDto>(JsonOptions);
            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.Score.Should().Be(100);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync(
        string root,
        bool includeSecurityLookup,
        bool runProviderSync = true,
        bool recordInternalSnapshot = true,
        decimal internalCash = 50_000m,
        decimal? internalSecuritiesMarketValue = 18_750m,
        decimal? internalAccruedInterest = 0m,
        decimal? internalPendingSettlement = 0m,
        decimal? internalUnrealizedPnl = 3_750m,
        decimal? internalRealizedPnl = null,
        ICapabilityRouter? capabilityRouter = null,
        bool includeBreakQueue = false,
        IBrokeragePortfolioSync? portfolioAdapter = null,
        IBrokerageActivitySync? activityAdapter = null,
        IOperatorOverridesStore? operatorOverridesStore = null,
        bool recordCustodianPosition = false,
        decimal custodianPositionQuantity = 100m,
        decimal custodianPositionMarketValue = 18_750m,
        bool recordBankStatement = false,
        decimal bankClosingBalance = 50_000m,
        decimal? bankIncomeAmount = null)
    {
        var accountId = Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"));
        services.AddSingleton<InMemoryFundAccountService>();
        services.AddSingleton<IFundAccountService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IAccountManagementService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IAccountQueryService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IBrokeragePortfolioSync>(portfolioAdapter ?? new FixedPortfolioAdapter());
        services.AddSingleton(activityAdapter ?? new EmptyActivityAdapter());
        if (includeSecurityLookup)
        {
            services.AddSingleton<ISecurityReferenceLookup>(new StaticSecurityReferenceLookup());
        }
        if (capabilityRouter is not null)
        {
            services.AddSingleton(capabilityRouter);
        }
        if (includeBreakQueue)
        {
            services.AddSingleton<IReconciliationBreakQueueRepository>(sp =>
                new FileReconciliationBreakQueueRepository(
                    Path.Combine(root, "casework"),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileReconciliationBreakQueueRepository>>()));
        }
        if (operatorOverridesStore is not null)
        {
            services.AddSingleton(operatorOverridesStore);
        }

        services.AddSingleton<BrokeragePortfolioSyncService>();
        services.AddSingleton<ProviderLedgerReconciliationService>();
        services.AddSingleton<FundAccountCloseReadinessService>();

        var provider = services.BuildServiceProvider();
        var accountService = provider.GetRequiredService<IFundAccountService>();
        await accountService.CreateAccountAsync(new CreateAccountRequest(
            accountId,
            AccountTypeDto.Brokerage,
            "BRK-LEDGER",
            "Provider Ledger Brokerage",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-10),
            "tests",
            Institution: "alpaca",
            LedgerReference: "BROKERAGE-CASH"));

        var brokerageSync = provider.GetRequiredService<BrokeragePortfolioSyncService>();
        if (runProviderSync)
        {
            await brokerageSync.RunSyncAsync(
                accountId,
                new WorkstationBrokerageSyncRunRequestDto("alpaca", "PA-LEDGER", "tests"));
        }

        var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (recordInternalSnapshot)
        {
            await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                accountId,
                asOfDate,
                "USD",
                internalCash,
                "internal-ledger",
                RecordedBy: "tests",
                SecuritiesMarketValue: internalSecuritiesMarketValue,
                AccruedInterest: internalAccruedInterest,
                PendingSettlement: internalPendingSettlement,
                ExternalReference: "internal-ledger-snapshot",
                UnrealizedPnl: internalUnrealizedPnl,
                RealizedPnl: internalRealizedPnl));
        }

        if (recordCustodianPosition)
        {
            var batchId = Guid.NewGuid();
            await accountService.IngestCustodianStatementAsync(new IngestCustodianStatementRequest(
                batchId,
                accountId,
                asOfDate,
                "alpaca-custodian",
                "test",
                Notes: "Provider-ledger shadow-book comparison fixture",
                [
                    new CustodianPositionLineDto(
                        Guid.NewGuid(),
                        batchId,
                        accountId,
                        asOfDate,
                        "AAPL",
                        "Symbol",
                        custodianPositionQuantity,
                        custodianPositionMarketValue,
                        "USD",
                        "Apple Inc.",
                        "Equity",
                        IsShort: false)
                ],
                "tests"));
        }

        if (recordBankStatement)
        {
            var batchId = Guid.NewGuid();
            var bankLines = new List<BankStatementLineDto>
            {
                new(
                    Guid.NewGuid(),
                    batchId,
                    accountId,
                    asOfDate,
                    asOfDate,
                    0m,
                    "USD",
                    "BALANCE",
                    "Closing cash balance",
                    "bank-close",
                    bankClosingBalance)
            };
            if (bankIncomeAmount.HasValue)
            {
                bankLines.Add(new BankStatementLineDto(
                    Guid.NewGuid(),
                    batchId,
                    accountId,
                    asOfDate,
                    asOfDate,
                    bankIncomeAmount.Value,
                    "USD",
                    "DIVIDEND",
                    "Bank statement dividend and interest cash flow",
                    "bank-income",
                    bankClosingBalance));
            }

            await accountService.IngestBankStatementAsync(new IngestBankStatementRequest(
                batchId,
                accountId,
                asOfDate,
                "test-bank",
                Notes: "Provider-ledger bank statement shadow-book comparison fixture",
                bankLines,
                "tests"));
        }

        return new TestFixture(
            provider,
            accountId,
            accountService,
            brokerageSync,
            provider.GetRequiredService<ProviderLedgerReconciliationService>(),
            provider.GetRequiredService<FundAccountCloseReadinessService>());
    }

    private static async Task<WebApplication> CreateEndpointAppAsync(
        TestFixture fixture,
        UserPermission permissions =
            UserPermission.ManageDirectLending |
            UserPermission.ViewTrades |
            UserPermission.ExecuteTrades)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(fixture.AccountService);
        builder.Services.AddSingleton<IAccountManagementService>(_ => (IAccountManagementService)fixture.AccountService);
        builder.Services.AddSingleton<IAccountQueryService>(_ => (IAccountQueryService)fixture.AccountService);
        builder.Services.AddSingleton(fixture.BrokerageSync);
        builder.Services.AddSingleton(fixture.Reconciliation);
        builder.Services.AddSingleton(fixture.CloseReadiness);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapFundAccountEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-provider-ledger-reconciliation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task BackdateBrokerageProjectionAsync(TestFixture fixture, TimeSpan age)
    {
        var projection = await fixture.BrokerageSync.GetActivityAsync(fixture.AccountId);
        projection.Should().NotBeNull();
        var staleSyncedAt = DateTimeOffset.UtcNow.Subtract(age);
        var warnings = projection!.Status.Warnings
            .Concat(["Brokerage sync is stale."])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var staleProjection = projection with
        {
            SyncedAt = staleSyncedAt,
            Status = projection.Status with
            {
                Health = WorkstationBrokerageSyncHealth.Stale,
                IsStale = true,
                LastAttemptedSyncAt = staleSyncedAt,
                LastSuccessfulSyncAt = staleSyncedAt,
                Warnings = warnings
            }
        };

        await using var stream = File.Create(projection.ProjectionPath);
        await JsonSerializer.SerializeAsync(stream, staleProjection, JsonOptions);
    }

    private sealed record TestFixture(
        ServiceProvider Services,
        Guid AccountId,
        IFundAccountService AccountService,
        BrokeragePortfolioSyncService BrokerageSync,
        ProviderLedgerReconciliationService Reconciliation,
        FundAccountCloseReadinessService CloseReadiness) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }

    private sealed class FixedPortfolioAdapter : IBrokeragePortfolioSync
    {
        public string ProviderId => "alpaca";

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
            string externalAccountId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokeragePortfolioSnapshotDto(
                new BrokerageExternalAccountDto("alpaca", externalAccountId, "Provider Ledger Account", "active", "USD", retrievedAt),
                new BrokerageBalanceSnapshotDto(50_000m, 68_750m, 70_000m, "USD"),
                [
                    new BrokeragePositionSnapshotDto(
                        "AAPL",
                        100m,
                        150m,
                        187.5m,
                        18_750m,
                        3_750m,
                        "Equity",
                        "Apple Inc.",
                        "pos-aapl",
                        "USD")
                ],
                retrievedAt));
        }
    }

    private sealed class FixedIncomePortfolioAdapter : IBrokeragePortfolioSync
    {
        public string ProviderId => "alpaca";

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
            string externalAccountId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokeragePortfolioSnapshotDto(
                new BrokerageExternalAccountDto("alpaca", externalAccountId, "Provider Ledger Account", "active", "USD", retrievedAt),
                new BrokerageBalanceSnapshotDto(50_000m, 59_900m, 60_000m, "USD"),
                [
                    new BrokeragePositionSnapshotDto(
                        "UST10Y",
                        10m,
                        1_000m,
                        990m,
                        9_900m,
                        -100m,
                        "Treasury Bond",
                        "US Treasury 10Y",
                        "pos-ust10y",
                        "USD")
                ],
                retrievedAt));
        }
    }

    private sealed class EmptyActivityAdapter : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                DateTimeOffset.UtcNow,
                Orders: [],
                Fills: [],
                CashTransactions: []));
        }
    }

    private sealed class IncomeActivityAdapter : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                retrievedAt,
                Orders: [],
                Fills: [],
                CashTransactions:
                [
                    new BrokerageCashTransactionDto(
                        "cash-dividend-aapl",
                        "DIVIDEND",
                        125m,
                        "USD",
                        retrievedAt.AddDays(-2),
                        "AAPL",
                        "AAPL dividend"),
                    new BrokerageCashTransactionDto(
                        "cash-interest-aapl",
                        "INTEREST",
                        17.25m,
                        "USD",
                        retrievedAt.AddDays(-1),
                        "AAPL",
                        "AAPL lending interest")
                ]));
        }
    }

    private sealed class PrincipalActivityAdapter : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                retrievedAt,
                Orders: [],
                Fills: [],
                CashTransactions:
                [
                    new BrokerageCashTransactionDto(
                        "principal-paydown-ust10y",
                        "PRINCIPAL_PAYDOWN",
                        250m,
                        "USD",
                        retrievedAt.AddDays(-1),
                        "UST10Y",
                        "UST10Y principal paydown")
                ]));
        }
    }

    private sealed class RealizedPnlActivityAdapter(decimal realizedPnl) : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                retrievedAt,
                Orders: [],
                Fills:
                [
                    new BrokerageFillSnapshotDto(
                        "fill-realized-aapl",
                        "ord-realized-aapl",
                        "AAPL",
                        OrderSide.Sell,
                        10m,
                        190m,
                        retrievedAt.AddMinutes(-12),
                        "XNAS",
                        0m,
                        realizedPnl)
                ],
                CashTransactions: []));
        }
    }

    private sealed class CorporateActionActivityAdapter : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                retrievedAt,
                Orders: [],
                Fills: [],
                CashTransactions: [],
                CorporateActions:
                [
                    new BrokerageCorporateActionSnapshotDto(
                        "factor-ust10y-20260501",
                        "FactorScheduleRefreshed",
                        "UST10Y",
                        new DateOnly(2026, 5, 1),
                        null,
                        Factor: 0.9825m,
                        Currency: "USD",
                        Description: "Monthly factor update")
                ]));
        }
    }

    private sealed class LoanScheduleActivityAdapter : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var retrievedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new BrokerageActivitySnapshotDto(
                "alpaca",
                externalAccountId,
                retrievedAt,
                Orders: [],
                Fills: [],
                CashTransactions: [],
                CorporateActions:
                [
                    new BrokerageCorporateActionSnapshotDto(
                        "loan-schedule-ust10y-20260501",
                        "LoanScheduleUpdated",
                        "UST10Y",
                        new DateOnly(2026, 5, 1),
                        null,
                        Amount: 250m,
                        Quantity: 0.9825m,
                        Currency: "USD",
                        Description: "Monthly loan schedule update")
                ]));
        }
    }

    private sealed class StaticSecurityReferenceLookup : ISecurityReferenceLookup
    {
        public Task<WorkstationSecurityReference?> GetBySymbolAsync(string symbol, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var normalized = symbol.Trim().ToUpperInvariant();
            return Task.FromResult<WorkstationSecurityReference?>(new WorkstationSecurityReference(
                Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D"),
                $"{normalized} security",
                "Equity",
                "USD",
                SecurityStatusDto.Active,
                normalized,
                MatchedIdentifierKind: "Ticker",
                MatchedIdentifierValue: normalized,
                MatchedProvider: "test"));
        }
    }

    private sealed class StaticOperatorOverridesStore(Guid securityId) : IOperatorOverridesStore
    {
        public Task<OperatorOverridesDto?> GetAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (requestedSecurityId != securityId)
            {
                return Task.FromResult<OperatorOverridesDto?>(null);
            }

            return Task.FromResult<OperatorOverridesDto?>(new OperatorOverridesDto(
                securityId,
                new Dictionary<string, string> { ["sector"] = "Technology" },
                "security-steward",
                new DateTimeOffset(2026, 5, 28, 14, 0, 0, TimeSpan.Zero))
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Approved,
                ReviewedBy = "security-steward",
                ReviewedAt = new DateTimeOffset(2026, 5, 28, 15, 0, 0, TimeSpan.Zero),
                ReasonCode = "provider-symbol-confirmed",
                AuditTrail =
                [
                    new SecurityOverrideAuditEntryDto(
                        "SecurityOverrideApproved",
                        "ops-user",
                        new DateTimeOffset(2026, 5, 28, 15, 0, 0, TimeSpan.Zero),
                        SecurityOverrideApprovalStatusDto.Approved,
                        ReasonCode: "provider-symbol-confirmed",
                        Comment: "Provider AAPL mapping confirmed against custodian statement.",
                        Reviewer: "security-steward",
                        ReviewedAt: new DateTimeOffset(2026, 5, 28, 15, 0, 0, TimeSpan.Zero))
                ]
            });
        }

        public Task<OperatorOverridesDto> PatchAsync(
            Guid requestedSecurityId,
            OperatorOverridesPatchRequest request,
            string updatedBy,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed record FixedCapabilityRouter(bool IsRoutable) : ICapabilityRouter
    {
        public ValueTask<ProviderRouteResult> RouteAsync(ProviderRouteContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRoutable)
            {
                return ValueTask.FromResult(new ProviderRouteResult(
                    context,
                    SelectedDecision: null,
                    Candidates: [],
                    SkippedCandidates: [$"Provider fixture does not support capability '{context.Capability}'."],
                    PolicyGate: $"Capability '{context.Capability}' requires an account-scoped provider binding."));
            }

            var decision = new ProviderRouteDecision(
                ConnectionId: $"fixture-{context.Capability}",
                ProviderFamilyId: "alpaca",
                Capability: context.Capability,
                SafetyMode: ProviderSafetyMode.NoAutomaticFailover,
                ScopeRank: 500,
                Priority: 100,
                IsHealthy: true,
                ReasonCodes: [$"Capability '{context.Capability}' is supported by the fixture."],
                FallbackConnectionIds: []);

            return ValueTask.FromResult(new ProviderRouteResult(
                context,
                SelectedDecision: decision,
                Candidates: [decision],
                SkippedCandidates: []));
        }
    }

    private sealed class SelectiveCapabilityRouter(params ProviderCapabilityKind[] unsupportedCapabilities) : ICapabilityRouter
    {
        private readonly HashSet<ProviderCapabilityKind> _unsupportedCapabilities = unsupportedCapabilities.ToHashSet();

        public ValueTask<ProviderRouteResult> RouteAsync(ProviderRouteContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_unsupportedCapabilities.Contains(context.Capability))
            {
                return ValueTask.FromResult(new ProviderRouteResult(
                    context,
                    SelectedDecision: null,
                    Candidates: [],
                    SkippedCandidates: [$"Provider fixture does not support capability '{context.Capability}'."],
                    PolicyGate: $"Capability '{context.Capability}' is unavailable for this provider account."));
            }

            var decision = new ProviderRouteDecision(
                ConnectionId: $"fixture-{context.Capability}",
                ProviderFamilyId: "alpaca",
                Capability: context.Capability,
                SafetyMode: ProviderSafetyMode.NoAutomaticFailover,
                ScopeRank: 500,
                Priority: 100,
                IsHealthy: true,
                ReasonCodes: [$"Capability '{context.Capability}' is supported by the fixture."],
                FallbackConnectionIds: []);

            return ValueTask.FromResult(new ProviderRouteResult(
                context,
                SelectedDecision: decision,
                Candidates: [decision],
                SkippedCandidates: []));
        }
    }

    private sealed class AssetClassPositionCapabilityRouter(string unsupportedAssetClass) : ICapabilityRouter
    {
        public ValueTask<ProviderRouteResult> RouteAsync(ProviderRouteContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (context.Capability == ProviderCapabilityKind.AccountPositions &&
                string.Equals(context.AssetClass, unsupportedAssetClass, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(new ProviderRouteResult(
                    context,
                    SelectedDecision: null,
                    Candidates: [],
                    SkippedCandidates: [$"Provider fixture does not support {unsupportedAssetClass} account positions."],
                    PolicyGate: $"Capability '{context.Capability}' is unavailable for asset class '{unsupportedAssetClass}'."));
            }

            var decision = new ProviderRouteDecision(
                ConnectionId: $"fixture-{context.Capability}",
                ProviderFamilyId: "alpaca",
                Capability: context.Capability,
                SafetyMode: ProviderSafetyMode.NoAutomaticFailover,
                ScopeRank: 500,
                Priority: 100,
                IsHealthy: true,
                ReasonCodes: [$"Capability '{context.Capability}' is supported by the fixture."],
                FallbackConnectionIds: []);

            return ValueTask.FromResult(new ProviderRouteResult(
                context,
                SelectedDecision: decision,
                Candidates: [decision],
                SkippedCandidates: []));
        }
    }
}
