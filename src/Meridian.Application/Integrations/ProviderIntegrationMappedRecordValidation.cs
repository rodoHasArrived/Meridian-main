using System.Globalization;
using System.Text.Json.Nodes;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

internal static class ProviderIntegrationMappedRecordValidation
{
    private const decimal MarketValueTolerance = 0.01m;

    private static readonly string[] MoneyFields =
    [
        "amount",
        "marketValue",
        "costBasis",
        "bookValue",
        "accruedIncome"
    ];

    public static void AddValidationIssues(
        ProviderCapabilityKindDto capability,
        JsonObject mappedRecord,
        ICollection<ValidationIssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(mappedRecord);
        ArgumentNullException.ThrowIfNull(issues);

        AddMoneyCurrencyIssues(capability, mappedRecord, issues);
        AddMarketValueToleranceIssues(capability, mappedRecord, issues);
    }

    private static void AddMoneyCurrencyIssues(
        ProviderCapabilityKindDto capability,
        JsonObject mappedRecord,
        ICollection<ValidationIssueDto> issues)
    {
        foreach (var moneyField in MoneyFields)
        {
            if (mappedRecord[moneyField] is not JsonObject moneyObject ||
                !HasValue(moneyObject, "amount"))
            {
                continue;
            }

            if (!HasValue(moneyObject, "currency"))
            {
                issues.Add(new ValidationIssueDto(
                    "money.currency.missing",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    $"{capability} record has {moneyField}.amount but no {moneyField}.currency.",
                    $"{moneyField}.currency",
                    "Map a provider currency field or configure a safe default currency before staging records."));
                continue;
            }

            var currency = ReadString(moneyObject, "currency");
            if (currency is null || currency.Length != 3 || currency.Any(character => !char.IsLetter(character)))
            {
                issues.Add(new ValidationIssueDto(
                    "currency.invalid",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    $"{capability} record has invalid currency '{currency}'.",
                    $"{moneyField}.currency",
                    "Use a three-letter ISO currency code such as USD, EUR, or GBP."));
            }
        }
    }

    private static void AddMarketValueToleranceIssues(
        ProviderCapabilityKindDto capability,
        JsonObject mappedRecord,
        ICollection<ValidationIssueDto> issues)
    {
        if (capability is not ProviderCapabilityKindDto.Positions and not ProviderCapabilityKindDto.Holdings)
        {
            return;
        }

        var quantity = ReadDecimal(mappedRecord, "quantity");
        var price = ReadDecimal(mappedRecord, "price.amount");
        var marketValue = ReadDecimal(mappedRecord, "marketValue.amount");
        if (quantity is null || price is null || marketValue is null)
        {
            return;
        }

        var expected = quantity.Value * price.Value;
        var variance = Math.Abs(expected - marketValue.Value);
        if (variance <= MarketValueTolerance)
        {
            return;
        }

        issues.Add(new ValidationIssueDto(
            "market-value.tolerance.exceeded",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"{capability} record market value differs from quantity times price by {variance}.",
            "marketValue.amount",
            "Confirm the provider price, quantity, market value, and units before staging the record."));
    }

    private static bool HasValue(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    private static string? ReadString(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var value) ||
            value is not JsonValue jsonValue)
        {
            return null;
        }

        return jsonValue.TryGetValue<string>(out var text) ? text.Trim() : null;
    }

    private static decimal? ReadDecimal(JsonObject root, string targetField)
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

        if (current is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        return value.TryGetValue<string>(out var text) &&
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }
}
