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
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in records
                     .OrderBy(static record => record.Entry.Timestamp)
                     .ThenBy(static record => record.GlobalSequence))
        {
            ledger.Post(record.Entry);
        }

        return ledger;
    }

    public static Task<Meridian.Ledger.Ledger> HydrateLedgerAsOfAsync(
        this ILedgerJournalStore store,
        Guid ledgerBookId,
        DateTimeOffset asOfUtc,
        Meridian.Ledger.LedgerLineDimensionSet? lineDimensions = null,
        CancellationToken ct = default)
    {
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
}
