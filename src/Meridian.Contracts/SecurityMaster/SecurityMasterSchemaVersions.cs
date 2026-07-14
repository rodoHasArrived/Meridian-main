using System.Globalization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Stable schema-version constants for Security Master payload families and the single, centralized
/// acceptance set that read/validation paths must consult. Keep additive compatibility work keyed to
/// these values — and route every "is this version supported?" decision through the helpers below —
/// rather than scattering literal version numbers across interop, projection, storage, and UI code.
/// </summary>
public static class SecurityMasterSchemaVersions
{
    public const int LegacyAssetSpecificTerms = 1;
    public const int EconomicTerms = 2;
    public const int CustomAssetProfileTerms = 3;

    /// <summary>
    /// The version stamped on legacy asset-specific-terms payloads that predate explicit versioning.
    /// A payload with no <c>schemaVersion</c> is treated as this version on read (migrate-on-read).
    /// </summary>
    public const int DefaultAssetSpecificTerms = LegacyAssetSpecificTerms;

    /// <summary>
    /// Single source of truth for whether an asset-specific-terms <c>schemaVersion</c> is accepted by
    /// the current Security Master compatibility adapter. Legacy terms are always accepted; the
    /// custom-asset-profile family is accepted only for profile-backed records. Both the validation
    /// surface and the mapping/deserialization guard consult this predicate so the accepted set can
    /// never drift between the two paths.
    /// </summary>
    public static bool IsAcceptedAssetSpecificTermsVersion(int schemaVersion, bool isProfileBacked)
        => schemaVersion == LegacyAssetSpecificTerms
           || (isProfileBacked && schemaVersion == CustomAssetProfileTerms);

    /// <summary>
    /// The accepted asset-specific-terms schema versions for the given record shape, in ascending
    /// order. Use this when a caller needs the set itself (e.g. for diagnostics) rather than a
    /// membership test.
    /// </summary>
    public static IReadOnlyList<int> AcceptedAssetSpecificTermsVersions(bool isProfileBacked)
        => isProfileBacked
            ? new[] { LegacyAssetSpecificTerms, CustomAssetProfileTerms }
            : new[] { LegacyAssetSpecificTerms };

    /// <summary>
    /// A human-readable rendering of <see cref="AcceptedAssetSpecificTermsVersions"/> for validation
    /// messages and remediation guidance (e.g. "1" or "1 or 3").
    /// </summary>
    public static string DescribeAcceptedAssetSpecificTermsVersions(bool isProfileBacked)
        => isProfileBacked
            ? $"{LegacyAssetSpecificTerms} or {CustomAssetProfileTerms}"
            : LegacyAssetSpecificTerms.ToString(CultureInfo.InvariantCulture);
}
