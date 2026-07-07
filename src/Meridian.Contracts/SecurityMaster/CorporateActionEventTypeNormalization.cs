namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Outcome of the one-time stored-value normalization sweep over corporate-action
/// EventType strings (e.g. legacy "Split" rows rewritten to "StockSplit"). Read paths
/// normalize aliases permanently, so this sweep is cosmetic — it cleans raw exports and
/// ad-hoc SQL. Values that do not normalize to a canonical name are reported in
/// <see cref="UnmappedValues"/> and left untouched, never guessed.
/// </summary>
public sealed record CorporateActionEventTypeNormalizationResult(
    bool Applied,
    IReadOnlyList<CorporateActionEventTypeRename> Renames,
    IReadOnlyList<CorporateActionEventTypeCount> UnmappedValues)
{
    public static CorporateActionEventTypeNormalizationResult Empty { get; } = new(false, [], []);
}

/// <summary>One planned or applied rewrite: every row holding <see cref="StoredValue"/> becomes <see cref="CanonicalName"/>.</summary>
public sealed record CorporateActionEventTypeRename(string StoredValue, string CanonicalName, int RowCount);

/// <summary>A stored value that could not be resolved to a canonical event type.</summary>
public sealed record CorporateActionEventTypeCount(string StoredValue, int RowCount);
