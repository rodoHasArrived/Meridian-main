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
        row.DrillThroughTargets.Should().Contain(target =>
            target.TargetType == "FactorCorporateActionEvidence" &&
            target.Route == "/evidence" &&
            target.Status == "Blocked" &&
            target.Source == "ProviderLedgerReconciliation");
    }

    [Fact]
    public async Task GetReadinessAsync_WithCloseReadinessBlocker_ShouldAttachDrillThroughBlocker()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "custodian",
            ExternalAccountId: "PA-CLOSE",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: "evidence/provider-ledger/latest.json",
            EvidenceItems:
            [
                Evidence("custom-provider", "ProviderEvidence", "Custom profile Factor schedule Dealer pricing NAV Cash Collateral quantity market value cash factor schedule custom-profile evidence", "Ready", assetClass: "CustomAsset"),
                Evidence("custom-ledger", "Ledger", "Profile-derived classification valuation adjustment income accrual commitment accounting", "Ready", assetClass: "CustomAsset"),
                Evidence("custom-recon", "Reconciliation", "quantity market value cash factor schedule custom-profile evidence", "Ready", assetClass: "CustomAsset"),
                Evidence("close-blocker", "CloseReadiness", "close readiness blocker valuation approval", "Blocked", assetClass: "CustomAsset", reason: "Close readiness is blocked until valuation approval evidence is signed off.")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "CustomAsset", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("Blocked");
        row.Blockers.Should().Contain(blocker =>
            blocker.Source == "CloseReadiness" &&
            blocker.Severity == "Blocker" &&
            blocker.EvidenceRoute == "/evidence" &&
            blocker.Message.Contains("valuation approval", StringComparison.OrdinalIgnoreCase));
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "ProviderEvidence" &&
            requirement.Status == "Ready");
    }

    [Fact]
    public async Task GetReadinessAsync_WithPrivateCreditObligationEvidence_ShouldClearLoanProviderGate()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "private-credit-agent",
            ExternalAccountId: "PA-PC",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: "evidence/provider-ledger/private-credit-latest.json",
            EvidenceItems:
            [
                Evidence("loan-security-master", "SecurityMaster", "Private credit borrower identifier commitment schedule covenant obligation", "Ready", assetClass: "PrivateCredit"),
                Evidence("loan-provider", "ProviderEvidence", "Borrower notice commitment schedule unfunded commitment paydown covenant obligation", "Ready", assetClass: "PrivateCredit"),
                Evidence("loan-ledger", "Ledger", "Loan receivable unfunded commitment obligation interest income fees realized unrealized P&L", "Ready", assetClass: "PrivateCredit"),
                Evidence("loan-recon", "Reconciliation", "principal commitment paydown obligation cash collateral market value", "Ready", assetClass: "PrivateCredit")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "DirectLoan", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.DisplayName.Should().Contain("Private credit");
        row.Status.Should().Be("Ready");
        row.Blockers.Should().BeEmpty();
        row.EvidenceRequirements.Should().OnlyContain(requirement => requirement.Status == "Ready");
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "ProviderEvidence" &&
            requirement.Label.Contains("Unfunded commitment", StringComparison.OrdinalIgnoreCase));
        row.DrillThroughTargets.Select(static target => target.TargetType).Should().Contain(
            "LoanScheduleEvidence",
            "CommitmentCovenantEvidence",
            "PaydownObligationLedger",
            "DirectLendingRuleKernel");
        row.DrillThroughTargets.Should().Contain(target =>
            target.TargetType == "PaydownObligationLedger" &&
            target.Source == "LoanAccountingProjector");
        row.DrillThroughTargets.Should().Contain(target =>
            target.TargetType == "DirectLendingRuleKernel" &&
            target.Source == "Meridian.FSharp.DirectLending.Aggregates");
        row.LedgerClassification["classification"].Should().Contain("unfunded commitment obligation");
        row.LedgerClassification["projectors"].Should().Contain("Meridian.FSharp.DirectLending.Aggregates");
        row.ReconciliationSignals["retainedEvidence"].Should().Contain("Borrower notice commitment schedule");
    }

    [Fact]
    public async Task GetReadinessAsync_WithStaleStructuredPrivateAssetEvidence_ShouldKeepProviderReviewGate()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "structured-servicer",
            ExternalAccountId: "PA-STRUCTURED",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: "evidence/provider-ledger/structured-latest.json",
            EvidenceItems:
            [
                Evidence("structured-profile", "SecurityMaster", "Custom profile approved profile profile version required profile fields", "Ready", assetClass: "PrivateAsset"),
                Evidence("structured-provider", "ProviderEvidence", "Servicer report trustee report NAV capital call distribution obligation schedule", "Stale", assetClass: "PrivateAsset", reason: "Servicer report is older than the controller freshness policy."),
                Evidence("structured-ledger", "Ledger", "Profile-derived classification valuation adjustment income accrual commitment obligation accounting", "Ready", assetClass: "PrivateAsset"),
                Evidence("structured-recon", "Reconciliation", "quantity market value cash NAV servicer report trustee report capital call distribution obligation custom-profile evidence", "Ready", assetClass: "PrivateAsset")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "CustomAsset", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("ReviewRequired");
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "ProviderEvidence" &&
            requirement.Status == "ReviewRequired" &&
            requirement.Label.Contains("Servicer report", StringComparison.OrdinalIgnoreCase));
        row.Blockers.Should().Contain(blocker =>
            blocker.Source == "ProviderEvidence" &&
            blocker.Severity == "Review" &&
            blocker.Message.Contains("Servicer report", StringComparison.OrdinalIgnoreCase));
        row.DrillThroughTargets.Select(static target => target.TargetType).Should().Contain(
            "AssetProfileLineage",
            "ServicerTrusteeEvidence",
            "StructuredValuationEvidence",
            "ObligationCloseEvidence");
        row.DrillThroughTargets.Should().Contain(target =>
            target.TargetType == "ObligationCloseEvidence" &&
            target.Status == "ReviewRequired" &&
            target.Source == "FundAccountCloseReadinessService");
        row.ReconciliationSignals["breaks"].Should().Contain("obligation");
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
