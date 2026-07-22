namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Read-tolerant enum parsing for Security Master persistence and interop reads. A value written by
/// a newer node (a new identifier kind, status, scope, or state) must degrade to the enum's
/// designated fallback member instead of throwing — a hard <c>Enum.Parse</c> on a row read turns
/// one unrecognized value into a total read outage for that row on every older node, which makes
/// mixed-version rollout unsafe. Write paths stay strict: this helper is for reads only.
/// </summary>
public static class SecurityMasterEnumReads
{
    /// <summary>
    /// Parses <paramref name="raw"/> case-insensitively into <typeparamref name="TEnum"/>, returning
    /// <paramref name="fallback"/> for null, unrecognized, or numerically-undefined input. Numeric
    /// strings that do not name a defined member also degrade (bare <c>Enum.TryParse</c> would
    /// happily produce an undefined value for them).
    /// </summary>
    public static TEnum ParseOrFallback<TEnum>(string? raw, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
}
