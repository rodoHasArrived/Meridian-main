using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>How a derivative settles at expiry or exercise.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecuritySettlementMethod>))]
public enum SecuritySettlementMethod
{
    /// <summary>Settled by a cash payment of the intrinsic value or final variation.</summary>
    Cash,

    /// <summary>Settled by delivery of the underlying.</summary>
    Physical,

    /// <summary>The holder elects cash or physical settlement.</summary>
    Elective
}

/// <summary>The right an option grants.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityOptionRight>))]
public enum SecurityOptionRight
{
    Call,
    Put
}

/// <summary>
/// The typed convention a string term field carries. A field declared with a convention keeps its
/// raw text verbatim in the payload, so every value already stored still reads back, but writes and
/// governed edits must name a value the convention's parser recognizes, and consumers read the
/// typed value through <see cref="SecurityTermConventions"/> instead of matching strings.
/// </summary>
public enum SecurityTermConventionKind
{
    None = 0,

    /// <summary>A day-count convention (<see cref="DayCountConvention"/>).</summary>
    DayCount,

    /// <summary>A derivative settlement method (<see cref="SecuritySettlementMethod"/>).</summary>
    SettlementMethod
}

/// <summary>Parsers and typed readers for the term conventions.</summary>
public static class SecurityTermConventions
{
    /// <summary>The canonical spellings offered when a form edits a convention field.</summary>
    public static IReadOnlyList<string> CanonicalValues(SecurityTermConventionKind kind)
        => kind switch
        {
            SecurityTermConventionKind.DayCount => Enum.GetValues<DayCountConvention>()
                .Where(static convention => convention != DayCountConvention.Unknown)
                .Select(static convention => convention.ToString())
                .ToArray(),
            SecurityTermConventionKind.SettlementMethod => Enum.GetNames<SecuritySettlementMethod>(),
            _ => []
        };

    /// <summary>Whether <paramref name="raw"/> names a recognized value of the convention.</summary>
    public static bool IsRecognized(SecurityTermConventionKind kind, string? raw)
        => kind switch
        {
            SecurityTermConventionKind.DayCount => DayCountConventions.TryParse(raw, out _),
            SecurityTermConventionKind.SettlementMethod => TryParseSettlementMethod(raw, out _),
            _ => true
        };

    /// <summary>The shared rejection reason for an unrecognized convention value.</summary>
    public static string DescribeUnrecognized(SecurityTermConventionKind kind, string assetClass, string key, string? raw)
        => $"Value '{raw}' is not a recognized {Describe(kind)} for '{key}' on asset class '{assetClass}'. " +
           $"Recognized values include: {string.Join(", ", CanonicalValues(kind))}.";

    /// <summary>Parses settlement-method spellings (Cash, Cash settled, Physical, Physical delivery, Delivery, Elective).</summary>
    public static bool TryParseSettlementMethod(string? raw, out SecuritySettlementMethod method)
    {
        var token = Normalize(raw);
        switch (token)
        {
            case "CASH":
            case "CASHSETTLED":
            case "CASHSETTLEMENT":
                method = SecuritySettlementMethod.Cash;
                return true;
            case "PHYSICAL":
            case "PHYSICALDELIVERY":
            case "PHYSICALLYSETTLED":
            case "DELIVERY":
            case "DELIVERABLE":
                method = SecuritySettlementMethod.Physical;
                return true;
            case "ELECTIVE":
            case "CASHORPHYSICAL":
                method = SecuritySettlementMethod.Elective;
                return true;
            default:
                method = default;
                return false;
        }
    }

    /// <summary>Parses an option right ("Call"/"Put", case-insensitive, also "C"/"P").</summary>
    public static bool TryParseOptionRight(string? raw, out SecurityOptionRight right)
    {
        switch (Normalize(raw))
        {
            case "CALL":
            case "C":
                right = SecurityOptionRight.Call;
                return true;
            case "PUT":
            case "P":
                right = SecurityOptionRight.Put;
                return true;
            default:
                right = default;
                return false;
        }
    }

    /// <summary>The typed day count a terms document declares at <paramref name="key"/>, or null.</summary>
    public static DayCountConvention? ReadDayCount(JsonElement terms, string key = "dayCount")
        => ReadString(terms, key) is { } raw && DayCountConventions.TryParse(raw, out var convention) ? convention : null;

    /// <summary>The typed settlement method a terms document declares, or null.</summary>
    public static SecuritySettlementMethod? ReadSettlementMethod(JsonElement terms, string key = "settlementType")
        => ReadString(terms, key) is { } raw && TryParseSettlementMethod(raw, out var method) ? method : null;

    /// <summary>The typed option right an option's terms declare, or null.</summary>
    public static SecurityOptionRight? ReadOptionRight(JsonElement terms, string key = "putCall")
        => ReadString(terms, key) is { } raw && TryParseOptionRight(raw, out var right) ? right : null;

    private static string? ReadString(JsonElement terms, string key)
        => terms.ValueKind == JsonValueKind.Object
           && terms.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Describe(SecurityTermConventionKind kind)
        => kind switch
        {
            SecurityTermConventionKind.DayCount => "day-count convention",
            SecurityTermConventionKind.SettlementMethod => "settlement method",
            _ => "value"
        };

    private static string Normalize(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : new string(raw.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
