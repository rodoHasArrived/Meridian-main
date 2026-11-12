using System.Globalization;
using System.Text.Json.Nodes;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.Integrations;

/// <summary>Reconciles a bond holding in its declared quotation and consideration units.</summary>
internal static class ProviderIntegrationBondValueNormalization
{
    private const decimal Tolerance = 0.01m;

    public static bool AddValidationIssues(
        JsonObject record,
        ICollection<ValidationIssueDto> issues)
    {
        // This is derived evidence, never a provider-supplied normalization assertion.
        record.Remove("bondValueReconciliation");
        var assetClass = Text(record, "security.assetClass") ?? Text(record, "assetClass");
        // A bond is identified by its catalog fact, not its class name.
        var isBondClass = SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor) && descriptor.CarriesBondCouponStructure;
        if (record["bond"] is not JsonObject &&
            !isBondClass &&
            !string.Equals(assetClass, "FixedIncome", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var face = Number(record, "bond.originalFace");
        var factor = Number(record, "bond.currentFactor");
        var quote = Number(record, "price.amount");
        var providerValue = Number(record, "marketValue.amount");
        var interest = Number(record, "bond.accruedInterest.amount");
        var quotation = Text(record, "bond.quotationConvention");
        var priceBasis = Text(record, "bond.priceBasis");
        var valueBasis = Text(record, "bond.marketValueBasis");
        var currency = Text(record, "marketValue.currency");
        var interestCurrency = Text(record, "bond.accruedInterest.currency");
        var priceCurrency = Text(record, "price.currency");
        var missing = new List<string>();
        if (face is null)
            missing.Add("bond.originalFace");
        if (factor is null || factor < 0)
            missing.Add("bond.currentFactor (non-negative)");
        if (quote is null || quote < 0)
            missing.Add("price.amount (non-negative)");
        if (providerValue is null)
            missing.Add("marketValue.amount");
        if (interest is null)
            missing.Add("bond.accruedInterest.amount (explicit zero if none)");
        if (!string.Equals(quotation, "Per100", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(quotation, "PercentOfPar", StringComparison.OrdinalIgnoreCase))
            missing.Add("bond.quotationConvention (Per100)");
        if (!IsBasis(priceBasis))
            missing.Add("bond.priceBasis (Clean or Dirty)");
        if (!IsBasis(valueBasis))
            missing.Add("bond.marketValueBasis (Clean or Dirty)");
        if (currency is null || interestCurrency is null ||
            !string.Equals(currency, interestCurrency, StringComparison.OrdinalIgnoreCase) ||
            (priceCurrency is not null && !string.Equals(currency, priceCurrency, StringComparison.OrdinalIgnoreCase)))
            missing.Add("matching market value, quotation and accrued-interest currencies");

        var evidence = new JsonObject
        {
            ["schema"] = "bond-value-reconciliation-v1",
            ["providerValue"] = Exact(providerValue),
            ["currency"] = currency,
            ["originalFace"] = Exact(face),
            ["currentFactor"] = Exact(factor),
            ["quotation"] = Exact(quote),
            ["quotationConvention"] = quotation,
            ["priceBasis"] = priceBasis,
            ["marketValueBasis"] = valueBasis,
            ["accruedInterest"] = Exact(interest),
            ["tolerance"] = Exact(Tolerance),
            ["normalizedValue"] = null,
            ["variance"] = null,
            ["cleanConsideration"] = null,
            ["dirtyConsideration"] = null,
            ["status"] = "Blocked"
        };
        record["bondValueReconciliation"] = evidence;
        if (missing.Count > 0)
        {
            evidence["explanation"] = $"Review conventions: {string.Join(", ", missing)}.";
            issues.Add(new ValidationIssueDto(
                "bond-value.conventions.incomplete",
                ProviderIntegrationIssueSeverityDto.Critical,
                $"Bond value cannot be normalized: {string.Join(", ", missing)}.",
                "bond",
                "Review conventions and map explicit original face, current factor, per-100 quotation, accrued interest, and clean/dirty bases before staging."));
            return true;
        }

        try
        {
            var quotedConsideration = checked(face!.Value * factor!.Value * quote!.Value / 100m);
            var clean = IsClean(priceBasis) ? quotedConsideration : checked(quotedConsideration - interest!.Value);
            var dirty = IsClean(priceBasis) ? checked(quotedConsideration + interest!.Value) : quotedConsideration;
            var normalized = IsClean(valueBasis) ? clean : dirty;
            var variance = checked(providerValue!.Value - normalized);
            evidence["cleanConsideration"] = Exact(clean);
            evidence["dirtyConsideration"] = Exact(dirty);
            evidence["normalizedValue"] = Exact(normalized);
            evidence["variance"] = Exact(variance);
            evidence["status"] = Math.Abs(variance) <= Tolerance ? "Matched" : "Variance";
            evidence["explanation"] = $"{Exact(face)} original face × {Exact(factor)} factor × {Exact(quote)} / 100 = {Exact(quotedConsideration)} {priceBasis} consideration; accrued interest {Exact(interest)}; compare on {valueBasis} basis. Variance = provider − normalized.";
            if (Math.Abs(variance) > Tolerance)
            {
                issues.Add(new ValidationIssueDto(
                    "market-value.tolerance.exceeded",
                    ProviderIntegrationIssueSeverityDto.Critical,
                    $"Bond provider value {Exact(providerValue)} differs from normalized {valueBasis} consideration {Exact(normalized)} by {Exact(variance)} {currency}.",
                    "marketValue.amount",
                    "Review original face, current factor, per-100 quotation, accrued interest, and the provider market-value basis."));
            }
        }
        catch (OverflowException)
        {
            evidence["explanation"] = "The declared amounts exceed the supported decimal calculation range.";
            issues.Add(new ValidationIssueDto(
                "bond-value.calculation.overflow",
                ProviderIntegrationIssueSeverityDto.Critical,
                "Bond value exceeds the supported decimal calculation range.",
                "bond",
                "Review the source units and amounts before staging."));
        }

        return true;
    }

    private static bool IsBasis(string? value) => IsClean(value) || string.Equals(value, "Dirty", StringComparison.OrdinalIgnoreCase);
    private static bool IsClean(string? value) => string.Equals(value, "Clean", StringComparison.OrdinalIgnoreCase);
    private static string? Exact(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static JsonNode? Node(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (var segment in path.Split('.'))
            node = node is JsonObject obj ? obj[segment] : null;
        return node;
    }
    private static string? Text(JsonObject root, string path)
        => Node(root, path) is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    private static decimal? Number(JsonObject root, string path)
    {
        if (Node(root, path) is not JsonValue value)
            return null;
        if (value.TryGetValue<decimal>(out var amount))
            return amount;
        return value.TryGetValue<string>(out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) ? amount : null;
    }
}
