using System.Data;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;

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
        var book = await LoadLedgerBookAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The accrual ledger book is unavailable.");
        // One snapshot reads every lot and its complete retained history once, including closed
        // rows. Returning only currently open lots would discard a full paydown's final interest.
        var (lots, mutations) = await ReadAssetScopedTaxLotHistoryAsync(connection, transaction, ledgerBookId,
            securityId, bookPositionId, through, ct).ConfigureAwait(false);
        var histories = mutations.ToLookup(static mutation => mutation.TaxLotRecordId);
        var result = new List<FaceValueAccrualHolding>();
        foreach (var lot in lots)
        {
            ct.ThrowIfCancellationRequested();
            if (lot.Currency != book.BaseCurrency || lot.Account.AccountType != LedgerAccountType.Asset
                || lot.Acquisition is { Direction: not OpenLotDirection.Long })
                throw new LedgerValidationException("Retained accrual requires long asset lots stated in the ledger book's functional currency.");
            if (lot.Acquisition is { } acquisition)
            {
                if (acquisition.FunctionalCurrency != book.BaseCurrency)
                    throw new LedgerValidationException("Retained accrual acquisition functional currency must match the ledger book.");
                // Prove the actual current record's basis convention and retained FX before the
                // historical face projection discards denominations. Already converted functional
                // basis is valid even when the acquisition FX rate was not one.
                _ = lot.ToOpenLot();
            }
            result.AddRange(HistoricalTaxLotAccrual.Project(lot, histories[lot.TaxLotRecordId].ToArray(), from, through, ct));
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result.OrderBy(static snapshot => snapshot.Date)
            .ThenBy(static snapshot => snapshot.Lot.LotId, StringComparer.Ordinal).ToArray();
    }
}
