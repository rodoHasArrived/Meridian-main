using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
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
using NSubstitute;

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

            var detail = await fixture.RunReconciliationAsync(
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

            var detail = await fixture.RunReconciliationAsync(
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
    public async Task Scenario_ProviderLedgerReconciliation_AttachesOpenIdentifierConflictsToPassport()
    {
        var root = CreateTempRoot();
        try
        {
            var securityId = Guid.Parse("35D27D8E-4460-4B17-92B8-6E5F53773D1D");
            var conflictId = Guid.Parse("C3C9D912-4F8D-4C8A-B737-0E015877E3F6");
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                securityMasterConflictService: new StaticSecurityMasterConflictService(
                    new SecurityMasterConflict(
                        conflictId,
                        securityId,
                        "Identifier",
                        "cusip",
                        "alpaca",
                        "037833100",
                        "polygon",
                        "037833101",
                        DateTimeOffset.UtcNow.AddMinutes(-15),
                        "Open")));

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var passport = detail.SecurityMasterPassports.Should().ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Resolved);
            passport.ConfidenceScore.Should().Be(60m);
            passport.IdentifierConflicts.Should().ContainSingle(conflict =>
                conflict.Contains(conflictId.ToString("N"), StringComparison.OrdinalIgnoreCase) &&
                conflict.Contains("providers=alpaca/polygon", StringComparison.OrdinalIgnoreCase));
            passport.ValidationIssueCodes.Should().Contain("SM_IDENTIFIER_CONFLICT");
            passport.Reason.Should().Contain("open Security Master identifier conflict");
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

            var detail = await fixture.RunReconciliationAsync(
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

            var detail = await fixture.RunReconciliationAsync(
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "position-cost-basis:AAPL" &&
                line.InternalSource == "custodian-statement" &&
                line.ProviderSource == "provider-sync" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Matched &&
                line.InternalAmount == 15_000m &&
                line.ProviderAmount == 15_000m);
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
    public async Task Scenario_ProviderLedgerReconciliation_EmitsScopedItemLevelCostBasisBreak()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                recordCustodianPosition: true,
                custodianPositionCostBasis: 14_750m,
                includeBreakQueue: true);

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

            var costBasisBreak = detail.Breaks.Should().ContainSingle(item =>
                item.Code == "SHADOW_BOOK_POSITION_COST_BASIS_AAPL_MISMATCH"
                && item.Symbol == "AAPL").Subject;
            costBasisBreak.ExpectedAmount.Should().Be(14_750m);
            costBasisBreak.ActualAmount.Should().Be(15_000m);
            costBasisBreak.Variance.Should().Be(250m);

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var queueCase = (await repository.GetAllAsync()).Should().ContainSingle(item =>
                item.RoutingDetail == costBasisBreak.CheckId).Subject;
            queueCase.LedgerBookId.Should().NotBeNull().And.NotBe(Guid.Empty);
            queueCase.AccountingPeriodId.Should().NotBeNullOrWhiteSpace();
            queueCase.AsOfDate.Should().Be(detail.Summary.InternalAsOfDate);
            queueCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.CostBasis
                && measure.Expected == 14_750m
                && measure.Actual == 15_000m
                && measure.Variance == 250m
                && measure.Unit == "USD");
            queueCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.Value
                && measure.Expected == null
                && !string.IsNullOrWhiteSpace(measure.UnavailableReason));
            queueCase.BlockedOutputs.Should().BeEquivalentTo("accounting-close", "certified-reporting");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_EmitsItemLevelQuantityBreak()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                recordCustodianPosition: true,
                custodianPositionQuantity: 99m,
                includeBreakQueue: true);

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);
            var quantityBreak = detail.Breaks.Should().ContainSingle(item =>
                item.Code == "SHADOW_BOOK_POSITION_QUANTITY_AAPL_MISMATCH"
                && item.Symbol == "AAPL").Subject;
            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var queueCase = (await repository.GetAllAsync()).Should().ContainSingle(item =>
                item.RoutingDetail == quantityBreak.CheckId).Subject;

            queueCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.Quantity
                && measure.Expected == 99m
                && measure.Actual == 100m
                && measure.Variance == 1m
                && measure.Unit == "units");
            queueCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.CostBasis
                && measure.Expected == null
                && !string.IsNullOrWhiteSpace(measure.UnavailableReason));
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
    public async Task Scenario_ProviderLedgerReconciliation_FlagsOptionAndFutureContractMetadataGaps()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: false,
                internalSecuritiesMarketValue: 12_000m,
                internalUnrealizedPnl: 0m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new DerivativeContractPortfolioAdapter(),
                securityValidationGate: new SymbolIssueSecurityValidationGate(
                    new SymbolValidationIssue(
                        "AAPL260619C00190000",
                        "SM_OPTION_CONTRACT_METADATA_MISSING",
                        "Option contract metadata missing",
                        "Option contract is missing expiry, strike, put/call, multiplier, and underlying evidence.",
                        "contract.expiry",
                        "contract.strike",
                        "contract.optionType",
                        "contract.multiplier",
                        "contract.underlying"),
                    new SymbolValidationIssue(
                        "ESM6",
                        "SM_FUTURE_CONTRACT_METADATA_MISSING",
                        "Future contract metadata missing",
                        "Future contract is missing contract month, exchange, tick size, multiplier, and settlement calendar evidence.",
                        "contract.month",
                        "contract.exchange",
                        "contract.tickSize",
                        "contract.multiplier",
                        "settlement.calendar")));

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Summary.SecurityIssueCount.Should().Be(2);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SM_OPTION_CONTRACT_METADATA_MISSING" &&
                breakRow.Symbol == "AAPL260619C00190000" &&
                breakRow.Category == ReconciliationBreakCategory.ClassificationGap &&
                breakRow.Severity == ReconciliationBreakSeverity.High);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SM_FUTURE_CONTRACT_METADATA_MISSING" &&
                breakRow.Symbol == "ESM6" &&
                breakRow.Category == ReconciliationBreakCategory.ClassificationGap &&
                breakRow.Severity == ReconciliationBreakSeverity.High);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountPositions:option" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.Checks.Should().Contain(check =>
                check.CheckId == "provider-capability:AccountPositions:future" &&
                check.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            detail.SecurityMasterPassports.Should().Contain(passport =>
                passport.Symbol == "AAPL260619C00190000" &&
                passport.Status == ProviderSecurityMasterPassportStatusDto.Blocked &&
                passport.ResolutionSource == "security-validation-gate" &&
                passport.ValidationIssueCodes.Contains("SM_OPTION_CONTRACT_METADATA_MISSING"));
            detail.SecurityMasterPassports.Should().Contain(passport =>
                passport.Symbol == "ESM6" &&
                passport.Status == ProviderSecurityMasterPassportStatusDto.Blocked &&
                passport.ResolutionSource == "security-validation-gate" &&
                passport.ValidationIssueCodes.Contains("SM_FUTURE_CONTRACT_METADATA_MISSING"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_FlagsFxCashSettlementGap()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalPendingSettlement: 250m,
                activityAdapter: new FxSettlementActivityAdapter());

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "TOTAL_EQUITY_MISMATCH" &&
                breakRow.CheckId == "total-equity" &&
                breakRow.Category == ReconciliationBreakCategory.AmountMismatch &&
                breakRow.ExpectedAmount == 69_000m &&
                breakRow.ActualAmount == 68_750m &&
                breakRow.Variance == -250m);
            detail.ShadowBookComparison.Should().NotBeNull();
            detail.ShadowBookComparison!.Lines.Should().Contain(line =>
                line.Dimension == "total-equity" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                line.InternalAmount == 69_000m &&
                line.ProviderAmount == 68_750m &&
                line.Variance == -250m);
            detail.ShadowBookComparison.Lines.Should().Contain(line =>
                line.Dimension == "pending-settlement" &&
                line.Status == ProviderLedgerReconciliationCheckStatusDto.Blocked &&
                line.InternalAmount == 250m &&
                line.ProviderAmount == null &&
                line.Reason.Contains("pending-settlement exposure", StringComparison.OrdinalIgnoreCase));
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
    public async Task Scenario_ProviderLedgerReconciliation_SeedsFactorScheduleGapCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.FactorSchedule),
                portfolioAdapter: new FixedIncomePortfolioAdapter());

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "PROVIDER_FACTOR_SCHEDULE_CAPABILITY_MISSING" &&
                breakRow.CheckId == "provider-capability:FactorSchedule" &&
                breakRow.Category == ReconciliationBreakCategory.MissingPortfolioCoverage &&
                breakRow.Severity == ReconciliationBreakSeverity.Medium);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.MissingFeeds.Should().Contain("factor-schedule");
            var candidate = detail.CorporateActionReadiness.EvidenceCandidates.Should().ContainSingle(item =>
                item.CandidateType == "FactorScheduleCandidate" &&
                item.Symbol == "UST10Y").Subject;
            candidate.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Break);
            candidate.RequiredFeed.Should().Be("factor-schedule,coupon-schedule");
            candidate.Reason.Should().Contain("factor-schedule capability is not routable");
            detail.CorporateActionReadiness.SecurityMasterScheduleFeeds.Should().Contain(feed =>
                feed.CandidateId == candidate.CandidateId &&
                feed.FeedKind == "SecurityMasterFactorCoverageRequirement" &&
                feed.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                !feed.CanUpdateSecurityMaster &&
                !feed.CanSupportLedgerValuation);

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var candidateCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider corporate-action evidence" &&
                item.RoutingDetail == candidate.CandidateId).Subject;
            candidateCase.Category.Should().Be(ReconciliationBreakCategory.MissingPortfolioCoverage);
            candidateCase.ExplainabilitySummary.Should().Contain("candidate=FactorScheduleCandidate");
            candidateCase.ExplainabilitySummary.Should().Contain("requiredFeed=factor-schedule,coupon-schedule");
            candidateCase.ExplainabilitySummary.Should().Contain("ledgerEffect=FactorScheduleCoverageCandidate");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_SeedsLoanScheduleGapCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new SelectiveCapabilityRouter(ProviderCapabilityKind.FactorSchedule),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new LoanScheduleActivityAdapter());

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "PROVIDER_FACTOR_SCHEDULE_CAPABILITY_MISSING" &&
                breakRow.CheckId == "provider-capability:FactorSchedule");
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.LoanScheduleEventCount.Should().Be(1);
            detail.CorporateActionReadiness.MissingFeeds.Should().Contain("factor-schedule");
            var candidate = detail.CorporateActionReadiness.EvidenceCandidates.Should().ContainSingle(item =>
                item.CandidateType == "LoanScheduleEvent" &&
                item.ProviderEventId == "loan-schedule-ust10y-20260501").Subject;
            candidate.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Break);
            candidate.RequiredFeed.Should().Be("loan-schedule,factor-schedule");
            candidate.Reason.Should().Contain("factor-schedule capability is not routable");
            detail.CorporateActionReadiness.LedgerEffects.Should().Contain(effect =>
                effect.CandidateId == candidate.CandidateId &&
                effect.LedgerEffectKind == "LoanScheduleValuationInput" &&
                effect.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                effect.CashAmount == 250m &&
                effect.Factor == 0.9825m);
            detail.CorporateActionReadiness.SecurityMasterScheduleFeeds.Should().Contain(feed =>
                feed.CandidateId == candidate.CandidateId &&
                feed.FeedKind == "SecurityMasterLoanSchedule" &&
                feed.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                !feed.CanUpdateSecurityMaster &&
                !feed.CanSupportLedgerValuation);

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var candidateCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider corporate-action evidence" &&
                item.RoutingDetail == candidate.CandidateId).Subject;
            candidateCase.ExplainabilitySummary.Should().Contain("candidate=LoanScheduleEvent");
            candidateCase.ExplainabilitySummary.Should().Contain("providerEventId=loan-schedule-ust10y-20260501");
            candidateCase.ExplainabilitySummary.Should().Contain("ledgerEffect=LoanScheduleValuationInput");
            candidateCase.ExplainabilitySummary.Should().Contain("principalAmount=250");
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
    public async Task Scenario_ProviderLedgerReconciliation_RetainsProviderAmortizationScheduleEvents()
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
                activityAdapter: new AmortizationScheduleActivityAdapter());

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Matched);
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.ProviderCorporateActionEventCount.Should().Be(1);
            detail.CorporateActionReadiness.AmortizationScheduleEventCount.Should().Be(1);
            detail.CorporateActionReadiness.FactorScheduleEventCount.Should().Be(0);
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("amortization-schedule");
            detail.CorporateActionReadiness.RequiredFeeds.Should().Contain("factor-schedule");
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "AmortizationScheduleEvent" &&
                candidate.ProviderEventId == "amortization-ust10y-20260501" &&
                candidate.RequiredFeed == "amortization-schedule,factor-schedule" &&
                candidate.EvidenceSource == "provider-corporate-action" &&
                candidate.Amount == 125m &&
                candidate.Quantity == 0.975m &&
                candidate.Symbol == "UST10Y" &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Matched);
            var amortizationEffect = detail.CorporateActionReadiness.LedgerEffects.Should().ContainSingle(effect =>
                effect.CandidateType == "AmortizationScheduleEvent" &&
                effect.ProviderEventId == "amortization-ust10y-20260501" &&
                effect.LedgerEffectKind == "AmortizationScheduleValuationInput").Subject;
            amortizationEffect.CashAmount.Should().Be(125m);
            amortizationEffect.PrincipalAmount.Should().Be(125m);
            amortizationEffect.Factor.Should().Be(0.975m);
            amortizationEffect.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Matched);
            amortizationEffect.JournalLines.Should().BeEmpty();
            var amortizationFeed = detail.CorporateActionReadiness.SecurityMasterScheduleFeeds.Should()
                .ContainSingle(feed => feed.ProviderEventId == "amortization-ust10y-20260501").Subject;
            amortizationFeed.FeedKind.Should().Be("SecurityMasterAmortizationSchedule");
            amortizationFeed.RequiredFeed.Should().Be("amortization-schedule,factor-schedule");
            amortizationFeed.SecurityId.Should().NotBeNull();
            amortizationFeed.PrincipalAmount.Should().Be(125m);
            amortizationFeed.Factor.Should().Be(0.975m);
            amortizationFeed.CanUpdateSecurityMaster.Should().BeTrue();
            amortizationFeed.CanSupportLedgerValuation.Should().BeTrue();
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync(fixture.QueueScope);
            var cashCase = cases.Should().ContainSingle(item =>
                item.Category == ReconciliationBreakCategory.CashMismatch &&
                item.FundAccountId == fixture.AccountId.ToString("D")).Subject;

            cashCase.TenantId.Should().Be(fixture.QueueScope.TenantId);
            cashCase.CompanyId.Should().Be(fixture.QueueScope.CompanyId);
            cashCase.FundProfileId.Should().Be(fixture.AccountId.ToString("D"));
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
            cashCase.LedgerBookId.Should().NotBeNull().And.NotBe(Guid.Empty);
            cashCase.SourceFingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
            cashCase.EvidenceLinks.Should().Contain(cashCase.RoutingTarget!);
            cashCase.BlockedOutputs.Should().BeEquivalentTo("accounting-close", "certified-reporting");
            cashCase.Measures.Should().HaveCount(3);
            cashCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.Value
                && measure.Expected == 49_900m
                && measure.Actual == 50_000m
                && measure.Variance == 100m
                && measure.Unit == "USD");
            cashCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.Quantity
                && measure.Variance == null
                && !string.IsNullOrWhiteSpace(measure.UnavailableReason));
            cashCase.Measures.Should().ContainSingle(measure =>
                measure.Kind == ReconciliationBreakMeasureKindDto.CostBasis
                && measure.Variance == null
                && !string.IsNullOrWhiteSpace(measure.UnavailableReason));

            var audit = await repository.GetAuditHistoryAsync(cashCase.BreakId);
            audit.Should().ContainSingle(entry =>
                entry.EventType == "CaseCreated" &&
                entry.BreakId == cashCase.BreakId &&
                entry.SignoffStatus == "assigned");
            (await repository.GetAllAsync(
                    new ReconciliationBreakQueueScope("another-tenant", fixture.QueueScope.CompanyId)))
                .Should()
                .BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_WithBreaksAndNoAuthoritativeScope_BlocksWithoutWritingCasework()
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
                new ProviderLedgerReconciliationRequestDto(
                    OperationId: "missing-authoritative-scope",
                    RequestedBy: "direct-test"));

            detail.Outcome.Should().NotBeNull();
            detail.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
            detail.Outcome.Issues.Should().ContainSingle(issue =>
                issue.IsBlocking &&
                issue.Message.Contains("tenant and company scope", StringComparison.OrdinalIgnoreCase));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            (await repository.GetAllAsync()).Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_ForeignScopeIsRejectedBeforeRunStateAndLatestRead()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true);
            var foreignScope = new ReconciliationBreakQueueScope(
                "tenant-provider-ledger-foreign",
                "company-provider-ledger-foreign");

            var rejected = await fixture.Reconciliation.RunAsync(
                fixture.AccountId,
                foreignScope,
                new ProviderLedgerReconciliationRequestDto(
                    OperationId: "foreign-authority-preflight",
                    RequestedBy: "foreign-operator"));

            rejected.Outcome.Should().NotBeNull();
            rejected.Outcome!.State.Should().Be(OperationTerminalState.Blocked);
            rejected.Outcome.Issues.Should().ContainSingle(issue =>
                issue.Code == "PROVIDER_RECONCILIATION_FUND_NOT_AUTHORIZED" &&
                issue.IsBlocking);
            Directory.Exists(Path.Combine(
                    root,
                    "reconciliation",
                    fixture.AccountId.ToString("N")))
                .Should()
                .BeFalse("foreign authority must be rejected before intent or detail persistence");

            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(
                    OperationId: "owner-authority-run",
                    RequestedBy: "owner-operator"));
            (await fixture.Reconciliation.GetLatestAsync(fixture.AccountId, foreignScope))
                .Should()
                .BeNull("foreign callers must not read the owner's retained latest result");
            (await fixture.Reconciliation.GetLatestAsync(fixture.AccountId, fixture.QueueScope))
                .Should()
                .NotBeNull();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_PersistsIntentBeforeCaseworkAndConvergesFailedReplay()
    {
        var root = CreateTempRoot();
        try
        {
            var operationId = $"provider-reconciliation-{Guid.NewGuid():N}";
            var cases = new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.Ordinal);
            var repository = Substitute.For<IReconciliationBreakQueueRepository>();
            var failAfterFirstWrite = true;
            var createCalls = 0;
            var intentExistedBeforeFirstCase = false;

            repository.GetByIdAsync(
                    Arg.Any<ReconciliationBreakQueueScope>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(
                    cases.TryGetValue(call.ArgAt<string>(1), out var item)
                        ? item
                        : null));
            repository.GetAllAsync(
                    Arg.Any<ReconciliationBreakQueueScope>(),
                    Arg.Any<ReconciliationBreakQueueStatus?>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>(cases.Values.ToArray()));
            repository.CreateIfMissingAsync(
                    Arg.Any<ReconciliationBreakQueueScope>(),
                    Arg.Any<ReconciliationBreakQueueItem>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var item = call.ArgAt<ReconciliationBreakQueueItem>(1);
                    createCalls++;
                    var operationKey = Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operationId)))
                        .ToLowerInvariant();
                    intentExistedBeforeFirstCase |= File.Exists(Path.Combine(
                        root,
                        "reconciliation",
                        item.FundAccountId!.Replace("-", string.Empty, StringComparison.Ordinal),
                        "operations",
                        operationKey,
                        "intent.json"));
                    cases[item.BreakId] = item;
                    if (failAfterFirstWrite)
                    {
                        failAfterFirstWrite = false;
                        throw new IOException("Injected queue acknowledgement failure after durable case write.");
                    }
                    return Task.FromResult(true);
                });

            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true,
                breakQueueRepository: repository);
            var request = new ProviderLedgerReconciliationRequestDto(
                RequestedBy: "ops-user",
                OperationId: operationId);

            var failed = await fixture.RunReconciliationAsync(fixture.AccountId, request);

            failed.Outcome.Should().NotBeNull();
            failed.Outcome!.State.Should().Be(OperationTerminalState.Failed);
            failed.Outcome.AttemptNumber.Should().Be(1);
            failed.Outcome.InputHashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
            failed.Outcome.Postconditions.Should().Contain(item =>
                item.Code == "reconciliation-casework-retained" &&
                item.State == OperationPostconditionState.NotSatisfied);
            VerifiedOperationOutcomeValidator.Validate(failed.Outcome).Should().BeEmpty();
            intentExistedBeforeFirstCase.Should().BeTrue();
            cases.Should().NotBeEmpty();
            cases.Values.Should().OnlyContain(item =>
                item.RunId == failed.Summary.ReconciliationRunId.ToString("N"));
            File.Exists(Path.Combine(
                root,
                "reconciliation",
                fixture.AccountId.ToString("N"),
                "runs",
                $"{failed.Summary.ReconciliationRunId:N}.json")).Should().BeTrue();

            var recovered = await fixture.RunReconciliationAsync(fixture.AccountId, request);

            recovered.Summary.ReconciliationRunId.Should().Be(failed.Summary.ReconciliationRunId);
            recovered.Outcome.Should().NotBeNull();
            recovered.Outcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
            recovered.Outcome.AttemptNumber.Should().Be(2);
            recovered.Outcome.InputHashSha256.Should().Be(failed.Outcome.InputHashSha256);
            VerifiedOperationOutcomeValidator.Validate(recovered.Outcome).Should().BeEmpty();
            cases.Values.Should().OnlyContain(item =>
                item.RunId == recovered.Summary.ReconciliationRunId.ToString("N"));
            var createsAfterRecovery = createCalls;

            var replayed = await fixture.RunReconciliationAsync(fixture.AccountId, request);

            replayed.Should().BeEquivalentTo(recovered);
            createCalls.Should().Be(createsAfterRecovery);
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

            var detail = await fixture.RunReconciliationAsync(
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
                entry.EventType == "CaseCreated" &&
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

            var detail = await fixture.RunReconciliationAsync(
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
            dividendCase.ExplainabilitySummary.Should().Contain("effectiveDate=");
            dividendCase.ExplainabilitySummary.Should().Contain("cashAmount=125");
            dividendCase.ExplainabilitySummary.Should().Contain("incomeAmount=125");
            dividendCase.ExplainabilitySummary.Should().Contain("currency=USD");
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

            var detail = await fixture.RunReconciliationAsync(
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
            principalCase.ExplainabilitySummary.Should().Contain("effectiveDate=");
            principalCase.ExplainabilitySummary.Should().Contain("cashAmount=250");
            principalCase.ExplainabilitySummary.Should().Contain("principalAmount=250");
            principalCase.ExplainabilitySummary.Should().Contain("currency=USD");
            principalCase.ExplainabilitySummary.Should().Contain("journalLines=0");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_RejectsCallerAssertedSignoffAndLeavesCaseOpen()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true);

            var firstDetail = await fixture.RunReconciliationAsync(fixture.AccountId);
            var firstCashBreak = firstDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            Func<Task> act = async () => await fixture.RunReconciliationAsync(
                    fixture.AccountId,
                    new ProviderLedgerReconciliationRequestDto(
                        RequestedBy: "ops-user",
                        SignedOffBreakKeys: [firstCashBreak.BreakKey!],
                        SignedOffBy: "controller"));

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*governed reconciliation casework*");

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var caseId = $"provider-ledger:{fixture.AccountId:N}:{firstCashBreak.BreakKey!.Replace(':', '-').ToLowerInvariant()}";
            var updated = await repository.GetByIdAsync(caseId);

            updated.Should().NotBeNull();
            updated!.Status.Should().Be(ReconciliationBreakQueueStatus.Open);
            updated.LifecycleState.Should().Be(ReconciliationCaseLifecycleState.Open);
            updated.ResolvedBy.Should().BeNull();
            updated.SignedOffBy.Should().BeNull();

            var audit = await repository.GetAuditHistoryAsync(caseId);
            audit.Select(entry => entry.EventType).Should().Contain("CaseCreated");
            audit.Should().ContainSingle();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_PreservesBreakAgingAcrossComparisonRuns()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true, internalCash: 49_900m);

            var firstDetail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(DefaultBreakOwner: "fund-controller"));
            var firstCashBreak = firstDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            var repeatedDetail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(
                    DefaultBreakOwner: "fund-controller"));
            var repeatedCashBreak = repeatedDetail.Breaks.Single(breakRow => breakRow.Code == "CASH_BALANCE_MISMATCH");

            repeatedCashBreak.BreakKey.Should().Be(firstCashBreak.BreakKey);
            repeatedCashBreak.Owner.Should().Be("fund-controller");
            repeatedCashBreak.FirstObservedAt.Should().Be(firstCashBreak.FirstObservedAt);
            repeatedCashBreak.SignOffState.Should().Be(ProviderLedgerReconciliationBreakSignOffStateDto.Assigned);
            repeatedCashBreak.SignedOffBy.Should().BeNull();
            repeatedDetail.Summary.SignedOffBreakCount.Should().Be(0);
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
    public async Task Scenario_ProviderLedgerReconciliation_FlagsCustomPrivateProfileEvidenceGap()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: false,
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 7_500m,
                internalUnrealizedPnl: 0m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new CustomPrivateProfilePortfolioAdapter(),
                securityValidationGate: new SymbolIssueSecurityValidationGate(
                    new SymbolValidationIssue(
                        "PRIVLOAN",
                        "SM_CUSTOM_PRIVATE_PROFILE_EVIDENCE_MISSING",
                        "Custom private profile evidence missing",
                        "Private credit profile is missing valuation policy, borrower identity, covenant, and source-document evidence.",
                        "profile.valuationPolicy",
                        "profile.borrower",
                        "profile.covenants",
                        "profile.evidence")));

            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Breaks);
            var securityBreak = detail.Breaks.Should().ContainSingle(breakRow =>
                breakRow.Code == "SM_CUSTOM_PRIVATE_PROFILE_EVIDENCE_MISSING" &&
                breakRow.Symbol == "PRIVLOAN" &&
                breakRow.Category == ReconciliationBreakCategory.ClassificationGap &&
                breakRow.Severity == ReconciliationBreakSeverity.High).Subject;
            securityBreak.EvidenceLink.Should().Be("/workstation/data/security-master");
            var passport = detail.SecurityMasterPassports.Should().ContainSingle(item => item.Symbol == "PRIVLOAN").Subject;
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Blocked);
            passport.AssetClass.Should().Be("Private Credit Loan");
            passport.ValidationIssueCodes.Should().Contain("SM_CUSTOM_PRIVATE_PROFILE_EVIDENCE_MISSING");
            passport.Reason.Should().Contain("valuation policy");
            detail.CorporateActionReadiness.Should().NotBeNull();
            detail.CorporateActionReadiness!.Status.Should().Be(ProviderLedgerReconciliationCheckStatusDto.Break);
            detail.CorporateActionReadiness.MissingFeeds.Should().Contain("security-master-identities");
            detail.CorporateActionReadiness.EvidenceCandidates.Should().Contain(candidate =>
                candidate.CandidateType == "FactorScheduleCandidate" &&
                candidate.Symbol == "PRIVLOAN" &&
                candidate.Status == ProviderLedgerReconciliationCheckStatusDto.Break &&
                candidate.Reason.Contains("Security Master identity attribution", StringComparison.OrdinalIgnoreCase));

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync();
            var securityCase = cases.Should().ContainSingle(item =>
                item.StrategyName == "Provider ledger reconciliation" &&
                item.RoutingDetail == securityBreak.CheckId).Subject;
            securityCase.Category.Should().Be(ReconciliationBreakCategory.ClassificationGap);
            securityCase.ToleranceProfileId.Should().Be("security-master-identity");
            securityCase.ExplainabilitySummary.Should().Contain("code=SM_CUSTOM_PRIVATE_PROFILE_EVIDENCE_MISSING");
            securityCase.ExplainabilitySummary.Should().Contain("validationIssues=SM_CUSTOM_PRIVATE_PROFILE_EVIDENCE_MISSING");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ProviderLedgerReconciliation_BlocksInactiveSecurityMasterReferenceForLedgerClose()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                securityStatus: SecurityStatusDto.Inactive);

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

            detail.Summary.Status.Should().Be(ProviderLedgerReconciliationStatusDto.Blocked);
            detail.Summary.SecurityIssueCount.Should().Be(1);
            detail.Breaks.Should().Contain(breakRow =>
                breakRow.Code == "SM_SECURITY_NOT_ACTIVE" &&
                breakRow.Symbol == "AAPL" &&
                breakRow.Severity == ReconciliationBreakSeverity.Critical);
            var passport = detail.SecurityMasterPassports.Should().NotBeNull().And.ContainSingle(item => item.Symbol == "AAPL").Subject;
            passport.Status.Should().Be(ProviderSecurityMasterPassportStatusDto.Blocked);
            passport.SecurityStatus.Should().Be(SecurityStatusDto.Inactive);
            passport.ConfidenceScore.Should().Be(0m);
            passport.ResolutionSource.Should().Be("security-master-status");
            passport.Reason.Should().Contain("active approved Security Master status");

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            readiness.Components.Should().Contain(component =>
                component.Key == "security-master-completeness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Blocked);
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.security_master.casework_blocked" &&
                blocker.Category == "SecurityMaster" &&
                blocker.Severity == "Critical");
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

            var detail = await fixture.RunReconciliationAsync(fixture.AccountId);

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
            securityCase.ExplainabilitySummary.Should().Contain("passportStatus=Unresolved");
            securityCase.ExplainabilitySummary.Should().Contain("resolutionSource=unresolved");
            securityCase.ExplainabilitySummary.Should().Contain("confidence=0");
            securityCase.ExplainabilitySummary.Should().Contain("freshnessMinutes=");
            securityCase.ExplainabilitySummary.Should().Contain("overrideCount=0");
            securityCase.BreakExplanation.Should().NotBeNull();
            securityCase.BreakExplanation!.SourceSystems.Should().Contain("Security Master");
            securityCase.BreakExplanation.LedgerImpact.Should().Contain("Provider-to-Security Master passport status Unresolved");
            securityCase.BreakExplanation.LedgerImpact.Should().Contain("resolution source unresolved");
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
    public async Task Endpoint_ProviderLedgerReconciliation_RequiresTenantAndCompanyScopeWithoutWritingCasework()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true);
            await using var app = await CreateEndpointAppAsync(
                fixture,
                includeTenantCompanyScope: false);

            var response = await app.GetTestClient().PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "spoofed-operator"),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            (await repository.GetAllAsync()).Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ProviderLedgerReconciliation_RetainsCaseworkOnlyForAuthenticatedScope()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                internalCash: 49_900m,
                includeBreakQueue: true);
            await using var app = await CreateEndpointAppAsync(fixture);

            var response = await app.GetTestClient().PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(
                    OperationId: "authenticated-scope-casework",
                    RequestedBy: "spoofed-operator"),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var retained = await repository.GetAllAsync(fixture.QueueScope);
            retained.Should().Contain(item =>
                item.Category == ReconciliationBreakCategory.CashMismatch &&
                item.TenantId == fixture.QueueScope.TenantId &&
                item.CompanyId == fixture.QueueScope.CompanyId);
            (await repository.GetAllAsync(
                    new ReconciliationBreakQueueScope(
                        fixture.QueueScope.TenantId,
                        "another-company")))
                .Should()
                .BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_ProviderLedgerReconciliation_RejectsClientAssertedSignoff()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(root, includeSecurityLookup: true);
            await using var app = await CreateEndpointAppAsync(fixture);
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconcile-ledger",
                new ProviderLedgerReconciliationRequestDto(
                    RequestedBy: "spoofed-operator",
                    SignedOffBreakKeys: ["client-asserted-break"],
                    SignedOffBy: "spoofed-controller"),
                JsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("cannot be asserted");
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
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().HaveCount(6);
            readiness.Components.Should().OnlyContain(component => component.Status == FundAccountCloseReadinessStatusDto.Ready);
            readiness.Components.Should().OnlyContain(component =>
                component.EvidenceLink == $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest");
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
            var detail = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));
            var securityId = detail.SecurityMasterPassports.Should().ContainSingle(passport => passport.Symbol == "AAPL").Subject.SecurityId;
            securityId.Should().NotBeNull();

            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var detectedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
            await repository.CreateIfMissingAsync(
                fixture.QueueScope,
                new ReconciliationBreakQueueItem(
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
                UpstreamSyncCursor: $"security-master-override:{securityId.Value:N}:Pending")
                {
                    TenantId = fixture.QueueScope.TenantId,
                    CompanyId = fixture.QueueScope.CompanyId
                });

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().Be(70);
            readiness.Components.Should().Contain(component =>
                component.Key == "security-master-completeness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Blocked &&
                component.Score == 0 &&
                component.BlockingReason!.Contains("Security Master case", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.security_master.casework_blocked" &&
                blocker.Category == "SecurityMaster" &&
                blocker.Severity == "Critical" &&
                blocker.EvidenceLink == $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest");
            readiness.Components.Should().Contain(component =>
                component.Key == "approvals-casework" &&
                component.Status == FundAccountCloseReadinessStatusDto.Blocked &&
                component.Score == 0);
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.approvals.critical_pending" &&
                blocker.Category == "Approvals");
            readiness.NextActions.Should().Contain(action => action.Code == "close.security_master.casework_blocked");
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
                recordInternalSnapshot: true,
                includeBreakQueue: true);

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

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
    public async Task Scenario_FundAccountCloseReadiness_RequiresSecurityMasterReviewWhenResolvedPassportIsStale()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));
            await BackdateBrokerageProjectionAsync(fixture, TimeSpan.FromHours(2));
            var reconciliation = await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(ProviderStaleAfterMinutes: 30, RequestedBy: "ops-user"));
            reconciliation.SecurityMasterPassports.Should().ContainSingle(passport =>
                passport.Symbol == "AAPL" &&
                passport.Status == ProviderSecurityMasterPassportStatusDto.Resolved &&
                passport.ProviderIsStale);

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.ReviewRequired);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Components.Should().Contain(component =>
                component.Key == "security-master-completeness" &&
                component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired &&
                component.BlockingReason.Contains("remain open for steward review", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.security_master.casework_review" &&
                blocker.Category == "SecurityMaster" &&
                blocker.Severity == "Warning" &&
                blocker.EvidenceLink == $"/api/fund-accounts/{fixture.AccountId}/brokerage-sync/reconciliation/latest");
            readiness.NextActions.Should().Contain(action =>
                action.Code == "close.security_master.casework_review" &&
                action.Label == "Resolve Security Master coverage");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_BlocksWhenRequiredProviderCapabilitiesAreMissing()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: false));
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Components.Should().Contain(component =>
                component.Key == "provider-freshness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Blocked &&
                component.BlockingReason!.Contains("AccountBalances", StringComparison.OrdinalIgnoreCase) &&
                component.BlockingReason.Contains("ReconciliationFeed", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.provider_capability.blocked" &&
                blocker.Category == "ProviderData" &&
                blocker.Severity == "Critical");
            readiness.NextActions.Should().Contain(action => action.Code == "close.provider_capability.blocked");
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
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                recordCustodianPosition: true,
                custodianPositionMarketValue: 18_600m);
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

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
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.ReviewRequired);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().BeLessThan(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "provider-freshness" &&
                component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired &&
                component.BlockingReason!.Contains("CorporateActions", StringComparison.OrdinalIgnoreCase));
            readiness.Blockers.Should().Contain(blocker =>
                blocker.Code == "close.provider_capability.review" &&
                blocker.Category == "ProviderData" &&
                blocker.Severity == "Warning");
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
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter());
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.ReviewRequired);
            readiness.IsReadyToClose.Should().BeFalse();
            readiness.Score.Should().Be(95);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.ReviewRequired &&
                component.Score == 5 &&
                component.Reason.Contains("matched Security Master schedule feed evidence", StringComparison.OrdinalIgnoreCase));
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
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new CorporateActionActivityAdapter());
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Ready &&
                component.Reason.Contains("Security Master schedule feed evidence", StringComparison.OrdinalIgnoreCase));
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
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new LoanScheduleActivityAdapter());
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Ready &&
                component.Reason.Contains("Security Master schedule feed evidence", StringComparison.OrdinalIgnoreCase));
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
                includeBreakQueue: true,
                internalSecuritiesMarketValue: 9_900m,
                internalUnrealizedPnl: -100m,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true),
                portfolioAdapter: new FixedIncomePortfolioAdapter(),
                activityAdapter: new PrincipalActivityAdapter());
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

            readiness.Should().NotBeNull();
            readiness!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            readiness.IsReadyToClose.Should().BeTrue();
            readiness.Score.Should().Be(100);
            readiness.Components.Should().Contain(component =>
                component.Key == "corporate-action-factor-readiness" &&
                component.Status == FundAccountCloseReadinessStatusDto.Ready &&
                component.Reason.Contains("Security Master schedule feed evidence", StringComparison.OrdinalIgnoreCase));
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
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));

            var readiness = await fixture.CloseReadiness.GetAsync(fixture.AccountId, fixture.QueueScope);

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
            await fixture.RunReconciliationAsync(
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

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_IgnoresCrossTenantCaseworkAndRejectsForeignAuthority()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));
            await fixture.RunReconciliationAsync(
                fixture.AccountId,
                new ProviderLedgerReconciliationRequestDto(RequestedBy: "ops-user"));
            var foreignScope = new ReconciliationBreakQueueScope("tenant-foreign", "company-foreign");
            var repository = fixture.Services.GetRequiredService<IReconciliationBreakQueueRepository>();
            var detectedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
            await repository.CreateIfMissingAsync(
                foreignScope,
                new ReconciliationBreakQueueItem(
                    BreakId: $"foreign-close-case:{fixture.AccountId:N}",
                    RunId: "foreign-provider-ledger-run",
                    StrategyName: "Foreign provider ledger reconciliation",
                    Category: ReconciliationBreakCategory.AmountMismatch,
                    Status: ReconciliationBreakQueueStatus.Open,
                    Variance: 250m,
                    Reason: "Foreign tenant casework must not affect this close.",
                    AssignedTo: "foreign-controller",
                    DetectedAt: detectedAt,
                    LastUpdatedAt: detectedAt,
                    Severity: ReconciliationBreakSeverity.Critical,
                    RequiredSignoffRole: "Controller",
                    SignoffStatus: "pending-signoff",
                    FundAccountId: fixture.AccountId.ToString("D"))
                {
                    TenantId = foreignScope.TenantId,
                    CompanyId = foreignScope.CompanyId
                });

            var owned = await fixture.CloseReadiness.GetAsync(
                fixture.AccountId,
                fixture.QueueScope);
            var foreign = await fixture.CloseReadiness.GetAsync(
                fixture.AccountId,
                foreignScope);

            owned.Should().NotBeNull();
            owned!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Ready);
            owned.Blockers.Should().BeEmpty();
            foreign.Should().NotBeNull();
            foreign!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            foreign.IsReadyToClose.Should().BeFalse();
            foreign.LatestReconciliationRunId.Should().BeNull();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_FundAccountCloseReadiness_MissingScopeOrCaseworkStore_FailsClosed()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: false,
                capabilityRouter: new FixedCapabilityRouter(IsRoutable: true));

#pragma warning disable CS0618 // Explicitly verifies the retained compatibility surface fails closed.
            var unscoped = await fixture.CloseReadiness.GetAsync(fixture.AccountId);
#pragma warning restore CS0618
            var missingStore = await fixture.CloseReadiness.GetAsync(
                fixture.AccountId,
                fixture.QueueScope);

            unscoped.Should().NotBeNull();
            unscoped!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            unscoped.IsReadyToClose.Should().BeFalse();
            unscoped.LatestReconciliationRunId.Should().BeNull();
            unscoped.Blockers.Should().ContainSingle(blocker =>
                blocker.Code == "close.authority.scope_required");
            missingStore.Should().NotBeNull();
            missingStore!.Status.Should().Be(FundAccountCloseReadinessStatusDto.Blocked);
            missingStore.IsReadyToClose.Should().BeFalse();
            missingStore.LatestReconciliationRunId.Should().BeNull();
            missingStore.Blockers.Should().ContainSingle(blocker =>
                blocker.Code == "close.casework.authority_unavailable");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Endpoint_FundAccountCloseReadiness_RequiresTenantAndCompanyScope()
    {
        var root = CreateTempRoot();
        try
        {
            await using var fixture = await CreateFixtureAsync(
                root,
                includeSecurityLookup: true,
                includeBreakQueue: true);
            await using var app = await CreateEndpointAppAsync(
                fixture,
                includeTenantCompanyScope: false);

            var response = await app.GetTestClient().GetAsync(
                $"/api/fund-accounts/{fixture.AccountId}/close-readiness");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        ISecurityMasterConflictService? securityMasterConflictService = null,
        ISecurityValidationGateService? securityValidationGate = null,
        bool recordCustodianPosition = false,
        decimal custodianPositionQuantity = 100m,
        decimal custodianPositionMarketValue = 18_750m,
        decimal? custodianPositionCostBasis = 15_000m,
        bool recordBankStatement = false,
        decimal bankClosingBalance = 50_000m,
        decimal? bankIncomeAmount = null,
        SecurityStatusDto securityStatus = SecurityStatusDto.Active,
        IReconciliationBreakQueueRepository? breakQueueRepository = null,
        string owningTenantId = "tenant-provider-ledger-tests",
        string owningCompanyId = "company-provider-ledger-tests")
    {
        var accountId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();
        var ledgerPeriodId = Guid.NewGuid();
        var fixtureAsOfDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new BrokeragePortfolioSyncOptions(root, TimeSpan.FromMinutes(30), "alpaca"));
        services.AddSingleton<InMemoryFundAccountService>();
        services.AddSingleton<IFundAccountService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IAccountManagementService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        services.AddSingleton<IAccountQueryService>(sp => sp.GetRequiredService<InMemoryFundAccountService>());
        var ledgerBookService = Substitute.For<ILedgerBookService>();
        ledgerBookService.ListBooksAsync(Arg.Any<LedgerBookQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerBookDto>>(
            [
                new LedgerBookDto(
                    ledgerBookId,
                    accountId.ToString("D"),
                    accountId,
                    FundStructureNodeKindDto.Fund,
                    "Provider Ledger Test Book",
                    "USD",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
            ]));
        ledgerBookService.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerPeriodDto>>(
            [
                new LedgerPeriodDto(
                    ledgerPeriodId,
                    ledgerBookId,
                    fixtureAsOfDate.Year,
                    fixtureAsOfDate.Month,
                    fixtureAsOfDate.ToString("yyyy-MM"),
                    fixtureAsOfDate.AddDays(-15),
                    fixtureAsOfDate.AddDays(15),
                    LedgerPeriodStatusDto.Open,
                    DateTimeOffset.UtcNow,
                    ClosedAt: null,
                    Version: 1)
            ]));
        services.AddSingleton(ledgerBookService);
        var tenancyRegistry = Substitute.For<IFundProfileTenancyRegistry>();
        tenancyRegistry
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<FundProfileOwnership?>(
                new FundProfileOwnership(
                    call.ArgAt<string>(0),
                    owningTenantId,
                    owningCompanyId)));
        services.AddSingleton(tenancyRegistry);
        services.AddSingleton<IBrokeragePortfolioSync>(portfolioAdapter ?? new FixedPortfolioAdapter());
        services.AddSingleton(activityAdapter ?? new EmptyActivityAdapter());
        if (includeSecurityLookup)
        {
            services.AddSingleton<ISecurityReferenceLookup>(new StaticSecurityReferenceLookup(securityStatus));
        }
        if (capabilityRouter is not null)
        {
            services.AddSingleton(capabilityRouter);
        }
        if (breakQueueRepository is not null)
        {
            services.AddSingleton(breakQueueRepository);
        }
        else if (includeBreakQueue)
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
        if (securityMasterConflictService is not null)
        {
            services.AddSingleton(securityMasterConflictService);
        }
        if (securityValidationGate is not null)
        {
            services.AddSingleton(securityValidationGate);
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
            FundId: accountId,
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
                        IsShort: false,
                        CostBasis: custodianPositionCostBasis)
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
            UserPermission.ExecuteTrades,
        bool includeTenantCompanyScope = true)
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
        builder.Services.AddSingleton<IScopedAuthorizationService>(
            new AccountScopedAuthorizationService(fixture.AccountId));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (includeTenantCompanyScope)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = fixture.QueueScope.TenantId;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = fixture.QueueScope.CompanyId;
            }
            await next();
        });
        app.MapFundAccountEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private sealed class AccountScopedAuthorizationService(Guid allowedAccountId) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission required,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var isAllowed =
                scopeKind == AccessScopeKindDto.Account &&
                scopeId == allowedAccountId &&
                (globalPermissions & required) == required;

            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                IsAllowed: isAllowed,
                Actor: actor,
                RequiredPermission: required,
                ScopeKind: scopeKind,
                ScopeId: scopeId,
                Reason: isAllowed
                    ? "The endpoint fixture grants the actor access to its test account."
                    : "The endpoint fixture denies access outside its test account."));
        }
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
        public ReconciliationBreakQueueScope QueueScope { get; } =
            new("tenant-provider-ledger-tests", "company-provider-ledger-tests");

        public Task<ProviderLedgerReconciliationDetailDto> RunReconciliationAsync(
            Guid accountId,
            ProviderLedgerReconciliationRequestDto? request = null,
            CancellationToken ct = default)
            => Reconciliation.RunAsync(accountId, QueueScope, request, ct);

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

    private sealed class DerivativeContractPortfolioAdapter : IBrokeragePortfolioSync
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
                new BrokerageBalanceSnapshotDto(50_000m, 62_000m, 62_000m, "USD"),
                [
                    new BrokeragePositionSnapshotDto(
                        "AAPL260619C00190000",
                        2m,
                        35m,
                        40m,
                        8_000m,
                        0m,
                        "Option",
                        "AAPL Jun 2026 190 Call",
                        "pos-aapl-option",
                        "USD"),
                    new BrokeragePositionSnapshotDto(
                        "ESM6",
                        1m,
                        4_000m,
                        4_000m,
                        4_000m,
                        0m,
                        "Future",
                        "E-mini S&P 500 Jun 2026",
                        "pos-esm6",
                        "USD")
                ],
                retrievedAt));
        }
    }

    private sealed class CustomPrivateProfilePortfolioAdapter : IBrokeragePortfolioSync
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
                new BrokerageBalanceSnapshotDto(50_000m, 57_500m, 57_500m, "USD"),
                [
                    new BrokeragePositionSnapshotDto(
                        "PRIVLOAN",
                        1m,
                        7_500m,
                        7_500m,
                        7_500m,
                        0m,
                        "Private Credit Loan",
                        "Private direct-lending profile",
                        "pos-private-loan",
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

    private sealed class FxSettlementActivityAdapter : IBrokerageActivitySync
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
                        "fx-settlement-eurusd",
                        "FX_SETTLEMENT",
                        250m,
                        "USD",
                        retrievedAt.AddDays(-1),
                        "EURUSD",
                        "EUR/USD cash settlement pending ledger confirmation")
                ]));
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

    private sealed class AmortizationScheduleActivityAdapter : IBrokerageActivitySync
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
                        "amortization-ust10y-20260501",
                        "AmortizationScheduleUpdated",
                        "UST10Y",
                        new DateOnly(2026, 5, 1),
                        null,
                        Amount: 125m,
                        Quantity: 0.975m,
                        Currency: "USD",
                        Description: "Monthly amortization schedule update")
                ]));
        }
    }

    private sealed class StaticSecurityReferenceLookup(SecurityStatusDto status = SecurityStatusDto.Active) : ISecurityReferenceLookup
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
                status,
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
            CancellationToken ct = default,
            long? expectedCanonicalVersion = null) =>
            throw new NotSupportedException();

        public Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
            Guid requestedSecurityId,
            OperatorOverrideDecision decision,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticSecurityMasterConflictService(params SecurityMasterConflict[] conflicts)
        : ISecurityMasterConflictService
    {
        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                conflicts
                    .Where(static conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
                    .ToArray());
        }

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(conflicts.FirstOrDefault(conflict => conflict.ConflictId == conflictId));
        }

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct) =>
            Task.CompletedTask;

        public Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed record SymbolValidationIssue(
        string Symbol,
        string Code,
        string Title,
        string Message,
        params string[] AffectedFields);

    private sealed class SymbolIssueSecurityValidationGate(params SymbolValidationIssue[] issues) : ISecurityValidationGateService
    {
        private readonly Dictionary<string, SymbolValidationIssue> _issues = issues.ToDictionary(
            static issue => issue.Symbol.Trim().ToUpperInvariant(),
            StringComparer.OrdinalIgnoreCase);

        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var normalized = symbol.Trim().ToUpperInvariant();
            var issue = _issues.TryGetValue(normalized, out var configured)
                ? configured
                : new SymbolValidationIssue(
                    normalized,
                    "SM_PROVIDER_POSITION_SECURITY_UNRESOLVED",
                    "Security Master identity unresolved",
                    $"Provider symbol {normalized} could not be resolved.",
                    "symbol");
            var validationIssue = new SecurityValidationIssueDto(
                SecurityValidationSeverityDto.Error,
                issue.Code,
                issue.Title,
                issue.Message,
                issue.AffectedFields,
                "Attach the missing Security Master evidence and rerun provider-ledger reconciliation.",
                [
                    new SecurityEvidenceLinkDto(
                        "ProviderPosition",
                        normalized,
                        "/workstation/data/security-master",
                        issue.Title)
                ]);
            var report = new SecurityValidationReportDto(
                null,
                normalized,
                DateTimeOffset.UtcNow,
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                [validationIssue]);
            return Task.FromResult(new SecurityValidationGateResultDto(
                workflow,
                normalized,
                SecurityId: null,
                IsResolved: false,
                IsBlocked: true,
                report,
                Snapshot: null));
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
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
