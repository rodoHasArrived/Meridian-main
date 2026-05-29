namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Stable schema-version constants for Security Master payload families.
/// Keep additive compatibility work keyed to these values rather than scattering
/// literal version numbers across interop, projection, and UI code.
/// </summary>
public static class SecurityMasterSchemaVersions
{
    public const int LegacyAssetSpecificTerms = 1;
    public const int EconomicTerms = 2;
    public const int CustomAssetProfileTerms = 3;
}
