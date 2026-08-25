using Meridian.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

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
        => IsStoredAsInfinity(retained) || IsStoredAsInfinity(candidate)
            ? retained == candidate
            : ToStoredPrecision(retained) == ToStoredPrecision(candidate);

    /// <summary>
    /// The two instants Npgsql encodes as PostgreSQL <c>infinity</c> and <c>-infinity</c> rather
    /// than as a microsecond count, under the default infinity conversions.
    /// </summary>
    /// <remarks>
    /// A sentinel is not a point on the microsecond grid, so reducing it to one puts it beside the
    /// largest finite instant the store can hold: <see cref="DateTimeOffset.MaxValue"/> and the
    /// tick below it — which persists finite — land on the same microsecond and would compare
    /// equal. Compared exactly instead, so an infinite retained timestamp is a replay only of
    /// another infinite one.
    /// </remarks>
    private static bool IsStoredAsInfinity(DateTimeOffset value)
        => value.UtcDateTime == DateTime.MaxValue || value.UtcDateTime == DateTime.MinValue;

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

    /// <summary>
    /// Compares a leg's transaction-currency detail at stored scale. A retained detail that is an
    /// identity translation of the functional amount is equivalent to a candidate that declares no
    /// detail at all — but not the reverse.
    /// <para>
    /// The asymmetry is the point. The retained side is what the books contain; the candidate side
    /// is what a caller claims. A candidate that omits the detail claims nothing about
    /// denomination, so accepting it against a retained identity translation acknowledges only
    /// what the books already hold. That is what makes replay work after the <c>V_ledger_029</c>
    /// repair, which stamps the identity translation onto legs written before the append path
    /// carried currency through, while most posting paths still build legs without it. Comparing
    /// presence rather than content there would make exactly the legacy postings that repair
    /// exists to heal permanently unreplayable.
    /// </para>
    /// <para>
    /// Read the other way it would be unsound. An identity translation is not contentless: it
    /// names a currency. A candidate declaring <c>EUR/EUR</c> at rate 1 against a leg that records
    /// no denomination is asserting the books say EUR, and nothing on either replay path checks a
    /// leg's functional currency against its book's base currency — so accepting it would confirm
    /// accounting content the retained journal does not contain. Corroborating such a claim needs
    /// the authoritative book base currency, which this comparison is not given, so it fails
    /// closed instead of guessing.
    /// </para>
    /// <para>
    /// Two details that are both present are compared field by field: a foreign denomination or a
    /// rate the other side does not record is a difference.
    /// </para>
    /// </summary>
    public static bool CurrencyMatches(
        LedgerEntryCurrency? retained,
        LedgerEntryCurrency? candidate,
        decimal functionalDebit,
        decimal functionalCredit)
    {
        if (retained is null && candidate is null)
            return true;
        if (candidate is null)
            return IsIdentityTranslation(retained!, functionalDebit, functionalCredit);
        if (retained is null)
            return false;

        return CurrencyDetailsMatch(retained, candidate);
    }

    /// <summary>
    /// Field-by-field comparison of two currency details, at stored scale. This is an equivalence
    /// relation, unlike <see cref="CurrencyMatches"/>, which also accepts an absent detail against
    /// a label.
    /// </summary>
    public static bool CurrencyDetailsMatch(LedgerEntryCurrency retained, LedgerEntryCurrency candidate)
        => CurrencyCodesMatch(retained.TransactionCurrency, candidate.TransactionCurrency)
           && CurrencyCodesMatch(retained.FunctionalCurrency, candidate.FunctionalCurrency)
           && AmountsMatch(retained.TransactionDebit, candidate.TransactionDebit)
           && AmountsMatch(retained.TransactionCredit, candidate.TransactionCredit)
           && AmountsMatch(retained.FxRateToFunctional, candidate.FxRateToFunctional);

    /// <summary>
    /// Whether <paramref name="currency"/> only labels the functional amount it accompanies rather
    /// than claiming a conversion — the shape that may stand in for an absent detail.
    /// </summary>
    public static bool IsIdentityTranslation(
        LedgerEntryCurrency currency,
        decimal functionalDebit,
        decimal functionalCredit)
        => CurrencyCodesMatch(currency.TransactionCurrency, currency.FunctionalCurrency)
           && AmountsMatch(currency.FxRateToFunctional, 1m)
           && AmountsMatch(currency.TransactionDebit, functionalDebit)
           && AmountsMatch(currency.TransactionCredit, functionalCredit);

    private static bool CurrencyCodesMatch(string? retained, string? candidate)
        => string.Equals(
            NormalizeOptional(retained),
            NormalizeOptional(candidate),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The instant PostgreSQL counts <c>timestamptz</c> microseconds from.</summary>
    private static readonly DateTime PostgresEpochUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Reduces an instant to what the store keeps, mirroring the provider's own conversion.
    /// <para>
    /// Npgsql encodes <c>timestamptz</c> as a <i>signed</i> microsecond delta from
    /// <see cref="PostgresEpochUtc"/> using integer division, which truncates toward the epoch
    /// rather than toward negative infinity. Flooring on absolute ticks agrees with that at or
    /// after the epoch and disagrees by one microsecond before it, so a journal dated earlier than
    /// 2000 whose timestamp carries sub-microsecond ticks would be normalized past the value the
    /// store actually returned — reporting an exact replay as different accounting content, which
    /// is the failure this whole comparison exists to prevent.
    /// </para>
    /// </summary>
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value)
    {
        var storedMicroseconds =
            (value.UtcDateTime.Ticks - PostgresEpochUtc.Ticks) / TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(
            PostgresEpochUtc.AddTicks(storedMicroseconds * TimeSpan.TicksPerMicrosecond),
            TimeSpan.Zero);
    }

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
