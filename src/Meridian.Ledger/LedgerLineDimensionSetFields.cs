using System.Globalization;
using System.Text;

namespace Meridian.Ledger;

/// <summary>
/// One enumerated dimension field of a <see cref="LedgerLineDimensionSet"/>. <see cref="Name"/> is
/// the canonical camelCase field name (for external GL dimensions it is prefixed with
/// <c>externalGl.</c>); <see cref="ExternalGlKey"/> carries the raw external GL key so format-specific
/// consumers (for example XML element names) can re-encode it.
/// </summary>
internal readonly record struct LedgerDimensionField(string Name, string Value, string? ExternalGlKey = null);

/// <summary>
/// Single source of truth for enumerating the fields of a <see cref="LedgerLineDimensionSet"/>.
/// The dimension-field list was previously repeated in the normalizer, the period-close scope key,
/// and each report-pack builder; adding a dimension now means extending the tables here instead of
/// four coordinated edits.
/// </summary>
internal static class LedgerLineDimensionSetFields
{
    /// <summary>
    /// String-valued dimension fields that precede the instrument/position identifiers in the
    /// canonical field order.
    /// </summary>
    internal static readonly (string Name, Func<LedgerLineDimensionSet, string?> GetValue)[] LeadingStringFields =
    [
        ("fundId", static dimensions => dimensions.FundId),
        ("entityId", static dimensions => dimensions.EntityId),
        ("sleeveId", static dimensions => dimensions.SleeveId),
        ("strategyId", static dimensions => dimensions.StrategyId),
        ("investorId", static dimensions => dimensions.InvestorId),
        ("capitalAccountId", static dimensions => dimensions.CapitalAccountId)
    ];

    /// <summary>
    /// String-valued dimension fields that follow the instrument/position identifiers in the
    /// canonical field order.
    /// </summary>
    internal static readonly (string Name, Func<LedgerLineDimensionSet, string?> GetValue)[] TrailingStringFields =
    [
        ("taxLotId", static dimensions => dimensions.TaxLotId),
        ("costCenterId", static dimensions => dimensions.CostCenterId),
        ("counterpartyId", static dimensions => dimensions.CounterpartyId),
        ("organizationId", static dimensions => dimensions.OrganizationId),
        ("portfolioId", static dimensions => dimensions.PortfolioId),
        ("bookId", static dimensions => dimensions.BookId),
        ("accountId", static dimensions => dimensions.AccountId),
        ("customerId", static dimensions => dimensions.CustomerId),
        ("vendorId", static dimensions => dimensions.VendorId),
        ("projectId", static dimensions => dimensions.ProjectId)
    ];

    /// <summary>All string-valued dimension fields in canonical order.</summary>
    internal static readonly (string Name, Func<LedgerLineDimensionSet, string?> GetValue)[] StringFields =
        [.. LeadingStringFields, .. TrailingStringFields];

    /// <summary>
    /// Enumerates the populated fields of a canonicalized dimension set in canonical order,
    /// followed by the external GL dimensions sorted case-insensitively by key. Returns an empty
    /// sequence when the set canonicalizes to <see langword="null"/>.
    /// </summary>
    internal static IEnumerable<LedgerDimensionField> Enumerate(LedgerLineDimensionSet? dimensions)
    {
        dimensions = LedgerLineDimensionSetNormalizer.Canonicalize(dimensions);
        if (dimensions is null)
            yield break;

        foreach (var (name, getValue) in LeadingStringFields)
        {
            if (getValue(dimensions) is { } value)
                yield return new LedgerDimensionField(name, value);
        }

        if (dimensions.InstrumentId is not null)
            yield return new LedgerDimensionField("instrumentId", dimensions.InstrumentId.Value.ToString("D"));
        if (dimensions.PositionId is not null)
            yield return new LedgerDimensionField("positionId", dimensions.PositionId.Value.ToString("D"));

        foreach (var (name, getValue) in TrailingStringFields)
        {
            if (getValue(dimensions) is { } value)
                yield return new LedgerDimensionField(name, value);
        }

        foreach (var (key, value) in dimensions.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            yield return new LedgerDimensionField($"externalGl.{key}", value, key);
    }

    /// <summary>
    /// Deterministic key for a dimensional scope, used for grouping and ordering (for example the
    /// period-close retained-earnings roll). Record value equality is unreliable for the
    /// external-GL dictionary, so this canonicalizes every field into a stable string. Values are
    /// taken as-is (no trimming) so existing grouping behavior is preserved.
    /// </summary>
    internal static string BuildScopeKey(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var (_, getValue) in LeadingStringFields)
            builder.Append(getValue(dimensions)).Append('|');

        builder.Append(dimensions.InstrumentId?.ToString("D", CultureInfo.InvariantCulture)).Append('|');

        foreach (var (_, getValue) in TrailingStringFields)
            builder.Append(getValue(dimensions)).Append('|');

        foreach (var pair in dimensions.ExternalGlDimensions.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
        }

        if (dimensions.PositionId.HasValue)
        {
            builder.Append('\u001e')
                .Append("positionId=")
                .Append(dimensions.PositionId.Value.ToString("D", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
