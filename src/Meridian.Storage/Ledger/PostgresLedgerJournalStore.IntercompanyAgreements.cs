using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    public async Task ValidateIntercompanyAgreementReferencesAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        RequireWriteTenant();
        IntercompanyAgreementPostingGuard.ValidateShape(write.Entry);
        if (!IntercompanyAgreementPostingGuard.HasReferences(write.Entry))
            return;
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using var authority = await AcquireAgreementAuthorityAsync(connection, transaction, write, ct).ConfigureAwait(false);
        await ValidateAgreementReversalNotAlreadyPostedAsync(connection, transaction, write, ct).ConfigureAwait(false);
    }

    private async Task ValidateAgreementReversalNotAlreadyPostedAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite write, CancellationToken ct)
    {
        if (!IntercompanyAgreementPostingGuard.HasReferences(write.Entry) ||
            write.PostingCommand?.Intent != AccountingPostingIntentDto.Reversal)
            return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select exists(select 1 from {Qualified("journal_entries")} je
            join {Qualified("ledger_event_audit_events")} a on a.subject_id = je.journal_entry_id
            where je.source_journal_entry_id = @source and a.subject_kind = 'journal'
              and a.action = 'JournalReversed');
            """;
        command.Parameters.AddWithValue("source", write.SourceJournalEntryId!.Value);
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
            throw new LedgerValidationException("The immutable source journal already has a committed reversal.");
    }

    private async Task<bool> TryRecoverAgreementPostingAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite write, CancellationToken ct)
    {
        if (!IntercompanyAgreementPostingGuard.HasReferences(write.Entry))
            return false;
        var collisions = await FindPostingIdentityCollisionsAsync(connection, transaction,
            LedgerPostingIdentity.FromWrite(write), ct).ConfigureAwait(false);
        if (collisions.Count == 0)
            return false;
        if (collisions.Count != 1 || !RetainedPostingEquivalence.Matches(collisions[0], write, out _))
            throw new LedgerValidationException("Agreement posting identity collision differs from the immutable committed journal.");
        var retainedPeriod = await LoadPeriodAsync(connection, transaction, collisions[0].PeriodId,
            forUpdate: false, callerTenantId: ResolveCallerTenant(), ct: ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The retained agreement posting period is unavailable.");
        if (retainedPeriod.LedgerBookId != write.LedgerBookId || write.LedgerBookId is not { } bookId)
            throw new LedgerValidationException("Agreement posting recovery requires the original ledger book.");
        await ReadAgreementPostingBookTenantAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
        _ = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<IAsyncDisposable?> AcquireAgreementAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite write, CancellationToken ct,
        Meridian.Contracts.FundStructure.LegalEntityReadAuthorityProof? structureAuthority = null)
    {
        var request = await BuildAgreementAuthorityRequestAsync(connection, transaction, write, ct, structureAuthority).ConfigureAwait(false);
        if (request is null)
            return null;
        var authority = _intercompanyAgreementAuthority?.Invoke()
            ?? throw new LedgerValidationException("Authoritative intercompany agreement validation is unavailable; posting is blocked.");
        try
        {
            return await authority.AcquireValidatedLeaseAsync(request, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("Agreement authority did not retain its validation lease.");
        }
        catch (InvalidOperationException exception) { throw new LedgerValidationException(exception.Message); }
    }

    private async Task<IntercompanyAgreementPostingRequest?> BuildAgreementAuthorityRequestAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite write, CancellationToken ct,
        Meridian.Contracts.FundStructure.LegalEntityReadAuthorityProof? structureAuthority = null)
    {
        if (!IntercompanyAgreementPostingGuard.HasReferences(write.Entry))
            return null;
        if (write.LedgerBookId is not { } bookId)
            throw new LedgerValidationException("Agreement posting requires an authoritative ledger-book identity.");
        // Book locks precede independent agreement/structure locks and the ledger audit head.
        var tenant = await ReadAgreementPostingBookTenantAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, bookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The agreement posting ledger book is unavailable.");
        var period = await LoadPeriodAsync(connection, transaction, write.PeriodId, forUpdate: false,
            callerTenantId: ResolveCallerTenant(), ct: ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The agreement posting period is unavailable.");
        if (period.LedgerBookId != bookId)
            throw new LedgerValidationException("The agreement posting period must belong to the retained ledger book.");

        var verifiedReversal = false;
        if (write.PostingCommand?.Intent == AccountingPostingIntentDto.Reversal)
        {
            if (write.SourceJournalEntryId is not { } sourceId)
                throw new LedgerValidationException("Agreement reversal requires immutable source-journal lineage.");
            var records = await QueryAsync(connection, transaction,
                new LedgerJournalEntryQuery(LedgerBookId: bookId, JournalEntryId: sourceId), null, ct).ConfigureAwait(false);
            if (records.Count != 1 || !IntercompanyAgreementPostingGuard.IsExactSourceReversal(write, records[0]))
                throw new LedgerValidationException("Agreement reversal must be the exact inverse of every immutable source line, account, dimension and currency fact in the same book.");
            verifiedReversal = true;
        }
        var request = IntercompanyAgreementPostingGuard.BuildRequest(write, book, tenant, verifiedReversal);
        request.StructureAuthorityProof = structureAuthority;
        return request;
    }

    private async Task<IAsyncDisposable?> AcquireCanonicalBatchAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct)
    {
        var requested = new List<(LedgerPostingAuthorityContext Context, IntercompanyAgreementPostingRequest Request)>();
        foreach (var context in contexts.Where(item => item.Book.LedgerBookId != Guid.Empty))
        {
            var request = await BuildAgreementAuthorityRequestAsync(connection, transaction, context.Write, ct,
                context.StructureAuthorityProof).ConfigureAwait(false);
            if (request is not null)
                requested.Add((context, request));
        }
        if (requested.Count == 0)
            return null;
        var authority = _intercompanyAgreementAuthority?.Invoke()
            ?? throw new LedgerValidationException("Authoritative intercompany agreement validation is unavailable; posting is blocked.");
        try
        {
            var lease = (authority is IIntercompanyAgreementBatchPostingAuthority batch
                ? await batch.AcquireValidatedBatchLeaseAsync(requested.Select(item => item.Request).ToArray(), ct).ConfigureAwait(false)
                : requested.Count == 1 ? await authority.AcquireValidatedLeaseAsync(requested[0].Request, ct).ConfigureAwait(false)
                    : throw new LedgerValidationException("Native agreement authority cannot retain one lease for this multi-journal transaction."))
                ?? throw new LedgerValidationException("Agreement authority did not retain its validation lease.");
            if (lease is Meridian.Storage.FundStructure.IIntercompanyAgreementPostingLease retained)
                foreach (var item in requested)
                    item.Context.VerifiedCanonicalAgreementRevisions = retained.Agreements.SelectMany(state => state.Revisions).ToArray();
            return lease;
        }
        catch (InvalidOperationException exception)
        {
            throw new LedgerValidationException(exception.Message);
        }
    }

    private async Task<string> ReadAgreementPostingBookTenantAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid bookId, CancellationToken ct)
    {
        await EnsureBookWriteAuthorityAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select tenant_id from {Qualified("ledger_books")} where ledger_book_id = @id for share";
        command.Parameters.AddWithValue("id", bookId);
        var tenant = (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string)?.Trim();
        var caller = ResolveCallerTenant()?.Trim();
        if (string.IsNullOrWhiteSpace(tenant) || tenant.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(caller) && !string.Equals(caller, tenant, StringComparison.OrdinalIgnoreCase)))
            throw new LedgerValidationException("Agreement posting requires the authoritative book and caller to have the same retained tenant.");
        return tenant;
    }
}
