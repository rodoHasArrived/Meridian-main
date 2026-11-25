using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Common identity/trading context that bounded economic profiles can safely ignore.</summary>
internal static class SecurityBoundedProfileCommonTerms
{
    private static readonly IReadOnlySet<string> IdentityFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "displayName", "currency", "countryOfRisk", "issuerName", "exchange", "lotSize", "tickSize",
        "primaryListingMic", "countryOfIncorporation", "settlementCycleDays", "holidayCalendarId"
    };

    internal static bool IsIdentityContext(JsonElement terms)
        => terms.ValueKind == JsonValueKind.Object
           && terms.EnumerateObject().All(property => IdentityFields.Contains(property.Name));
}
