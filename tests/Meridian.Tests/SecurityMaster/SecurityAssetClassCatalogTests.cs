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
}
