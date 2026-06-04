using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Application.AssetOperations;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.AssetOperations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_MultiAssetCoverage_ShouldReturnSharedReadinessRows()
    {
        await using var app = await CreateAppAsync();
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
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "DirectLoan" &&
            row.ReconciliationSignals["breaks"].Contains("loan schedule", StringComparison.OrdinalIgnoreCase) &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "AssetOperations") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "LoanScheduleEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "CommitmentCovenantEvidence") &&
            row.DrillThroughTargets.Any(static target => target.TargetType == "PaydownObligationLedger"));
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
