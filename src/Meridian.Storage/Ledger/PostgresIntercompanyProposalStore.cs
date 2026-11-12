using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>Durable retry leases and terminal receipts around immutable journal-backed source facts.</summary>
public sealed class PostgresIntercompanyProposalStore(LedgerJournalStoreOptions options) : IIntercompanyProposalStore
{
    private string Table => $"{Quote(options.SchemaName)}.intercompany_proposals";
    private string Links => $"{Quote(options.SchemaName)}.intercompany_proposal_links";
    private string Reservations => $"{Quote(options.SchemaName)}.intercompany_counterpart_reservations";
    private string Projection => $"""
        p.*, (select jsonb_build_object('tenantId',l.tenant_id,'requestId',l.request_id,'proposalId',l.proposal_id,
            'targetBookId',l.target_book_id,'targetJournalId',l.target_journal_id,'actor',l.actor,'reason',l.reason,
            'recordedAt',l.recorded_at,'rejectedDraftId',l.rejected_draft_id,'rejectedDraftVersion',l.rejected_draft_version)
            from {Links} l where l.tenant_id=p.tenant_id and l.proposal_id=p.proposal_id
            order by l.recorded_at desc,l.request_id desc limit 1)::text as latest_link
        """;
    private string Retries => $"{Quote(options.SchemaName)}.intercompany_proposal_retries";
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
    public async Task<IReadOnlyList<IntercompanyProposalDto>> ListAsync(string tenantId, Guid sourceBookId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {Projection} from {Table} p where p.tenant_id=@tenant and p.source_book_id=@book order by p.recorded_at,p.proposal_id";
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("book", sourceBookId);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }
    public async Task<IntercompanyProposalDto?> GetAsync(string tenantId, Guid proposalId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {Projection} from {Table} p where p.tenant_id=@tenant and p.proposal_id=@id";
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", proposalId);
        return (await ReadManyAsync(command, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<IReadOnlyList<IntercompanyProposalDto>> ClaimAsync(string workerId, int limit, TimeSpan lease, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (limit is < 1 or > 100 || lease <= TimeSpan.Zero || lease > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            with selected as (
                select proposal_id from {Table}
                where status in ('Pending','NeedsFix') and next_attempt_at <= now()
                    and (lease_until is null or lease_until < now())
                order by recorded_at,proposal_id for update skip locked limit @limit)
            update {Table} p set claim_token=@token,lease_until=now()+@lease,
                attempts=attempts+1 from selected s where p.proposal_id=s.proposal_id returning p.*
            """;
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("token", workerId + ":" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("lease", lease);
        return await ReadManyAsync(command, ct).ConfigureAwait(false);
    }
    public async Task CompleteAsync(string tenantId, Guid proposalId, string claimToken, Guid targetBookId,
        Guid targetJournalId, CancellationToken ct = default)
    {
        if (targetBookId == Guid.Empty || targetJournalId == Guid.Empty) throw new ArgumentException("Target identities are required.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var current = await LockProposalAsync(connection, transaction, tenantId, proposalId, ct).ConfigureAwait(false);
        if (current is null || current.Status is not ("Pending" or "NeedsFix") || !current.HasActiveLease || current.ClaimToken != claimToken)
            throw new InvalidOperationException("Counterpart delivery lease expired or was superseded; retry the retained proposal.");
        await ReserveCounterpartAsync(connection, transaction, tenantId, proposalId, targetBookId, targetJournalId, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            update {Table} set status='Drafted',target_book_id=@book,target_journal_id=@journal,
                claim_token=null,lease_until=null,last_error=null where tenant_id=@tenant and proposal_id=@id
            """;
        Scope(command, tenantId, proposalId, claimToken);
        command.Parameters.AddWithValue("book", targetBookId);
        command.Parameters.AddWithValue("journal", targetJournalId);
        await ExecuteMutationAsync(command, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
    public async Task FailAsync(string tenantId, Guid proposalId, string claimToken, string error, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            update {Table} set status='NeedsFix',last_error=@error,claim_token=null,lease_until=null,
                next_attempt_at=now()+interval '1 minute'
            where tenant_id=@tenant and proposal_id=@id and claim_token=@token and lease_until>now()
                and status in ('Pending','NeedsFix')
            """;
        Scope(command, tenantId, proposalId, claimToken);
        command.Parameters.AddWithValue("error", error.Length > 2000 ? error[..2000] : error);
        if (await ExecuteMutationAsync(command, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Counterpart delivery lease is no longer current.");
    }
    public Task LinkAsync(string tenantId, Guid proposalId, LinkIntercompanyCounterpartRequest request,
        string actor, CancellationToken ct = default)
        => LinkCoreAsync(tenantId, proposalId, request, actor, null, ct);

    public Task LinkRejectedDraftAsync(string tenantId, Guid proposalId, LinkIntercompanyCounterpartRequest request,
        string actor, RejectedIntercompanyDraftEvidenceDto rejectedDraft, CancellationToken ct = default)
        => LinkCoreAsync(tenantId, proposalId, request, actor, rejectedDraft, ct);

    private async Task LinkCoreAsync(string tenantId, Guid proposalId, LinkIntercompanyCounterpartRequest request,
        string actor, RejectedIntercompanyDraftEvidenceDto? rejectedDraft, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.RequestId == Guid.Empty || request.TargetBookId == Guid.Empty || request.TargetJournalId == Guid.Empty)
            throw new ArgumentException("Link request and target identities are required.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var current = await LockProposalAsync(connection, transaction, tenantId, proposalId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained source proposal is unavailable in this tenant.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select proposal_id,target_book_id,target_journal_id,actor,reason from {Links}
            where tenant_id=@tenant and request_id=@request;
            """;
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("request", request.RequestId);
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.GetGuid(0) != proposalId || reader.GetGuid(1) != request.TargetBookId
                    || reader.GetGuid(2) != request.TargetJournalId || reader.GetString(3) != actor || reader.GetString(4) != request.Reason)
                    throw new InvalidOperationException("Link request identity already retains a different decision.");
                return;
            }
        }
        command.Parameters.Clear();
        command.CommandText = $"select exists(select 1 from {Links} where tenant_id=@tenant and proposal_id=@id)";
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", proposalId);
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
            throw new InvalidOperationException("A reviewed manual linkage is already retained. Linked-target replacement requires a separate governed correction workflow.");
        var replacement = current.Status == "Drafted";
        if (replacement || rejectedDraft is not null)
        {
            if (rejectedDraft is null || rejectedDraft.ProposalId != proposalId || rejectedDraft.SourceDigest != current.SourceDigest
                || (replacement && (rejectedDraft.LedgerBookId != current.TargetBook || rejectedDraft.JournalEntryId != current.TargetJournal))
                || (!replacement && (current.Status is not ("Pending" or "NeedsFix") || current.HasActiveLease || rejectedDraft.JournalEntryId != proposalId))
                || rejectedDraft.Version < 1 || rejectedDraft.LedgerBookId == Guid.Empty
                || request.TargetJournalId == rejectedDraft.JournalEntryId)
                throw new InvalidOperationException("Replacing a delivered target requires the exact source-bound rejected draft under its mutation authority.");
        }
        else if (current.Status is not ("Pending" or "NeedsFix") || current.HasActiveLease || rejectedDraft is not null)
            throw new InvalidOperationException("Only an unclaimed undelivered source or an independently verified rejected delivery may be linked.");
        command.Parameters.Clear();
        command.CommandText = $"select exists(select 1 from {Quote(options.SchemaName)}.journal_entries where journal_entry_id=@generated)";
        command.Parameters.AddWithValue("generated", rejectedDraft?.JournalEntryId ?? proposalId);
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))! && request.TargetJournalId != (rejectedDraft?.JournalEntryId ?? proposalId))
            throw new InvalidOperationException("The generated counterpart is already posted; recover its delivery instead of linking another amount.");
        await ReserveCounterpartAsync(connection, transaction, tenantId, proposalId, request.TargetBookId, request.TargetJournalId, ct).ConfigureAwait(false);
        if (!replacement)
        {
            command.Parameters.Clear();
            command.CommandText = $"""
                update {Table} set status='Linked',target_book_id=@book,target_journal_id=@journal,
                    claim_token=null,lease_until=null,last_error=null where tenant_id=@tenant and proposal_id=@id;
                """;
            Scope(command, tenantId, proposalId, "unused");
            command.Parameters.AddWithValue("book", request.TargetBookId);
            command.Parameters.AddWithValue("journal", request.TargetJournalId);
            await ExecuteMutationAsync(command, ct).ConfigureAwait(false);
        }
        command.Parameters.Clear();
        command.CommandText = $"""
            insert into {Links}(tenant_id,request_id,proposal_id,target_book_id,target_journal_id,actor,reason,rejected_draft_id,rejected_draft_version)
            values (@tenant,@request,@id,@book,@journal,@actor,@reason,@rejected,@version)
            """;
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("request", request.RequestId);
        command.Parameters.AddWithValue("id", proposalId);
        command.Parameters.AddWithValue("book", request.TargetBookId);
        command.Parameters.AddWithValue("journal", request.TargetJournalId);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", request.Reason);
        command.Parameters.AddWithValue("rejected", (object?)rejectedDraft?.JournalEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("version", (object?)rejectedDraft?.Version ?? DBNull.Value);
        await ExecuteMutationAsync(command, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private sealed record ProposalState(string Status, Guid? TargetBook, Guid? TargetJournal,
        string? ClaimToken, bool HasActiveLease, string SourceDigest);

    private async Task<ProposalState?> LockProposalAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tenant, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select status,target_book_id,target_journal_id,claim_token,coalesce(lease_until>now(),false),source_digest from {Table} where tenant_id=@tenant and proposal_id=@id for update";
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), reader.GetString(5)) : null;
    }

    private async Task ReserveCounterpartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tenant, Guid proposal, Guid book, Guid journal, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Reservations}(tenant_id,target_book_id,target_journal_id,proposal_id)
            values (@tenant,@book,@journal,@proposal) on conflict do nothing;
            select proposal_id from {Reservations} where tenant_id=@tenant and target_book_id=@book and target_journal_id=@journal;
            """;
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("book", book);
        command.Parameters.AddWithValue("journal", journal);
        command.Parameters.AddWithValue("proposal", proposal);
        var owner = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (owner is not Guid retained || retained != proposal)
            throw new InvalidOperationException("The counterpart journal is already reserved by another retained source event. Select its exact transaction rather than reusing the amount.");
    }

    public async Task RetryAsync(string tenantId, Guid proposalId, RetryIntercompanyProposalRequest request,
        string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        if (request.RequestId == Guid.Empty) throw new ArgumentException("Retry identity is required.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select proposal_id,actor,reason from {Retries} where tenant_id=@tenant and request_id=@request";
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("request", request.RequestId);
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.GetGuid(0) != proposalId || reader.GetString(1) != actor || reader.GetString(2) != request.Reason)
                    throw new InvalidOperationException("Retry identity already retains a different decision.");
                return;
            }
        }
        command.Parameters.AddWithValue("id", proposalId);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", request.Reason);
        command.CommandText = $"""
            update {Table} set status='Pending',next_attempt_at=now(),last_error=null
            where tenant_id=@tenant and proposal_id=@id and status in ('Pending','NeedsFix')
                and (lease_until is null or lease_until<now())
            """;
        if (await ExecuteMutationAsync(command, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Only an undelivered, unclaimed proposal can be retried.");
        command.CommandText = $"insert into {Retries}(tenant_id,request_id,proposal_id,actor,reason) values (@tenant,@request,@id,@actor,@reason)";
        await ExecuteMutationAsync(command, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteMutationAsync(NpgsqlCommand command, CancellationToken ct)
    {
        try { return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("The counterpart journal or request identity is already retained by another reviewed proposal. Reload and select the correct transaction.", error);
        }
    }

    private static void Scope(NpgsqlCommand command, string tenant, Guid id, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("token", token);
    }
    private static async Task<IReadOnlyList<IntercompanyProposalDto>> ReadManyAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var result = new List<IntercompanyProposalDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var latestOrdinal = Enumerable.Range(0, reader.FieldCount).Where(index => reader.GetName(index) == "latest_link").DefaultIfEmpty(-1).First();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var payload = reader.GetString(reader.GetOrdinal("source_payload"));
            var digest = reader.GetString(reader.GetOrdinal("source_digest"));
            if (!Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(payload), digest))
                throw new InvalidOperationException("Intercompany proposal source digest is invalid.");
            var source = JsonSerializer.Deserialize(payload, IntercompanyProposalJsonContext.Default.IntercompanyProposalSourceDto)
                ?? throw new InvalidOperationException("Intercompany proposal source is invalid.");
            if (source.ProposalId != reader.GetGuid(reader.GetOrdinal("proposal_id"))
                || source.TenantId != reader.GetString(reader.GetOrdinal("tenant_id"))
                || source.SourceBookId != reader.GetGuid(reader.GetOrdinal("source_book_id"))
                || source.SourceJournalId != reader.GetGuid(reader.GetOrdinal("source_journal_id")))
                throw new InvalidOperationException("Intercompany proposal indexed scope differs from retained source.");
            T? Optional<T>(string name) where T : struct => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetFieldValue<T>(reader.GetOrdinal(name));
            string? Text(string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(reader.GetOrdinal(name));
            result.Add(new(source, digest, Enum.Parse<IntercompanyProposalStatusDto>(reader.GetString(reader.GetOrdinal("status"))),
                reader.GetInt32(reader.GetOrdinal("attempts")), reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("recorded_at")),
                Optional<Guid>("target_book_id"), Optional<Guid>("target_journal_id"), Text("last_error"), Text("claim_token"),
                Optional<DateTimeOffset>("lease_until"))
            {
                LatestLink = latestOrdinal >= 0 && !reader.IsDBNull(latestOrdinal)
                    ? JsonSerializer.Deserialize(reader.GetString(latestOrdinal), IntercompanyProposalJsonContext.Default.IntercompanyCounterpartLinkDto) : null
            });
        }
        return result;
    }
}
