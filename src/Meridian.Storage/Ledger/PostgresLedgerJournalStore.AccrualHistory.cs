using System.Data;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FaceValueAccrualHolding>> ListTaxLotAccrualSnapshotsAsync(
        Guid ledgerBookId,
        Guid securityId,
        Guid bookPositionId,
        DateOnly from,
        DateOnly through,
        CancellationToken ct = default)
    {
        RequireWriteTenant();
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        if (securityId == Guid.Empty)
            throw new ArgumentException("Security Master identity is required.", nameof(securityId));
        if (bookPositionId == Guid.Empty)
            throw new ArgumentException("Book-position identity is required.", nameof(bookPositionId));
        if (through < from)
            throw new ArgumentException("An accrual range cannot end before it starts.", nameof(through));

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        await EnsureBookWriteAuthorityAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        // One snapshot reads every lot and its complete retained history once, including closed
        // rows. Returning only currently open lots would discard a full paydown's final interest.
        var (lots, mutations) = await ReadAssetScopedTaxLotHistoryAsync(connection, transaction, ledgerBookId,
            securityId, bookPositionId, through, ct).ConfigureAwait(false);
        var histories = mutations.ToLookup(static mutation => mutation.TaxLotRecordId);
        var result = new List<FaceValueAccrualHolding>();
        foreach (var lot in lots)
        {
            ct.ThrowIfCancellationRequested();
            result.AddRange(HistoricalTaxLotAccrual.Project(lot, histories[lot.TaxLotRecordId].ToArray(), from, through, ct));
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result.OrderBy(static snapshot => snapshot.Date)
            .ThenBy(static snapshot => snapshot.Lot.LotId, StringComparer.Ordinal).ToArray();
    }
}
