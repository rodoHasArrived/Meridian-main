namespace Meridian.Contracts.Ledger;

/// <summary>
/// Owns the canonical presence and legacy metadata-tag semantics for the shared ledger dimension
/// envelope so persistence and presentation adapters cannot drift.
/// </summary>
public static class LedgerDimensionSetNormalizer
{
    /// <summary>
    /// Trims optional dimension values, removes blank external-GL entries, and returns
    /// <see langword="null"/> when no meaningful dimension remains.
    /// </summary>
    public static LedgerDimensionSetDto? Canonicalize(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var canonical = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };

        return HasAny(canonical) ? canonical : null;
    }

    /// <summary>
    /// Returns whether the dimension envelope contains at least one nonblank string dimension,
    /// identifier dimension, or canonical external-GL dimension.
    /// </summary>
    public static bool HasAny(LedgerDimensionSetDto? dimensions)
        => dimensions is not null
           && (!string.IsNullOrWhiteSpace(dimensions.FundId)
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
               || HasAnyExternalGlDimension(dimensions.ExternalGlDimensions)
               || !string.IsNullOrWhiteSpace(dimensions.OrganizationId)
               || !string.IsNullOrWhiteSpace(dimensions.PortfolioId)
               || !string.IsNullOrWhiteSpace(dimensions.BookId)
               || !string.IsNullOrWhiteSpace(dimensions.AccountId)
               || !string.IsNullOrWhiteSpace(dimensions.CustomerId)
               || !string.IsNullOrWhiteSpace(dimensions.VendorId)
               || !string.IsNullOrWhiteSpace(dimensions.ProjectId));

    /// <summary>
    /// Normalizes external-GL names and values with deterministic, case-insensitive keys.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(
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

    /// <summary>
    /// Returns the normalized value for the first metadata key that is present. A present blank
    /// value remains authoritative and resolves to <see langword="null"/> rather than falling
    /// through to a compatibility alias.
    /// </summary>
    public static string? FirstTag(
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

    /// <summary>
    /// Extracts external-GL dimensions from legacy journal metadata using the supported
    /// <c>externalGl.</c>, <c>externalGl:</c>, <c>gl.</c>, and <c>gl:</c> aliases.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags,
        string? prefix = null)
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
            if (dimensionKey is not null)
            {
                result[dimensionKey] = value;
            }
        }

        return result;
    }

    private static bool HasAnyExternalGlDimension(IReadOnlyDictionary<string, string> dimensions)
        => dimensions.Any(static pair =>
            !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));

    private static string? StripPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizeOptional(value[prefix.Length..])
            : null;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
