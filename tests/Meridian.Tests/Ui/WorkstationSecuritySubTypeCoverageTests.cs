using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Pins the workstation sub-type derivation to the asset-class registry (remediation slice 8).
/// The expected table below names every catalog class, so adding a class without deciding its
/// sub-type fails here (omission-failing). Three legacy workstation spellings — "Bond",
/// "Deposit", "MoneyMarket" — are pinned as the declared generic labels reconciliation lines
/// already carry, Equity is pinned as declared sub-type-ambiguous (null), and every other class
/// reads the registry's declared sub-type label.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WorkstationSecuritySubTypeCoverageTests
{
    private static readonly IReadOnlyDictionary<string, string?> ExpectedSubTypes =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Declared sub-type-ambiguous: an Equity record may be common, preferred, an ADR or
            // an MLP unit, so the class derives nothing rather than guessing the default.
            ["Equity"] = null,
            ["Option"] = "OptionContract",
            ["Future"] = "FutureContract",
            ["Bond"] = "Bond",
            ["FxSpot"] = "FxSpot",
            ["Deposit"] = "Deposit",
            ["MoneyMarketFund"] = "MoneyMarket",
            ["CertificateOfDeposit"] = "CertificateOfDeposit",
            ["CommercialPaper"] = "CommercialPaper",
            ["TreasuryBill"] = "TreasuryBill",
            ["Repo"] = "Repo",
            ["CashSweep"] = "CashSweep",
            ["OtherSecurity"] = "OtherSecurity",
            ["Swap"] = "SwapContract",
            ["DirectLoan"] = "DirectLoan",
            ["StructuredCredit"] = "StructuredCredit",
            ["PrivateFundInterest"] = "LimitedPartnershipInterest",
            ["PrivateCompanyEquity"] = "PrivateCompanyEquity",
            ["RealEstateHolding"] = "RealEstateHolding",
            ["CommitmentGuarantee"] = "CommitmentGuarantee",
            ["Commodity"] = "Commodity",
            ["CryptoCurrency"] = "CryptoCurrency",
            ["Cfd"] = "Cfd",
            ["Warrant"] = "Warrant",
            ["FxForward"] = "FxForward",
            ["NonDeliverableForward"] = "NonDeliverableForward",
            ["CreditDefaultSwap"] = "CreditDefaultSwap",
            ["Swaption"] = "Swaption",
            ["CapFloor"] = "CapFloor",
            ["StructuredNote"] = "StructuredNote",
            ["InvestmentFund"] = "InvestmentFund",
            ["CustomAsset"] = "CustomAsset",
        };

    [Fact]
    public void Expected_table_names_every_registry_class_and_nothing_else()
    {
        var registryClasses = SecurityAssetClassRegistry.All.Select(static definition => definition.AssetClass).ToHashSet(StringComparer.Ordinal);

        ExpectedSubTypes.Keys.ToHashSet(StringComparer.Ordinal).Should().BeEquivalentTo(
            registryClasses,
            "every registry asset class must declare a workstation sub-type here, and every pinned class must still exist");
    }

    [Fact]
    public void Every_registry_class_derives_its_pinned_sub_type()
    {
        foreach (var (assetClass, expected) in ExpectedSubTypes)
        {
            SecurityMasterSecurityReferenceLookup.DeriveSubType(assetClass)
                .Should().Be(expected, $"asset class {assetClass} derives its sub-type from the registry or the pinned legacy spelling");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotACatalogClass")]
    [InlineData("bond")] // the lookup keys on canonical spelling, as the hand-written switch did
    public void Undeclared_names_stay_null(string? assetClass)
        => SecurityMasterSecurityReferenceLookup.DeriveSubType(assetClass).Should().BeNull();
}
