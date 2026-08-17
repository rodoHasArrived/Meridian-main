using static Meridian.Contracts.Text.TextPrimitives;
namespace Meridian.Contracts.Ledger;

/// <summary>
/// Canonical reading of ledger GL dimensions out of a free-form tag bag, and the single answer to
/// "does this dimension set carry anything at all?".
/// </summary>
/// <remarks>
/// <para>
/// GL dimension parsing is ledger domain logic. It is owned here so that the storage layer and the
/// HTTP endpoint layer resolve a tag bag identically instead of each carrying a copy — per ADR-017,
/// capability logic belongs to the owning module and an endpoint is a thin adapter over it.
/// </para>
/// <para>
/// <see cref="HasAnyDimension"/> treats a whitespace-only value as absent. That matches the
/// <c>NormalizeOptional</c> convention used throughout the ledger surface, where whitespace-only
/// input collapses to <see langword="null"/>, and it means the predicate is correct on its own
/// rather than only when its caller happens to have normalized first.
/// </para>
/// </remarks>
public static class LedgerDimensionTags
{
    /// <summary>Prefixes that mark a tag as an external GL dimension, in resolution order.</summary>
    private static readonly string[] ExternalGlPrefixes =
        ["externalGl.", "externalGl:", "gl.", "gl:"];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="dimensions"/> carries at least one
    /// populated dimension. Whitespace-only values count as absent.
    /// </summary>
    public static bool HasAnyDimension(LedgerDimensionSetDto dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        return !string.IsNullOrWhiteSpace(dimensions.FundId)
               || !string.IsNullOrWhiteSpace(dimensions.EntityId)
               || !string.IsNullOrWhiteSpace(dimensions.SleeveId)
               || !string.IsNullOrWhiteSpace(dimensions.StrategyId)
               || !string.IsNullOrWhiteSpace(dimensions.InvestorId)
               || !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId)
               || dimensions.InstrumentId.HasValue
               || dimensions.PositionId.HasValue
               || !string.IsNullOrWhiteSpace(dimensions.TaxLotId)
               || !string.IsNullOrWhiteSpace(dimensions.CostCenterId)
               || !string.IsNullOrWhiteSpace(dimensions.CounterpartyId)
               // A blank key or value is not a dimension. ExtractExternalGlDimensions never emits
               // one, so this only bites on a caller-supplied dictionary -- where the close path
               // already answered "not dimensioned" and everyone else answered "dimensioned".
               // Holding that rule here keeps the two from disagreeing (#2672).
               || dimensions.ExternalGlDimensions.Any(static pair =>
                      !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
               || !string.IsNullOrWhiteSpace(dimensions.OrganizationId)
               || !string.IsNullOrWhiteSpace(dimensions.PortfolioId)
               || !string.IsNullOrWhiteSpace(dimensions.BookId)
               || !string.IsNullOrWhiteSpace(dimensions.AccountId)
               || !string.IsNullOrWhiteSpace(dimensions.CustomerId)
               || !string.IsNullOrWhiteSpace(dimensions.VendorId)
               || !string.IsNullOrWhiteSpace(dimensions.ProjectId);
    }

    /// <summary>
    /// Reads external GL dimensions out of <paramref name="tags"/>, recognising the
    /// <c>externalGl.</c>, <c>externalGl:</c>, <c>gl.</c>, and <c>gl:</c> prefixes.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags)
        => ExtractExternalGlDimensions(tags, prefix: null);

    /// <summary>
    /// Reads external GL dimensions out of <paramref name="tags"/>, first stripping
    /// <paramref name="prefix"/> from each tag key when one is supplied.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
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

            var dimensionKey = ResolveExternalGlKey(scopedKey);
            if (!string.IsNullOrWhiteSpace(dimensionKey))
            {
                result[dimensionKey] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the normalized value of the first tag in <paramref name="keys"/> that is present, or
    /// <see langword="null"/> when none of them are.
    /// </summary>
    public static string? FirstTag(
        IReadOnlyDictionary<string, string>? tags,
        params string[] keys)
    {
        if (tags is null || tags.Count == 0)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value))
            {
                return NormalizeOptional(value);
            }
        }

        return null;
    }

    private static string? ResolveExternalGlKey(string scopedKey)
    {
        foreach (var prefix in ExternalGlPrefixes)
        {
            var stripped = StripPrefix(scopedKey, prefix);
            if (stripped is not null)
            {
                return stripped;
            }
        }

        return null;
    }

    private static string? StripPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizeOptional(value[prefix.Length..])
            : null;
}
