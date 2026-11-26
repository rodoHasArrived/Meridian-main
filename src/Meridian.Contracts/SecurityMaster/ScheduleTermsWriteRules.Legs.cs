using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

public static partial class ScheduleTermsWriteRules
{
    private static readonly IReadOnlySet<string> SeparatelyCheckedLegContainers = new HashSet<string>(StringComparer.Ordinal)
    { SecurityVersionedTerms.ScheduleConventionsKey, "resetSchedule", "notionalSchedule" };

    internal static void ValidateLegBlock(string assetClass, JsonElement block)
    {
        ValidateMembers(block, SecurityAssetTermsSchema.ElementFields(assetClass, "legs.scheduleConventions"),
            "legs.scheduleConventions", fresh: true);
        ValidateCalendars(block, fresh: true);
    }

    internal static void ValidateLegMembers(string assetClass, JsonElement leg)
        => ValidateMembers(leg, SecurityAssetTermsSchema.ElementFields(assetClass, "legs"), "legs", fresh: true,
            separatelyCheckedContainers: SeparatelyCheckedLegContainers);

    internal static void ValidateLegResetMembers(string assetClass, JsonElement reset)
        => ValidateMembers(reset, SecurityAssetTermsSchema.ElementFields(assetClass, "legs.resetSchedule"),
            "legs.resetSchedule", fresh: true);

    private static void ValidateLegDeclarations(string assetClass, JsonElement payload, int termsVersion)
    {
        if (SecurityAssetTermsSchema.ElementFields(assetClass, "legs").Any(field =>
                field.Key == SecurityVersionedTerms.ScheduleConventionsKey && field.SinceTermsVersion <= termsVersion)
            && SecurityVanillaSwapProfile.HasLegDeclarations(assetClass, payload))
            SecurityVanillaSwapProfile.Build(assetClass, payload, currency: null, forWrite: true);
    }

    private static void EnsureAmendmentDoesNotPromoteStrayLegs(string assetClass, JsonElement retained, JsonElement? replacement)
    {
        if (SecurityAssetTermsSchema.ElementFields(assetClass, "legs")
                .FirstOrDefault(static field => field.Key == SecurityVersionedTerms.ScheduleConventionsKey) is not { } field
            || SecurityTermsEnvelope.ResolveAssetClassTermsVersion(retained) >= field.SinceTermsVersion
            || !SecurityVanillaSwapProfile.HasLegDeclarations(assetClass, retained))
            return;
        if (replacement is { } submitted && (!SecurityVanillaSwapProfile.HasLegDeclarations(assetClass, submitted)
                || SecurityTermsEnvelope.ResolveAssetClassTermsVersion(submitted) >= field.SinceTermsVersion))
            return;
        Refuse($"Retained undeclared leg conventions cannot be promoted by an unrelated amendment; explicitly clear them or submit a reviewed replacement stamped at version {field.SinceTermsVersion} or later.");
    }
}
