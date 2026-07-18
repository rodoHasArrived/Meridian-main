using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public static class LedgerJournalStoreHydrationExtensions
{
    public static async Task<Meridian.Ledger.Ledger> HydrateLedgerAsync(
        this ILedgerJournalStore store,
        LedgerJournalEntryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(query);

        var records = await store.QueryAsync(query, ct).ConfigureAwait(false);
        return Hydrate(records);
    }

    public static Task<Meridian.Ledger.Ledger> HydrateLedgerAsOfAsync(
        this ILedgerJournalStore store,
        Guid ledgerBookId,
        DateTimeOffset asOfUtc,
        LedgerLineDimensionSet? lineDimensions = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        return store.HydrateLedgerAsync(
            new LedgerJournalEntryQuery(
                LedgerBookId: ledgerBookId,
                LineDimensions: lineDimensions,
                OccurredTo: asOfUtc),
            ct);
    }

    public static Task<Meridian.Ledger.Ledger> HydrateLedgerPeriodAsync(
        this ILedgerJournalStore store,
        Guid ledgerBookId,
        Guid periodId,
        LedgerLineDimensionSet? lineDimensions = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        if (periodId == Guid.Empty)
        {
            throw new ArgumentException("Period id is required.", nameof(periodId));
        }

        return store.HydrateLedgerAsync(
            new LedgerJournalEntryQuery(
                LedgerBookId: ledgerBookId,
                PeriodId: periodId,
                LineDimensions: lineDimensions),
            ct);
    }

    /// <summary>
    /// Rebuilds one fund-wide ledger projection from every durable ledger book on the
    /// requested accounting basis. Journal records retain durable global-sequence order
    /// when timestamps are equal, so statements, trial balance, and NAV share replay semantics.
    /// </summary>
    public static async Task<Meridian.Ledger.Ledger> HydrateFundLedgerAsOfAsync(
        this ILedgerJournalStore store,
        string fundProfileId,
        DateTimeOffset asOfUtc,
        AccountingBasisKindDto? accountingBasis = AccountingBasisKindDto.Primary,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);

        var books = await store
            .ListLedgerBooksAsync(fundProfileId.Trim(), ct: ct)
            .ConfigureAwait(false);
        var selectedBooks = books
            .Where(book => accountingBasis is null || book.AccountingBasis == accountingBasis.Value)
            .OrderBy(static book => book.LedgerBookId)
            .ToArray();

        var records = new List<LedgerJournalEntryRecord>();
        foreach (var book in selectedBooks)
        {
            ct.ThrowIfCancellationRequested();
            records.AddRange(await store
                .QueryAsync(
                    new LedgerJournalEntryQuery(
                        LedgerBookId: book.LedgerBookId,
                        OccurredTo: asOfUtc),
                    ct)
                .ConfigureAwait(false));
        }

        return Hydrate(records);
    }

    private static Meridian.Ledger.Ledger Hydrate(IEnumerable<LedgerJournalEntryRecord> records)
    {
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in records
                     .OrderBy(static record => record.Entry.Timestamp)
                     .ThenBy(static record => record.GlobalSequence))
        {
            ledger.Post(record.Entry);
        }

        return ledger;
    }
}
