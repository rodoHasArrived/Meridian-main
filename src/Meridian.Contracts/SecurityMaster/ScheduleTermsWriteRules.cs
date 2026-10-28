using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Semantic write rules for declared schedule terms. Readable-only containers and profile fields
/// retain ADR-024's separate guards; this validator never grants a readable version write authority.
/// </summary>
public static class ScheduleTermsWriteRules
{
    /// <summary>Checks the complete effective payload against this node's writable schedule contract.</summary>
    public static void Validate(string assetClass, JsonElement payload)
        => ValidateDeclarations(assetClass, payload, SecurityAssetClassCatalog.GetTermsVersion(assetClass));

    /// <summary>Pure pre-rollout diagnostics through an explicit readable version, without changing the write version.</summary>
    public static void ValidateDeclarations(string assetClass, JsonElement payload, int termsVersion)
    {
        var canonical = SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass;
        if (termsVersion < 1 || termsVersion > SecurityAssetClassCatalog.GetReadsTermsThrough(canonical))
            throw new ArgumentOutOfRangeException(nameof(termsVersion));
        if (SecurityTermsEnvelope.IsProfileGovernedPayload(canonical, payload))
            return;
        bool Available(string key) => SecurityAssetTermsSchema.Field(canonical, key) is { } field && field.SinceTermsVersion <= termsVersion;
        var block = Available(SecurityVersionedTerms.ScheduleConventionsKey) ? Container(payload, SecurityVersionedTerms.ScheduleConventionsKey) : null;
        if (block is { } declaredBlock && SecurityAssetTermsSchema.IsPopulatedTermValue(declaredBlock))
            ValidateMembers(declaredBlock, SecurityAssetTermsSchema.ElementFields(canonical, SecurityVersionedTerms.ScheduleConventionsKey), SecurityVersionedTerms.ScheduleConventionsKey);
        ValidateCalendars(block);
        ValidateShape(canonical, payload, termsVersion, block);
        var calls = Available(SecurityVersionedTerms.CallScheduleKey) ? Container(payload, SecurityVersionedTerms.CallScheduleKey) : null;
        var puts = Available(SecurityVersionedTerms.PutScheduleKey) ? Container(payload, SecurityVersionedTerms.PutScheduleKey) : null;
        var hasEntries = calls is { ValueKind: JsonValueKind.Array } callArray && callArray.EnumerateArray().Any(row => !IsUnsetScheduleEntry(row))
            || puts is { ValueKind: JsonValueKind.Array } putArray && putArray.EnumerateArray().Any(row => !IsUnsetScheduleEntry(row));
        var maturity = hasEntries ? Date(payload, "maturity") ?? Date(payload, "maturityDate") : null;
        var frequency = hasEntries ? Text(payload, "paymentFrequency") : null;
        if (Available(SecurityVersionedTerms.CallScheduleKey))
        {
            var first = ValidateSchedule(calls, true, canonical, maturity, frequency, block);
            if (first is not null)
            {
                if (SecurityAssetTermsSchema.Field(canonical, "isCallable") is not null && Member(payload, "isCallable") is not { ValueKind: JsonValueKind.True })
                    Refuse("A declared call schedule requires isCallable=true so scalar consumers agree with it.");
                var scalarKey = SecurityAssetTermsSchema.Field(canonical, "callDate") is not null ? "callDate"
                    : SecurityAssetTermsSchema.Field(canonical, "callableDate") is not null ? "callableDate" : null;
                if (scalarKey is not null && Date(payload, scalarKey) != first)
                    Refuse($"{scalarKey} must equal the earliest declared call date so scalar consumers agree with it.");
            }
        }
        if (Available(SecurityVersionedTerms.PutScheduleKey))
            ValidateSchedule(puts, false, canonical, maturity, frequency, block);

        // Swap's write version remains 1: no per-leg declaration is admitted by this contract step.
        // If a later step makes that field writable, ResolveShape must support it before admission.
        if (SecurityAssetTermsSchema.ElementFields(canonical, "legs").Any(field => field.Key == SecurityVersionedTerms.ScheduleConventionsKey && field.SinceTermsVersion <= termsVersion)
            && Member(payload, "legs") is { ValueKind: JsonValueKind.Array } legs)
        {
            foreach (var leg in legs.EnumerateArray())
                if (Container(leg, SecurityVersionedTerms.ScheduleConventionsKey) is { } legBlock && SecurityAssetTermsSchema.IsPopulatedTermValue(legBlock))
                    Refuse("Declared leg schedule conventions require a later per-leg contract step.");
        }
    }

    /// <summary>
    /// Prevents a write-version raise from turning retained, undeclared residue into a contract on
    /// an unrelated amendment. An explicit clear repairs it; an explicitly higher-stamped replacement
    /// states the new declaration and is subsequently checked by the ordinary write validator.
    /// </summary>
    public static void EnsureAmendmentDoesNotPromoteStrayContainers(string assetClass, JsonElement retained, JsonElement? replacement)
    {
        if (SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, retained))
            return;
        var stamp = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(retained);
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var replacementStamp = replacement is { } submitted ? SecurityTermsEnvelope.ResolveAssetClassTermsVersion(submitted) : 0;
        foreach (var field in SecurityAssetTermsSchema.WritableFields(assetClass).Where(field =>
            SecurityVersionedTerms.IsVersionedContainer(field) && field.SinceTermsVersion > stamp && field.SinceTermsVersion <= writes))
        {
            if (!AnyPopulated(retained, field.Key))
                continue;
            if (replacement is { } patch && (!AnyPopulated(patch, field.Key) || replacementStamp >= field.SinceTermsVersion))
                continue;
            Refuse($"Retained undeclared '{field.Key}' cannot be promoted by an unrelated amendment; explicitly clear it or submit a fully validated replacement stamped at version {field.SinceTermsVersion} or later.");
        }
    }

    private static bool AnyPopulated(JsonElement payload, string key)
        => payload.ValueKind == JsonValueKind.Object && payload.EnumerateObject().Any(property =>
            property.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && SecurityAssetTermsSchema.IsPopulatedTermValue(property.Value));

    private static DeclaredScheduleResolution? ValidateShape(string assetClass, JsonElement payload, int termsVersion, JsonElement? block)
    {
        if (block is not { } declaration || !SecurityAssetTermsSchema.IsPopulatedTermValue(declaration))
            return null;
        // The twin makes the diagnostic entrypoint see the proposed version without changing the
        // caller's payload or the catalog's write authority. The factory remains the one shape rule.
        var twin = JsonNode.Parse(payload.GetRawText())!.AsObject();
        twin[SecurityTermsEnvelope.AssetClassTermsVersionKey] = termsVersion;
        var terms = JsonSerializer.SerializeToElement(twin);
        var security = new SecurityDetailDto(Guid.Empty, assetClass, SecurityStatusDto.Active, "Schedule shape", "USD",
            JsonSerializer.SerializeToElement(new { }), terms, [], [], 1, DateTimeOffset.MinValue, null);
        var resolved = StructuredCashFlowTermsResolver.Resolve(security);
        var result = ScheduleSpecFactory.ResolveShape(security, resolved);
        if (result.Status == DeclaredScheduleStatus.Refused)
            Refuse(result.Refusal!.Message);
        if (result.Status != DeclaredScheduleStatus.Declared)
            Refuse("A populated scheduleConventions block could not be resolved by its class's declared schedule factory.");
        var calendars = HasCalendars(block);
        if (DayCountConventions.TryParse(resolved.DayCountConvention, out var dayCount))
        {
            if (dayCount == DayCountConvention.Business252 && !calendars)
                Refuse("Business/252 with declared conventions requires block paymentCalendars.");
            if (dayCount == DayCountConvention.ActualActualIcma && Member(declaration, "accrualAdjusted") is { ValueKind: JsonValueKind.True })
                Refuse("Actual/Actual ICMA cannot be written with accrualAdjusted=true.");
        }
        if (Text(declaration, "couponCashBasis") == "Level" && result.Spec!.Tenor.Unit == ScheduleTenorUnit.Term)
            Refuse("Level coupon cash cannot be written with an AtMaturity frequency.");
        return result;
    }

    private static DateOnly? ValidateSchedule(JsonElement? declaration, bool call, string assetClass, DateOnly? maturity,
        string? couponFrequency, JsonElement? conventions)
    {
        if (declaration is not { } schedule || !SecurityAssetTermsSchema.IsPopulatedTermValue(schedule))
            return null;
        if (schedule.ValueKind != JsonValueKind.Array)
            Refuse("Call and put schedules must be arrays.");
        var startKey = call ? "callDate" : "putDate";
        var endKey = call ? "callEndDate" : "putEndDate";
        var schemaKey = call ? SecurityVersionedTerms.CallScheduleKey : SecurityVersionedTerms.PutScheduleKey;
        var rows = schedule.EnumerateArray().Where(row => !IsUnsetScheduleEntry(row)).Select(row =>
        {
            ValidateMembers(row, SecurityAssetTermsSchema.ElementFields(assetClass, schemaKey), schemaKey);
            return (Row: row, Start: Date(row, startKey), End: Date(row, endKey));
        }).OrderBy(row => row.Start).ToArray();
        if (rows.Any(row => row.Start is null))
            Refuse($"Every written {schemaKey} entry requires an ISO {startKey}.");
        for (var index = 0; index < rows.Length; index++)
        {
            var (row, start, end) = rows[index];
            var next = index + 1 < rows.Length ? rows[index + 1].Start : null;
            if (next == start)
                Refuse($"Duplicate {startKey} values are invalid.");
            if (end is null && next is null && maturity is null)
                Refuse("The final exercise window needs an end date or maturity.");
            if (end < start || maturity is not null && (start > maturity || end > maturity))
                Refuse("An exercise window is inverted or extends beyond maturity.");
            if (end is not null && next is not null && end >= next)
                Refuse("Exercise windows in one schedule must not overlap.");
            var style = Vocabulary(row, "exerciseStyle", required: true, "European", "American", "Bermudan");
            Vocabulary(row, "priceBasis", false, "PercentOfPar", "PercentOfAccretedValue");
            var noticeType = Vocabulary(row, "noticeDayType", false, "Business", "Calendar");
            if (call)
            {
                Vocabulary(row, "priceType", false, "Fixed", "MakeWhole");
                Vocabulary(row, "partialRedemption", false, "WholeOnly", "ProRata", "ByLot");
            }
            var minimum = Integer(row, "minimumNoticeDays");
            var maximum = Integer(row, "maximumNoticeDays");
            if (minimum is < 0 || maximum is < 0 || minimum > maximum)
                Refuse("Notice bounds must be nonnegative and minimum must not exceed maximum.");
            if ((minimum is not null || maximum is not null) && noticeType is null)
                Refuse("Notice counts require noticeDayType.");
            if ((style == "American" || noticeType == "Business") && !HasCalendars(conventions))
                Refuse("American exercise and Business notice require block paymentCalendars, with no fallback.");
            var frequency = Text(row, "exerciseFrequency");
            if (frequency is not null && !SecurityTermConventions.IsRecognized(SecurityTermConventionKind.CouponFrequency, frequency))
                Refuse("Unknown exerciseFrequency.");
            if (style == "Bermudan" && (!SecurityTermConventions.TryParsePaymentFrequency(frequency ?? couponFrequency, out var cadence)
                || !SecurityTermConventions.IsSchedulableCoupon(cadence) || cadence.PeriodsPerYear() is null))
                Refuse("Bermudan exercise requires a regular exercise or coupon frequency.");
            var price = Member(row, call ? "callPrice" : "putPrice");
            // Owner-approved increment-5 decision 14 (extensibility review, 2026-10-28): a house
            // plausibility floor of 10 percent. This is not an inherent bound of percentage prices.
            if (price is { ValueKind: not JsonValueKind.Null } value && (!value.TryGetDecimal(out var amount) || amount < 10m))
                Refuse("Exercise prices below the approved plausibility floor of 10 percent require repair.");
        }
        return rows.Length == 0 ? null : rows[0].Start;
    }

    // ADR-024's protocol-unset spellings are not entries and the tolerant codec drops them.
    // This exception belongs only to the versioned call/put arrays, never ordinary item arrays.
    private static bool IsUnsetScheduleEntry(JsonElement row)
        => row.ValueKind == JsonValueKind.Null
            || row.ValueKind == JsonValueKind.Object && !SecurityAssetTermsSchema.IsPopulatedTermValue(row);

    private static void ValidateMembers(JsonElement value, IReadOnlyList<SecurityAssetTermField> fields, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            Refuse($"{path} must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var field = fields.FirstOrDefault(field => field.Key == property.Name);
            if (field is null || !names.Add(property.Name))
                Refuse($"Unknown, case-variant or repeated member '{path}.{property.Name}'.");
            if (property.Value.ValueKind == JsonValueKind.Null)
                continue;
            var valid = field.Type switch
            {
                SecurityAssetTermFieldType.String => property.Value.ValueKind == JsonValueKind.String,
                SecurityAssetTermFieldType.Boolean => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SecurityAssetTermFieldType.Integer => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out _),
                SecurityAssetTermFieldType.Decimal => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out _),
                SecurityAssetTermFieldType.Date => property.Value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(property.Value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                SecurityAssetTermFieldType.Array => property.Value.ValueKind == JsonValueKind.Array,
                _ => true
            };
            if (!valid)
                Refuse($"{path}.{field.Key} has the wrong JSON type or literal.");
        }
    }

    private static void ValidateCalendars(JsonElement? block)
    {
        if (block is { ValueKind: JsonValueKind.Object } declaration && SecurityAssetTermsSchema.IsPopulatedTermValue(declaration) && Member(declaration, "paymentCalendars") is { ValueKind: not JsonValueKind.Null } ids
            && (ids.ValueKind != JsonValueKind.Array || ids.EnumerateArray().Any(id => id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))))
            Refuse("paymentCalendars must contain nonblank calendar ids.");
    }

    private static bool HasCalendars(JsonElement? block)
        => block is { ValueKind: JsonValueKind.Object } declaration && Member(declaration, "paymentCalendars") is { ValueKind: JsonValueKind.Array } ids && ids.GetArrayLength() > 0;

    private static string? Vocabulary(JsonElement source, string key, bool required, params string[] values)
    {
        var text = Text(source, key);
        if (text is null ? required : !values.Contains(text, StringComparer.Ordinal))
            Refuse($"{key} must be explicitly stated as one of {string.Join(", ", values)}.");
        return text;
    }

    private static JsonElement? Container(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object)
            return null;
        if (source.EnumerateObject().Any(property => property.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && property.Name != key))
            Refuse($"Case-variant container '{key}' is not writable.");
        return Member(source, key);
    }

    private static JsonElement? Member(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object)
            return null;
        var values = source.EnumerateObject().Where(property => property.Name == key).ToArray();
        if (values.Length > 1)
            Refuse($"Repeated member '{key}'.");
        return values.Length == 1 ? values[0].Value : null;
    }

    private static string? Text(JsonElement source, string key)
        => Member(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid($"{key} must be a string.");

    private static int? Integer(JsonElement source, string key)
        => Member(source, key) is not { ValueKind: not JsonValueKind.Null } value ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw Invalid($"{key} must be an integer.");

    private static DateOnly? Date(JsonElement source, string key)
        => Text(source, key) is not { } token ? null
            : DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Invalid($"{key} must be an ISO date.");

    private static InvalidOperationException Invalid(string reason) => new($"Invalid declared schedule terms: {reason}");
    [DoesNotReturn]
    private static void Refuse(string reason) => throw Invalid(reason);
}
