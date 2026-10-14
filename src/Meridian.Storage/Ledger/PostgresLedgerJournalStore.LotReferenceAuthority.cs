using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task LockAcquisitionDisposalAuthorityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, AtomicTaxLotJournalCommand command, CancellationToken ct)
    {
        if (command.MutationKind is not (AtomicTaxLotMutationKind.Acquisition or AtomicTaxLotMutationKind.Disposal))
        {
            if (command.ReferenceVersions is not null)
                throw new LedgerValidationException("Acquisition/disposal reference authority cannot be supplied for another mutation kind.");
            return;
        }

        if (command.ReferenceVersions is not { SecurityMasterVersion: > 0, BookPositionVersion: > 0 } versions)
            throw new LedgerValidationException("New acquisition and disposal postings require positive retained reference authority versions.");
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Acquisition/disposal reference authority requires PostgreSQL Security Master and book-position stores.");

        // The caller holds the book and period. Both reference stores enlist in this same
        // transaction and reject a different database; lot locks follow these reference locks.
        var scope = ResolveAtomicAssetScope(command.Journal);
        var security = await securities.LockForLotPostingAsync(connection, transaction, scope.SecurityId, ct).ConfigureAwait(false);
        var position = await positions.LockForLotPostingAsync(connection, transaction, scope.BookPositionId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, command.LedgerBookId, ct).ConfigureAwait(false);
        var timestamp = command.Journal.Entry.Timestamp;
        var date = command.Journal.Entry.Metadata.EffectiveDate
            ?? throw new LedgerValidationException("Acquisition/disposal reference authority requires a governed journal effective date.");
        if (security is null || security.SecurityId != scope.SecurityId || security.Version != versions.SecurityMasterVersion
            || security.Status != SecurityStatusDto.Active || security.EffectiveFrom > timestamp
            || (security.EffectiveTo is { } securityEnd && securityEnd < timestamp))
            throw new LedgerValidationException("Acquisition/disposal Security Master reference authority is missing, inactive or stale; rebuild and review the draft.");
        if (position is null || book is null || position.PositionId != scope.BookPositionId
            || position.SecurityId != scope.SecurityId || position.Version != versions.BookPositionVersion
            || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset) || position.Status != "Active"
            || position.EffectiveFrom > date || (position.EffectiveTo is { } positionEnd && positionEnd < date))
            throw new LedgerValidationException("Acquisition/disposal book-position reference authority is missing, inactive or stale; rebuild and review the draft.");

        var context = position.BookContext;
        if (context.LedgerBookId != command.LedgerBookId || context.FundProfileId != book.FundProfileId
            || context.FundStructureNodeId != book.FundStructureNodeId || context.FundStructureNodeKind != book.FundStructureNodeKind
            || context.BaseCurrency != book.BaseCurrency || context.AccountingBasis != book.AccountingBasis
            || context.AccountingPolicyId != book.AccountingPolicyId || context.AccountingPolicyVersion != book.AccountingPolicyVersion
            || (context.PeriodId is { } periodId && periodId != command.Journal.PeriodId)
            || book.BaseCurrency != ResolveAtomicFunctionalCurrency(command.Journal))
            throw new LedgerValidationException("Acquisition/disposal reference authority must match the complete authoritative ledger-book and period scope.");
        if (command.AcquisitionLot?.Acquisition is { } acquisition && security.Currency != acquisition.AcquisitionCurrency)
            throw new LedgerValidationException("Acquisition reference authority currency must match the retained acquisition currency.");
    }
}
