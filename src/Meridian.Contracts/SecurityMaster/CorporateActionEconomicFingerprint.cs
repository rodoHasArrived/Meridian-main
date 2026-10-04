using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Computes a provider-neutral SHA-256 fingerprint for the economic terms of a corporate action.
/// Source/provider identity, workflow state, actor, and append identity are deliberately excluded;
/// retries and independent providers describing the same economics therefore converge.
/// </summary>
public static class CorporateActionEconomicFingerprint
{
    public static string Compute(CorporateActionDto action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var canonical = new StringBuilder(512);
        Append(canonical, "securityId", action.SecurityId.ToString("N"));
        Append(canonical, "eventType", CorporateActionEventTypes.Normalize(action.EventType));
        Append(canonical, "exDate", Format(action.ExDate));
        Append(canonical, "recordDate", Format(action.RecordDate));
        Append(canonical, "payDate", Format(action.PayDate));
        Append(canonical, "dividendPerShare", Format(action.DividendPerShare));
        Append(canonical, "currency", Normalize(action.Currency));
        Append(canonical, "splitRatio", Format(action.SplitRatio));
        Append(canonical, "newSecurityId", Format(action.NewSecurityId));
        Append(canonical, "distributionRatio", Format(action.DistributionRatio));
        Append(canonical, "acquirerSecurityId", Format(action.AcquirerSecurityId));
        Append(canonical, "exchangeRatio", Format(action.ExchangeRatio));
        Append(canonical, "subscriptionPricePerShare", Format(action.SubscriptionPricePerShare));
        Append(canonical, "rightsPerShare", Format(action.RightsPerShare));
        Append(canonical, "redemptionPricePercentOfPar", Format(action.RedemptionPricePercentOfPar));
        Append(canonical, "payloadSchemaVersion", action.PayloadSchemaVersion.ToString(CultureInfo.InvariantCulture));
        Append(canonical, "payload", CanonicalizePayload(action.EventType, action.Payload));
        return Sha256Digest.ComputeUtf8(canonical.ToString());
    }

    private static void Append(StringBuilder builder, string key, string? value)
    {
        var normalized = value ?? string.Empty;
        builder.Append(key.Length).Append(':').Append(key).Append('=')
            .Append(normalized.Length).Append(':').Append(normalized).Append(';');
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Format(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Format(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture);

    private static string? Format(Guid? value) => value?.ToString("N");

    private static string CanonicalizePayload(string eventType, JsonElement? payload)
    {
        if (payload is not { ValueKind: not JsonValueKind.Undefined } element)
        {
            return string.Empty;
        }

        var buffer = new ArrayBufferWriter<byte>();
        var typedFields = CorporateActionPayloadSchemaCatalog.Get(eventType).Fields
            .ToDictionary(static field => field.Key, static field => field.ValueType, StringComparer.Ordinal);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, element, typedFields, isPayloadRoot: true);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonElement element,
        IReadOnlyDictionary<string, CorporateActionPayloadValueType>? typedFields = null,
        bool isPayloadRoot = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    if (isPayloadRoot
                        && typedFields is not null
                        && typedFields.TryGetValue(property.Name, out var valueType)
                        && TryWriteTypedPayloadValue(writer, property.Value, valueType))
                    {
                        continue;
                    }

                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number when element.TryGetDecimal(out var decimalValue):
                writer.WriteRawValue(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static bool TryWriteTypedPayloadValue(
        Utf8JsonWriter writer,
        JsonElement value,
        CorporateActionPayloadValueType valueType)
    {
        switch (valueType)
        {
            case CorporateActionPayloadValueType.Decimal:
                var parsedDecimal = value.ValueKind == JsonValueKind.Number
                    && value.TryGetDecimal(out var numericDecimal)
                        ? numericDecimal
                        : value.ValueKind == JsonValueKind.String
                          && decimal.TryParse(
                              value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture,
                              out var stringDecimal)
                            ? stringDecimal
                            : (decimal?)null;
                if (parsedDecimal is { } decimalValue)
                {
                    writer.WriteRawValue(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
                    return true;
                }
                break;
            case CorporateActionPayloadValueType.Boolean:
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    writer.WriteBooleanValue(value.GetBoolean());
                    return true;
                }
                if (value.ValueKind == JsonValueKind.String
                    && bool.TryParse(value.GetString(), out var boolValue))
                {
                    writer.WriteBooleanValue(boolValue);
                    return true;
                }
                break;
            case CorporateActionPayloadValueType.Date:
                if (value.ValueKind == JsonValueKind.String
                    && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var dateValue))
                {
                    writer.WriteStringValue(dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    return true;
                }
                break;
            case CorporateActionPayloadValueType.Guid:
                if (value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(value.GetString(), out var guidValue))
                {
                    writer.WriteStringValue(guidValue.ToString("N"));
                    return true;
                }
                break;
        }

        return false;
    }
}
