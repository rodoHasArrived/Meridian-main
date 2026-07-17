using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityAssetClassCatalogTests
{
    [Fact]
    public void AssetClasses_ShouldExposeCanonicalSecurityMasterCoverage()
    {
        SecurityAssetClassCatalog.AssetClasses.Should().Contain(
            "Equity",
            "Option",
            "Bond",
            "Future",
            "FxSpot",
            "Deposit",
            "MoneyMarketFund",
            "CertificateOfDeposit",
            "CommercialPaper",
            "TreasuryBill",
            "Repo",
            "CashSweep",
            "Swap",
            "DirectLoan",
            "StructuredCredit",
            "PrivateFundInterest",
            "PrivateCompanyEquity",
            "RealEstateHolding",
            "CommitmentGuarantee",
            "Commodity",
            "CryptoCurrency",
            "Cfd",
            "Warrant",
            "OtherSecurity");
    }

    [Fact]
    public void GetPreferredIdentifierKinds_ShouldPrioritizeOccSymbolForOptions()
    {
        var kinds = SecurityAssetClassCatalog.GetPreferredIdentifierKinds("Option");

        kinds.Should().NotBeEmpty();
        kinds[0].Should().Be(SecurityIdentifierKind.OccOptionSymbol);
        kinds.Should().Contain(SecurityIdentifierKind.ProviderSymbol);
    }

    [Fact]
    public void GetOrDefault_ShouldDescribeLotAndScheduleBehaviorForFaceValueAssets()
    {
        var bond = SecurityAssetClassCatalog.GetOrDefault("Bond");

        bond.UsesFaceValueLots.Should().BeTrue();
        bond.SupportsCashflowScheduleByDefault.Should().BeTrue();
        bond.SupportsBasicCreateWorkflow.Should().BeFalse();
    }

    [Fact]
    public void GetOrDefault_ShouldMarkBasicEquityCreateWorkflowAsSupported()
    {
        var equity = SecurityAssetClassCatalog.GetOrDefault("Equity");

        equity.SupportsBasicCreateWorkflow.Should().BeTrue();
        equity.UsesFaceValueLots.Should().BeFalse();
    }

    [Fact]
    public void AssetPackRegistry_ShouldExposeInitialDeepCoveragePacks()
    {
        SecurityAssetPackRegistry.All.Select(static pack => pack.PackId).Should().BeEquivalentTo(
            "cash-bank",
            "public-equity-etf",
            "fixed-income",
            "private-fund-partnership",
            "private-loan-credit",
            "real-estate",
            "derivatives-fx",
            "mortgage-facility-intercompany",
            "commitment-guarantee",
            "controlled-other-asset");

        SecurityAssetPackRegistry.All
            .Where(static pack => pack.PackId != "controlled-other-asset")
            .Should()
            .OnlyContain(static pack => pack.AutomationDepth == AssetPackAutomationDepth.DeepAccountingAutomation);
        SecurityAssetPackRegistry.Find("controlled-other-asset")!.AutomationDepth.Should().Be(AssetPackAutomationDepth.WideCapture);
    }

    [Fact]
    public void AssetPackRegistry_ShouldClaimAlternativeAssetClassesDirectlyAndRetainWideFallback()
    {
        SecurityAssetPackRegistry.Find("fixed-income")!.AssetClasses.Should().Contain("StructuredCredit");
        SecurityAssetPackRegistry.Find("private-fund-partnership")!.AssetClasses.Should().Contain(
            "PrivateFundInterest",
            "PrivateCompanyEquity");
        SecurityAssetPackRegistry.Find("real-estate")!.AssetClasses.Should().Contain("RealEstateHolding");
        SecurityAssetPackRegistry.Find("commitment-guarantee")!.AssetClasses.Should().Contain("CommitmentGuarantee");

        SecurityAssetPackRegistry.Find("controlled-other-asset")!.AssetClasses.Should().Contain(
            "OtherSecurity",
            "CustomAsset");
    }

    [Fact]
    public void AssetPackRegistry_ShouldSatisfyRequestedObjectiveCoverageMatrix()
    {
        var requiredPackIds = new[]
        {
            "cash-bank",
            "public-equity-etf",
            "fixed-income",
            "private-fund-partnership",
            "private-loan-credit",
            "real-estate",
            "derivatives-fx",
            "mortgage-facility-intercompany",
            "commitment-guarantee",
            "controlled-other-asset"
        };
        var lifecycleEvents = new[]
        {
            "Purchase",
            "Sale",
            "Coupon",
            "Dividend",
            "Draw",
            "Repayment",
            "CapitalCall",
            "Distribution",
            "Appraisal",
            "Impairment",
            "Maturity",
            "Default",
            "Amendment",
            "CorporateAction"
        };
        var valuationMethods = new[]
        {
            "MarketPrice",
            "ManagerReportedNav",
            "Appraisal",
            "DiscountedCashFlow",
            "AmortizedCost",
            "UserEstimate",
            "ExternalModel"
        };

        var builtIns = SecurityAssetPackRegistry.All;

        builtIns.Select(static pack => pack.PackId).Should().BeEquivalentTo(requiredPackIds);
        builtIns.SelectMany(static pack => pack.SupportedLifecycleEvents).Distinct().Should().BeEquivalentTo(lifecycleEvents);
        builtIns.SelectMany(static pack => pack.SupportedValuationMethods).Distinct().Should().BeEquivalentTo(valuationMethods);
        builtIns.Should().OnlyContain(static pack =>
            pack.ContractSchema.Terms.Count > 0 &&
            pack.ContractSchema.Counterparties.Count > 0 &&
            pack.ContractSchema.Dates.Count > 0 &&
            pack.ContractSchema.Currencies.Count > 0 &&
            pack.ContractSchema.Ownership.Count > 0 &&
            pack.ContractSchema.Seniority.Count > 0 &&
            pack.ContractSchema.Collateral.Count > 0 &&
            pack.ContractSchema.Rates.Count > 0 &&
            pack.ContractSchema.OptionalAttributes.Count > 0 &&
            pack.AccountingRules.JournalTemplates.All(template =>
                template.LifecycleEvent.Length > 0 &&
                template.AccountingBases.Count > 0 &&
                template.CurrencyScopes.Count > 0 &&
                template.EntityScopes.Count > 0) &&
            pack.ValidationRules.RequiredFields.Count > 0 &&
            pack.ValidationRules.ExpectedSchedules.Count > 0 &&
            pack.ValidationRules.ToleranceChecks.Count > 0 &&
            pack.ValidationRules.IncompatibleCombinations.Count > 0 &&
            pack.ReportingTaxonomy.AssetClass.Count > 0 &&
            pack.ReportingTaxonomy.Liquidity.Count > 0 &&
            pack.ReportingTaxonomy.Geography.Count > 0 &&
            pack.ReportingTaxonomy.Industry.Count > 0 &&
            pack.ReportingTaxonomy.Risk.Count > 0 &&
            pack.ReportingTaxonomy.Tax.Count > 0 &&
            pack.ReportingTaxonomy.CustomClientClassifications.Count > 0 &&
            pack.AdmissionPolicy.AllowsWideCapture &&
            pack.AdmissionPolicy.RequiresJournalTemplateBeforeAutomatedPosting &&
            pack.LedgerExtensionPolicy.Contains("core ledger", StringComparison.OrdinalIgnoreCase));
        builtIns.Where(static pack => pack.PackId != "controlled-other-asset").Should().OnlyContain(static pack =>
            pack.AutomationDepth == AssetPackAutomationDepth.DeepAccountingAutomation &&
            pack.AccountingRules.JournalTemplates.Count > 0);
        builtIns.Single(static pack => pack.PackId == "controlled-other-asset").AutomationDepth.Should().Be(AssetPackAutomationDepth.WideCapture);
    }

    [Fact]
    public void AssetPackRegistry_ShouldDescribeSchemaRulesValidationAndReportingTaxonomy()
    {
        SecurityAssetPackRegistry.All.Should().OnlyContain(static pack =>
            pack.ContractSchema.Terms.Count > 0 &&
            pack.ContractSchema.Counterparties.Count > 0 &&
            pack.ContractSchema.Dates.Count > 0 &&
            pack.ContractSchema.Currencies.Count > 0 &&
            pack.ContractSchema.Ownership.Count > 0 &&
            pack.ContractSchema.Seniority.Count > 0 &&
            pack.ContractSchema.Collateral.Count > 0 &&
            pack.ContractSchema.Rates.Count > 0 &&
            pack.ContractSchema.OptionalAttributes.Count > 0 &&
            pack.LifecycleCoverage.Count == pack.LifecycleEvents.Count &&
            pack.AccountingRules.JournalTemplateEvents.Count > 0 &&
            pack.AccountingRules.JournalTemplates.Count == pack.AccountingRules.JournalTemplateEvents.Count &&
            pack.AccountingRules.AccountingBases.Count > 0 &&
            pack.AccountingRules.Currencies.Count > 0 &&
            pack.AccountingRules.EntityScopes.Count > 0 &&
            pack.ValidationRules.RequiredFields.Count > 0 &&
            pack.ValidationRules.ExpectedSchedules.Count > 0 &&
            pack.ValidationRules.ToleranceChecks.Count > 0 &&
            pack.ValidationRules.IncompatibleCombinations.Count > 0 &&
            pack.ReportingTaxonomy.AssetClass.Count > 0 &&
            pack.ReportingTaxonomy.Liquidity.Count > 0 &&
            pack.ReportingTaxonomy.Geography.Count > 0 &&
            pack.ReportingTaxonomy.Industry.Count > 0 &&
            pack.ReportingTaxonomy.Risk.Count > 0 &&
            pack.ReportingTaxonomy.Tax.Count > 0 &&
            pack.ReportingTaxonomy.CustomClientClassifications.Count > 0 &&
            pack.AdmissionPolicy.AllowsWideCapture &&
            pack.AdmissionPolicy.RequiresJournalTemplateBeforeAutomatedPosting &&
            pack.AdmissionPolicy.AutomationEscalationTriggers.Count > 0 &&
            pack.LedgerExtensionPolicy.Contains("core ledger", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssetPackRegistry_ShouldCoverRequestedLifecycleEventsAndValuationMethods()
    {
        var anyPack = SecurityAssetPackRegistry.All[0];

        anyPack.SupportedLifecycleEvents.Should().Contain(
            "Purchase",
            "Sale",
            "Coupon",
            "Dividend",
            "Draw",
            "Repayment",
            "CapitalCall",
            "Distribution",
            "Appraisal",
            "Impairment",
            "Maturity",
            "Default",
            "Amendment",
            "CorporateAction");
        anyPack.SupportedValuationMethods.Should().Contain(
            "MarketPrice",
            "ManagerReportedNav",
            "Appraisal",
            "DiscountedCashFlow",
            "AmortizedCost",
            "UserEstimate",
            "ExternalModel");
    }

    [Fact]
    public void AssetPackRegistry_ShouldStructureJournalTemplatesByLifecycleBasisCurrencyAndEntity()
    {
        var loanPack = SecurityAssetPackRegistry.Find("private-loan-credit");
        var equityPack = SecurityAssetPackRegistry.Find("public-equity-etf");

        loanPack.Should().NotBeNull();
        equityPack.Should().NotBeNull();
        loanPack!.AccountingRules.JournalTemplates.Should().Contain(template =>
            template.LifecycleEvent == "Draw" &&
            template.TemplateId == "asset-pack.principal-draw" &&
            template.AccountingBases.Contains("GAAP") &&
            template.CurrencyScopes.Contains("transaction currency") &&
            template.EntityScopes.Contains("book"));
        loanPack.AccountingRules.JournalTemplates.Should().Contain(template =>
            template.LifecycleEvent == "Repayment" &&
            template.TemplateId == "asset-pack.principal-repayment");
        equityPack!.AccountingRules.JournalTemplates.Should().Contain(template =>
            template.LifecycleEvent == "CorporateAction" &&
            template.TemplateId == "asset-pack.corporate-action");
        SecurityAssetPackRegistry.All.Should().OnlyContain(pack =>
            pack.AccountingRules.JournalTemplates.All(template =>
                pack.SupportedLifecycleEvents.Contains(template.LifecycleEvent) &&
                template.TemplateId.StartsWith("asset-pack.", StringComparison.OrdinalIgnoreCase) &&
                template.AccountingBases.Count > 0 &&
                template.CurrencyScopes.Count > 0 &&
                template.EntityScopes.Count > 0));
    }

    [Fact]
    public void AssetPackRegistry_ShouldExposeLifecycleAutomationCoverage()
    {
        var loanPack = SecurityAssetPackRegistry.Find("private-loan-credit");
        var otherPack = SecurityAssetPackRegistry.Find("controlled-other-asset");

        loanPack.Should().NotBeNull();
        otherPack.Should().NotBeNull();
        loanPack!.LifecycleCoverage.Should().Contain(coverage =>
            coverage.LifecycleEvent == "Draw" &&
            coverage.AccountingAutomationStatus == "AutomatedTemplateAvailable" &&
            coverage.JournalTemplateIds.Contains("asset-pack.principal-draw") &&
            coverage.CapturePolicy.Contains("source evidence", StringComparison.OrdinalIgnoreCase));
        loanPack.LifecycleCoverage.Should().Contain(coverage =>
            coverage.LifecycleEvent == "Default" &&
            coverage.AccountingAutomationStatus == "AutomatedTemplateAvailable" &&
            coverage.JournalTemplateIds.Contains("asset-pack.default"));
        otherPack!.LifecycleCoverage.Should().Contain(coverage =>
            coverage.LifecycleEvent == "Maturity" &&
            coverage.AccountingAutomationStatus == "ManualReviewRequired" &&
            coverage.JournalTemplateIds.Count == 0);
        SecurityAssetPackRegistry.All.Should().OnlyContain(pack =>
            pack.LifecycleEvents.All(lifecycleEvent =>
                pack.LifecycleCoverage.Any(coverage => coverage.LifecycleEvent == lifecycleEvent)));
    }

    [Fact]
    public void AssetPackRegistry_ShouldExposeWideCaptureNarrowAutomationAdmissionPolicy()
    {
        var privateLoan = SecurityAssetPackRegistry.Find("private-loan-credit");
        var otherPack = SecurityAssetPackRegistry.Find("controlled-other-asset");

        privateLoan.Should().NotBeNull();
        otherPack.Should().NotBeNull();
        SecurityAssetPackRegistry.All.Should().OnlyContain(pack =>
            pack.AdmissionPolicy.AllowsWideCapture &&
            pack.AdmissionPolicy.RequiresJournalTemplateBeforeAutomatedPosting &&
            pack.AdmissionPolicy.EvidencePolicy.Contains("source evidence", StringComparison.OrdinalIgnoreCase) &&
            pack.AdmissionPolicy.CoreLedgerMutationPolicy.Contains("core ledger", StringComparison.OrdinalIgnoreCase));
        privateLoan!.AdmissionPolicy.AccountingPostingPolicy.Should().Contain("idempotency key");
        privateLoan.AdmissionPolicy.AccountingPostingPolicy.Should().Contain("period posture");
        otherPack!.AdmissionPolicy.AccountingPostingPolicy.Should().Contain("accounting automation remains disabled");
        otherPack.AdmissionPolicy.AutomationEscalationTriggers.Should().Contain("approved journal-template coverage");
    }

    [Fact]
    public void AssetPackRegistry_ValidateAll_ShouldAcceptBuiltInPacks()
    {
        var result = SecurityAssetPackRegistry.ValidateAll();

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void AssetPackRegistry_ValidateCandidateSet_ShouldAdmitNewPackWithoutMutatingBuiltIns()
    {
        var candidate = SecurityAssetPackRegistry.CreateCandidateDescriptor(
            "royalty-stream",
            "Royalty streams",
            ["RoyaltyStream"],
            ["Purchase", "Sale", "Distribution", "Appraisal", "Impairment", "Amendment"],
            ["DiscountedCashFlow", "UserEstimate", "ExternalModel"],
            [],
            AssetPackAutomationDepth.WideCapture);

        var result = SecurityAssetPackRegistry.ValidateCandidateSet([candidate]);

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        candidate.ContractSchema.Terms.Should().NotBeEmpty();
        candidate.LifecycleCoverage.Should().OnlyContain(static coverage =>
            coverage.AccountingAutomationStatus == "ManualReviewRequired" &&
            coverage.JournalTemplateIds.Count == 0);
        candidate.AdmissionPolicy.AccountingPostingPolicy.Should().Contain("accounting automation remains disabled");
        SecurityAssetPackRegistry.Find("royalty-stream").Should().BeNull();
    }

    [Fact]
    public void AssetPackRegistry_ValidateCandidateSet_ShouldRejectDuplicateCandidateClaims()
    {
        var baseline = SecurityAssetPackRegistry.Find("controlled-other-asset");
        baseline.Should().NotBeNull();
        var duplicate = baseline! with
        {
            PackId = "custom-direct-loan",
            DisplayName = "Custom direct loan",
            AssetClasses = ["DirectLoan"]
        };

        var result = SecurityAssetPackRegistry.ValidateCandidateSet([duplicate]);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.asset-class-overlap" &&
            issue.Target == "AssetClasses");
    }

    [Fact]
    public void AssetPackRegistry_ValidateDescriptor_ShouldFailClosedForInvalidCandidatePack()
    {
        var baseline = SecurityAssetPackRegistry.Find("private-loan-credit");
        baseline.Should().NotBeNull();

        var invalid = baseline! with
        {
            LifecycleEvents = ["UnsupportedLifecycle"],
            LifecycleCoverage = [],
            ValuationMethods = ["UnsupportedValuation"],
            AccountingRules = baseline.AccountingRules with { JournalTemplates = [] },
            AdmissionPolicy = baseline.AdmissionPolicy with
            {
                AllowsWideCapture = false,
                RequiresJournalTemplateBeforeAutomatedPosting = false,
                CoreLedgerMutationPolicy = "Add an asset-specific posting table."
            },
            LedgerExtensionPolicy = "Add a new ledger table for this pack."
        };

        var result = SecurityAssetPackRegistry.ValidateDescriptor(invalid);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.unsupported-lifecycle-event" &&
            issue.Target == "LifecycleEvents");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.lifecycle-coverage-required" &&
            issue.Target == "LifecycleCoverage");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.unsupported-valuation-method" &&
            issue.Target == "ValuationMethods");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.deep-automation-template-required" &&
            issue.Target == "AccountingRules.JournalTemplates");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.wide-capture-policy-required" &&
            issue.Target == "AdmissionPolicy.AllowsWideCapture");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.template-before-posting-policy-required" &&
            issue.Target == "AdmissionPolicy.RequiresJournalTemplateBeforeAutomatedPosting");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.core-ledger-mutation-policy-reference-required" &&
            issue.Target == "AdmissionPolicy.CoreLedgerMutationPolicy");
        result.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.core-ledger-policy-required" &&
            issue.Target == "LedgerExtensionPolicy");
    }

    [Fact]
    public void AssetPackRegistry_ShouldSurfaceAdvertisedAssetClassesWithoutCatalogBacking()
    {
        var gap = SecurityAssetPackRegistry.AssetClassesWithoutCatalogBacking();
        var modeled = new HashSet<string>(SecurityAssetClassCatalog.AssetClasses, StringComparer.OrdinalIgnoreCase);

        // Referential-integrity check: packs advertise coverage the type system does not model.
        // ExchangeTradedFund is a concrete example — a pack claims it (see
        // AssetPackRegistry_FindByAssetClass_...) while the catalog only models Equity — so the
        // registry's advertised coverage must not be read as backed create/classification support.
        gap.Should().NotBeEmpty();
        gap.Should().Contain("ExchangeTradedFund");

        // The diagnostic must never flag an asset class the catalog already models.
        gap.Should().OnlyContain(assetClass => !modeled.Contains(assetClass));
        gap.Should().NotContain("Equity");
        gap.Should().NotContain("Bond");
    }

    [Fact]
    public void AssetPackRegistry_ShouldReportNoGap_WhenModeledSetCoversEveryAdvertisedClass()
    {
        // Feeding the full advertised set in as "modeled" yields an empty gap, proving the diagnostic
        // is a pure function of the (advertised, modeled) relationship rather than a hardcoded list.
        var advertised = SecurityAssetPackRegistry.All
            .SelectMany(static pack => pack.AssetClasses)
            .Distinct()
            .ToArray();

        SecurityAssetPackRegistry.AssetClassesWithoutCatalogBacking(advertised).Should().BeEmpty();
    }

    [Fact]
    public void AssetPackRegistry_FindByAssetClass_ShouldMapAssetClassToPackWithoutLedgerChanges()
    {
        var loanPacks = SecurityAssetPackRegistry.FindByAssetClass("DirectLoan");
        var etfPacks = SecurityAssetPackRegistry.FindByAssetClass("ExchangeTradedFund");
        var structuredCreditPacks = SecurityAssetPackRegistry.FindByAssetClass("StructuredCredit");
        var commitmentPacks = SecurityAssetPackRegistry.FindByAssetClass("CommitmentGuarantee");

        loanPacks.Should().Contain(static pack => pack.PackId == "private-loan-credit");
        etfPacks.Should().ContainSingle(static pack => pack.PackId == "public-equity-etf");
        etfPacks[0].LedgerExtensionPolicy.Should().Contain("journal templates");
        structuredCreditPacks.Should().ContainSingle(static pack => pack.PackId == "fixed-income");
        commitmentPacks.Should().ContainSingle(static pack => pack.PackId == "commitment-guarantee");
    }
}
