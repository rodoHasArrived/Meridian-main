using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

internal static partial class SecurityMasterConflictDetection
{
    private static bool IsVersionedContainerPath(string path)
        => path is "EconomicTerms.scheduleConventions" or "EconomicTerms.callSchedule" or "EconomicTerms.putSchedule"
           || (path.StartsWith("EconomicTerms.legs.", StringComparison.Ordinal)
               && path.EndsWith(".scheduleConventions", StringComparison.Ordinal)
               && int.TryParse(path["EconomicTerms.legs.".Length..^".scheduleConventions".Length], NumberStyles.None, CultureInfo.InvariantCulture, out _));

    private static IReadOnlyDictionary<string, string?> ReadVersionedContainerValues(SecurityDetailDto detail)
    {
        var assetClass = SecurityAssetClassCatalog.GetOrDefault(detail.AssetClass).AssetClass;
        var terms = SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, detail.AssetSpecificTerms);
        var decoding = SecurityVersionedTerms.Decoding(assetClass, terms, writeMode: false);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (decoding.ProfileGoverned || terms.ValueKind != JsonValueKind.Object)
            return values;
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (SecurityVersionedTerms.IsVersionedContainer(field))
            {
                if (decoding.Decodes(field) && terms.TryGetProperty(field.Key, out var value))
                    values[$"EconomicTerms.{field.Key}"] = NormalizeVersionedContainer(assetClass, field.Key, value);
                continue;
            }
            if (field.Type != SecurityAssetTermFieldType.Array || !terms.TryGetProperty(field.Key, out var array)
                || array.ValueKind != JsonValueKind.Array)
                continue;
            var nested = SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)
                .Where(member => SecurityVersionedTerms.IsVersionedContainer(member) && decoding.Decodes(member)).ToArray();
            var index = 0;
            foreach (var row in array.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object)
                    foreach (var member in nested)
                        if (row.TryGetProperty(member.Key, out var block))
                            values[$"EconomicTerms.{field.Key}.{index}.{member.Key}"] = NormalizeVersionedContainer(assetClass, $"{field.Key}.{member.Key}", block);
                index++;
            }
        }
        return values;
    }

    private static string? NormalizeVersionedConflictText(string path, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var key = path[(path.LastIndexOf('.') + 1)..];
            return NormalizeVersionedContainer("Bond", key, document.RootElement);
        }
        catch (JsonException)
        {
            // An unresolved foreign value keeps its identity; it must never become absence.
            return text;
        }
    }

    private static string? NormalizeVersionedContainer(string assetClass, string schemaPath, JsonElement value)
    {
        var fields = SecurityAssetTermsSchema.ElementFields(assetClass, schemaPath);
        if (!SecurityAssetTermsSchema.IsPopulatedTermValue(value) || !Meaningful(value, fields))
            return null;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteComparable(writer, value, fields, schemaPath[(schemaPath.LastIndexOf('.') + 1)..]);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool Meaningful(JsonElement value, IReadOnlyList<SecurityAssetTermField> fields)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.Object => value.EnumerateObject().Any(property =>
                fields.FirstOrDefault(field => field.Key == property.Name) is not { } field || !KnownUnset(field, property.Value)),
            JsonValueKind.Array => value.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object || Meaningful(row, fields)),
            _ => true
        };

    private static bool KnownUnset(SecurityAssetTermField field, JsonElement value)
        => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
           || (field.Type == SecurityAssetTermFieldType.Array && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

    private static void WriteComparable(Utf8JsonWriter writer, JsonElement value,
        IReadOnlyList<SecurityAssetTermField> fields, string containerKey, SecurityAssetTermField? declared = null)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                var field = fields.FirstOrDefault(field => field.Key == property.Name || field.Aliases.Contains(property.Name, StringComparer.Ordinal));
                if (field is not null && KnownUnset(field, property.Value))
                    continue;
                writer.WritePropertyName(field?.Key ?? property.Name);
                WriteComparable(writer, property.Value, [], property.Name, field);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var rows = value.EnumerateArray().ToArray();
            if (containerKey is SecurityVersionedTerms.CallScheduleKey or SecurityVersionedTerms.PutScheduleKey)
            {
                rows = rows.Where(row => row.ValueKind != JsonValueKind.Null
                    && (row.ValueKind != JsonValueKind.Object
                        || SecurityAssetTermsSchema.IsPopulatedTermValue(row) && Meaningful(row, fields))).ToArray();
                var dateKey = containerKey == SecurityVersionedTerms.CallScheduleKey ? "callDate" : "putDate";
                var dated = rows.Select(row => (Row: row, Date: ComparableStart(row, dateKey))).ToArray();
                if (dated.All(row => row.Date is not null) && dated.Select(row => row.Date).Distinct().Count() == dated.Length)
                    rows = dated.OrderBy(row => row.Date).Select(row => row.Row).ToArray();
            }
            else if (containerKey == "paymentCalendars" && rows.All(row => row.ValueKind == JsonValueKind.String))
                rows = rows.GroupBy(row => row.GetString(), StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => group.First()).ToArray();
            writer.WriteStartArray();
            foreach (var row in rows)
                WriteComparable(writer, row, fields, "");
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
        else if (value.ValueKind == JsonValueKind.String && declared is not null)
        {
            var text = value.GetString()!;
            if (declared.Type == SecurityAssetTermFieldType.Date && DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var date))
                text = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (declared.Convention == SecurityTermConventionKind.BusinessDayConvention && SecurityTermConventions.TryParseBusinessDayConvention(text, out var convention))
                text = convention.ToString();
            else if (declared.Convention == SecurityTermConventionKind.RollConvention && SecurityTermConventions.TryParseRollConvention(text, out var roll))
                text = roll.ToString();
            else if (declared.Convention == SecurityTermConventionKind.StubPolicy && SecurityTermConventions.TryParseStubPolicy(text, out var stub))
                text = stub.ToString();
            else if (declared.Convention is SecurityTermConventionKind.CouponFrequency or SecurityTermConventionKind.PaymentFrequency
                     && SecurityTermConventions.TryParsePaymentFrequency(text, out var frequency))
                text = frequency.ToString();
            else
                text = declared.AllowedValues.FirstOrDefault(candidate => candidate.Equals(text, StringComparison.OrdinalIgnoreCase)) ?? text;
            writer.WriteStringValue(text);
        }
        else
            value.WriteTo(writer);
    }

    private static DateOnly? ComparableStart(JsonElement row, string key)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var date) ? date : null;
}
