using System.Text.Json;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Storage.Export;

public sealed partial class AnalysisExportService
{
    private static readonly string[] HistoricalPriceFields = ["Open", "High", "Low", "Close"];
    private static readonly string[] AdjustedHistoricalPriceFields = ["AdjustedOpen", "AdjustedHigh", "AdjustedLow", "AdjustedClose"];
    private static readonly string[] HistoricalBasisFields = ["PriceBasis", "IsAdjusted"];

    /// <summary>
    /// Keeps financial meaning next to exported OHLC values. Retained JSON without a precise
    /// declaration stays Unknown even when it carries a legacy isAdjusted=false flag.
    /// </summary>
    private static void AddHistoricalPriceBasisMetadata(JsonElement root, Dictionary<string, object?> record)
    {
        var prices = root;
        if (!HasHistoricalPriceProperties(prices))
        {
            if (!TryGetPropertyIgnoreCase(root, "payload", out prices) || !HasHistoricalPriceProperties(prices))
                return;
        }

        if (HasOhlcProperties(prices))
        {
            var basis = ReadHistoricalBasis(prices, "priceBasis");
            bool? adjusted = null;
            if (TryGetPropertyIgnoreCase(prices, "isAdjusted", out var adjustedElement))
            {
                adjusted = adjustedElement.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => throw new InvalidDataException("Historical export contains an invalid adjustment flag.")
                };
            }

            if (basis != HistoricalPriceBasis.Unknown)
            {
                var expectedAdjusted = basis != HistoricalPriceBasis.Raw;
                if (adjusted.HasValue && adjusted.Value != expectedAdjusted)
                    throw new InvalidDataException("Historical export price basis contradicts its adjustment flag.");
                adjusted = expectedAdjusted;
            }

            record["PriceBasis"] = basis.ToString();
            record["IsAdjusted"] = adjusted;
        }

        // Optional adjusted components are a separate price set. Its basis must never be
        // inherited from the primary OHLC or from the legacy flag that describes that set.
        if (HasAdjustedPriceProperties(prices))
            record["AdjustedPriceBasis"] = ReadHistoricalBasis(prices, "adjustedPriceBasis").ToString();
    }

    private static HistoricalPriceBasis ReadHistoricalBasis(JsonElement prices, string field)
    {
        if (!TryGetPropertyIgnoreCase(prices, field, out var value) || value.ValueKind == JsonValueKind.Null)
            return HistoricalPriceBasis.Unknown;

        if (value.ValueKind == JsonValueKind.String &&
            HistoricalPriceBasisValues.TryParse(value.GetString(), out var namedBasis))
            return namedBasis;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric) &&
            Enum.IsDefined((HistoricalPriceBasis)numeric))
            return (HistoricalPriceBasis)numeric;

        throw new InvalidDataException($"Historical export contains an unsupported {field}.");
    }

    private static bool HasHistoricalPriceProperties(JsonElement value) =>
        HasOhlcProperties(value) || HasAdjustedPriceProperties(value);

    private static bool HasOhlcProperties(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        HistoricalPriceFields.All(field => TryGetPropertyIgnoreCase(value, field, out _));

    private static bool HasAdjustedPriceProperties(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        AdjustedHistoricalPriceFields.Any(field => TryGetPropertyIgnoreCase(value, field, out _));

    private static bool TryGetPropertyIgnoreCase(JsonElement value, string name, out JsonElement property)
    {
        property = default;
        var found = false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in value.EnumerateObject())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (found)
                        throw new InvalidDataException($"Historical export contains duplicate {name} declarations.");
                    property = candidate.Value;
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool RetainsHistoricalPrices(Dictionary<string, object?> record) =>
        HistoricalPriceFields.Any(record.ContainsKey) || record.ContainsKey("Payload");

    private static bool RetainsAdjustedHistoricalPrices(Dictionary<string, object?> record) =>
        AdjustedHistoricalPriceFields.Any(record.ContainsKey) || record.ContainsKey("Payload");

    private static IEnumerable<string> RequiredHistoricalBasisFields(
        Dictionary<string, object?> original, Dictionary<string, object?> projected)
    {
        if (original.ContainsKey("PriceBasis") && RetainsHistoricalPrices(projected))
        {
            foreach (var field in HistoricalBasisFields)
                yield return field;
        }

        if (original.ContainsKey("AdjustedPriceBasis") && RetainsAdjustedHistoricalPrices(projected))
            yield return "AdjustedPriceBasis";
    }

    private static void PreserveHistoricalPriceBasis(
        Dictionary<string, object?> original,
        Dictionary<string, object?> projected)
    {
        foreach (var field in RequiredHistoricalBasisFields(original, projected))
            projected[field] = original.GetValueOrDefault(field);
    }

    private static List<string> PreserveHistoricalBasisColumns(
        IEnumerable<string> selectedColumns,
        IEnumerable<Dictionary<string, object?>> records)
    {
        var columns = selectedColumns.ToList();
        foreach (var field in records.SelectMany(record => RequiredHistoricalBasisFields(record, record)))
        {
            if (!columns.Contains(field, StringComparer.OrdinalIgnoreCase))
                columns.Add(field);
        }

        return columns;
    }

    private static void RequireHistoricalBasisColumns(Dictionary<string, object?> record, IReadOnlyList<string> columns)
    {
        var required = RequiredHistoricalBasisFields(record, record).ToArray();
        if (required.Any(field => !columns.Contains(field, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Historical prices require {string.Join(" and ", required)} columns; export historical bars separately from records with a different schema.");
        }

        var priceFields = (record.ContainsKey("PriceBasis") ? HistoricalPriceFields : [])
            .Concat(record.ContainsKey("AdjustedPriceBasis") ? AdjustedHistoricalPriceFields : [])
            .Where(record.ContainsKey);
        if (priceFields.Any(field => !columns.Contains(field, StringComparer.OrdinalIgnoreCase)) ||
            (required.Length > 0 && record.ContainsKey("Payload") && !columns.Contains("Payload", StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Historical price columns cannot be omitted by the output schema; export records with different schemas separately.");
        }
    }

    private static void RequireComparableHistoricalFeaturePrices(List<Dictionary<string, object?>> records)
    {
        // Plain trade series keep their existing behavior. Once a series contains historical
        // closes, every consumed price needs the same precise basis; a raw/split transition
        // must not appear as an investment return or enter a rolling statistic.
        if (!records.Any(record => record.Keys.Any(key => key.Equals("Close", StringComparison.OrdinalIgnoreCase))))
            return;

        HistoricalPriceBasis? seriesBasis = null;
        foreach (var record in records)
        {
            if (!record.Keys.Any(key => key.Equals("Price", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Close", StringComparison.OrdinalIgnoreCase) || key.Equals("last", StringComparison.OrdinalIgnoreCase)))
                continue;

            var declaration = record.FirstOrDefault(pair => pair.Key.Equals("PriceBasis", StringComparison.OrdinalIgnoreCase)).Value;
            var valid = declaration is string text && HistoricalPriceBasisValues.TryParse(text, out var parsed)
                ? parsed
                : declaration is HistoricalPriceBasis basis && Enum.IsDefined(basis) ? basis : HistoricalPriceBasis.Unknown;
            if (valid == HistoricalPriceBasis.Unknown || (seriesBasis.HasValue && seriesBasis.Value != valid))
                throw new InvalidDataException("Historical price features require one explicit known price basis throughout the series.");
            seriesBasis = valid;
        }
    }
}
