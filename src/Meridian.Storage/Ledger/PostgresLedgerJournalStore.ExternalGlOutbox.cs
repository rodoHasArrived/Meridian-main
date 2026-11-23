using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Integrity;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    public async Task<ExternalGlPostingPackage?> DecideExternalGlPackageAsync(ExternalGlPostingScope scope,
        string packageId, bool approve, ExternalGlPostingDecision decision, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockExternalGlBookAsync(connection, transaction, scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId, write: false, ct).ConfigureAwait(false);
        await RequireExternalGlPeriodAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        await using (var rowLock = new NpgsqlCommand($"""
            select 1 from {Qualified("external_gl_posting_packages")} p
            where p.package_id = @package and {ExternalGlScopeSql} for update
            """, connection, transaction))
        {
            BindScope(rowLock, scope, packageId);
            if (await rowLock.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                return null;
        }

        var package = await ReadExternalGlPackageAsync(connection, transaction, scope, packageId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The external GL package is unavailable.");
        if (!string.Equals(package.PayloadHash, decision.PayloadHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Review the current package before certifying or approving it.");
        if ((approve ? package.Approval : package.Certification) is not null)
            return package;
        if (approve && package.Certification?.PayloadHash != package.PayloadHash)
            throw new InvalidOperationException("Certify this exact package before approving it.");

        if (approve && string.Equals(package.PreparedBy, decision.Actor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Approval requires an operator other than the preparer.");

        var policy = await ReadExternalGlPolicyAsync(connection, transaction, scope.LedgerBookId, ct).ConfigureAwait(false);
        if (policy?.Destination != package.Payload.Destination)
            throw new InvalidOperationException("The external destination changed. Prepare a new package.");
        await using (var retain = new NpgsqlCommand($"""
            insert into {Qualified("external_gl_posting_decisions")} (package_id, decision_kind, decision)
            values (@package, @kind, @decision)
            """, connection, transaction))
        {
            retain.Parameters.AddWithValue("package", packageId);
            retain.Parameters.AddWithValue("kind", approve ? "Approval" : "Certification");
            retain.Parameters.AddWithValue("decision", SerializeExternalGl(decision));
            await retain.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (approve)
        {
            try
            {
                foreach (var document in package.Payload.Documents.OrderBy(doc => doc.MeridianJournalId))
                    await EnqueueExternalGlDocumentAsync(connection, transaction, package, document, ct).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new InvalidOperationException("An approved package already controls one or more of these journals for this external ledger.", ex);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return approve ? package with { Approval = decision } : package with { Certification = decision };
    }

    private async Task EnqueueExternalGlDocumentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ExternalGlPostingPackage package, ExternalGlPostingDocument document, CancellationToken ct)
    {
        var destination = package.Payload.Destination;
        if (document.SupersedesIdempotencyKey is not null)
        {
            await using var supersede = new NpgsqlCommand($"""
                update {Qualified("external_gl_posting_outbox")}
                set superseded = true
                where package_id = @predecessor and idempotency_key = @previous and journal_entry_id = @journal
                  and provider_id = @provider and external_ledger_id = @external and environment = @environment
                  and status = 'Rejected' and not superseded
                """, connection, transaction);
            supersede.Parameters.AddWithValue("predecessor", package.Payload.PredecessorPackageId!);
            supersede.Parameters.AddWithValue("previous", document.SupersedesIdempotencyKey);
            supersede.Parameters.AddWithValue("journal", document.MeridianJournalId);
            BindDestination(supersede, destination);
            if (await supersede.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("A correction must reference the current, definitively rejected journal.");
        }

        await using var enqueue = new NpgsqlCommand($"""
            insert into {Qualified("external_gl_posting_outbox")}
                (idempotency_key, package_id, journal_entry_id, provider_id, external_ledger_id, environment)
            select @key, p.package_id, j.journal_entry_id, @provider, @external, @environment
            from {Qualified("external_gl_posting_packages")} p
            join {Qualified("journal_entries")} j on j.period_id = p.period_id
            left join {Qualified("journal_entries")} original on original.journal_entry_id = j.source_journal_entry_id
            where p.package_id = @package and j.journal_entry_id = @journal and j.posting_kind <> 'ClosingEntry'
              and original.posting_kind is distinct from 'ClosingEntry'
            """, connection, transaction);
        enqueue.Parameters.AddWithValue("key", document.IdempotencyKey);
        enqueue.Parameters.AddWithValue("package", package.PackageId);
        enqueue.Parameters.AddWithValue("journal", document.MeridianJournalId);
        BindDestination(enqueue, destination);
        if (await enqueue.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("An external GL document must reference an eligible journal in the exact accounting period.");
    }

    public async Task<ExternalGlOutboxClaim?> ClaimExternalGlDocumentAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var lease = Guid.NewGuid();
        await using var command = new NpgsqlCommand($"""
            with due as (
                select o.idempotency_key from {Qualified("external_gl_posting_outbox")} o
                where o.status in ('Pending', 'OutcomeUnknown') and not o.superseded
                  and o.next_check_at <= clock_timestamp() and (o.lease_until is null or o.lease_until <= clock_timestamp())
                order by o.next_check_at, o.idempotency_key limit 1 for update skip locked
            )
            update {Qualified("external_gl_posting_outbox")} o
            set lease_token = @lease, lease_until = clock_timestamp() + interval '2 minutes'
            from due, {Qualified("external_gl_posting_packages")} p,
                 {Qualified("external_gl_posting_decisions")} a, {Qualified("external_gl_posting_decisions")} c
            where o.idempotency_key = due.idempotency_key and p.package_id = o.package_id
              and a.package_id = p.package_id and a.decision_kind = 'Approval'
              and c.package_id = p.package_id and c.decision_kind = 'Certification'
            returning o.idempotency_key, o.post_attempts, o.provider_acknowledged, o.status,
                      p.payload, p.payload_hash, a.decision, c.decision
            """, connection);
        command.Parameters.AddWithValue("lease", lease);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var key = reader.GetString(0);
        var payload = reader.GetString(4);
        var hash = reader.GetString(5);
        if (Sha256Digest.ComputeUtf8(payload) != hash ||
            DeserializeExternalGl<ExternalGlPostingDecision>(reader.GetString(6)).PayloadHash != hash ||
            DeserializeExternalGl<ExternalGlPostingDecision>(reader.GetString(7)).PayloadHash != hash)
            throw new InvalidDataException("The external GL outbox item does not match its certification and approval.");
        var retained = DeserializeExternalGl<ExternalGlPostingPayload>(payload);
        return new ExternalGlOutboxClaim(lease, retained.Scope, retained.Destination,
            retained.Documents.Single(doc => doc.IdempotencyKey == key), reader.GetInt32(1), reader.GetBoolean(2),
            Enum.Parse<ExternalGlPostingStatus>(reader.GetString(3)));
    }

    /// <summary>Persists the attempt before any request can cross the network boundary.</summary>
    public async Task<bool> MarkExternalGlSubmittingAsync(ExternalGlOutboxClaim claim, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            update {Qualified("external_gl_posting_outbox")}
            set post_attempts = post_attempts + 1, last_post_at = clock_timestamp(), status = 'OutcomeUnknown'
            where {ExternalGlLeaseSql}
            """, connection);
        BindLease(command, claim);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> RecordExternalGlReceiptAsync(ExternalGlOutboxClaim claim, ExternalGlReceiptObservation observation,
        ExternalGlPostingStatus status, bool keepLease, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            with updated as (
                update {Qualified("external_gl_posting_outbox")}
                set status = @status, provider_acknowledged = provider_acknowledged or @acknowledged,
                    lease_token = case when @keep then lease_token else null end,
                    lease_until = case when @keep then lease_until else null end,
                    next_check_at = clock_timestamp() + interval '1 minute' * greatest(1, least(post_attempts, 15))
                where {ExternalGlLeaseSql}
                returning idempotency_key
            )
            insert into {Qualified("external_gl_posting_receipts")} (idempotency_key, observation)
            select idempotency_key, @observation from updated
            """, connection);
        BindLease(command, claim);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("keep", keepLease);
        command.Parameters.AddWithValue("acknowledged", observation.Receipt.Kind == ExternalGlReceiptKind.Pending ||
            observation.Receipt.ProviderDocumentId is not null || observation.Receipt.ProviderBatchId is not null ||
            observation.Receipt.ErrorCode is ExternalGlErrorCode.DuplicateReference or ExternalGlErrorCode.ValidationFailed);
        command.Parameters.AddWithValue("observation", SerializeExternalGl(observation));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private const string ExternalGlLeaseSql = "idempotency_key = @key and lease_token = @lease and lease_until > clock_timestamp()";

    private static void BindLease(NpgsqlCommand command, ExternalGlOutboxClaim claim)
    {
        command.Parameters.AddWithValue("key", claim.Document.IdempotencyKey);
        command.Parameters.AddWithValue("lease", claim.LeaseToken);
    }
}

public sealed record ExternalGlOutboxClaim(Guid LeaseToken, ExternalGlPostingScope Scope, ExternalGlDestination Destination,
    ExternalGlPostingDocument Document, int PostAttempts, bool ProviderAcknowledged, ExternalGlPostingStatus Status);
