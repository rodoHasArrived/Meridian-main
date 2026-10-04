using System.Globalization;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Versioned schemas for the JSON payload carried by <see cref="CorporateActionDto"/>. Unknown
/// properties remain allowed so a provider can retain evidence ahead of a Meridian release, while
/// known properties are type-checked and acceptance-required properties fail closed.
/// </summary>
public static class CorporateActionPayloadSchemaCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CorporateActionPayloadFieldDescriptor>> FieldsByType =
        new Dictionary<string, IReadOnlyList<CorporateActionPayloadFieldDescriptor>>(StringComparer.Ordinal)
        {
            [CorporateActionEventTypes.TenderOffer] =
            [
                Decimal(CorporateActionPayloads.OfferPricePerShare),
                Date(CorporateActionPayloads.OfferExpiryDate),
                Boolean(CorporateActionPayloads.IsPartialTender),
                Boolean(CorporateActionPayloads.IsVoluntary),
            ],
            [CorporateActionEventTypes.ReturnOfCapital] =
            [
                Decimal(CorporateActionPayloads.CapitalReturnPerShare),
            ],
            [CorporateActionEventTypes.PrincipalPaydown] =
            [
                Decimal(CorporateActionPayloads.PrincipalAmount),
                Decimal(CorporateActionPayloads.PostPaydownFactor),
            ],
            [CorporateActionEventTypes.CryptoFork] =
            [
                String(CorporateActionPayloads.ForkNewAssetSymbol),
                Decimal(CorporateActionPayloads.ForkRatio),
            ],
            [CorporateActionEventTypes.OptionContractAdjustment] =
            [
                Decimal(CorporateActionPayloads.AdjustedMultiplier),
                Decimal(CorporateActionPayloads.AdjustedStrike),
                String(CorporateActionPayloads.AdjustedDeliverable),
            ],
            [CorporateActionEventTypes.Delisting] =
            [
                Date(CorporateActionPayloads.FinalTradingDate),
                String(CorporateActionPayloads.DelistingReason),
            ],
            [CorporateActionEventTypes.BondCall] =
            [
                Boolean(CorporateActionPayloads.IsPartialRedemption),
                Boolean(CorporateActionPayloads.IsMakeWhole),
            ],
            [CorporateActionEventTypes.MergerAbsorption] =
            [
                Decimal(CorporateActionPayloads.CashConsiderationPerUnit),
            ],
            [CorporateActionEventTypes.Rule144ARegSExchange] =
            [
                Decimal(CorporateActionPayloads.CashConsiderationPerUnit),
            ],
            [CorporateActionEventTypes.AdvanceRefunding] =
            [
                Guid(CorporateActionPayloads.RefundedSecurityId, requiredForAcceptance: true),
                Guid(CorporateActionPayloads.UnrefundedSecurityId, requiredForAcceptance: true),
                Decimal(CorporateActionPayloads.RefundedAllocationRatio, requiredForAcceptance: true),
            ],
            [CorporateActionEventTypes.BankruptcyDistribution] =
            [
                String(CorporateActionPayloads.BankruptcyProcessingMethod),
                Decimal(CorporateActionPayloads.CashAmount),
                Guid(CorporateActionPayloads.EscrowSecurityId),
                Guid(CorporateActionPayloads.SuccessorSecurityId),
            ],
            [CorporateActionEventTypes.ConsentSolicitation] =
            [
                Decimal(CorporateActionPayloads.ConsentPaymentPerUnit),
            ],
            [CorporateActionEventTypes.Conversion] =
            [
                Decimal(CorporateActionPayloads.CashConsiderationPerUnit),
            ],
            [CorporateActionEventTypes.ExchangeOffer] =
            [
                Decimal(CorporateActionPayloads.OfferPricePerShare),
                Date(CorporateActionPayloads.OfferExpiryDate),
                Decimal(CorporateActionPayloads.CashConsiderationPerUnit),
                Boolean(CorporateActionPayloads.IsVoluntary),
            ],
            [CorporateActionEventTypes.FractionalUnitsCashInLieu] =
            [
                Decimal(CorporateActionPayloads.FractionalUnits, requiredForAcceptance: true),
                Decimal(CorporateActionPayloads.CashAmount, requiredForAcceptance: true),
            ],
            [CorporateActionEventTypes.PaymentInKind] =
            [
                Decimal(CorporateActionPayloads.PikRate, requiredForAcceptance: true),
                Decimal(CorporateActionPayloads.PikIssuePrice),
                Guid(CorporateActionPayloads.SuccessorSecurityId),
            ],
            [CorporateActionEventTypes.PutRedemption] =
            [
                Decimal(CorporateActionPayloads.CashConsiderationPerUnit),
            ],
            [CorporateActionEventTypes.RightsDistribution] =
            [
                Guid(CorporateActionPayloads.RightsSecurityId, requiredForAcceptance: true),
                Decimal(CorporateActionPayloads.RightsPerHeldUnit, requiredForAcceptance: true),
            ],
            [CorporateActionEventTypes.RightsExercise] =
            [
                Guid(CorporateActionPayloads.RightsSecurityId, requiredForAcceptance: true),
                Guid(CorporateActionPayloads.SuccessorSecurityId, requiredForAcceptance: true),
                Decimal(CorporateActionPayloads.UnitsPerRight, requiredForAcceptance: true),
                Decimal(CorporateActionPayloads.ExercisePricePerUnit, requiredForAcceptance: true),
            ],
        };

    /// <summary>Returns a v1 schema for every canonical event type, including an empty schema.</summary>
    public static IReadOnlyList<CorporateActionPayloadSchemaDescriptor> All =>
        CorporateActionTypeDescriptorCatalog.All
            .Select(static descriptor => Get(descriptor.CanonicalName))
            .ToArray();

    public static CorporateActionPayloadSchemaDescriptor Get(string eventType)
    {
        if (!CorporateActionTypeDescriptorCatalog.TryNormalize(eventType, out var descriptor))
        {
            throw new ArgumentException($"Unsupported corporate action EventType '{eventType}'.", nameof(eventType));
        }

        return new CorporateActionPayloadSchemaDescriptor(
            descriptor.CanonicalName,
            CorporateActionPayloads.CurrentSchemaVersion,
            FieldsByType.GetValueOrDefault(descriptor.CanonicalName) ?? []);
    }

    /// <summary>
    /// Validates schema version, payload object shape, known field types, and optionally fields
    /// required before an observation can be accepted as a canonical source event.
    /// </summary>
    public static string? Validate(CorporateActionDto action, bool forAcceptance)
    {
        ArgumentNullException.ThrowIfNull(action);

        CorporateActionPayloadSchemaDescriptor schema;
        try
        {
            schema = Get(action.EventType);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        if (action.PayloadSchemaVersion != schema.Version)
        {
            return $"Corporate action payload schema version {action.PayloadSchemaVersion} is not supported for " +
                   $"{schema.EventType}; supported version: {schema.Version}.";
        }

        if (action.Payload is { ValueKind: JsonValueKind.Undefined })
        {
            return "Corporate action Payload must be a JSON object when supplied.";
        }

        if (action.Payload is { ValueKind: not JsonValueKind.Object })
        {
            return "Corporate action Payload must be a JSON object when supplied.";
        }

        foreach (var field in schema.Fields)
        {
            if (action.Payload is not { ValueKind: JsonValueKind.Object } payload
                || !payload.TryGetProperty(field.Key, out var value)
                || value.ValueKind == JsonValueKind.Null)
            {
                if (forAcceptance && field.RequiredForAcceptance)
                {
                    return $"{schema.EventType} payload schema v{schema.Version} requires '{field.Key}' before acceptance.";
                }

                continue;
            }

            if (!Matches(value, field.ValueType))
            {
                return $"{schema.EventType} payload field '{field.Key}' must be a {Describe(field.ValueType)} value.";
            }
        }

        return null;
    }

    private static bool Matches(JsonElement value, CorporateActionPayloadValueType type)
        => type switch
        {
            CorporateActionPayloadValueType.String =>
                value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
            CorporateActionPayloadValueType.Decimal =>
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _)
                || value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            CorporateActionPayloadValueType.Boolean =>
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out _),
            CorporateActionPayloadValueType.Date =>
                value.ValueKind == JsonValueKind.String
                && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out _),
            CorporateActionPayloadValueType.Guid =>
                value.ValueKind == JsonValueKind.String && System.Guid.TryParse(value.GetString(), out _),
            _ => false,
        };

    private static string Describe(CorporateActionPayloadValueType type) => type switch
    {
        CorporateActionPayloadValueType.Guid => "UUID",
        _ => type.ToString().ToLowerInvariant(),
    };

    private static CorporateActionPayloadFieldDescriptor String(string key, bool requiredForAcceptance = false) =>
        new(key, CorporateActionPayloadValueType.String, requiredForAcceptance);

    private static CorporateActionPayloadFieldDescriptor Decimal(string key, bool requiredForAcceptance = false) =>
        new(key, CorporateActionPayloadValueType.Decimal, requiredForAcceptance);

    private static CorporateActionPayloadFieldDescriptor Boolean(string key, bool requiredForAcceptance = false) =>
        new(key, CorporateActionPayloadValueType.Boolean, requiredForAcceptance);

    private static CorporateActionPayloadFieldDescriptor Date(string key, bool requiredForAcceptance = false) =>
        new(key, CorporateActionPayloadValueType.Date, requiredForAcceptance);

    private static CorporateActionPayloadFieldDescriptor Guid(string key, bool requiredForAcceptance = false) =>
        new(key, CorporateActionPayloadValueType.Guid, requiredForAcceptance);
}

public sealed record CorporateActionPayloadSchemaDescriptor(
    string EventType,
    int Version,
    IReadOnlyList<CorporateActionPayloadFieldDescriptor> Fields);

public sealed record CorporateActionPayloadFieldDescriptor(
    string Key,
    CorporateActionPayloadValueType ValueType,
    bool RequiredForAcceptance);

public enum CorporateActionPayloadValueType
{
    String,
    Decimal,
    Boolean,
    Date,
    Guid,
}
