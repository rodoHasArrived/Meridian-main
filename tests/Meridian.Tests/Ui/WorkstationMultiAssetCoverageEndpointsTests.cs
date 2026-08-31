using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_MultiAssetCoverage_ShouldReturnSharedReadinessRows()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewTrades);
        using var client = app.GetTestClient();

        var payload = await client.GetFromJsonAsync<MultiAssetCoverageSummaryDto>(
            $"{UiApiRoutes.WorkstationPortfolioMultiAssetCoverage}?fundAccountId=fund-ops&entity=master-fund",
            ServerJsonOptions);

        payload.Should().NotBeNull();
        payload!.FundAccountId.Should().Be("fund-ops");
        payload.Entity.Should().Be("master-fund");
        payload.DrillThroughRoutes.Should().ContainKey("coverage");
        payload.DrillThroughRoutes.Should().ContainKeys(
            "securityMaster",
            "securityMasterProfiles",
            "providerEvidence",
            "reconciliation",
            "ledgerMapping",
            "assetOperations",
            "closeReadiness");
        payload.AssetPacks.Should().Contain(static pack =>
            pack.PackId == "private-loan-credit" &&
            pack.AutomationDepth == "DeepAccountingAutomation" &&
            pack.ContractSchema.Rates.Contains("reference rate") &&
            pack.LifecycleEvents.Contains("Draw") &&
            pack.LifecycleEvents.Contains("Repayment") &&
            pack.LifecycleCoverage.Any(coverage =>
                coverage.LifecycleEvent == "Draw" &&
                coverage.AccountingAutomationStatus == "AutomatedTemplateAvailable" &&
                coverage.JournalTemplateIds.Contains("asset-pack.principal-draw")) &&
            pack.ValuationMethods.Contains("DiscountedCashFlow") &&
            pack.AccountingRules.JournalTemplateEvents.Contains("principal repayment") &&
            pack.AccountingRules.JournalTemplates.Any(template =>
                template.LifecycleEvent == "Draw" &&
                template.TemplateId == "asset-pack.principal-draw" &&
                template.AccountingBases.Contains("GAAP") &&
                template.CurrencyScopes.Contains("transaction currency") &&
                template.EntityScopes.Contains("book")) &&
            pack.RegistryValidationStatus == "Valid" &&
            pack.RegistryValidationIssues.Count == 0 &&
            pack.ValidationRules.RequiredFields.Contains("source evidence or operator rationale") &&
            pack.AdmissionPolicy.RequiresJournalTemplateBeforeAutomatedPosting &&
            pack.AdmissionPolicy.AccountingPostingPolicy.Contains("idempotency key") &&
            pack.ReportingTaxonomy.Risk.Contains("credit risk") &&
            pack.LedgerExtensionPolicy.Contains("core ledger", StringComparison.OrdinalIgnoreCase));
        payload.AssetPacks.Should().Contain(static pack =>
            pack.PackId == "fixed-income" &&
            pack.AssetClasses.Contains("StructuredCredit"));
        payload.AssetPacks.Should().Contain(static pack =>
            pack.PackId == "private-fund-partnership" &&
            pack.AssetClasses.Contains("PrivateFundInterest") &&
            pack.AssetClasses.Contains("PrivateCompanyEquity"));
        payload.AssetPacks.Should().Contain(static pack =>
            pack.PackId == "real-estate" &&
            pack.AssetClasses.Contains("RealEstateHolding"));
        payload.AssetPacks.Should().Contain(static pack =>
            pack.PackId == "commitment-guarantee" &&
            pack.AssetClasses.Contains("CommitmentGuarantee"));
        payload.AssetPacks.Should().Contain(static pack =>
            pack.PackId == "controlled-other-asset" &&
            pack.AutomationDepth == "WideCapture" &&
            pack.AssetClasses.Contains("OtherSecurity") &&
            pack.AssetClasses.Contains("CustomAsset") &&
            // Art, insurance policies and vehicles are anticipated coverage, not modelled classes,
            // so they are reported apart from what the pack actually covers today.
            pack.PlannedAssetClasses.Contains("Art") &&
            pack.PlannedAssetClasses.Contains("InsurancePolicy") &&
            pack.PlannedAssetClasses.Contains("Vehicle") &&
            pack.LifecycleCoverage.Any(coverage =>
                coverage.LifecycleEvent == "Maturity" &&
                coverage.AccountingAutomationStatus == "ManualReviewRequired") &&
            pack.AdmissionPolicy.AllowsWideCapture &&
            pack.AdmissionPolicy.AccountingPostingPolicy.Contains("accounting automation remains disabled") &&
            pack.ValuationMethods.Contains("Appraisal") &&
            pack.ValidationRules.IncompatibleCombinations.Contains("deep accounting automation without journal template coverage"));
        payload.AssetPacks
            .Where(static pack => pack.PackId != "controlled-other-asset")
            .Should()
            .OnlyContain(static pack => pack.AutomationDepth == "DeepAccountingAutomation");
        payload.AssetPacks.Should().OnlyContain(static pack =>
            pack.RegistryValidationStatus == "Valid" &&
            pack.RegistryValidationIssues.Count == 0);
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "DirectLoan" &&
            row.ReconciliationSignals["breaks"].Contains("loan schedule", StringComparison.OrdinalIgnoreCase) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "AssetOperations") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "LoanScheduleEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "CommitmentCovenantEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "PaydownObligationLedger") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "DirectLendingRuleKernel"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "Bond" &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "FactorCorporateActionEvidence"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "StructuredCredit" &&
            row.EvidenceRequirements.Any(static requirement =>
                requirement.Category == "ProviderEvidence" &&
                requirement.Label.Contains("Trustee report", StringComparison.OrdinalIgnoreCase)) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "StructuredCreditTrusteeEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "FactorScheduleEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "StructuredCollateralTape") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "StructuredValuationEvidence"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "PrivateFundInterest" &&
            row.EvidenceRequirements.Any(static requirement => requirement.Label.Contains("Capital account schedule", StringComparison.OrdinalIgnoreCase)) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "FundAdministratorStatement") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "CapitalAccountScheduleEvidence"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "PrivateCompanyEquity" &&
            row.EvidenceRequirements.Any(static requirement => requirement.Label.Contains("Cap table", StringComparison.OrdinalIgnoreCase)) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "CapTableEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "PrivateCompanyValuationEvidence"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "RealEstateHolding" &&
            row.EvidenceRequirements.Any(static requirement => requirement.Label.Contains("Rent roll", StringComparison.OrdinalIgnoreCase)) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "PropertyManagerEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "RealEstateAppraisalEvidence"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "CommitmentGuarantee" &&
            row.EvidenceRequirements.Any(static requirement => requirement.Label.Contains("Fee schedule", StringComparison.OrdinalIgnoreCase)) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "CommitmentAgreementEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "ReleaseExpiryEvidence"));
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "CustomAsset" &&
            row.EvidenceRequirements.Any(static requirement => requirement.Category == "Governance") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "AssetProfileLineage") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "ServicerTrusteeEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "StructuredValuationEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "ObligationCloseEvidence"));
        payload.AssetClasses.Should().OnlyContain(static row =>
            row.DrillThroughTargets.Any(static target => target.TargetType == "SecurityMasterPassport") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "ProviderEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "ReconciliationCase") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "LedgerMapping") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "AssetOperations") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "CloseReadiness"));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_MultiAssetCoverage_WithoutCompanyScope_ShouldFailClosed()
    {
        await using var app = await CreateAppAsync(
            currentUserCompanyId: null,
            currentUserTenantId: "tenant-test");

        using var response = await app.GetTestClient().GetAsync(
            $"{UiApiRoutes.WorkstationPortfolioMultiAssetCoverage}?fundAccountId={Guid.NewGuid():D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_AssetOperations_ShouldReturnSameSharedShapeForLoanAndBondSubjects()
    {
        var loanSecurityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bondSecurityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var store = new InMemoryAssetOperationsProjectionStore();
        await store.UpsertAsync(BuildProjection(loanSecurityId, "DirectLoan", "Northwind Senior Term Loan"), Approval());
        await store.UpsertAsync(BuildProjection(bondSecurityId, "Bond", "Meridian 5.875% 2031 Corporate Bond"), Approval());

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IAssetOperationsProjectionStore>(store);
            services.AddSingleton<IAssetOperationsQueryService, AssetOperationsReadService>();
        });
        using var client = app.GetTestClient();

        var loan = await client.GetFromJsonAsync<AssetOperationsDetailDto>(
            UiApiRoutes.WorkstationAssetOperations.Replace("{securityId:guid}", loanSecurityId.ToString("D"), StringComparison.OrdinalIgnoreCase),
            ServerJsonOptions);
        var bond = await client.GetFromJsonAsync<AssetOperationsDetailDto>(
            UiApiRoutes.WorkstationAssetOperations.Replace("{securityId:guid}", bondSecurityId.ToString("D"), StringComparison.OrdinalIgnoreCase),
            ServerJsonOptions);

        loan.Should().NotBeNull();
        bond.Should().NotBeNull();
        loan!.Subject.AssetClass.Should().Be("DirectLoan");
        bond!.Subject.AssetClass.Should().Be("Bond");
        loan.ProjectedCashFlows.Should().ContainSingle(static flow => flow.FlowType == "Interest");
        bond.ProjectedCashFlows.Should().ContainSingle(static flow => flow.FlowType == "Coupon");
        loan.TermsObligationsTimeline.Should().NotBeNull();
        bond.TermsObligationsTimeline.Should().NotBeNull();
        loan.TermsObligationsTimeline!.Events.Should().ContainSingle(static timelineEvent =>
            timelineEvent.EventKind == "Interest" &&
            timelineEvent.EventLane == "Interest" &&
            timelineEvent.ExpectedAmount == 100m);
        bond.TermsObligationsTimeline!.Events.Should().ContainSingle(static timelineEvent =>
            timelineEvent.EventKind == "Coupon" &&
            timelineEvent.EventLane == "Coupon" &&
            timelineEvent.ExpectedAmount == 100m);
        loan.Readiness.Capabilities.Should().BeEquivalentTo(bond.Readiness.Capabilities);
    }

    private static AssetOperationsProjectionDto BuildProjection(Guid securityId, string assetClass, string displayName)
    {
        var projectionRunId = Guid.NewGuid();
        var flowType = string.Equals(assetClass, "Bond", StringComparison.OrdinalIgnoreCase) ? "Coupon" : "Interest";
        var subject = new AssetOperationSubjectDto(
            securityId,
            assetClass,
            displayName,
            "InternalCode:OPS",
            ["Identity", "TermsHistory", "LifecycleState", "ProjectedCashFlows", "ActualActivity", "Reconciliation", "LedgerProjection", "Evidence", "WorkflowAudit", "Readiness"]);
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            "Ready",
            subject.OperationalProfile,
            subject.OperationalProfile,
            [],
            [],
            DateTimeOffset.UtcNow,
            assetClass,
            securityId.ToString("D"));
        return new AssetOperationsProjectionDto(
            subject,
            [new AssetTermsVersionDto(Guid.NewGuid(), securityId, 1, "terms-hash", new DateOnly(2026, 1, 1), DateTimeOffset.UtcNow, assetClass, securityId.ToString("D"), $"{displayName} terms")],
            [new AssetLifecycleEventDto(Guid.NewGuid(), securityId, "Lifecycle", "Active", new DateOnly(2026, 1, 1), DateTimeOffset.UtcNow, assetClass, securityId.ToString("D"), "Active lifecycle")],
            [new AssetCashFlowProjectionRunDto(projectionRunId, securityId, new DateOnly(2026, 6, 30), "test-engine", "Completed", DateTimeOffset.UtcNow, assetClass, securityId.ToString("D"))],
            [new AssetProjectedCashFlowDto(Guid.NewGuid(), projectionRunId, securityId, 1, flowType, new DateOnly(2026, 6, 30), 100m, "USD", "Projected", SourceDomain: assetClass, SourceEntityId: securityId.ToString("D"))],
            [new AssetActualActivityDto(Guid.NewGuid(), securityId, $"{flowType}Payment", new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 1), 100m, "USD", "Posted", assetClass, securityId.ToString("D"), "evidence://activity")],
            [new AssetReconciliationRunDto(Guid.NewGuid(), securityId, projectionRunId, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, assetClass, securityId.ToString("D"))],
            [],
            [new AssetLedgerProjectionDto(Guid.NewGuid(), securityId, "LedgerProjection", new DateOnly(2026, 6, 30), "Primary", "Ready", 100m, 0m, "USD", assetClass, securityId.ToString("D"), "ledger://projection")],
            readiness,
            [new AssetLifecycleEventDto(Guid.NewGuid(), securityId, "WorkflowAudit", "Approved", new DateOnly(2026, 1, 1), DateTimeOffset.UtcNow, assetClass, securityId.ToString("D"), "Approved projection")]);
    }

    private static AssetOperationsWriteApprovalDto Approval()
        => new("ops-user", "approval:test", "unit-test", DateTimeOffset.UtcNow);
}
