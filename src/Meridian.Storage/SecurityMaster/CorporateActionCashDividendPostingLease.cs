using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Server-created, invocation-bound authority for one exact native candidate. Its internal
/// constructor and sealed type prevent HTTP inputs from manufacturing a posting capability.
/// </summary>
public sealed class CorporateActionCashDividendPostingLease
{
    private readonly Func<CancellationToken, Task> _ensureCurrent;
    private readonly Func<string?, Guid, long, DateTimeOffset, CancellationToken, Task> _ensureCanonicalInstrument;
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private bool _active = true;
    private Guid? _journalEntryId;
    private Guid? _postedSpineJournalEntryId;

    internal CorporateActionCashDividendPostingLease(
        CorporateActionCashDividendAttestationDto attestation,
        CorporateActionCaseAccountingProjectionDto projection,
        CashDividendAttestationDeclarationDto declaration,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Func<CancellationToken, Task> ensureCurrent,
        Func<string?, Guid, long, DateTimeOffset, CancellationToken, Task> ensureCanonicalInstrument)
    {
        Attestation = attestation;
        Projection = projection;
        Declaration = declaration;
        _connection = connection;
        _transaction = transaction;
        _ensureCurrent = ensureCurrent;
        _ensureCanonicalInstrument = ensureCanonicalInstrument;
    }

    public CorporateActionCashDividendAttestationDto Attestation { get; }
    public CorporateActionCaseAccountingProjectionDto Projection { get; }
    public CashDividendAttestationDeclarationDto Declaration { get; }

    /// <summary>Checks the exact candidate and rereads native authority within the held transaction.</summary>
    public async Task EnsureCurrentAsync(string actor, Guid ledgerBookId, Guid periodId,
        string draftedCandidateFingerprint, string postingIntentHash, CancellationToken ct = default)
    {
        if (!_active || !string.Equals(actor?.Trim(), Attestation.AttestedBy.Trim(), StringComparison.OrdinalIgnoreCase)
            || ledgerBookId != Projection.LedgerBookId || periodId != Projection.PeriodId
            || !string.Equals(draftedCandidateFingerprint, Projection.DraftedCandidateFingerprint, StringComparison.Ordinal)
            || !string.Equals(postingIntentHash, Projection.PostingIntentHash, StringComparison.Ordinal))
            throw new CorporateActionValidationException("The economic attestation posting lease does not authorize this exact candidate.");
        await _ensureCurrent(ct).ConfigureAwait(false);
        if (!_active)
            throw new CorporateActionValidationException("The economic attestation posting lease has expired.");
    }

    internal void Deactivate() => _active = false;

    public Task EnsureCanonicalInstrumentAsync(string? symbol, Guid securityId, long expectedSecurityVersion,
        DateTimeOffset accountingTimestamp, CancellationToken ct = default)
    {
        if (!_active)
            throw new CorporateActionValidationException("The native instrument authority lease has expired.");
        return _ensureCanonicalInstrument(symbol, securityId, expectedSecurityVersion, accountingTimestamp, ct);
    }

    internal bool OwnsTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction) => _active
        && ReferenceEquals(connection, _connection) && ReferenceEquals(transaction, _transaction)
        && ReferenceEquals(transaction.Connection, connection);

    public async Task AppendJournalAsync(ITransactionalLedgerJournalStore store, LedgerJournalEntryWrite entry,
        CancellationToken ct = default)
    {
        if (!_active || _journalEntryId is not null || store is not PostgresLedgerJournalStore native)
            throw new CorporateActionValidationException("One unused current native PostgreSQL lease is required for an attested journal.");
        await native.AppendCashDividendAttestedAsync(_connection, _transaction, entry, this, ct).ConfigureAwait(false);
        _journalEntryId = entry.Entry.JournalEntryId;
    }

    public async Task<LedgerJournalEntryRecord?> GetJournalAsync(ITransactionalLedgerJournalStore store,
        Guid journalEntryId, CancellationToken ct = default)
    {
        if (!_active || store is not PostgresLedgerJournalStore native)
            throw new CorporateActionValidationException("A current native PostgreSQL lease is required to read an uncommitted attested journal.");
        var result = await native.GetJournalEntryWithinTransactionAsync(_connection, _transaction, journalEntryId, ct).ConfigureAwait(false);
        if (result is not null && (result.AggregateId != Projection.LedgerBookId
            || result.PeriodId != Projection.PeriodId || result.SourceEventId != Projection.AccountingEventId))
            throw new CorporateActionValidationException("The uncommitted journal does not belong to this attested source boundary.");
        return result;
    }

    public async Task<AssetAccountingEventAppendResult> AppendSpineAsync(ITransactionalAssetAccountingEventProjectionStore store,
        AssetAccountingEventSpineDto projection, long expectedSpineVersion, long expectedPositionVersion,
        CancellationToken ct = default)
    {
        EnsureSpineIdentity(projection.EventId, projection.EventVersion);
        if (store is not PostgresAssetOperationsProjectionStore)
            throw new CorporateActionValidationException("The native dividend transaction requires the native asset event store.");
        var result = await store.AppendAsync(_connection, _transaction, projection, expectedSpineVersion, expectedPositionVersion, this, ct).ConfigureAwait(false);
        if (projection.PostedJournalImpact is { } impact)
            _postedSpineJournalEntryId = impact.JournalEntryId;
        return result;
    }

    public Task<AssetAccountingEventProjectionRecord?> GetLatestSpineAsync(ITransactionalAssetAccountingEventProjectionStore store,
        Guid eventId, long eventVersion, CancellationToken ct = default)
    {
        EnsureSpineIdentity(eventId, eventVersion);
        return store.GetLatestAsync(_connection, _transaction, eventId, eventVersion, ct);
    }

    private void EnsureSpineIdentity(Guid eventId, long eventVersion)
    {
        if (!_active || eventId != Projection.AccountingEventId || eventVersion != Projection.AccountingEventVersion)
            throw new CorporateActionValidationException("The current native lease authorizes only its bound accounting event stream.");
    }

    internal void EnsureCompleted(Guid journalEntryId)
    {
        if (!_active || _journalEntryId != journalEntryId || _postedSpineJournalEntryId != journalEntryId)
            throw new CorporateActionValidationException("Native journal, posted lifecycle and case receipt must complete in the same transaction.");
    }
}
