using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;

namespace Meridian.Contracts.SecurityMaster;

// The schedule-terms rule gaps found after declaring writes went live: E4, E7, E9a/b, E10d, C1a and C5c,
// and the strict-walk gaps (canonical and unrepeated calendar ids, member names in unset containers (R30),
// blank values, a periodic exerciseFrequency).
// Each applies only to a container introduced or changed by the write (DeclarationChanges): a stored row
// that predates a rule stays amendable without an unrelated repair, and an explicit replacement of the
// container states it again and is judged in full.
public static partial class ScheduleTermsWriteRules
{
    private const string IssueDateKey = "issueDate";
    private const string NonCallPeriodEndKey = "nonCallPeriodEnd";

    private static readonly JsonElement AbsentValue = JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>Which gap rules judge one schedule: its own entries when changed, C1a also when the issue date changed.</summary>
    private readonly record struct GapScope(bool Entries, bool IssueChanged, DateOnly? Issue, IReadOnlySet<string> RetainedNames);

    // R30 names-only check of an unset block or entry. Unset values never count as a change, so this runs
    // on every write; a member name the stored container already holds (in any entry) is kept, so a stored
    // row stays amendable, while any other undeclared, case-variant or repeated name is refused.
    private static void ValidateUnsetMemberNames(JsonElement value, IReadOnlyList<SecurityAssetTermField> fields, string path,
        IReadOnlySet<string> retainedNames)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)
                || !fields.Any(field => field.Key == property.Name) && !retainedNames.Contains(property.Name))
                Refuse($"Unknown, case-variant or repeated member '{path}.{property.Name}'.");
    }

    // The member names the retained (stored) container holds: the block's, or every object entry's.
    private static IReadOnlySet<string> RetainedMemberNames(JsonElement? retained, string key)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (retained is not { ValueKind: JsonValueKind.Object } stored || !stored.TryGetProperty(key, out var container))
            return names;
        var holders = container.ValueKind == JsonValueKind.Array ? container.EnumerateArray().ToArray() : [container];
        foreach (var holder in holders.Where(holder => holder.ValueKind == JsonValueKind.Object))
            foreach (var property in holder.EnumerateObject())
                names.Add(property.Name);
        return names;
    }

    /// <summary>
    /// Whether a root key's value differs from the terms the write replaces, as terms: unset members and
    /// entries (<see cref="SecurityAssetTermsSchema.IsPopulatedTermValue"/> false) are absent, member order
    /// is not significant and numbers compare by value, so the codec's frozen re-serialization of a stored
    /// declaration is the same declaration. A write that names no retained terms (a create) changes every key.
    /// </summary>
    private readonly struct DeclarationChanges(JsonElement payload, JsonElement? retained)
    {
        public bool Changed(string key)
            => retained is not { ValueKind: JsonValueKind.Object } stored
               || !SameTermValue(ValueOrAbsent(payload, key), ValueOrAbsent(stored, key));

        private static JsonElement ValueOrAbsent(JsonElement source, string key)
            => source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) ? value : AbsentValue;
    }

    private static bool SameTermValue(JsonElement left, JsonElement right)
    {
        var leftSet = SecurityAssetTermsSchema.IsPopulatedTermValue(left);
        var rightSet = SecurityAssetTermsSchema.IsPopulatedTermValue(right);
        if (!leftSet || !rightSet)
            return leftSet == rightSet;
        if (left.ValueKind != right.ValueKind)
            return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftMembers = PopulatedMembers(left);
                var rightMembers = PopulatedMembers(right);
                return leftMembers.Count == rightMembers.Count
                       && leftMembers.All(member => rightMembers.TryGetValue(member.Key, out var other) && SameTermValue(member.Value, other));
            case JsonValueKind.Array:
                var leftItems = left.EnumerateArray().Where(SecurityAssetTermsSchema.IsPopulatedTermValue).ToArray();
                var rightItems = right.EnumerateArray().Where(SecurityAssetTermsSchema.IsPopulatedTermValue).ToArray();
                return leftItems.Length == rightItems.Length && leftItems.Zip(rightItems).All(pair => SameTermValue(pair.First, pair.Second));
            case JsonValueKind.Number:
                return left.TryGetDecimal(out var leftNumber) && right.TryGetDecimal(out var rightNumber)
                    ? leftNumber == rightNumber
                    : string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            default:
                return true; // true/false: the kinds already agree.
        }
    }

    // Repeated names cannot reach here on a write (the walker refuses them first); the last one wins, as in jsonb.
    private static Dictionary<string, JsonElement> PopulatedMembers(JsonElement value)
    {
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in value.EnumerateObject())
            if (SecurityAssetTermsSchema.IsPopulatedTermValue(member.Value))
                members[member.Name] = member.Value;
        return members;
    }

    // A root date the schema declares under exactly this key, read only when written yyyy-MM-dd: a
    // timestamp or culture-specific spelling is the root validators' to judge, never a reason to refuse here.
    private static DateOnly? RootAnchorDate(string assetClass, JsonElement payload, string key)
        => SecurityAssetTermsSchema.Field(assetClass, key) is { Type: SecurityAssetTermFieldType.Date } field
           && string.Equals(field.Key, key, StringComparison.Ordinal)
           && payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
           && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string? DescribeEntryGap(JsonElement row, string path, bool call, DateOnly? end, string style,
        string? frequency, string? noticeType, int? minimum, int? maximum)
    {
        // E4: a frequency only on a Bermudan entry.
        if (frequency is not null && style != "Bermudan")
            return $"Declared {path} exerciseFrequency '{frequency}' applies only to a Bermudan entry, and its exerciseStyle is '{style}'.";

        // E7: a European entry exercises on its start date only.
        if (style == "European" && end is not null)
            return $"Declared {path} is European, which exercises on its start date only, so it cannot declare a {(call ? "callEndDate" : "putEndDate")}.";

        // E9: a make-whole spread only on a make-whole entry, and never negative.
        if (call && Member(row, "makeWholeSpreadBps") is { ValueKind: JsonValueKind.Number } spreadValue && spreadValue.TryGetDecimal(out var spread))
        {
            var priceType = Text(row, "priceType");
            if (priceType != "MakeWhole")
                return priceType is not null
                    ? $"Declared {path} makeWholeSpreadBps applies only to a priceType 'MakeWhole' entry; its priceType is '{priceType}'."
                    : $"Declared {path} makeWholeSpreadBps applies only to a priceType 'MakeWhole' entry; it declares no priceType, which means Fixed.";
            if (spread < 0m)
                return string.Create(CultureInfo.InvariantCulture, $"Declared {path} makeWholeSpreadBps {spread} must be zero or more.");
        }

        // E10d: a notice day type has no effect without counts.
        if (minimum is null && maximum is null && noticeType is not null)
            return $"Declared {path} noticeDayType '{noticeType}' has no effect without minimumNoticeDays or maximumNoticeDays.";

        return null;
    }

    // C1a: a window opens on or after issue.
    private static string DescribeWindowBeforeIssue(string path, string startKey, DateOnly start, DateOnly issue)
        => $"Declared {path} {startKey} {Format(start)} is before the {IssueDateKey} {Format(issue)}; an exercise window cannot open before issue.";

    // C5c: no call may open before the non-call period ends.
    private static string DescribeCallBeforeNonCallPeriodEnd(DateOnly earliest, DateOnly nonCallPeriodEnd)
        => $"Declared {SecurityVersionedTerms.CallScheduleKey} opens its earliest call on {Format(earliest)}, before the {NonCallPeriodEndKey} {Format(nonCallPeriodEnd)}; " +
           "no call may open before the non-call period ends.";

    // A declared exercise frequency recurs a whole number of months apart.
    private static bool IsPeriodicExerciseFrequency(string frequency)
        => SecurityTermConventions.TryParsePaymentFrequency(frequency, out var cadence) && cadence.MonthsPerPeriod() is not null;

    private static string DescribeNotPeriodic(string assetClass, string path, string frequency)
        => $"Value '{frequency}' is not a periodic exercise frequency for '{path}' on asset class '{assetClass}': exercise dates recur a whole number of months apart. " +
           $"Periodic values: {string.Join(", ", SecurityTermConventions.CanonicalValues(SecurityTermConventionKind.CouponFrequency).Where(IsPeriodicExerciseFrequency))}.";

    // Declared paymentCalendars name canonical calendar ids (the grammar calendar files use), each once.
    private static string? DescribeCalendarIdFault(JsonElement ids)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in ids.EnumerateArray())
        {
            var path = $"{SecurityVersionedTerms.ScheduleConventionsKey}.paymentCalendars[{index++}]";
            var id = item.GetString();
            if (!BusinessDayCalendars.IsCanonicalCalendarId(id))
                return $"Property '{path}' must be a canonical calendar id ({BusinessDayCalendars.CanonicalCalendarIdDescription}), but was '{id}'.";
            if (!named.Add(id))
                return $"Property '{path}' names the calendar '{id}' again; name each calendar once.";
        }
        return null;
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
