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
            pack.AccountingRules.JournalTemplateEvents.Count > 0 &&
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
    public void AssetPackRegistry_FindByAssetClass_ShouldMapAssetClassToPackWithoutLedgerChanges()
    {
        var loanPacks = SecurityAssetPackRegistry.FindByAssetClass("DirectLoan");
        var etfPacks = SecurityAssetPackRegistry.FindByAssetClass("ExchangeTradedFund");

        loanPacks.Should().Contain(static pack => pack.PackId == "private-loan-credit");
        etfPacks.Should().ContainSingle(static pack => pack.PackId == "public-equity-etf");
        etfPacks[0].LedgerExtensionPolicy.Should().Contain("journal templates", StringComparison.OrdinalIgnoreCase);
    }
}
