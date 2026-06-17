using System.Globalization;
using System.Text.Json.Nodes;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

internal static class ProviderIntegrationMappedRecordIdentity
{
    public static string? ResolveSourceRecordId(
        ProviderCapabilityKindDto capability,
        JsonObject mappedRecord)
    {
        ArgumentNullException.ThrowIfNull(mappedRecord);

        var explicitSourceId = ReadJsonString(mappedRecord, "sourceRecordId");
        if (!string.IsNullOrWhiteSpace(explicitSourceId))
        {
            return explicitSourceId;
        }

        return capability switch
        {
            ProviderCapabilityKindDto.Accounts => FirstNonBlank(
                ReadJsonString(mappedRecord, "providerAccountId"),
                ReadJsonString(mappedRecord, "internalAccountId")),

            ProviderCapabilityKindDto.Positions or ProviderCapabilityKindDto.Holdings => JoinIdentityParts(
                ReadJsonString(mappedRecord, "providerAccountId"),
                ReadBestSecurityIdentifier(mappedRecord),
                ReadJsonString(mappedRecord, "asOf")),

            ProviderCapabilityKindDto.Transactions => FirstNonBlank(
                ReadJsonString(mappedRecord, "providerTransactionId")),

            ProviderCapabilityKindDto.SecurityReferenceData => ReadBestSecurityIdentifier(mappedRecord),

            ProviderCapabilityKindDto.MarketPrices => JoinIdentityParts(
                ReadBestSecurityIdentifier(mappedRecord),
                ReadJsonString(mappedRecord, "asOf"),
                ReadJsonString(mappedRecord, "price.asOf")),

            ProviderCapabilityKindDto.Balances => JoinIdentityParts(
                ReadJsonString(mappedRecord, "providerAccountId"),
                ReadJsonString(mappedRecord, "asOf")),

            ProviderCapabilityKindDto.TaxLots => FirstNonBlank(
                ReadJsonString(mappedRecord, "providerTaxLotId"),
                ReadJsonString(mappedRecord, "providerLotId"),
                JoinIdentityParts(
                    ReadJsonString(mappedRecord, "providerAccountId"),
                    ReadBestSecurityIdentifier(mappedRecord),
                    ReadJsonString(mappedRecord, "acquisitionDate"))),

            ProviderCapabilityKindDto.CorporateActions => FirstNonBlank(
                ReadJsonString(mappedRecord, "providerCorporateActionId"),
                JoinIdentityParts(
                    ReadBestSecurityIdentifier(mappedRecord),
                    ReadJsonString(mappedRecord, "eventType"),
                    ReadJsonString(mappedRecord, "exDate"))),

            ProviderCapabilityKindDto.Documents => FirstNonBlank(
                ReadJsonString(mappedRecord, "providerDocumentId"),
                ReadJsonString(mappedRecord, "documentId")),

            ProviderCapabilityKindDto.Alerts or ProviderCapabilityKindDto.Events => FirstNonBlank(
                ReadJsonString(mappedRecord, "providerEventId"),
                ReadJsonString(mappedRecord, "eventId")),

            _ => null
        };
    }

    public static string BuildDedupeKey(
        string connectionId,
        ProviderCapabilityKindDto capability,
        int ordinal,
        string? sourceRecordId)
        => BuildDedupeKey(
            connectionId,
            capability,
            ordinal.ToString(CultureInfo.InvariantCulture),
            sourceRecordId);

    public static string BuildDedupeKey(
        string connectionId,
        ProviderCapabilityKindDto capability,
        string ordinal,
        string? sourceRecordId)
        => string.Join(
            ':',
            connectionId,
            capability.ToString(),
            string.IsNullOrWhiteSpace(sourceRecordId) ? ordinal : sourceRecordId);

    public static string? ResolveSourceIdentityTargetField(ProviderCapabilityKindDto capability)
        => capability switch
        {
            ProviderCapabilityKindDto.Transactions => "providerTransactionId",
            ProviderCapabilityKindDto.Accounts => "providerAccountId",
            ProviderCapabilityKindDto.Positions or ProviderCapabilityKindDto.Holdings => "sourceRecordId",
            ProviderCapabilityKindDto.SecurityReferenceData => "security.cusip",
            _ => "sourceRecordId"
        };

    private static string? ReadBestSecurityIdentifier(JsonObject mappedRecord)
        => FirstNonBlank(
            ReadJsonString(mappedRecord, "security.internalSecurityId"),
            ReadJsonString(mappedRecord, "security.cusip"),
            ReadJsonString(mappedRecord, "security.isin"),
            ReadJsonString(mappedRecord, "security.sedol"),
            ReadJsonString(mappedRecord, "security.figi"),
            ReadJsonString(mappedRecord, "security.providerSecurityId"),
            ReadJsonString(mappedRecord, "security.symbol"),
            ReadJsonString(mappedRecord, "security.ticker"));

    private static string? JoinIdentityParts(params string?[] parts)
    {
        var identityParts = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return identityParts.Length == 0 ? null : string.Join('|', identityParts);
    }

    private static string? FirstNonBlank(params string?[] candidates)
        => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private static string? ReadJsonString(JsonObject root, string targetField)
    {
        JsonNode? current = root;
        foreach (var part in targetField.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is not JsonObject currentObject ||
                !currentObject.TryGetPropertyValue(part, out current) ||
                current is null)
            {
                return null;
            }
        }

        if (current is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return text;
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }
}
