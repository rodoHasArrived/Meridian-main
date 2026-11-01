namespace Meridian.Contracts.SecurityMaster;

public static class SecurityProjectionRecordExtensions
{
    /// <summary>
    /// The retained projection row viewed as the detail contract that terms resolvers, schedule
    /// factories and calculation policies consume. Identity, terms, version and effective window
    /// are carried unchanged; provenance and primary identifier are projection-only facts.
    /// </summary>
    public static SecurityDetailDto ToDetail(this SecurityProjectionRecord security)
    {
        ArgumentNullException.ThrowIfNull(security);
        return new(security.SecurityId, security.AssetClass, security.Status, security.DisplayName,
            security.Currency, security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
            security.Version, security.EffectiveFrom, security.EffectiveTo);
    }
}
