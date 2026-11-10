using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The readiness vocabulary (<c>ReadinessAliases</c>) and the readiness projector switch
/// (<see cref="SecurityAssetPackRegistry.ReadinessProjectorsFor"/>) are hand-maintained beside the
/// catalog. These guards fail when either drifts: an alias that names a non-canonical class or is
/// shadowed by a catalog label, or a catalog class that silently falls through to the projector
/// fallback without an explicit, reasoned exception.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetPackReadinessMappingTests
{
    /// <summary>
    /// The readiness-only legacy vocabulary as it stands. Pinned so adding, removing or retargeting
    /// an alias is a deliberate change here: these broad labels classify readiness requests and
    /// evidence only, never securities.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExpectedReadinessAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FixedIncome"] = "Bond",
            ["FixedIncomeSecurity"] = "Bond",
            ["Debt"] = "Bond",
            ["PrivateCredit"] = "DirectLoan",
            ["CreditFacility"] = "DirectLoan",
            ["StructuredProduct"] = "StructuredCredit",
            ["PrivateAsset"] = "CustomAsset",
            ["PrivateEquity"] = "PrivateCompanyEquity",
            ["PrivateCompany"] = "PrivateCompanyEquity",
            ["VentureCapital"] = "PrivateCompanyEquity",
            ["RealEstate"] = "RealEstateHolding",
            ["RealEstateInterest"] = "RealEstateHolding",
            ["Property"] = "RealEstateHolding",
            ["SPV"] = "RealEstateHolding",
            ["UnfundedCommitment"] = "CommitmentGuarantee",
            ["Guarantee"] = "CommitmentGuarantee",
            ["Commitment"] = "CommitmentGuarantee",
            ["Other"] = "OtherSecurity",
            ["FX"] = "FxSpot",
            ["ForeignExchange"] = "FxSpot",
            ["Currency"] = "FxSpot"
        };

    [Fact]
    public void ReadinessAliases_MatchThePinnedVocabulary()
    {
        SecurityAssetPackRegistry.ReadinessAliases
            .Should().BeEquivalentTo(ExpectedReadinessAliases,
                "a readiness alias is a deliberate classification decision; update the pinned table with it");
    }

    [Fact]
    public void EveryReadinessAlias_TargetsACanonicalClass_AndIsReachable()
    {
        var catalogLabels = SecurityAssetClassCatalog.All
            .SelectMany(static descriptor => new[] { descriptor.AssetClass }.Concat(descriptor.Aliases ?? []))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var scope = new AssertionScope();
        foreach (var (alias, assetClass) in SecurityAssetPackRegistry.ReadinessAliases)
        {
            SecurityAssetClassCatalog.AssetClasses.Should().Contain(assetClass,
                $"readiness alias '{alias}' must name a canonical catalog class");
            catalogLabels.Should().NotContain(alias,
                $"readiness alias '{alias}' would be shadowed by a catalog name or alias and never consulted");
            SecurityAssetPackRegistry.ResolveAssetClass(alias).Should().Be(assetClass,
                $"readiness alias '{alias}' must resolve to its declared class");
        }
    }

    private const string ProjectorFallback = "Asset-specific ledger mapping requires operational profile review";

    /// <summary>
    /// Catalog classes that deliberately take the projector fallback, with the reason. None has an
    /// accepted ledger/NAV projector named for readiness: consumer acceptance for these classes is
    /// outside this mapping (review D4), so naming one here would assert a capability that has not
    /// been proven end to end.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ProjectorFallbackClasses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Deposit"] = "Cash-equivalent; no readiness projector accepted.",
            ["MoneyMarketFund"] = "Cash-equivalent fund units; no readiness projector accepted.",
            ["CertificateOfDeposit"] = "Cash-equivalent; no readiness projector accepted.",
            ["CommercialPaper"] = "Money-market discount instrument; no readiness projector accepted.",
            ["TreasuryBill"] = "Money-market discount instrument; no readiness projector accepted.",
            ["Repo"] = "Financing; no readiness projector accepted.",
            ["CashSweep"] = "Cash program; no readiness projector accepted.",
            ["Swap"] = "OTC derivative; leg accounting consumer not accepted.",
            ["Commodity"] = "No readiness projector accepted.",
            ["CryptoCurrency"] = "No readiness projector accepted.",
            ["Cfd"] = "Derivative; no readiness projector accepted.",
            ["Warrant"] = "Derivative; no readiness projector accepted.",
            ["FxForward"] = "OTC derivative; no readiness projector accepted.",
            ["NonDeliverableForward"] = "OTC derivative; no readiness projector accepted.",
            ["CreditDefaultSwap"] = "OTC derivative; no readiness projector accepted.",
            ["Swaption"] = "OTC rate option; no readiness projector accepted.",
            ["CapFloor"] = "OTC rate option; no readiness projector accepted.",
            ["StructuredNote"] = "No explicit accounting class yet (review D10); no readiness projector accepted.",
            ["InvestmentFund"] = "Fund units; no readiness projector accepted."
        };

    [Fact]
    public void ReadinessProjectorsFor_NamesEveryCatalogClass_OrListsItAsAReasonedFallback()
    {
        ProjectorFallbackClasses.Keys.Should().BeSubsetOf(SecurityAssetClassCatalog.AssetClasses,
            "a fallback exception must name a canonical class");

        using var scope = new AssertionScope();
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var projectors = SecurityAssetPackRegistry.ReadinessProjectorsFor(assetClass);
            if (ProjectorFallbackClasses.ContainsKey(assetClass))
            {
                projectors.Should().Be(ProjectorFallback,
                    $"{assetClass} is listed as a fallback; remove the exception if it now names projectors");
            }
            else
            {
                projectors.Should().NotBe(ProjectorFallback,
                    $"{assetClass} falls through to the projector fallback; name its projectors or list it with a reason in {nameof(ProjectorFallbackClasses)}");
            }
        }
    }
}
