using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
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
        ICollection<ValidationIssueDto> issues,
        IBusinessDayCalendarSource? calendars = null)
    {
        ArgumentNullException.ThrowIfNull(mappedRecord);
        ArgumentNullException.ThrowIfNull(issues);

        mappedRecord.Remove("bondValueReconciliation");
        AddMoneyCurrencyIssues(capability, mappedRecord, issues);
        AddMarketValueToleranceIssues(capability, mappedRecord, issues);
        ProviderIntegrationFixedIncomeTermsValidation.AddValidationIssues(capability, mappedRecord, issues, calendars);
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

        // Retained full holdings and the reviewed quotation import have distinct explicit
        // contracts. Neither may reach the generic quantity-times-price fallback.
        var bondFields = mappedRecord["bond"] as JsonObject;
        if (bondFields is not null && new[] { "currentFace", "principalFactor", "inflationRatio", "quoteBasis", "invoiceBasis", "quote", "quoteIsDirty" }
            .Any(bondFields.ContainsKey))
        {
            try
            {
                if (new[] { "currentFactor", "quotationConvention", "priceBasis", "marketValueBasis" }.Any(bondFields.ContainsKey)
                    || bondFields["accruedInterest"] is JsonObject)
                    throw new ArgumentException("A holding cannot combine retained face/invoice economics with another quotation contract; choose one explicit bond normalization profile.");
                var bond = mappedRecord["bond"]?.Deserialize<BondHoldingEconomicsDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new ArgumentException("Bond holdings require explicit face, factor and quotation basis in bond economics.");
                var currency = (mappedRecord["marketValue"] as JsonObject is { } money ? ReadString(money, "currency") : null)
                    ?? ReadString(mappedRecord, "currency")
                    ?? throw new ArgumentException("Bond holdings require explicit currency.");
                if (mappedRecord["price"] is JsonObject quoteMoney && ReadString(quoteMoney, "currency") is { } quoteCurrency
                    && !string.Equals(currency, quoteCurrency, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Bond quote and market-value currencies must agree; no FX assumption is inferred.");
                var normalized = BondHoldingNormalization.Calculate(bond, currency);
                if (ReadDecimal(mappedRecord, "marketValue.amount") is { } observed
                    && LedgerCurrencyRounding.RoundCurrency(observed, currency) != LedgerCurrencyRounding.RoundCurrency(normalized.Dirty, currency))
                    throw new ArgumentException("Reported bond market value must reconcile to dirty value using explicit face and units.");
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or OverflowException)
            {
                issues.Add(new("bond.normalization.blocked", ProviderIntegrationIssueSeverityDto.Critical,
                    exception.Message, "bond", "Map explicit face, principal factor, inflation ratio, quotation basis, accrued interest and currency."));
            }
            return;
        }
        if (ProviderIntegrationBondValueNormalization.AddValidationIssues(mappedRecord, issues))
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
