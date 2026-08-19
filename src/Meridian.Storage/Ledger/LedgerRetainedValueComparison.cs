namespace Meridian.Storage.Ledger;

/// <summary>
/// Shared value comparisons for deciding whether a retained journal is the posting a caller is
/// asking to append.
/// <para>
/// Both replay seams — <see cref="RetainedPostingEquivalence"/> at the posting-candidate boundary
/// and <see cref="DurableLedgerPostingTarget"/> at the durable append boundary — compare a value
/// that has been through the store against one that has not. The retained side is whatever
/// PostgreSQL gave back; the candidate side is still the caller's in-memory object. Comparing them
/// raw reports a difference the caller never made, which turns an ordinary idempotent retry into a
/// permanent failure: the retained journal already holds the identity, so no later attempt can
/// succeed either.
/// </para>
/// <para>
/// These live in one place rather than once per comparer because they encode facts about the
/// schema, not about either caller. A copy is free to drift away from the column it describes.
/// </para>
/// </summary>
internal static class LedgerRetainedValueComparison
{
    /// <summary>Decimal scale of the ledger's durable numeric columns.</summary>
    private const int StoredDecimalScale = 10;

    /// <summary>
    /// Compares two timestamps at the resolution the store actually keeps. Journal and leg timing
    /// is <c>timestamptz</c>, which is microsecond-resolution, while a CLR tick is 100ns — so a
    /// caller-supplied timestamp carrying sub-microsecond ticks, which is anything derived from
    /// <see cref="DateTimeOffset.UtcNow"/>, is reduced on the way in. Comparing raw ticks would
    /// reject a retry that submitted the very same value it submitted the first time.
    /// </summary>
    public static bool TimestampsMatch(DateTimeOffset retained, DateTimeOffset candidate)
        => ToStoredPrecision(retained) == ToStoredPrecision(candidate);

    /// <summary>
    /// Compares two amounts at the scale the store actually keeps. Journal legs are
    /// <c>numeric(38, 10)</c> and a .NET decimal carries more fractional digits than that, so a
    /// submitted amount comes back rounded. PostgreSQL rounds numeric half away from zero, which
    /// is what is mirrored here.
    /// </summary>
    public static bool AmountsMatch(decimal retained, decimal candidate)
        => ToStoredScale(retained) == ToStoredScale(candidate);

    /// <summary>
    /// One-to-one comparison of external GL dimensions.
    /// <para>
    /// Scanning each retained key for its first case-insensitive match in the candidate lets two
    /// keys that fold together — <c>Dept</c> and <c>dept</c>, or <c>Dept</c> and <c>Dept </c> —
    /// both resolve to the same candidate entry, leaving an unrelated candidate key unexamined
    /// while the counts still agree. That reports different dimensional scope as a replay. Folding
    /// both sides into case-insensitive maps first makes the pairing unambiguous.
    /// </para>
    /// <para>
    /// A side that carries keys which fold together cannot be compared unambiguously at all, so it
    /// is reported as a difference rather than resolved by guessing which key was meant.
    /// </para>
    /// </summary>
    public static bool ExternalDimensionsMatch(
        IReadOnlyDictionary<string, string> retained,
        IReadOnlyDictionary<string, string> candidate)
    {
        if (retained.Count != candidate.Count)
            return false;
        if (!TryFoldKeys(retained, out var retainedFolded) || !TryFoldKeys(candidate, out var candidateFolded))
            return false;
        if (retainedFolded.Count != candidateFolded.Count)
            return false;

        foreach (var (key, retainedValue) in retainedFolded)
        {
            if (!candidateFolded.TryGetValue(key, out var candidateValue)
                || !string.Equals(retainedValue, candidateValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value)
        => value.AddTicks(-(value.UtcTicks % TimeSpan.TicksPerMicrosecond));

    private static decimal ToStoredScale(decimal value)
        => Math.Round(value, StoredDecimalScale, MidpointRounding.AwayFromZero);

    private static bool TryFoldKeys(
        IReadOnlyDictionary<string, string> source,
        out Dictionary<string, string> folded)
    {
        folded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (!folded.TryAdd(key?.Trim() ?? string.Empty, value?.Trim() ?? string.Empty))
                return false;
        }

        return true;
    }
}
