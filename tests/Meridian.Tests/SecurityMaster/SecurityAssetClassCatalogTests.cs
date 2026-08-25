using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityAssetClassCatalogTests
{
    [Theory]
    [InlineData("MortgageBacked")]
    [InlineData("MortgageBackedSecurity")]
    [InlineData("MBS")]
    [InlineData("AssetBacked")]
    [InlineData("AssetBackedSecurity")]
    [InlineData("ABS")]
    [InlineData(" mbs ")]
    public void GetOrDefault_ResolvesStructuredCreditVendorAliases(string alias)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(alias);

        descriptor.AssetClass.Should().Be("StructuredCredit");
        descriptor.AmortizesTowardPar.Should().BeTrue();
    }

    [Theory]
    [InlineData("Loan")]
    [InlineData("AmortizingLoan")]
    public void GetOrDefault_ResolvesDirectLoanAliases(string alias)
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(alias);

        descriptor.AssetClass.Should().Be("DirectLoan");
        descriptor.AmortizesTowardPar.Should().BeTrue();
    }

    [Fact]
    public void GetOrDefault_UnknownClass_FallsBackToNonThrowingDefault()
    {
        var descriptor = SecurityAssetClassCatalog.GetOrDefault("EsotericBasketCertificate");

        descriptor.AssetClass.Should().Be("Unknown");
        descriptor.AmortizesTowardPar.Should().BeFalse();
        descriptor.RequiresMaturity.Should().BeFalse();
        descriptor.SupportsProfileBackedTerms.Should().BeFalse();
    }

    [Fact]
    public void AmortizesTowardPar_CoversExactlyTheParPricedClasses()
    {
        // Deliberately narrower than UsesFaceValueLots: Deposit and Repo hold face-value lots but
        // are booked at par, so nothing amortizes toward par for them.
        SecurityAssetClassCatalog.All
            .Where(descriptor => descriptor.AmortizesTowardPar)
            .Select(descriptor => descriptor.AssetClass)
            .Should().BeEquivalentTo(
                "Bond", "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "DirectLoan", "StructuredCredit");
    }

    [Fact]
    public void RequiresMaturity_CoversExactlyTheMaturityValidatedClasses()
    {
        SecurityAssetClassCatalog.All
            .Where(descriptor => descriptor.RequiresMaturity)
            .Select(descriptor => descriptor.AssetClass)
            .Should().BeEquivalentTo(
                "Bond", "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "Swap");
    }

    [Fact]
    public void SupportsProfileBackedTerms_CoversExactlyTheProfileBackedClasses()
    {
        SecurityAssetClassCatalog.All
            .Where(descriptor => descriptor.SupportsProfileBackedTerms)
            .Select(descriptor => descriptor.AssetClass)
            .Should().BeEquivalentTo(
                "OtherSecurity", "CustomAsset", "StructuredCredit", "PrivateFundInterest",
                "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee");
    }

    [Fact]
    public void AliasesAndCanonicalNames_AreGloballyDistinct()
    {
        // An alias that shadows a canonical name (or another alias) would silently redirect
        // dispatch for that spelling; the lookup builder throws, this test names the offender.
        var allNames = SecurityAssetClassCatalog.All.Select(descriptor => descriptor.AssetClass)
            .Concat(SecurityAssetClassCatalog.All.SelectMany(descriptor => descriptor.Aliases ?? []))
            .Select(name => name.ToUpperInvariant())
            .ToList();

        allNames.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Catalog_StaysInLockstepWithTheFSharpAssetClassRegistry()
    {
        // Every catalog class must exist in the kind-keyed F# registry and vice versa — this is
        // the guard that keeps the two canonical tables from drifting apart. CustomAsset gained a
        // first-class SecurityKind (profile envelope + verbatim document), so the historical
        // C#-side-only exclusion no longer applies.
        SecurityAssetClassCatalog.AssetClasses.Should().BeEquivalentTo(AssetClassRegistry.assetClasses);
    }

    [Fact]
    public void AssetClasses_ShouldExposeCanonicalSecurityMasterCoverage()
    {
        // Pass an explicit collection: the params overload of Contain binds its second argument to
        // `because`, which would silently assert only the first asset class.
        SecurityAssetClassCatalog.AssetClasses.Should().Contain(new[]
        {
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
            "InvestmentFund",
            "OtherSecurity"
        });
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
    public void AssetPackRegistry_ValidatesCleanly_AndCoversEveryCatalogAssetClass()
    {
        // The registry is normative metadata the operational-readiness service consumes, not
        // documentation shaped like code: its own validation rules must pass for every declared
        // pack, and EVERY canonical Security Master asset class must be claimed by at least one
        // pack — an unclaimed class silently drops out of asset-pack coverage routing. Packs may
        // additionally claim broader business vocabulary (e.g. "Cash", "Mortgage") that the
        // Security Master catalog does not model as classes.
        var validation = SecurityAssetPackRegistry.ValidateAll();
        validation.IsValid.Should().BeTrue(string.Join(
            "; ", validation.Issues.Select(static issue => $"[{issue.Code}] {issue.Target}: {issue.Message}")));

        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            SecurityAssetPackRegistry.FindByAssetClass(assetClass).Should().NotBeEmpty(
                $"canonical asset class '{assetClass}' must be claimed by at least one asset pack");
        }
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
            "PrivateCompanyEquity",
            "__VACUITY_PROBE__");
        SecurityAssetPackRegistry.Find("real-estate")!.AssetClasses.Should().Contain("RealEstateHolding");
        SecurityAssetPackRegistry.Find("commitment-guarantee")!.AssetClasses.Should().Contain("CommitmentGuarantee");

        SecurityAssetPackRegistry.Find("controlled-other-asset")!.AssetClasses.Should().Contain(
            "OtherSecurity",
            "CustomAsset",
            "__VACUITY_PROBE__");
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
            "CorporateAction",
            "__VACUITY_PROBE__");
        anyPack.SupportedValuationMethods.Should().Contain(
            "MarketPrice",
            "ManagerReportedNav",
            "Appraisal",
            "DiscountedCashFlow",
            "AmortizedCost",
            "UserEstimate",
            "ExternalModel",
            "__VACUITY_PROBE__");
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
