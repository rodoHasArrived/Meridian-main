using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Tests.ReferenceData.SecurityMaster;

public sealed class SecurityKindMappingTests
{
    [Fact]
    public void TryNormalizeAssetClass_ReturnsCanonicalAssetClass_WhenInputMatchesIgnoringCase()
    {
        var normalized = SecurityKindMapping.TryNormalizeAssetClass("cryptocurrency", out var canonicalAssetClass);

        normalized.Should().BeTrue();
        canonicalAssetClass.Should().Be("CryptoCurrency");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("UnknownAssetClass")]
    public void TryNormalizeAssetClass_ReturnsFalse_ForUnknownOrMissingAssetClass(string assetClass)
    {
        var normalized = SecurityKindMapping.TryNormalizeAssetClass(assetClass, out var canonicalAssetClass);

        normalized.Should().BeFalse();
        canonicalAssetClass.Should().BeEmpty();
    }

    [Fact]
    public void InstrumentTypeDescriptorCatalog_ReturnsOperationalMetadata_ForIndexOptions()
    {
        var descriptor = InstrumentTypeDescriptorCatalog.Find(InstrumentType.IndexOption);

        descriptor.Should().NotBeNull();
        descriptor!.SecurityMasterAssetClass.Should().Be("Option");
        descriptor.DefaultProviderSecurityType.Should().Be("OPT");
        descriptor.IsDerivative.Should().BeTrue();
        descriptor.RequiresUnderlying.Should().BeTrue();
        descriptor.LifecycleEvents.Should().Contain("CashSettlement");
        descriptor.LedgerBehaviorHints.Should().Contain(hint =>
            hint.Contains("cash-settled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetCompatibilityProfile_ForBond_ReturnsLedgerAndCompatibleSecurityMasterHints()
    {
        var profile = SecurityKindMapping.GetCompatibilityProfile(InstrumentType.Bond);

        profile.Should().NotBeNull();
        profile!.AssetClass.Should().Be("Bond");
        profile.PrimaryInstrumentType.Should().Be(InstrumentType.Bond);
        profile.CompatibleSecurityMasterAssetClasses.Should().Contain(
            [
                "Bond",
                "TreasuryBill",
                "StructuredCredit"
            ]);
        profile.RequiredEconomicTerms.Should().Contain("maturity");
        profile.LedgerBehaviorHints.Should().Contain(hint =>
            hint.Contains("interest accrual", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetCompatibilityProfile_ForStructuredCredit_PreservesDirectMappingGapWithCompatibleDescriptorEvidence()
    {
        var profile = SecurityKindMapping.GetCompatibilityProfile("structuredcredit");

        profile.Should().NotBeNull();
        profile!.AssetClass.Should().Be("StructuredCredit");
        profile.InstrumentTypes.Should().BeEmpty();
        profile.PrimaryInstrumentType.Should().BeNull();
        profile.IsAmbiguous.Should().BeTrue();
        profile.CompatibleSecurityMasterAssetClasses.Should().Contain("StructuredCredit");
        profile.Summary.Should().Contain("No direct market-data InstrumentType mapping");
        SecurityKindMapping.ToInstrumentTypes("StructuredCredit").Should().BeEmpty();
        SecurityKindMapping.ToInstrumentTypeDescriptors("StructuredCredit")
            .Should().Contain(descriptor => descriptor.InstrumentType == InstrumentType.Bond);
    }

    [Fact]
    public void ToInstrumentTypeDescriptors_ForOptions_ReturnsAllCompatibleOptionTypes()
    {
        var descriptors = SecurityKindMapping.ToInstrumentTypeDescriptors("Option");

        descriptors.Select(descriptor => descriptor.InstrumentType).Should().BeEquivalentTo(
            [
                InstrumentType.EquityOption,
                InstrumentType.IndexOption,
                InstrumentType.FuturesOption
            ]);
        SecurityKindMapping.GetCompatibilityProfile("Option")!.IsAmbiguous.Should().BeTrue();
    }
}
