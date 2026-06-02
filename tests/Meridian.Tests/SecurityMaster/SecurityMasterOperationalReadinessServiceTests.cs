using FluentAssertions;
using Meridian.Application.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterOperationalReadinessServiceTests
{
    [Fact]
    public async Task GetReadinessAsync_ShouldDeclareRequiredMultiAssetOperationalCoverage()
    {
        var service = new SecurityMasterOperationalReadinessService();

        var result = await service.GetReadinessAsync(new SecurityMasterOperationalReadinessRequest());

        result.AssetClasses.Select(static row => row.AssetClass).Should().Contain(
            "Equity",
            "Option",
            "Future",
            "FxSpot",
            "Bond",
            "DirectLoan",
            "CustomAsset",
            "OtherSecurity");
        result.AssetClasses.Should().OnlyContain(static row =>
            row.EvidenceRequirements.Any(static requirement => requirement.Category == "SecurityMaster")
            && row.EvidenceRequirements.Any(static requirement => requirement.Category == "ProviderEvidence")
            && row.EvidenceRequirements.Any(static requirement => requirement.Category == "Ledger")
            && row.EvidenceRequirements.Any(static requirement => requirement.Category == "Reconciliation")
            && row.LedgerClassification.ContainsKey("postingGate")
            && row.ReconciliationSignals.ContainsKey("breaks"));
    }

    [Fact]
    public async Task GetReadinessAsync_ShouldKeepGovernedCustomAssetsReviewGated()
    {
        var service = new SecurityMasterOperationalReadinessService();

        var result = await service.GetReadinessAsync(new SecurityMasterOperationalReadinessRequest(AssetClass: "CustomAsset"));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.DisplayName.Should().Contain("MBS");
        row.EvidenceRequirements.Should().Contain(static requirement =>
            requirement.Category == "Governance" &&
            requirement.Status == "Ready");
        row.Blockers.Should().Contain(static blocker =>
            blocker.Source == "ProviderEvidence" &&
            blocker.Severity == "Review");
        row.ReconciliationSignals["breaks"].Should().Contain("custom-profile evidence");
    }
}
