using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(
        IReadOnlyDictionary<string, string>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is not null && value is not null && !normalized.ContainsKey(key))
            {
                normalized[key] = value;
            }
        }

        return normalized;
    }

    private static bool HasAnyCanonicalDimension(LedgerDimensionSetDto dimensions)
        => dimensions.FundId is not null
           || dimensions.EntityId is not null
           || dimensions.SleeveId is not null
           || dimensions.StrategyId is not null
           || dimensions.InvestorId is not null
           || dimensions.CapitalAccountId is not null
           || dimensions.InstrumentId.HasValue
           || dimensions.PositionId.HasValue
           || dimensions.TaxLotId is not null
           || dimensions.CostCenterId is not null
           || dimensions.CounterpartyId is not null
           || dimensions.ExternalGlDimensions.Count > 0
           || dimensions.OrganizationId is not null
           || dimensions.PortfolioId is not null
           || dimensions.BookId is not null
           || dimensions.AccountId is not null
           || dimensions.CustomerId is not null
           || dimensions.VendorId is not null
           || dimensions.ProjectId is not null;

    private static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags)
        => ExtractExternalGlDimensions(tags, prefix: null);

    private static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags,
        string? prefix)
    {
        if (tags is null || tags.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in tags)
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is null || value is null)
            {
                continue;
            }

            var scopedKey = prefix is null
                ? key
                : StripPrefix(key, prefix);
            if (scopedKey is null)
            {
                continue;
            }

            var dimensionKey = StripPrefix(scopedKey, "externalGl.")
                               ?? StripPrefix(scopedKey, "externalGl:")
                               ?? StripPrefix(scopedKey, "gl.")
                               ?? StripPrefix(scopedKey, "gl:");
            if (!string.IsNullOrWhiteSpace(dimensionKey))
            {
                result[dimensionKey] = value;
            }
        }

        return result;
    }

    private static string? StripPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizeOptional(value[prefix.Length..])
            : null;

    private static string? FirstTag(
        IReadOnlyDictionary<string, string>? tags,
        params string[] keys)
    {
        if (tags is null || tags.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value))
            {
                return NormalizeOptional(value);
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
