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

    /// <summary>
    /// The asset-class to market-data instrument-type table as it stood when the mapping moved onto
    /// <see cref="SecurityAssetClassDescriptor.InstrumentTypes"/>. Pinned so the move is provably
    /// behavior-preserving; changing a mapping now means changing this table deliberately.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, InstrumentType[]> ExpectedInstrumentTypesByAssetClass =
        new Dictionary<string, InstrumentType[]>(StringComparer.Ordinal)
        {
            ["Equity"] = [InstrumentType.Equity],
            ["Option"] = [InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.FuturesOption],
            ["Future"] = [InstrumentType.Future, InstrumentType.SingleStockFuture],
            ["Bond"] = [InstrumentType.Bond],
            ["FxSpot"] = [InstrumentType.Forex],
            ["Deposit"] = [InstrumentType.Deposit],
            ["MoneyMarketFund"] = [InstrumentType.Equity],
            ["CertificateOfDeposit"] = [InstrumentType.Deposit],
            ["CommercialPaper"] = [InstrumentType.Bond],
            ["TreasuryBill"] = [InstrumentType.Bond],
            ["Repo"] = [InstrumentType.Repo],
            ["CashSweep"] = [InstrumentType.Deposit],
            ["OtherSecurity"] = [],
            ["CustomAsset"] = [],
            ["Swap"] = [InstrumentType.Swap],
            ["DirectLoan"] = [InstrumentType.DirectLoan],
            ["StructuredCredit"] = [],
            ["PrivateFundInterest"] = [],
            ["PrivateCompanyEquity"] = [],
            ["RealEstateHolding"] = [],
            ["CommitmentGuarantee"] = [],
            ["Commodity"] = [InstrumentType.Commodity],
            ["CryptoCurrency"] = [InstrumentType.Crypto],
            ["Cfd"] = [InstrumentType.CFD],
            ["Warrant"] = [InstrumentType.Warrant],
            ["FxForward"] = [],
            ["NonDeliverableForward"] = [],
            ["CreditDefaultSwap"] = [],
            ["Swaption"] = [],
            ["CapFloor"] = [],
            ["StructuredNote"] = [],
            ["InvestmentFund"] = [],
        };

    [Fact]
    public void ExpectedInstrumentTypeTable_CoversExactlyTheCatalogAssetClasses()
    {
        ExpectedInstrumentTypesByAssetClass.Keys
            .Should().BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses);
    }

    [Fact]
    public void ToInstrumentTypes_MatchesPinnedTable_ForEveryAssetClass()
    {
        foreach (var (assetClass, expected) in ExpectedInstrumentTypesByAssetClass)
        {
            SecurityKindMapping.ToInstrumentTypes(assetClass)
                .Should().Equal(expected, $"'{assetClass}' must keep its pre-catalog instrument-type mapping");
        }
    }

    [Fact]
    public void ToInstrumentTypes_IsCaseInsensitive_AndEmptyForUnknownAssetClass()
    {
        SecurityKindMapping.ToInstrumentTypes("equity").Should().Equal(InstrumentType.Equity);
        SecurityKindMapping.ToInstrumentTypes("UnknownAssetClass").Should().BeEmpty();
    }

    /// <summary>
    /// InstrumentType values deliberately left without a reverse asset-class mapping, each with its
    /// reason. Empty today: every market-data instrument type names one Security Master class. A new
    /// enum value must either be mapped in <see cref="SecurityKindMapping"/> or be listed here with
    /// the ambiguity or outside-slice reason that justifies leaving it unmapped.
    /// </summary>
    private static readonly IReadOnlyDictionary<InstrumentType, string> UnmappedInstrumentTypes =
        new Dictionary<InstrumentType, string>();

    /// <summary>
    /// Mapped InstrumentType values whose asset class does not list them back, each with its reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<InstrumentType, string> OneWayInstrumentTypes =
        new Dictionary<InstrumentType, string>
        {
            [InstrumentType.Index] =
                "An index is not a holdable security: it routes to OtherSecurity, but OtherSecurity declares no market-data instrument type.",
        };

    [Fact]
    public void ToAssetClass_MapsEveryInstrumentType_OrListsItAsDeliberatelyUnmapped()
    {
        UnmappedInstrumentTypes.Keys.Should().NotIntersectWith(OneWayInstrumentTypes.Keys,
            "a type is either unmapped or mapped one-way, never both");

        foreach (var instrumentType in Enum.GetValues<InstrumentType>())
        {
            var assetClass = SecurityKindMapping.ToAssetClass(instrumentType);
            if (UnmappedInstrumentTypes.ContainsKey(instrumentType))
            {
                assetClass.Should().BeNull($"{instrumentType} is listed as deliberately unmapped");
                continue;
            }

            assetClass.Should().NotBeNull(
                $"{instrumentType} has no reverse mapping; map it in SecurityKindMapping or list it with a reason in {nameof(UnmappedInstrumentTypes)}");
            SecurityAssetClassCatalog.AssetClasses.Should().Contain(assetClass!,
                $"{instrumentType} must map to a canonical catalog class");
        }
    }

    [Fact]
    public void ToAssetClass_RoundTripsThroughToInstrumentTypes_ExceptDocumentedOneWayMappings()
    {
        foreach (var instrumentType in Enum.GetValues<InstrumentType>().Where(type => !UnmappedInstrumentTypes.ContainsKey(type)))
        {
            var assetClass = SecurityKindMapping.ToAssetClass(instrumentType);
            assetClass.Should().NotBeNull();
            var listedBack = SecurityKindMapping.ToInstrumentTypes(assetClass!).Contains(instrumentType);

            listedBack.Should().Be(!OneWayInstrumentTypes.ContainsKey(instrumentType),
                OneWayInstrumentTypes.ContainsKey(instrumentType)
                    ? $"{instrumentType} is documented as one-way; if '{assetClass}' now lists it back, remove the exception"
                    : $"{instrumentType} maps to '{assetClass}', which must list it back");
        }
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
