using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Parses dated economic schedules once, preserving legacy aliases and governed-source ownership.
/// This boundary never rewrites retained JSON or applies a wall-clock cutoff. Record-contextual
/// rules (maturity windows and principal versus par) remain at canonical command admission.
/// </summary>
public static class SecurityEconomicScheduleParser
{
    private static readonly string[] FactorKeys = ["factorScheduleEntries", "factorSchedule", "factorSchedules"];
    private static readonly string[] PrincipalKeys = ["principalSchedule"];
    private static readonly string[] StepKeys = ["stepSchedule", "stepCouponSchedule", "couponSteps"];
    private static readonly string[] FactorDates = ["asOfDate", "factorDate", "effectiveDate", "date"];
    private static readonly string[] FactorValues = ["factor", "currentFactor"];
    private static readonly string[] PrincipalDates = ["paymentDate"];
    private static readonly string[] PrincipalValues = ["amount"];
    private static readonly string[] StepDates = ["effectiveDate", "stepDate", "date"];
    private static readonly string[] StepValues = ["rate", "couponRate"];

    /// <summary>
    /// Reads profile fields, outer asset terms and common terms in that order. Null is absent;
    /// an empty array claims ownership; a malformed selected declaration refuses without fallback.
    /// Callers retain their existing terms-version read guard before passing upgraded terms here.
    /// </summary>
    public static SecurityEconomicScheduleValidation Parse(JsonElement assetSpecificTerms, JsonElement commonTerms = default)
    {
        var sources = new List<(JsonElement Value, string Path)>();
        var profiles = GetProperties(assetSpecificTerms, "profileFields");
        if (profiles.Count > 0 && profiles[0].Value.ValueKind == JsonValueKind.Object)
        {
            var profile = profiles[0];
            sources.Add((profile.Value, $"assetSpecificTerms.{profile.Name}"));
        }
        sources.Add((assetSpecificTerms, "assetSpecificTerms"));
        sources.Add((commonTerms, "commonTerms"));

        return new(
            Read(sources, SecurityEconomicScheduleKind.Factor, FactorKeys, FactorDates, FactorValues,
                static (date, value) => new StructuredFactorScheduleEntry(date, value),
                DuplicateProfilePath(profiles, SecurityEconomicScheduleKind.Factor, FactorKeys)),
            Read(sources, SecurityEconomicScheduleKind.Principal, PrincipalKeys, PrincipalDates, PrincipalValues,
                static (date, value) => new StructuredPrincipalScheduleEntry(date, value),
                DuplicateProfilePath(profiles, SecurityEconomicScheduleKind.Principal, PrincipalKeys)),
            Read(sources, SecurityEconomicScheduleKind.StepCoupon, StepKeys, StepDates, StepValues,
                static (date, value) => new StructuredStepCouponEntry(date, value),
                DuplicateProfilePath(profiles, SecurityEconomicScheduleKind.StepCoupon, StepKeys)));
    }

    /// <summary>Recognizes canonical and legacy schedule container names, independently of casing.</summary>
    public static bool TryGetKind(string key, out SecurityEconomicScheduleKind kind)
    {
        if (FactorKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            kind = SecurityEconomicScheduleKind.Factor;
            return true;
        }
        if (PrincipalKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            kind = SecurityEconomicScheduleKind.Principal;
            return true;
        }
        if (StepKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            kind = SecurityEconomicScheduleKind.StepCoupon;
            return true;
        }
        kind = default;
        return false;
    }

    /// <summary>Validates a whole-array field replacement with the same grammar used by readers.</summary>
    public static SecurityEconomicScheduleValidation ParseContainer(SecurityEconomicScheduleKind kind, JsonElement value, string path)
        => kind switch
        {
            SecurityEconomicScheduleKind.Factor => new(
                ParseValue(value, path, kind, FactorDates, FactorValues, static (date, amount) => new StructuredFactorScheduleEntry(date, amount)),
                Absent<StructuredPrincipalScheduleEntry>(), Absent<StructuredStepCouponEntry>()),
            SecurityEconomicScheduleKind.Principal => new(
                Absent<StructuredFactorScheduleEntry>(),
                ParseValue(value, path, kind, PrincipalDates, PrincipalValues, static (date, amount) => new StructuredPrincipalScheduleEntry(date, amount)),
                Absent<StructuredStepCouponEntry>()),
            SecurityEconomicScheduleKind.StepCoupon => new(
                Absent<StructuredFactorScheduleEntry>(), Absent<StructuredPrincipalScheduleEntry>(),
                ParseValue(value, path, kind, StepDates, StepValues, static (date, amount) => new StructuredStepCouponEntry(date, amount))),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static SecurityScheduleParseResult<T> Absent<T>() => new(SecuritySchedulePresence.Absent, [], []);

    private static SecurityScheduleParseResult<T> Read<T>(
        IEnumerable<(JsonElement Value, string Path)> sources,
        SecurityEconomicScheduleKind kind, string[] keys, string[] dates, string[] values,
        Func<DateOnly, decimal, T> create,
        string? duplicateProfilePath)
    {
        if (duplicateProfilePath is not null)
        {
            return Invalid<T>(duplicateProfilePath, [DuplicateMember(duplicateProfilePath)]);
        }
        foreach (var (source, sourcePath) in sources)
        {
            foreach (var key in keys)
            {
                var properties = GetProperties(source, key);
                if (properties.Count == 0)
                {
                    continue;
                }
                // The frozen free-text field is a trustee/servicer evidence reference. Its legacy
                // array form is arithmetic, but prose never shadows a typed schedule or falls foul
                // of the schedule container check.
                if (kind == SecurityEconomicScheduleKind.Factor
                    && key == "factorSchedule"
                    && properties.All(static property => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    continue;
                }
                // JSONB retains the last exact duplicate key and may reorder case variants.
                // Reject an ambiguous selected spelling before null skipping or source fallback.
                // Distinct aliases still follow the documented priority order above.
                if (properties.Count > 1)
                {
                    var duplicatePath = $"{sourcePath}.{properties[1].Name}";
                    return Invalid<T>(duplicatePath, [DuplicateMember(duplicatePath)]);
                }
                var property = properties[0];
                if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }
                return ParseValue(property.Value, $"{sourcePath}.{property.Name}", kind, dates, values, create);
            }
        }
        return Absent<T>();
    }

    private sealed record Row(DateOnly Date, decimal Value, string DatePath, string ValuePath);

    private static SecurityScheduleParseResult<T> ParseValue<T>(
        JsonElement value, string path, SecurityEconomicScheduleKind kind,
        string[] dates, string[] values, Func<DateOnly, decimal, T> create)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Absent<T>();
        }
        var diagnostics = new List<SecurityScheduleDiagnostic>();
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Invalid<T>(path, [new("SM_SCHEDULE_CONTAINER_INVALID", path, "A schedule must be an array.")]);
        }

        var rows = new List<Row>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var rowPath = $"{path}[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new("SM_SCHEDULE_ROW_INVALID", rowPath, "Each schedule row must be an object."));
                continue;
            }
            var date = ReadDate(item, dates, rowPath, diagnostics, out var datePath);
            var amount = ReadDecimal(item, values, rowPath, diagnostics, out var valuePath);
            if (amount is { } number && !InRange(kind, number))
            {
                diagnostics.Add(new("SM_SCHEDULE_RANGE_INVALID", valuePath, kind switch
                {
                    SecurityEconomicScheduleKind.Factor => "Factors must be between zero and one.",
                    SecurityEconomicScheduleKind.Principal => "Principal amounts must be greater than zero.",
                    _ => "Step coupon rates must be zero or greater."
                }));
            }
            if (date is { } parsedDate && amount is { } parsedValue)
            {
                rows.Add(new(parsedDate, parsedValue, datePath, valuePath));
            }
        }
        if (diagnostics.Count > 0)
        {
            return Invalid<T>(path, diagnostics);
        }

        if (kind == SecurityEconomicScheduleKind.Principal)
        {
            // A principal journal is identified by security/date. Multiple legitimate instalments
            // on that date assert one combined payment; this is aggregation, not duplicate repair.
            var totals = new List<(DateOnly Date, decimal Value)>();
            foreach (var group in rows.GroupBy(static row => row.Date))
            {
                var result = SecurityScheduleDecimal.Sum(group.Select(static row => row.Value), out var total);
                if (result == SecurityScheduleDecimal.SumResult.Exact)
                {
                    totals.Add((group.Key, total));
                }
                else
                {
                    // Diagnose the complete sum at its final contributing source row, without
                    // letting rounded or unrepresentable intermediate subtotals affect the result.
                    var finalPath = group.Last().ValuePath;
                    diagnostics.Add(result == SecurityScheduleDecimal.SumResult.Overflow
                        ? new("SM_SCHEDULE_SUM_OVERFLOW", finalPath,
                            "Same-date principal payments exceed the supported decimal range.")
                        : new("SM_SCHEDULE_SUM_PRECISION", finalPath,
                            "The complete same-date principal payment cannot be represented exactly as a decimal."));
                }
            }
            return diagnostics.Count > 0
                ? Invalid<T>(path, diagnostics)
                : Accepted(path, totals.OrderBy(static pair => pair.Date).Select(pair => create(pair.Date, pair.Value)).ToArray(),
                    rows.Select(row => create(row.Date, row.Value)).ToArray());
        }

        var seen = new HashSet<DateOnly>();
        foreach (var row in rows)
        {
            if (!seen.Add(row.Date))
            {
                diagnostics.Add(new("SM_SCHEDULE_DUPLICATE_DATE", row.DatePath,
                    "Schedule dates must be unique; same-date factors or coupon steps cannot be resolved by input order."));
            }
        }
        if (diagnostics.Count > 0)
        {
            return Invalid<T>(path, diagnostics);
        }

        var ordered = rows.OrderBy(static row => row.Date).ToArray();
        if (kind == SecurityEconomicScheduleKind.Factor)
        {
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i].Value > ordered[i - 1].Value)
                {
                    diagnostics.Add(new("SM_SCHEDULE_FACTOR_INCREASE", ordered[i].ValuePath,
                        "Factors must be non-increasing in date order."));
                }
            }
        }
        return diagnostics.Count > 0
            ? Invalid<T>(path, diagnostics)
            : Accepted(path, ordered.Select(row => create(row.Date, row.Value)).ToArray(),
                rows.Select(row => create(row.Date, row.Value)).ToArray());
    }

    private static SecurityScheduleParseResult<T> Invalid<T>(string path, IReadOnlyList<SecurityScheduleDiagnostic> diagnostics)
        => new(SecuritySchedulePresence.Invalid, [], diagnostics, path);

    private static SecurityScheduleParseResult<T> Accepted<T>(string path, IReadOnlyList<T> entries, IReadOnlyList<T> sourceEntries)
        => new(entries.Count == 0 ? SecuritySchedulePresence.Empty : SecuritySchedulePresence.Populated, entries, [], path)
        {
            SourceEntries = sourceEntries
        };

    private static bool InRange(SecurityEconomicScheduleKind kind, decimal value)
        => kind switch
        {
            SecurityEconomicScheduleKind.Factor => value is >= 0m and <= 1m,
            SecurityEconomicScheduleKind.Principal => value > 0m,
            _ => value >= 0m
        };

    private static DateOnly? ReadDate(JsonElement row, string[] aliases, string path,
        List<SecurityScheduleDiagnostic> diagnostics, out string memberPath)
    {
        if (!TryReadMember(row, aliases, path, out var property, out memberPath, out var duplicate))
        {
            diagnostics.Add(duplicate ? DuplicateMember(memberPath)
                : new("SM_SCHEDULE_DATE_REQUIRED", memberPath, "A schedule row requires a date."));
            return null;
        }
        if (property.Value.ValueKind == JsonValueKind.String)
        {
            var raw = property.Value.GetString();
            if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                return DateOnly.FromDateTime(timestamp.UtcDateTime.Date);
        }
        diagnostics.Add(new("SM_SCHEDULE_DATE_INVALID", memberPath, "The schedule date must be a parseable date or timestamp."));
        return null;
    }

    private static decimal? ReadDecimal(JsonElement row, string[] aliases, string path,
        List<SecurityScheduleDiagnostic> diagnostics, out string memberPath)
    {
        if (!TryReadMember(row, aliases, path, out var property, out memberPath, out var duplicate))
        {
            diagnostics.Add(duplicate ? DuplicateMember(memberPath)
                : new("SM_SCHEDULE_VALUE_REQUIRED", memberPath, "A schedule row requires a numeric value."));
            return null;
        }
        if (SecurityScheduleDecimal.TryRead(property.Value, out var value))
            return value;
        diagnostics.Add(new("SM_SCHEDULE_VALUE_INVALID", memberPath,
            "The schedule value must be a number or invariant decimal string exactly representable as a decimal."));
        return null;
    }

    private static bool TryReadMember(JsonElement row, string[] aliases, string path,
        out JsonProperty property, out string memberPath, out bool duplicate)
    {
        duplicate = false;
        string? unsetMemberPath = null;
        foreach (var alias in aliases)
        {
            var properties = GetProperties(row, alias);
            if (properties.Count == 0)
            {
                continue;
            }
            if (properties.Count > 1)
            {
                property = default;
                memberPath = $"{path}.{properties[1].Name}";
                duplicate = true;
                return false;
            }
            property = properties[0];
            if (property.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                memberPath = $"{path}.{property.Name}";
                return true;
            }
            unsetMemberPath ??= $"{path}.{property.Name}";
        }
        property = default;
        memberPath = unsetMemberPath ?? $"{path}.{aliases[0]}";
        return false;
    }

    private static SecurityScheduleDiagnostic DuplicateMember(string path)
        => new("SM_SCHEDULE_MEMBER_DUPLICATE", path,
            "Repeated JSON members with the same case-insensitive name are ambiguous; keep one spelling.");

    private static string? DuplicateProfilePath(
        IReadOnlyList<JsonProperty> profiles, SecurityEconomicScheduleKind kind, string[] keys)
    {
        if (profiles.Count < 2)
            return null;
        foreach (var profile in profiles)
        {
            if (profile.Value.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var member in profile.Value.EnumerateObject())
            {
                if (!keys.Contains(member.Name, StringComparer.OrdinalIgnoreCase)
                    || member.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    || (kind == SecurityEconomicScheduleKind.Factor
                        && member.Name.Equals("factorSchedule", StringComparison.OrdinalIgnoreCase)
                        && member.Value.ValueKind == JsonValueKind.String))
                {
                    continue;
                }
                // Duplicate profile envelopes only affect this boundary when one actually asserts
                // its schedule kind. Unrelated profile fields and evidence references stay outside it.
                return $"assetSpecificTerms.{profiles[1].Name}";
            }
        }
        return null;
    }

    private static IReadOnlyList<JsonProperty> GetProperties(JsonElement value, string key)
    {
        var matches = new List<JsonProperty>();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(property);
                }
            }
        }
        return matches;
    }
}
