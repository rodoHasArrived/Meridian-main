using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.Ui.Shared.Services;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Holds the per-class surfaces that used to keep their own asset-class lists to the facts now
/// declared once on <see cref="SecurityAssetClassDescriptor"/>: asset-pack membership, canonical
/// bullet amortization, governed-profile requirement, the operational-readiness rows and depth
/// targets, and the workstation sub-type derivation against the F# classification table.
/// <para>
/// Where a surface is derived from the descriptor, the pre-move table is pinned so the move is
/// provably behavior-preserving. Where a surface is content rather than a derivation (readiness
/// prose), the guard is that it names only catalog classes and that its membership is deliberate.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassSurfaceParityTests
{
    /// <summary>
    /// Asset-pack membership as the pack registry declared it before membership moved onto the
    /// descriptor. Order is no longer significant (packs now list classes in catalog order).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedPackMembership =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["cash-bank"] = ["Deposit", "CashSweep", "MoneyMarketFund"],
            ["public-equity-etf"] = ["Equity", "InvestmentFund"],
            ["fixed-income"] = ["Bond", "StructuredCredit", "TreasuryBill", "CommercialPaper", "CertificateOfDeposit", "Repo"],
            ["private-fund-partnership"] = ["PrivateFundInterest", "PrivateCompanyEquity"],
            ["private-loan-credit"] = ["DirectLoan"],
            ["real-estate"] = ["RealEstateHolding"],
            ["derivatives-fx"] = ["Option", "Future", "Swap", "FxSpot", "Cfd", "Warrant", "FxForward", "NonDeliverableForward", "CreditDefaultSwap"],
            ["mortgage-facility-intercompany"] = ["DirectLoan"],
            ["commitment-guarantee"] = ["CommitmentGuarantee"],
            ["controlled-other-asset"] = ["OtherSecurity", "CustomAsset", "Commodity", "CryptoCurrency"],
        };

    [Fact]
    public void AssetPackRegistry_DerivesExactlyThePreMoveMembership()
    {
        SecurityAssetPackRegistry.All.Select(static pack => pack.PackId)
            .Should().BeEquivalentTo(ExpectedPackMembership.Keys);

        foreach (var (packId, expected) in ExpectedPackMembership)
        {
            SecurityAssetPackRegistry.Find(packId)!.AssetClasses
                .Should().BeEquivalentTo(expected, $"pack '{packId}' must keep the classes it claimed before membership moved onto the descriptor");
        }
    }

    [Fact]
    public void EveryDescriptorPackId_NamesARegisteredPack_AndEveryClassHasAPack()
    {
        foreach (var descriptor in SecurityAssetClassCatalog.All)
        {
            descriptor.AssetPackIds.Should().NotBeNullOrEmpty($"'{descriptor.AssetClass}' must belong to at least one asset pack");
            foreach (var packId in descriptor.AssetPackIds!)
            {
                SecurityAssetPackRegistry.Find(packId).Should().NotBeNull($"'{descriptor.AssetClass}' names pack '{packId}', which the registry does not define");
            }
        }
    }

    [Fact]
    public void CanonicalBulletAmortization_CoversExactlyTheBulletClasses_AndStaysWithinParAmortizers()
    {
        var bullet = SecurityAssetClassCatalog.All
            .Where(static descriptor => descriptor.SupportsCanonicalBulletAmortization)
            .Select(static descriptor => descriptor.AssetClass)
            .ToArray();

        bullet.Should().BeEquivalentTo("Bond", "TreasuryBill", "CommercialPaper");
        bullet.Should().OnlyContain(
            assetClass => SecurityAssetClassCatalog.GetOrDefault(assetClass).AmortizesTowardPar,
            "the canonical bullet kernel is a special case of amortizing toward par");
    }

    [Fact]
    public void RequiresGovernedProfile_CoversExactlyTheClassesWithNoTermsContract_AndStaysWithinProfileBacked()
    {
        var governed = SecurityAssetClassCatalog.All
            .Where(static descriptor => descriptor.RequiresGovernedProfile)
            .Select(static descriptor => descriptor.AssetClass)
            .ToArray();

        governed.Should().BeEquivalentTo("CustomAsset", "OtherSecurity");
        governed.Should().OnlyContain(
            assetClass => SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms,
            "a class that must carry a profile must at least be able to");
    }

    [Fact]
    public void ReadinessRows_NameOnlyCatalogClasses_Once_AndCoverTheDeliberateSet()
    {
        var rows = SecurityMasterOperationalReadinessService.Specifications
            .Select(static specification => specification.AssetClass)
            .ToArray();

        rows.Should().OnlyHaveUniqueItems();
        rows.Should().BeSubsetOf(SecurityAssetClassCatalog.AssetClasses);
        // The readiness surface lists the classes that carry operational prose today. Adding a
        // catalog class does not add a row automatically; changing this set is a reviewed decision.
        rows.Should().BeEquivalentTo(
            "Equity", "Option", "Future", "FxSpot", "Bond", "DirectLoan", "StructuredCredit",
            "PrivateFundInterest", "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee",
            "CustomAsset", "OtherSecurity");
    }

    [Fact]
    public void ReadinessDepthTargets_AreKeyedByListedCatalogClasses()
    {
        var listed = SecurityMasterOperationalReadinessService.Specifications
            .Select(static specification => specification.AssetClass)
            .ToHashSet(StringComparer.Ordinal);

        SecurityMasterOperationalReadinessService.AssetClassDepthTargetSpecs.Keys
            .Should().OnlyContain(
                assetClass => SecurityAssetClassCatalog.AssetClasses.Contains(assetClass) && listed.Contains(assetClass),
                "a depth target for a class with no readiness row can never be reached");
    }

    /// <summary>
    /// Sub-type strings the workstation derives that do NOT match the F# classification table's
    /// default sub-type for the class. Each is a pre-existing divergence, pinned here so it is a
    /// visible decision: fix the derivation (and remove the entry) or keep the workstation label.
    /// The set may only shrink.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Workstation, string FSharp)> KnownSubTypeDivergences =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["Bond"] = ("Bond", "CorporateBond"),
            ["Deposit"] = ("Deposit", "TimeDeposit"),
            ["MoneyMarketFund"] = ("MoneyMarket", "MoneyMarketFund"),
        };

    [Fact]
    public void WorkstationSubType_MatchesTheFSharpDefaultSubType_ExceptPinnedDivergences()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var derived = SecurityMasterSecurityReferenceLookup.DeriveSubType(assetClass);
            if (derived is null)
            {
                // Documented: classes with more than one plausible sub-type derive nothing.
                KnownSubTypeDivergences.Should().NotContainKey(assetClass, "a class that derives no sub-type cannot also be a pinned divergence");
                continue;
            }

            var option = AssetClassRegistry.tryDescriptorByName(assetClass);
            OptionModule.IsSome(option).Should().BeTrue($"the F# registry must know '{assetClass}'");
            var fsharpSubType = SubTypeName(option.Value.SubType);

            if (KnownSubTypeDivergences.TryGetValue(assetClass, out var divergence))
            {
                derived.Should().Be(divergence.Workstation, $"'{assetClass}' divergence changed on the workstation side; update or remove the pin");
                fsharpSubType.Should().Be(divergence.FSharp, $"'{assetClass}' divergence changed on the F# side; update or remove the pin");
                derived.Should().NotBe(fsharpSubType, $"'{assetClass}' no longer diverges; remove it from KnownSubTypeDivergences");
            }
            else
            {
                derived.Should().Be(fsharpSubType, $"'{assetClass}' sub-type must match the F# classification table");
            }
        }
    }

    [Fact]
    public void WorkstationSubType_IsDerivedFromOneTable()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            Meridian.Ui.Shared.Endpoints.WorkstationEndpoints.DeriveSubType(assetClass)
                .Should().Be(SecurityMasterSecurityReferenceLookup.DeriveSubType(assetClass));
        }
    }

    private static string SubTypeName(SecuritySubType subType)
    {
        var (unionCase, fields) = FSharpValue.GetUnionFields(subType, typeof(SecuritySubType), null);
        return unionCase.Name == "OtherSubType" ? (string)fields[0] : unionCase.Name;
    }
}
