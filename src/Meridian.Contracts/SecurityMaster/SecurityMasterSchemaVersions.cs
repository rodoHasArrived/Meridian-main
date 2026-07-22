using System.Globalization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Schema versions for the asset-specific-terms payload family — the JSON stored in a security's
/// <c>AssetSpecificTerms</c> slot and validated by the mapping/deserialization guard. This family
/// owns its own acceptance logic; versions from other payload families (e.g.
/// <see cref="EconomicTermsSchema"/>) are never valid here and must be converted through
/// <see cref="SecurityAssetSpecificTermsUpcasterChain"/> before reaching this slot.
/// </summary>
public static class AssetSpecificTermsSchema
{
    /// <summary>Flat per-asset-class term payloads (the original, pre-versioning shape).</summary>
    public const int Legacy = 1;

    /// <summary>Custom-asset profile-backed term payloads (profileFields + customProfileId).</summary>
    public const int CustomAssetProfile = 3;

    /// <summary>
    /// The version stamped on legacy payloads that predate explicit versioning. A payload with no
    /// <c>schemaVersion</c> is treated as this version on read (migrate-on-read).
    /// </summary>
    public const int Default = Legacy;

    /// <summary>
    /// Single source of truth for whether an asset-specific-terms <c>schemaVersion</c> is accepted
    /// by the current compatibility adapter. Legacy terms are always accepted; the
    /// custom-asset-profile family is accepted only for profile-backed records.
    /// </summary>
    public static bool IsAccepted(int schemaVersion, bool isProfileBacked)
        => schemaVersion == Legacy
           || (isProfileBacked && schemaVersion == CustomAssetProfile);

    /// <summary>The accepted versions for the given record shape, ascending.</summary>
    public static IReadOnlyList<int> Accepted(bool isProfileBacked)
        => isProfileBacked
            ? new[] { Legacy, CustomAssetProfile }
            : new[] { Legacy };

    /// <summary>Human-readable accepted-version list for validation messages (e.g. "1" or "1 or 3").</summary>
    public static string DescribeAccepted(bool isProfileBacked)
        => isProfileBacked
            ? $"{Legacy} or {CustomAssetProfile}"
            : Legacy.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Schema versions for the economic-terms payload family — the structured
/// maturity/coupon/discount/accrual/payment document written by the economic definition adapter.
/// This is a distinct family from <see cref="AssetSpecificTermsSchema"/>: the two shapes share
/// nothing but the <c>schemaVersion</c> key, so their version numbers must never be compared
/// against each other's acceptance sets.
/// </summary>
public static class EconomicTermsSchema
{
    /// <summary>The current structured economic-terms shape.</summary>
    public const int Current = 2;
}

/// <summary>
/// Back-compatibility facade over the per-family schema-version types. Historically all Security
/// Master payload families shared this one constants class, which let an economic-terms version
/// leak into asset-specific-terms acceptance checks; the authoritative definitions now live on
/// <see cref="AssetSpecificTermsSchema"/> and <see cref="EconomicTermsSchema"/>. Prefer those in
/// new code.
/// </summary>
public static class SecurityMasterSchemaVersions
{
    public const int LegacyAssetSpecificTerms = AssetSpecificTermsSchema.Legacy;
    public const int EconomicTerms = EconomicTermsSchema.Current;
    public const int CustomAssetProfileTerms = AssetSpecificTermsSchema.CustomAssetProfile;

    /// <inheritdoc cref="AssetSpecificTermsSchema.Default"/>
    public const int DefaultAssetSpecificTerms = AssetSpecificTermsSchema.Default;

    /// <inheritdoc cref="AssetSpecificTermsSchema.IsAccepted"/>
    public static bool IsAcceptedAssetSpecificTermsVersion(int schemaVersion, bool isProfileBacked)
        => AssetSpecificTermsSchema.IsAccepted(schemaVersion, isProfileBacked);

    /// <inheritdoc cref="AssetSpecificTermsSchema.Accepted"/>
    public static IReadOnlyList<int> AcceptedAssetSpecificTermsVersions(bool isProfileBacked)
        => AssetSpecificTermsSchema.Accepted(isProfileBacked);

    /// <inheritdoc cref="AssetSpecificTermsSchema.DescribeAccepted"/>
    public static string DescribeAcceptedAssetSpecificTermsVersions(bool isProfileBacked)
        => AssetSpecificTermsSchema.DescribeAccepted(isProfileBacked);
}
