using System.Collections.Frozen;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.ReferenceData.SecurityMaster;

/// <summary>
/// Read-only bridge between <see cref="InstrumentType"/> (market-data layer) and the
/// F# <c>SecurityKind</c> asset-class strings (security master layer).
///
/// <para>
/// These two classification systems evolved independently: <see cref="InstrumentType"/> is
/// used by the data-collection pipeline and provider adapters, while the security master uses
/// F# discriminated-union asset-class names. This static lookup provides a non-breaking,
/// read-only mapping so routing code does not need to hardcode string comparisons.
/// </para>
/// </summary>
public static class SecurityKindMapping
{
    // AssetClass strings match SecurityKind.assetClass in SecurityMaster.fs.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<InstrumentType>> AssetClassToInstrumentTypes =
        new Dictionary<string, IReadOnlyList<InstrumentType>>(StringComparer.OrdinalIgnoreCase)
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
        };

    private static readonly IReadOnlyDictionary<InstrumentType, string> InstrumentTypeToAssetClass =
        new Dictionary<InstrumentType, string>
        {
            [InstrumentType.Equity] = "Equity",
            [InstrumentType.EquityOption] = "Option",
            [InstrumentType.IndexOption] = "Option",
            [InstrumentType.FuturesOption] = "Option",
            [InstrumentType.Future] = "Future",
            [InstrumentType.SingleStockFuture] = "Future",
            [InstrumentType.Forex] = "FxSpot",
            [InstrumentType.Commodity] = "Commodity",
            [InstrumentType.Crypto] = "CryptoCurrency",
            [InstrumentType.Bond] = "Bond",
            [InstrumentType.Index] = "OtherSecurity",
            [InstrumentType.CFD] = "Cfd",
            [InstrumentType.Warrant] = "Warrant",
            [InstrumentType.Swap] = "Swap",
            [InstrumentType.DirectLoan] = "DirectLoan",
            [InstrumentType.Repo] = "Repo",
            [InstrumentType.Deposit] = "Deposit",
        };

    private static readonly FrozenDictionary<string, string> CanonicalAssetClassLookup =
        SecurityAssetClassCatalog.AssetClasses
            .ToDictionary(static assetClass => assetClass, static assetClass => assetClass, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, SecurityKindCompatibilityProfile> CompatibilityProfileByAssetClass =
        SecurityAssetClassCatalog.AssetClasses
            .Select(BuildAssetClassCompatibilityProfile)
            .ToDictionary(static profile => profile.AssetClass, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<InstrumentType, SecurityKindCompatibilityProfile> CompatibilityProfileByInstrumentType =
        InstrumentTypeDescriptorCatalog.All
            .Select(BuildInstrumentTypeCompatibilityProfile)
            .ToDictionary(static profile => profile.PrimaryInstrumentType!.Value)
            .ToFrozenDictionary();

    public static IReadOnlyList<SecurityKindCompatibilityProfile> CompatibilityProfiles { get; } =
        SecurityAssetClassCatalog.AssetClasses
            .Select(BuildAssetClassCompatibilityProfile)
            .ToArray();

    /// <summary>
    /// Returns the canonical security master asset-class string for the given
    /// <paramref name="instrumentType"/>, or <c>null</c> when no mapping exists.
    /// </summary>
    public static string? ToAssetClass(InstrumentType instrumentType) =>
        InstrumentTypeToAssetClass.TryGetValue(instrumentType, out var ac) ? ac : null;

    /// <summary>
    /// Returns all <see cref="InstrumentType"/> values that correspond to the given
    /// security master <paramref name="assetClass"/> string.
    /// Returns an empty list when the asset class is not mapped to a market-data instrument type.
    /// </summary>
    public static IReadOnlyList<InstrumentType> ToInstrumentTypes(string assetClass) =>
        AssetClassToInstrumentTypes.TryGetValue(assetClass, out var types)
            ? types
            : Array.Empty<InstrumentType>();

    /// <summary>
    /// Returns the single most-specific <see cref="InstrumentType"/> hint for a given
    /// <paramref name="assetClass"/>, or <c>null</c> when ambiguous or unmapped.
    /// Useful for provider subscription routing when a precise type is needed.
    /// </summary>
    public static InstrumentType? ToPrimaryInstrumentType(string assetClass)
    {
        var types = ToInstrumentTypes(assetClass);
        return types.Count == 1 ? types[0] : null;
    }

    /// <summary>
    /// Returns contract-owned operational descriptors compatible with a security master asset class.
    /// Direct mappings are returned first; asset classes without a direct market-data mapping can still
    /// return descriptors whose compatibility list names the asset class.
    /// </summary>
    public static IReadOnlyList<InstrumentTypeDescriptor> ToInstrumentTypeDescriptors(string assetClass)
    {
        var directDescriptors = ToInstrumentTypes(assetClass)
            .Select(static instrumentType => InstrumentTypeDescriptorCatalog.Find(instrumentType))
            .Where(static descriptor => descriptor is not null)
            .Select(static descriptor => descriptor!)
            .ToArray();

        return directDescriptors.Length > 0
            ? directDescriptors
            : InstrumentTypeDescriptorCatalog.FindBySecurityMasterAssetClass(assetClass);
    }

    /// <summary>
    /// Returns the descriptor for an instrument type when the catalog contains operational metadata.
    /// </summary>
    public static InstrumentTypeDescriptor? ToInstrumentTypeDescriptor(InstrumentType instrumentType) =>
        InstrumentTypeDescriptorCatalog.Find(instrumentType);

    /// <summary>
    /// Returns the compatibility profile for a security master asset class.
    /// </summary>
    public static SecurityKindCompatibilityProfile? GetCompatibilityProfile(string assetClass)
    {
        if (!TryNormalizeAssetClass(assetClass, out var normalizedAssetClass))
        {
            return null;
        }

        return CompatibilityProfileByAssetClass.TryGetValue(normalizedAssetClass, out var profile)
            ? profile
            : null;
    }

    /// <summary>
    /// Returns the compatibility profile for an instrument type.
    /// </summary>
    public static SecurityKindCompatibilityProfile? GetCompatibilityProfile(InstrumentType instrumentType) =>
        CompatibilityProfileByInstrumentType.TryGetValue(instrumentType, out var profile)
            ? profile
            : null;

    /// <summary>
    /// Normalizes an asset-class string to the canonical security master casing when it is known.
    /// </summary>
    public static bool TryNormalizeAssetClass(string assetClass, out string normalizedAssetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            normalizedAssetClass = string.Empty;
            return false;
        }

        if (CanonicalAssetClassLookup.TryGetValue(assetClass, out var canonicalAssetClass))
        {
            normalizedAssetClass = canonicalAssetClass;
            return true;
        }

        normalizedAssetClass = string.Empty;
        return false;
    }

    private static SecurityKindCompatibilityProfile BuildAssetClassCompatibilityProfile(string assetClass)
    {
        var instrumentTypes = ToInstrumentTypes(assetClass);
        var descriptors = ToInstrumentTypeDescriptors(assetClass);
        var descriptorNames = descriptors.Select(static descriptor => descriptor.DisplayName).ToArray();
        var compatibleAssetClasses = DistinctStrings(
            [assetClass],
            descriptors.SelectMany(static descriptor => descriptor.CompatibleSecurityMasterAssetClasses));
        var primaryInstrumentType = instrumentTypes.Count == 1 ? instrumentTypes[0] : (InstrumentType?)null;
        var summary = instrumentTypes.Count switch
        {
            0 when descriptorNames.Length > 0 =>
                $"No direct market-data InstrumentType mapping exists for {assetClass}; compatible descriptor evidence is available through {string.Join(", ", descriptorNames)}.",
            0 =>
                $"No direct market-data InstrumentType mapping exists for {assetClass}.",
            1 =>
                $"{assetClass} maps to {descriptorNames.FirstOrDefault() ?? instrumentTypes[0].ToString()} for provider routing and operations checks.",
            _ =>
                $"{assetClass} maps to {instrumentTypes.Count} instrument types: {string.Join(", ", descriptorNames)}."
        };

        return new SecurityKindCompatibilityProfile(
            AssetClass: assetClass,
            InstrumentTypes: instrumentTypes,
            PrimaryInstrumentType: primaryInstrumentType,
            CompatibleSecurityMasterAssetClasses: compatibleAssetClasses,
            RequiredEconomicTerms: DistinctStrings(descriptors.SelectMany(static descriptor => descriptor.RequiredEconomicTerms)),
            ProviderCapabilities: DistinctStrings(descriptors.SelectMany(static descriptor => descriptor.ProviderCapabilities)),
            LifecycleEvents: DistinctStrings(descriptors.SelectMany(static descriptor => descriptor.LifecycleEvents)),
            ValidationRules: DistinctStrings(descriptors.SelectMany(static descriptor => descriptor.ValidationRules)),
            LedgerBehaviorHints: DistinctStrings(descriptors.SelectMany(static descriptor => descriptor.LedgerBehaviorHints)),
            RiskModelHints: DistinctStrings(descriptors.SelectMany(static descriptor => descriptor.RiskModelHints)),
            IsAmbiguous: instrumentTypes.Count != 1,
            Summary: summary);
    }

    private static SecurityKindCompatibilityProfile BuildInstrumentTypeCompatibilityProfile(InstrumentTypeDescriptor descriptor)
    {
        return new SecurityKindCompatibilityProfile(
            AssetClass: descriptor.SecurityMasterAssetClass,
            InstrumentTypes: [descriptor.InstrumentType],
            PrimaryInstrumentType: descriptor.InstrumentType,
            CompatibleSecurityMasterAssetClasses: DistinctStrings(
                [descriptor.SecurityMasterAssetClass],
                descriptor.CompatibleSecurityMasterAssetClasses),
            RequiredEconomicTerms: descriptor.RequiredEconomicTerms,
            ProviderCapabilities: descriptor.ProviderCapabilities,
            LifecycleEvents: descriptor.LifecycleEvents,
            ValidationRules: descriptor.ValidationRules,
            LedgerBehaviorHints: descriptor.LedgerBehaviorHints,
            RiskModelHints: descriptor.RiskModelHints,
            IsAmbiguous: false,
            Summary: $"{descriptor.DisplayName} maps to Security Master {descriptor.SecurityMasterAssetClass}.");
    }

    private static IReadOnlyList<string> DistinctStrings(params IEnumerable<string>[] values) =>
        values
            .SelectMany(static value => value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed record SecurityKindCompatibilityProfile(
    string AssetClass,
    IReadOnlyList<InstrumentType> InstrumentTypes,
    InstrumentType? PrimaryInstrumentType,
    IReadOnlyList<string> CompatibleSecurityMasterAssetClasses,
    IReadOnlyList<string> RequiredEconomicTerms,
    IReadOnlyList<string> ProviderCapabilities,
    IReadOnlyList<string> LifecycleEvents,
    IReadOnlyList<string> ValidationRules,
    IReadOnlyList<string> LedgerBehaviorHints,
    IReadOnlyList<string> RiskModelHints,
    bool IsAmbiguous,
    string Summary);
