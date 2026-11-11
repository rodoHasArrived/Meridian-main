using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;

namespace Meridian.Contracts.SecurityMaster;

public sealed partial class SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
{
    private static bool HasExactScheduleField(JsonElement? module, string key)
        => module is { } value && value.TryGetProperty(key, out _);

    private static JsonElement? ReadExactDeclaredSchedule(JsonElement economic, string assetClass)
    {
        var declarations = economic.EnumerateObject().Where(static field => field.NameEquals("declaredSchedule")).ToArray();
        if (declarations.Length > 1)
            throw new InvalidOperationException("Repeated economic module declaredSchedule.");
        if (declarations.Length == 0 || declarations[0].Value.ValueKind == JsonValueKind.Null)
            return null;
        var module = declarations[0].Value;
        if (module.ValueKind != JsonValueKind.Object || assetClass is not ("Bond" or "CertificateOfDeposit" or "StructuredCredit"))
            throw new InvalidOperationException($"{assetClass} cannot recover declaredSchedule.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in module.EnumerateObject())
        {
            if (!names.Add(field.Name) || field.Name is not (SecurityVersionedTerms.ScheduleConventionsKey or SecurityVersionedTerms.CallScheduleKey or SecurityVersionedTerms.PutScheduleKey))
                throw new InvalidOperationException($"Unknown or repeated declaredSchedule field '{field.Name}'.");
            if (field.Name == SecurityVersionedTerms.ScheduleConventionsKey)
            {
                if (field.Value.ValueKind != JsonValueKind.Null)
                    RequireCompleteScheduleObject(field.Value, assetClass, field.Name);
            }
            else
            {
                if (field.Value.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException($"declaredSchedule.{field.Name} must be an array.");
                foreach (var entry in field.Value.EnumerateArray())
                    RequireCompleteScheduleObject(entry, assetClass, field.Name);
            }
        }
        if (!names.Contains(SecurityVersionedTerms.ScheduleConventionsKey) || names.Contains(SecurityVersionedTerms.CallScheduleKey) != names.Contains(SecurityVersionedTerms.PutScheduleKey))
            throw new InvalidOperationException("A declaredSchedule must carry its convention block and either both exercise arrays or neither.");

        // The old modules are summaries of the same source. Refuse contradictory populated
        // summaries, rather than letting different readers choose different dates or conventions.
        if (module.TryGetProperty(SecurityVersionedTerms.ScheduleConventionsKey, out var block) && ReadScheduleSummary(economic, "accrual") is { } accrual)
        {
            if (accrual.TryGetProperty("businessDayConvention", out var oldConvention) && oldConvention.ValueKind != JsonValueKind.Null
                && (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("businessDayConvention", out var exactConvention)
                    || !JsonElement.DeepEquals(oldConvention, exactConvention)))
                throw new InvalidOperationException("declaredSchedule contradicts accrual.businessDayConvention.");
            if (accrual.TryGetProperty("holidayCalendar", out var oldCalendar) && oldCalendar.ValueKind != JsonValueKind.Null)
            {
                var calendars = block.ValueKind == JsonValueKind.Object && block.TryGetProperty("paymentCalendars", out var ids)
                    ? BusinessDayCalendars.JointCalendarId(ids.EnumerateArray().Select(static id => id.GetString()))
                    : null;
                if (oldCalendar.ValueKind != JsonValueKind.String
                    || !string.Equals(oldCalendar.GetString(), calendars, StringComparison.Ordinal))
                    throw new InvalidOperationException("declaredSchedule contradicts accrual.holidayCalendar.");
            }
        }
        if (ReadScheduleSummary(economic, "call") is { } call)
        {
            RequireExerciseSummaryAgreement(module, call, SecurityVersionedTerms.CallScheduleKey, "callDate", "callPrice", "callDt", "callPx");
            RequireExerciseSummaryAgreement(module, call, SecurityVersionedTerms.PutScheduleKey, "putDate", "putPrice", "putDt", "putPx");
            if (module.TryGetProperty(SecurityVersionedTerms.PutScheduleKey, out var puts) && call.TryGetProperty("isPuttable", out var puttable)
                && puttable.ValueKind != JsonValueKind.Null
                && (puttable.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || puttable.GetBoolean() != (puts.GetArrayLength() > 0)))
                throw new InvalidOperationException("declaredSchedule contradicts call.isPuttable.");
            if (module.TryGetProperty(SecurityVersionedTerms.CallScheduleKey, out var calls) && call.TryGetProperty("makeWholeSpreadBps", out var spread)
                && spread.ValueKind != JsonValueKind.Null)
            {
                var expected = calls.EnumerateArray().OrderBy(static entry => entry.GetProperty("callDate").GetString(), StringComparer.Ordinal)
                    .Where(static entry => entry.GetProperty("priceType").GetString() == "MakeWhole")
                    .Select(static entry => entry.GetProperty("makeWholeSpreadBps"))
                    .FirstOrDefault(static value => value.ValueKind != JsonValueKind.Null);
                if (expected.ValueKind == JsonValueKind.Undefined || !JsonElement.DeepEquals(spread, expected))
                    throw new InvalidOperationException("declaredSchedule contradicts call.makeWholeSpreadBps.");
            }
        }
        return module;
    }

    private static JsonElement? ReadScheduleSummary(JsonElement economic, string name)
    {
        var declarations = economic.EnumerateObject().Where(field => field.NameEquals(name)).ToArray();
        if (declarations.Length > 1)
            throw new InvalidOperationException($"Repeated schedule summary module {name}.");
        if (declarations.Length == 0 || declarations[0].Value.ValueKind == JsonValueKind.Null)
            return null;
        if (declarations[0].Value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Schedule summary module {name} must be an object or null.");
        RequireUniqueSummaryMembers(declarations[0].Value, name);
        return declarations[0].Value;
    }

    private static void RequireUniqueSummaryMembers(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object
            || value.EnumerateObject().Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
            throw new InvalidOperationException($"Schedule summary {path} must be an object with unique members.");
    }

    private static void RequireCompleteScheduleObject(JsonElement value, string assetClass, string path)
    {
        var fields = SecurityAssetTermsSchema.ElementFields(assetClass, path);
        if (value.ValueKind != JsonValueKind.Object || fields.Count == 0)
            throw new InvalidOperationException($"declaredSchedule.{path} has an invalid object shape.");
        var members = value.EnumerateObject().ToArray();
        if (members.Length != fields.Count || members.Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw new InvalidOperationException($"declaredSchedule.{path} must carry every canonical member exactly once.");
        foreach (var field in fields)
        {
            if (!value.TryGetProperty(field.Key, out var member))
                throw new InvalidOperationException($"declaredSchedule.{path} is missing '{field.Key}'.");
            // The flat schema tolerates missing historical members. This exact module instead
            // claims the canonical typed source, whose primary dates and calendar array exist.
            var requiredByTypedSource = field.Type == SecurityAssetTermFieldType.Array
                || path == SecurityVersionedTerms.CallScheduleKey && field.Key == "callDate"
                || path == SecurityVersionedTerms.PutScheduleKey && field.Key == "putDate";
            if (member.ValueKind == JsonValueKind.Null && !field.Required && !requiredByTypedSource)
                continue;
            var valid = field.Type switch
            {
                SecurityAssetTermFieldType.String => member.ValueKind == JsonValueKind.String,
                SecurityAssetTermFieldType.Date => member.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(member.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                SecurityAssetTermFieldType.Decimal => member.ValueKind == JsonValueKind.Number && member.TryGetDecimal(out _),
                SecurityAssetTermFieldType.Integer => member.ValueKind == JsonValueKind.Number && member.TryGetInt32(out _),
                SecurityAssetTermFieldType.Boolean => member.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SecurityAssetTermFieldType.Array => member.ValueKind == JsonValueKind.Array && member.EnumerateArray().All(static item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())),
                _ => false
            };
            if (!valid)
                throw new InvalidOperationException($"declaredSchedule.{path}.{field.Key} has an invalid canonical value.");
        }
    }

    private static void RequireExerciseSummaryAgreement(JsonElement exact, JsonElement summary, string key,
        string exactDate, string exactPrice, string summaryDate, string summaryPrice)
    {
        if (!exact.TryGetProperty(key, out var rows) || !summary.TryGetProperty(key, out var oldRows) || oldRows.ValueKind == JsonValueKind.Null)
            return;
        if (oldRows.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"call.{key} must be an array.");
        foreach (var row in oldRows.EnumerateArray())
        {
            RequireUniqueSummaryMembers(row, $"call.{key}");
            if (!row.TryGetProperty(summaryDate, out var date) || date.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidOperationException($"Schedule summary call.{key}.{summaryDate} must be a canonical date.");
        }
        var source = rows.EnumerateArray().OrderBy(entry => entry.GetProperty(exactDate).GetString(), StringComparer.Ordinal).ToArray();
        var old = oldRows.EnumerateArray().OrderBy(entry => entry.GetProperty(summaryDate).GetString(), StringComparer.Ordinal).ToArray();
        if (source.Length != old.Length)
            throw new InvalidOperationException($"declaredSchedule contradicts call.{key}.");
        for (var index = 0; index < source.Length; index++)
        {
            var priceType = key == SecurityVersionedTerms.CallScheduleKey ? source[index].GetProperty("priceType").GetString() : null;
            var priceBasis = source[index].GetProperty("priceBasis").GetString();
            var carriesPrice = (priceType is null or "Fixed") && (priceBasis is null or "PercentOfPar");
            var expectedPrice = carriesPrice ? source[index].GetProperty(exactPrice) : default;
            if (!JsonElement.DeepEquals(source[index].GetProperty(exactDate), old[index].GetProperty(summaryDate))
                || (old[index].TryGetProperty(summaryPrice, out var amount) && amount.ValueKind != JsonValueKind.Null
                    && (!carriesPrice || !JsonElement.DeepEquals(amount, expectedPrice))))
                throw new InvalidOperationException($"declaredSchedule contradicts call.{key}.");
            if (key == SecurityVersionedTerms.CallScheduleKey)
            {
                var expectedType = priceType == "MakeWhole" ? "MakeWhole" : source[index].GetProperty("exerciseStyle").GetString();
                if (old[index].TryGetProperty("callType", out var callType) && callType.ValueKind != JsonValueKind.Null
                    && (callType.ValueKind != JsonValueKind.String || !string.Equals(callType.GetString(), expectedType, StringComparison.Ordinal)))
                    throw new InvalidOperationException("declaredSchedule contradicts call.callSchedule.callType.");
                var expectedPar = carriesPrice && expectedPrice.ValueKind == JsonValueKind.Number && expectedPrice.GetDecimal() == 100m;
                if (old[index].TryGetProperty("isParCall", out var par) && par.ValueKind != JsonValueKind.Null
                    && (par.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || par.GetBoolean() != expectedPar))
                    throw new InvalidOperationException("declaredSchedule contradicts call.callSchedule.isParCall.");
            }
        }
    }
}
