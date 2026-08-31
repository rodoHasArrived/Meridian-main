namespace Meridian.Storage.Ledger;

/// <summary>
/// Why a currency-blind journal leg — one persisted before the append path carried currency detail
/// through to the store — can or cannot be repaired from what the ledger still retains.
/// </summary>
public enum LedgerCurrencyBackfillDisposition
{
    /// <summary>
    /// The leg's ledger book corroborates single-currency operation: it has currency-bearing legs,
    /// and every one of them is an identity translation at the book's own base currency. Repairing
    /// records that denomination without inventing an FX rate.
    /// </summary>
    Repairable,

    /// <summary>
    /// Nothing contradicts single-currency operation and nothing corroborates it — the book has no
    /// currency-bearing leg at all. Silence is not evidence, so completing these takes an
    /// operator's explicit affirmation rather than an inference from the data.
    /// </summary>
    UnaffirmedSingleCurrency,

    /// <summary>
    /// The book does transact in foreign currency, so a blind leg in it may be a foreign leg whose
    /// rate is unrecoverable. Stamping it as base currency would misstate the denomination.
    /// </summary>
    ForeignCurrencyEvidence,

    /// <summary>
    /// The book's currency-bearing legs name a functional currency other than the book's own base
    /// currency, which leaves the base currency untrustworthy as a label for anything in the book.
    /// </summary>
    FunctionalCurrencyMismatch,

    /// <summary>
    /// The book's base currency is not a three-letter code, so it cannot be stamped onto a leg.
    /// </summary>
    UnusableBaseCurrency,

    /// <summary>
    /// The leg's accounting period was never scoped to a ledger book, so no authoritative
    /// functional currency resolves for it.
    /// </summary>
    UnresolvedLedgerBook,
}

/// <summary>
/// The currency-blind legs of one ledger book that share a disposition.
/// </summary>
/// <param name="LedgerBookId">
/// The owning ledger book, or <see langword="null"/> for legs whose period is not scoped to one.
/// </param>
/// <param name="BaseCurrency">
/// The book's normalized base currency, or <see langword="null"/> when it does not resolve to a
/// usable three-letter code.
/// </param>
/// <param name="ClosedPeriodLegs">
/// How many of these legs sit in a soft- or hard-closed period. Repairing them changes no
/// functional amount, so no statement moves, but an operator affirming a book should see how much
/// of what they are completing is closed history.
/// </param>
public sealed record LedgerCurrencyBackfillScope(
    Guid? LedgerBookId,
    string? BaseCurrency,
    LedgerCurrencyBackfillDisposition Disposition,
    int CurrencyBlindLegs,
    int ClosedPeriodLegs);

/// <summary>
/// What currency detail the ledger is still missing, and what can be done about each part of it.
/// An empty <see cref="Scopes"/> means every retained leg carries its currency.
/// </summary>
public sealed record LedgerCurrencyBackfillSurvey(IReadOnlyList<LedgerCurrencyBackfillScope> Scopes)
{
    /// <summary>Every retained leg with no currency detail.</summary>
    public int CurrencyBlindLegs => Scopes.Sum(static scope => scope.CurrencyBlindLegs);

    /// <summary>Legs the retained evidence already determines; see <see cref="LedgerCurrencyBackfillDisposition.Repairable"/>.</summary>
    public int RepairableLegs => LegsWith(LedgerCurrencyBackfillDisposition.Repairable);

    /// <summary>Legs waiting on an operator affirmation, and on nothing else.</summary>
    public int AffirmableLegs => LegsWith(LedgerCurrencyBackfillDisposition.UnaffirmedSingleCurrency);

    /// <summary>Legs no evidence and no affirmation can complete; they stay currency-blind.</summary>
    public int BlockedLegs => CurrencyBlindLegs - RepairableLegs - AffirmableLegs;

    /// <summary>True when nothing is left to repair.</summary>
    public bool IsComplete => Scopes.Count == 0;

    private int LegsWith(LedgerCurrencyBackfillDisposition disposition)
        => Scopes
            .Where(scope => scope.Disposition == disposition)
            .Sum(static scope => scope.CurrencyBlindLegs);
}

/// <summary>
/// The record of an operator asserting that a ledger book with no retained currency evidence
/// transacted only in its base currency, and of the legs that assertion completed.
/// </summary>
public sealed record LedgerCurrencyAffirmationResult(
    Guid AffirmationId,
    Guid LedgerBookId,
    string AffirmedCurrency,
    string Actor,
    int LegsRepaired,
    DateTimeOffset AffirmedAt);
