using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <summary>Native and ordinary writers both acquire entitlement before graph authority.</summary>
    internal async Task<IAsyncDisposable> AcquireNativeBookStructureAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid ledgerBookId, CancellationToken ct)
    {
        // Keep this ordering indivisible at the producer: taking graph first can cycle with
        // an ordinary entitlement holder and a queued legal-entity metadata writer.
        await LockCashDividendBookAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        return await AcquireNativeStructureAuthorityAsync(ct).ConfigureAwait(false);
    }

    internal Task<IAsyncDisposable> AcquireNativeStructureAuthorityAsync(CancellationToken ct)
        => (_postingAuthorities?.Invoke().OfType<ILedgerStructurePostingAuthority>().SingleOrDefault())
            ?.AcquireStructureAuthorityAsync(ct) ?? Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance);
    /// <summary>Serializes complete-book entitlement reads with every governed ledger append.</summary>
    public async Task LockCashDividendBookAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid ledgerBookId, CancellationToken ct = default)
    {
        EnsureCashDividendTransactionBackend(connection, transaction);
        // Gate the complete-book producer before dividend/book/position authority. The stronger
        // table mode waits for every active append before queuing exclusive dividend authority;
        // their separate-connection canonical readers can still take shared book authority.
        // AppendCore requests only the weaker ordinary mode, so no later upgrade is needed.
        await LockCouponHistoryBoundaryAsync(connection, transaction, historySensitive: true, ct).ConfigureAwait(false);
        await CashDividendEntitlementLock.AcquireAsync(connection, transaction, ledgerBookId, true, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The producer reads the book. SHARE prevents direct UPDATE/DELETE while coexisting
        // with caller-owned append transactions that already retained book read authority.
        command.CommandText = $"select base_currency from {Qualified("ledger_books")} where ledger_book_id=@book for share";
        command.Parameters.AddWithValue("book", ledgerBookId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string currency || currency != "USD")
            throw new LedgerValidationException("The selected native dividend book is unavailable or is not USD.");
    }

    /// <summary>The only journal append which accepts native preparer authority.</summary>
    internal async Task AppendCashDividendAttestedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry, CorporateActionCashDividendPostingLease lease, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(lease);
        EnsureCashDividendTransactionBackend(connection, transaction);
        if (!lease.OwnsTransaction(connection, transaction)
            || entry.PostingCommand is not { CashDividendAttestation: { } binding } command
            || command.ApprovalState != AccountingPostingApprovalStateDto.PreparerAttested
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(binding.Receipt), JsonSerializer.SerializeToElement(lease.Attestation))
            || binding.Receipt.DeclarationId != lease.Declaration.DeclarationId
            || binding.Receipt.DeclarationRevision != lease.Declaration.Revision
            || binding.EventId != lease.Projection.AccountingEventId
            || binding.EventVersion != lease.Projection.AccountingEventVersion
            || binding.LedgerBookId != lease.Projection.LedgerBookId
            || binding.PeriodId != lease.Projection.PeriodId
            || binding.TenantId != lease.Declaration.TenantId || binding.CompanyId != lease.Declaration.CompanyId
            || binding.DraftedCandidateFingerprint != lease.Projection.DraftedCandidateFingerprint
            || binding.PostingIntentHash != lease.Projection.PostingIntentHash
            || command.SourceEventId != lease.Projection.AccountingEventId
            || entry.SourceEventId != lease.Projection.AccountingEventId
            || entry.AggregateId != lease.Projection.LedgerBookId
            || entry.PeriodId != lease.Projection.PeriodId
            || entry.Entry.Lines.Sum(line => line.Debit) != lease.Attestation.GrossImpactUsd)
            throw new LedgerValidationException("A cash-dividend journal requires its active native producer lease and exact retained attestation.");
        var actor = command.Actor;
        if (string.IsNullOrWhiteSpace(actor))
            throw new LedgerValidationException("A cash-dividend journal requires an identified posting actor.");
        await lease.EnsureCurrentAsync(actor, lease.Projection.LedgerBookId, entry.PeriodId,
            lease.Projection.DraftedCandidateFingerprint, lease.Projection.PostingIntentHash, ct).ConfigureAwait(false);
        RejectJournalOnlySuccessorPosting(entry);
        // Only this producer-verified path may normalize native preparer authority. Generic
        // eligibility remains mandatory and is retained by the outer case transaction's lease.
        var postingAuthority = await AcquirePostingAuthorityCoreAsync(connection, transaction, [entry], true, ct,
            lease.StructureAuthorityLease).ConfigureAwait(false);
        try
        { lease.RetainPostingAuthority(postingAuthority); }
        catch { await postingAuthority.DisposeAsync().ConfigureAwait(false); throw; }
        await AppendCoreAsync(connection, transaction, entry, ct, verifiedCashDividendAttestation: true).ConfigureAwait(false);
    }

    public async Task<LedgerJournalEntryRecord?> GetJournalEntryWithinTransactionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid journalEntryId, CancellationToken ct = default)
    {
        EnsureCashDividendTransactionBackend(connection, transaction);
        var query = new LedgerJournalEntryQuery(JournalEntryId: journalEntryId);
        var records = await QueryAsync(connection, transaction, query, ValidateJournalQuery(query), ct).ConfigureAwait(false);
        return records.SingleOrDefault();
    }

    public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryWithinCashDividendTransactionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryQuery query, CancellationToken ct = default)
    {
        EnsureCashDividendTransactionBackend(connection, transaction);
        return QueryAsync(connection, transaction, query, ValidateJournalQuery(query), ct);
    }

    private void EnsureCashDividendTransactionBackend(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection))
            throw new LedgerValidationException("The native economic posting transaction has expired or changed connection.");
        var configured = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        var database = string.IsNullOrWhiteSpace(configured.Database) ? configured.Username : configured.Database;
        if (!string.Equals(configured.Host, connection.Host, StringComparison.OrdinalIgnoreCase)
            || configured.Port != connection.Port || !string.Equals(database, connection.Database, StringComparison.Ordinal))
            throw new LedgerValidationException("Economic attestation requires ledger, asset and case authority on the same PostgreSQL backend.");
    }
}
