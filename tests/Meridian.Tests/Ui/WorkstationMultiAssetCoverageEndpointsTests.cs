using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.TestHost;

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
            "closeReadiness");
        payload.AssetClasses.Should().Contain(static row =>
            row.AssetClass == "DirectLoan" &&
            row.ReconciliationSignals["breaks"].Contains("loan schedule", StringComparison.OrdinalIgnoreCase) &&
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
            row.DrillThroughTargets.Any(static target => target.TargetType == "CloseReadiness"));
    }
}
