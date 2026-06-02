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

    [Fact]
    public async Task GetReadinessAsync_WithRetainedProviderEvidence_ShouldClearHardBlockerReview()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "ibkr",
            ExternalAccountId: "PA-DERIV",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: "evidence/provider-ledger/latest.json",
            EvidenceItems:
            [
                Evidence("option-position", "ProviderEvidence", "Quote Option contract Underlying security Trade Cash quantity market value option contract metadata", "Ready", assetClass: "Option"),
                Evidence("option-ledger", "Ledger", "Derivative asset liability premium realized unrealized P&L quantity market value cash option contract metadata", "Ready", assetClass: "Option"),
                Evidence("option-recon", "Reconciliation", "quantity market value cash option contract metadata", "Ready", assetClass: "Option")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "Option", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("Ready");
        row.Blockers.Should().BeEmpty();
        row.EvidenceRequirements.Should().OnlyContain(requirement => requirement.Status == "Ready");
        row.ReconciliationSignals["retainedEvidence"].Should().Contain("Option contract");
        row.ReconciliationSignals["providerStatus"].Should().Be("Matched");
    }

    [Fact]
    public async Task GetReadinessAsync_WithRetainedSevereReconciliationBreak_ShouldFailClosed()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "custodian",
            ExternalAccountId: "PA-FI",
            ReconciliationStatus: "Breaks",
            ReconciliationDetailPath: "evidence/provider-ledger/latest.json",
            EvidenceItems:
            [
                Evidence("factor-break", "ProviderEvidence", "factor schedule", "Blocked", assetClass: "Bond", reason: "Factor schedule evidence is missing for fixed-income valuation support."),
                Evidence("market-value", "ShadowBook", "market value", "Ready", assetClass: "Bond"),
                Evidence("cash", "ShadowBook", "cash", "Ready", assetClass: "Bond")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "Bond", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("Blocked");
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "ProviderEvidence" &&
            requirement.Status == "Blocked");
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "Reconciliation" &&
            requirement.Status == "ReviewRequired");
        row.Blockers.Should().Contain(blocker =>
            blocker.Code == "Bond:retained-evidence:factorschedule" &&
            blocker.Severity == "Blocker" &&
            blocker.Message.Contains("Factor schedule evidence is missing", StringComparison.OrdinalIgnoreCase));
    }

    private static SecurityMasterOperationalEvidenceItem Evidence(
        string id,
        string category,
        string kind,
        string status,
        string? assetClass = null,
        string? reason = null)
        => new(
            EvidenceId: id,
            Category: category,
            EvidenceKind: kind,
            Status: status,
            Source: "test-evidence",
            AssetClass: assetClass,
            EvidenceRoute: "/evidence",
            EvidenceLink: "evidence/provider-ledger/latest.json",
            Reason: reason);
}
