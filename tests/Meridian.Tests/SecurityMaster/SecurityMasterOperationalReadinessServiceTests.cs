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
            "StructuredCredit",
            "PrivateFundInterest",
            "PrivateCompanyEquity",
            "RealEstateHolding",
            "CommitmentGuarantee",
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

    [Theory]
    [InlineData("StructuredCredit", "Trustee report", "StructuredCreditTrusteeEvidence")]
    [InlineData("PrivateFundInterest", "Administrator statement", "FundAdministratorStatement")]
    [InlineData("PrivateCompanyEquity", "Cap table", "CapTableEvidence")]
    [InlineData("RealEstateHolding", "Property manager statement", "PropertyManagerEvidence")]
    [InlineData("CommitmentGuarantee", "Commitment agreement", "CommitmentAgreementEvidence")]
    public async Task GetReadinessAsync_AlternativeAssetRowsWithoutEvidence_ShouldStayReviewGated(
        string assetClass,
        string expectedEvidenceLabel,
        string expectedTargetType)
    {
        var service = new SecurityMasterOperationalReadinessService();

        var result = await service.GetReadinessAsync(new SecurityMasterOperationalReadinessRequest(AssetClass: assetClass));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("ReviewRequired");
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "ProviderEvidence" &&
            requirement.Status == "ReviewRequired" &&
            requirement.Label.Contains(expectedEvidenceLabel, StringComparison.OrdinalIgnoreCase));
        row.Blockers.Should().Contain(blocker =>
            blocker.Code == $"{assetClass}:provider-evidence-review" &&
            blocker.Severity == "Review");
        row.DrillThroughTargets.Should().Contain(target => target.TargetType == expectedTargetType);
    }

    [Theory]
    [InlineData(
        "StructuredCredit",
        "Trustee report Servicer report Factor schedule Collateral tape Dealer pricing Valuation source Cash remittance Structured credit security / factor amortization / interest income / realized and unrealized P&L quantity market value cash remittance factor schedule collateral tape trustee report",
        "FactorScheduleEvidence")]
    [InlineData(
        "PrivateFundInterest",
        "Administrator statement GP statement Capital call Distribution notice NAV statement Capital account schedule Private fund interest / capital call receivable-payable / distribution income / NAV adjustment commitment funded unfunded NAV capital call distribution capital account",
        "CapitalAccountScheduleEvidence")]
    [InlineData(
        "PrivateCompanyEquity",
        "Cap table Transfer-agent evidence Financing documents Share-class documents Valuation memo 409A Transaction evidence Exit evidence Dividend evidence Private company equity / cost basis / valuation adjustment / dividend and realized gain/loss ownership market value cost basis cap table valuation transaction restriction",
        "PrivateCompanyValuationEvidence")]
    [InlineData(
        "RealEstateHolding",
        "Property manager statement Rent roll Lease schedule Appraisal Debt-service statement Ownership evidence SPV evidence Real estate holding / rental income / appraisal adjustment / debt-service and ownership accounting ownership market value cash rent roll lease appraisal debt service SPV",
        "RentRollLeaseEvidence")]
    [InlineData(
        "CommitmentGuarantee",
        "Commitment agreement Guarantee agreement Draw notice Usage notice Fee schedule Accrual schedule Collateral evidence Covenant evidence Release evidence Expiry evidence Commitment or guarantee exposure / fee accrual / contingent obligation / release accounting commitment guarantee exposure fee accrual draw usage collateral covenant release",
        "ReleaseExpiryEvidence")]
    public async Task GetReadinessAsync_AlternativeAssetRowsWithRetainedEvidence_ShouldBecomeReady(
        string assetClass,
        string retainedEvidence,
        string expectedTargetType)
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "alt-provider",
            ExternalAccountId: "PA-ALT",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: "evidence/provider-ledger/alt-latest.json",
            EvidenceItems:
            [
                Evidence($"{assetClass}:retained", "ShadowBook", retainedEvidence, "Ready", assetClass: assetClass)
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: assetClass, EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("Ready");
        row.Blockers.Should().BeEmpty();
        row.EvidenceRequirements.Should().OnlyContain(requirement => requirement.Status == "Ready");
        row.DrillThroughTargets.Should().Contain(target =>
            target.TargetType == expectedTargetType &&
            target.Status == "Ready");
    }

    [Fact]
    public async Task GetReadinessAsync_AlternativeAssetRowWithStaleEvidence_ShouldStayReviewRequired()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "fund-admin",
            ExternalAccountId: "PA-FUND",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: "evidence/provider-ledger/fund-latest.json",
            EvidenceItems:
            [
                Evidence("fund-nav-stale", "ProviderEvidence", "Administrator statement NAV statement Capital account schedule", "Stale", assetClass: "PrivateFundInterest", reason: "NAV statement is older than the controller freshness policy.")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "PrivateFundInterest", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("ReviewRequired");
        row.EvidenceRequirements.Should().Contain(requirement =>
            requirement.Category == "ProviderEvidence" &&
            requirement.Status == "ReviewRequired");
        row.Blockers.Should().Contain(blocker =>
            blocker.Source == "ProviderEvidence" &&
            blocker.Severity == "Review" &&
            blocker.Message.Contains("NAV statement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetReadinessAsync_AlternativeAssetRowWithBlockedEvidence_ShouldFailClosed()
    {
        var service = new SecurityMasterOperationalReadinessService();
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "trustee",
            ExternalAccountId: "PA-STRUCTURED",
            ReconciliationStatus: "Breaks",
            ReconciliationDetailPath: "evidence/provider-ledger/structured-latest.json",
            EvidenceItems:
            [
                Evidence("structured-collateral-break", "ProviderEvidence", "Collateral tape Factor schedule Trustee report", "Blocked", assetClass: "StructuredCredit", reason: "Collateral tape was rejected by reconciliation controls.")
            ]);

        var result = await service.GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: "StructuredCredit", EvidenceSnapshot: snapshot));

        var row = result.AssetClasses.Should().ContainSingle().Subject;
        row.Status.Should().Be("Blocked");
        row.Blockers.Should().Contain(blocker =>
            blocker.Severity == "Blocker" &&
            blocker.Message.Contains("Collateral tape", StringComparison.OrdinalIgnoreCase));
        row.DrillThroughTargets.Should().Contain(target =>
            target.TargetType == "StructuredCollateralTape" &&
            target.Status == "Blocked");
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
