using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Semantic write rules for declared schedule terms. Admission checks only this node's writable
/// fields; readable-ahead declarations and profile-governed fields retain ADR-024's separate rules.
/// </summary>
public static class ScheduleTermsWriteRules
{
    /// <summary>Refuses invalid schedule declarations at this node's current class write version.</summary>
    public static void Validate(string assetClass, JsonElement payload)
        => ValidateDeclarations(assetClass, payload, SecurityAssetClassCatalog.GetTermsVersion(assetClass));

    /// <summary>
    /// Validates declarations through a readable schema version without granting permission to write
    /// that version. This pure diagnostic entrypoint also permits testing the rules before rollout.
    /// </summary>
    public static void ValidateDeclarations(string assetClass, JsonElement payload, int termsVersion)
    {
        if (termsVersion < 1 || termsVersion > SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass))
            throw new ArgumentOutOfRangeException(nameof(termsVersion));
        if (SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload))
            return;
        bool Available(string key) => SecurityAssetTermsSchema.Field(assetClass, key) is { } field && field.SinceTermsVersion <= termsVersion;
        var conventions = Available("scheduleConventions") ? Member(payload, "scheduleConventions") : null;
        ValidateBlock(conventions);
        var hasCalls = Available("callSchedule") && Member(payload, "callSchedule") is { ValueKind: not JsonValueKind.Null };
        var hasPuts = Available("putSchedule") && Member(payload, "putSchedule") is { ValueKind: not JsonValueKind.Null };
        var maturity = hasCalls || hasPuts ? Date(payload, "maturity") ?? Date(payload, "maturityDate") : null;
        var frequency = hasCalls || hasPuts ? Text(payload, "paymentFrequency") : null;
        if (hasCalls)
        {
            var first = ValidateSchedule(Member(payload, "callSchedule"), true, maturity, frequency, conventions);
            if (first is not null && SecurityAssetTermsSchema.Field(assetClass, "isCallable") is not null && Member(payload, "isCallable") is { ValueKind: JsonValueKind.False })
                Refuse("A call schedule contradicts isCallable=false.");
            var scalarKey = SecurityAssetTermsSchema.Field(assetClass, "callDate") is not null ? "callDate" : "callableDate";
            if (first is not null && Date(payload, scalarKey) is { } scalar && scalar != first)
                Refuse($"{scalarKey} must agree with the earliest declared call date.");
        }
        if (hasPuts)
            ValidateSchedule(Member(payload, "putSchedule"), false, maturity, frequency, conventions);
        if (SecurityAssetTermsSchema.ElementFields(assetClass, "legs")
            .Any(field => field.Key == "scheduleConventions" && field.SinceTermsVersion <= termsVersion)
            && Member(payload, "legs") is { ValueKind: JsonValueKind.Array } legs)
        {
            foreach (var leg in legs.EnumerateArray())
                ValidateBlock(Member(leg, "scheduleConventions"));
        }
    }

    private static void ValidateBlock(JsonElement? declaration)
    {
        if (declaration is not { } block || !SecurityAssetTermsSchema.IsPopulatedTermValue(block))
            return;
        if (block.ValueKind != JsonValueKind.Object)
            Refuse("scheduleConventions must be an object.");
        Vocabulary(block, "direction", "Forward", "Backward");
        Vocabulary(block, "couponRateUnit", "Percent", "Fraction");
        Vocabulary(block, "couponCashBasis", "Level", "DayCount");
        if (Text(block, "couponRateUnit") is null || Text(block, "couponCashBasis") is null)
            Refuse("A populated scheduleConventions block requires explicit couponRateUnit and couponCashBasis.");
        var convention = Text(block, "businessDayConvention");
        if (convention is not null && !SecurityTermConventions.TryParseBusinessDayConvention(convention, out _))
            Refuse("Unknown businessDayConvention.");
        var roll = Text(block, "rollConvention");
        if (roll is not null && !SecurityTermConventions.TryParseRollConvention(roll, out _))
            Refuse("Unknown rollConvention.");
        var stub = Text(block, "stubPolicy");
        if (stub is not null && !SecurityTermConventions.TryParseStubPolicy(stub, out _))
            Refuse("Unknown stubPolicy.");
        if (Integer(block, "paymentLagBusinessDays") is < 0)
            Refuse("paymentLagBusinessDays cannot be negative.");
        var first = Date(block, "firstRegularDate");
        var last = Date(block, "lastRegularDate");
        if (first is not null && last is not null && first > last)
            Refuse("firstRegularDate must not follow lastRegularDate.");
        if (Member(block, "paymentCalendars") is { ValueKind: not JsonValueKind.Null } calendars
            && (calendars.ValueKind != JsonValueKind.Array || calendars.EnumerateArray().Any(id => id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))))
            Refuse("paymentCalendars must contain nonblank calendar ids.");
    }

    private static DateOnly? ValidateSchedule(JsonElement? declaration, bool call, DateOnly? maturity,
        string? couponFrequency, JsonElement? conventions)
    {
        if (declaration is not { ValueKind: not JsonValueKind.Null } schedule)
            return null;
        if (schedule.ValueKind != JsonValueKind.Array)
            Refuse("Call and put schedules must be arrays.");
        var startKey = call ? "callDate" : "putDate";
        var endKey = call ? "callEndDate" : "putEndDate";
        var rows = schedule.EnumerateArray()
            .Where(row => row.ValueKind != JsonValueKind.Object || SecurityAssetTermsSchema.IsPopulatedTermValue(row))
            .Select(row => (Row: row, Start: Date(row, startKey), End: Date(row, endKey))).ToArray();
        if (rows.Length == 0)
            return null;
        if (rows.Any(row => row.Row.ValueKind != JsonValueKind.Object || row.Start is null))
            Refuse($"Every {(call ? "call" : "put")} entry requires an ISO {startKey}.");
        var ordered = rows.OrderBy(row => row.Start).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var (row, start, explicitEnd) = ordered[index];
            var next = index + 1 < ordered.Length ? ordered[index + 1].Start : null;
            if (next == start)
                Refuse($"Duplicate {startKey} values are invalid.");
            if (explicitEnd is null && next is null && maturity is null)
                Refuse("The final exercise window needs an end date or maturity.");
            if (explicitEnd < start || (maturity is not null && (start > maturity || explicitEnd > maturity)))
                Refuse("An exercise window is inverted or extends beyond maturity.");
            if (explicitEnd is not null && next is not null && explicitEnd >= next)
                Refuse("Exercise windows in one schedule must not overlap.");
            Vocabulary(row, "exerciseStyle", "European", "American", "Bermudan");
            Vocabulary(row, "priceBasis", "PercentOfPar", "PercentOfAccretedValue");
            Vocabulary(row, "noticeDayType", "Business", "Calendar");
            if (call)
            {
                Vocabulary(row, "priceType", "Fixed", "MakeWhole");
                Vocabulary(row, "partialRedemption", "WholeOnly", "ProRata", "ByLot");
            }
            var minimum = Integer(row, "minimumNoticeDays");
            var maximum = Integer(row, "maximumNoticeDays");
            if (minimum is < 0 || maximum is < 0 || minimum > maximum)
                Refuse("Notice bounds must be nonnegative and minimum must not exceed maximum.");
            var noticeType = Text(row, "noticeDayType");
            if ((minimum is not null || maximum is not null) && noticeType is null)
                Refuse("Notice counts require noticeDayType.");
            if ((minimum is not null || maximum is not null) && noticeType?.Equals("Business", StringComparison.OrdinalIgnoreCase) == true
                && (conventions is not { } block || Member(block, "paymentCalendars") is not { ValueKind: JsonValueKind.Array } ids || ids.GetArrayLength() == 0))
                Refuse("Business-day notice requires declared paymentCalendars.");
            var frequency = Text(row, "exerciseFrequency");
            if (frequency is not null && !SecurityTermConventions.IsRecognized(SecurityTermConventionKind.CouponFrequency, frequency))
                Refuse("Unknown exerciseFrequency.");
            if (Text(row, "exerciseStyle")?.Equals("Bermudan", StringComparison.OrdinalIgnoreCase) == true
                && (!SecurityTermConventions.TryParsePaymentFrequency(frequency ?? couponFrequency, out var cadence) || cadence.PeriodsPerYear() is null))
                Refuse("Bermudan exercise requires a regular exercise or coupon frequency.");
            if (Member(row, call ? "callPrice" : "putPrice") is { ValueKind: JsonValueKind.Number } price
                && (!price.TryGetDecimal(out var amount) || amount < 0m))
                Refuse("Exercise price must be a representable nonnegative decimal.");
        }
        return ordered[0].Start;
    }

    private static void Vocabulary(JsonElement source, string key, params string[] values)
    {
        if (Text(source, key) is { } text && !values.Contains(text, StringComparer.OrdinalIgnoreCase))
            Refuse($"Unknown {key}.");
    }

    private static JsonElement? Member(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object)
            return null;
        var values = source.EnumerateObject().Where(property => property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (values.Length > 1)
            Refuse($"Ambiguous member '{key}'.");
        return values.Length == 1 ? values[0].Value : null;
    }

    private static string? Text(JsonElement source, string key)
        => Member(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : throw Invalid($"{key} must be a string.");

    private static int? Integer(JsonElement source, string key)
        => Member(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw Invalid($"{key} must be an integer.");

    private static DateOnly? Date(JsonElement source, string key)
        => Text(source, key) is not { } token ? null
            : DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Invalid($"{key} must be an ISO date.");

    private static InvalidOperationException Invalid(string reason) => new($"Invalid declared schedule terms: {reason}");
    private static void Refuse(string reason) => throw Invalid(reason);
}
