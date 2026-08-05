using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.DirectLending;

namespace Meridian.Storage.DirectLending;

public sealed partial class PostgresDirectLendingStateStore
{
    public async Task<OperationsWorkflowAuditRecord> AppendOperationsWorkflowAuditAsync(OperationsWorkflowAuditAppendRequest request, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var (lockKey1, lockKey2) = ComputeWorkflowAuditLockKeys(request.WorkflowId);

        await using (var workflowLock = connection.CreateCommand())
        {
            workflowLock.Transaction = transaction;
            workflowLock.CommandText = "select pg_advisory_xact_lock(@lock_key_1, @lock_key_2);";
            workflowLock.Parameters.AddWithValue("lock_key_1", lockKey1);
            workflowLock.Parameters.AddWithValue("lock_key_2", lockKey2);
            await workflowLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        string? previousHash = null;
        await using (var previous = connection.CreateCommand())
        {
            previous.Transaction = transaction;
            previous.CommandText =
                $"""
                with recursive workflow_entries as (
                    select hash, previous_hash
                    from {Qualified("operations_workflow_audit")}
                    where workflow_id = @workflow_id
                ),
                audit_chain as (
                    select hash, previous_hash
                    from workflow_entries
                    where previous_hash is null

                    union all

                    select successor.hash, successor.previous_hash
                    from workflow_entries as successor
                    inner join audit_chain as predecessor
                        on successor.previous_hash = predecessor.hash
                ),
                chain_tails as (
                    select candidate.hash
                    from audit_chain as candidate
                    where not exists (
                        select 1
                        from audit_chain as successor
                        where successor.previous_hash = candidate.hash)
                )
                select (select count(*) from workflow_entries) as stream_count,
                       (select count(*) from audit_chain) as reachable_count,
                       count(*) as tail_count,
                       min(hash) as tail_hash
                from chain_tails;
                """;
            previous.Parameters.AddWithValue("workflow_id", request.WorkflowId);
            await using var reader = await previous.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Operations workflow audit chain '{request.WorkflowId}' could not be inspected.");
            }

            var streamCount = reader.GetInt64(0);
            var reachableCount = reader.GetInt64(1);
            var tailCount = reader.GetInt64(2);
            previousHash = reader.IsDBNull(3) ? null : reader.GetString(3);

            var isEmptyChain = streamCount == 0 && reachableCount == 0 && tailCount == 0 && previousHash is null;
            var isLinearChain = streamCount > 0 && reachableCount == streamCount && tailCount == 1 && previousHash is not null;
            if (!isEmptyChain && !isLinearChain)
            {
                throw new InvalidOperationException(
                    $"Operations workflow audit chain '{request.WorkflowId}' is corrupt: " +
                    $"{streamCount} stored entries, {reachableCount} reachable entries, and {tailCount} tails were found.");
            }
        }

        var computedHash = ComputeOperationsWorkflowAuditHash(request, previousHash);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("operations_workflow_audit")} (
                    audit_id, occurred_at_utc, workflow_id, fund_account_id, period_id, event_type,
                    from_state, to_state, gate, from_gate_status, to_gate_status, actor, rationale,
                    trace_id, request_id, session_id, run_id,
                    broker_reference_id, security_reference_id, ledger_reference_id, reconciliation_reference_id, evidence_reference_id, audit_reference_id,
                    hash, previous_hash, severity, tags)
                values (
                    @audit_id, @occurred_at_utc, @workflow_id, @fund_account_id, @period_id, @event_type,
                    @from_state, @to_state, @gate, @from_gate_status, @to_gate_status, @actor, @rationale,
                    @trace_id, @request_id, @session_id, @run_id,
                    @broker_reference_id, @security_reference_id, @ledger_reference_id, @reconciliation_reference_id, @evidence_reference_id, @audit_reference_id,
                    @hash, @previous_hash, @severity, @tags);
                """;
            insert.Parameters.AddWithValue("audit_id", request.AuditId);
            insert.Parameters.AddWithValue("occurred_at_utc", request.OccurredAtUtc.UtcDateTime);
            insert.Parameters.AddWithValue("workflow_id", request.WorkflowId);
            insert.Parameters.AddWithValue("fund_account_id", request.FundAccountId);
            insert.Parameters.AddWithValue("period_id", request.PeriodId);
            insert.Parameters.AddWithValue("event_type", request.EventType);
            insert.Parameters.AddWithValue("from_state", (object?)request.FromState ?? DBNull.Value);
            insert.Parameters.AddWithValue("to_state", (object?)request.ToState ?? DBNull.Value);
            insert.Parameters.AddWithValue("gate", (object?)request.Gate ?? DBNull.Value);
            insert.Parameters.AddWithValue("from_gate_status", (object?)request.FromGateStatus ?? DBNull.Value);
            insert.Parameters.AddWithValue("to_gate_status", (object?)request.ToGateStatus ?? DBNull.Value);
            insert.Parameters.AddWithValue("actor", request.Actor);
            insert.Parameters.AddWithValue("rationale", request.Rationale);
            insert.Parameters.AddWithValue("trace_id", (object?)request.TraceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("request_id", (object?)request.RequestId ?? DBNull.Value);
            insert.Parameters.AddWithValue("session_id", (object?)request.SessionId ?? DBNull.Value);
            insert.Parameters.AddWithValue("run_id", (object?)request.RunId ?? DBNull.Value);
            insert.Parameters.AddWithValue("broker_reference_id", (object?)request.BrokerReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("security_reference_id", (object?)request.SecurityReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("ledger_reference_id", (object?)request.LedgerReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("reconciliation_reference_id", (object?)request.ReconciliationReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("evidence_reference_id", (object?)request.EvidenceReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("audit_reference_id", (object?)request.AuditReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("hash", computedHash);
            insert.Parameters.AddWithValue("previous_hash", (object?)previousHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("severity", request.Severity);
            insert.Parameters.AddWithValue("tags", request.Tags.ToArray());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return CreateOperationsWorkflowAuditRecord(request, computedHash, previousHash);
    }

    public async Task<IReadOnlyList<OperationsWorkflowAuditRecord>> GetOperationsWorkflowAuditAsync(string workflowId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            with recursive audit_chain as (
                select audit_id, workflow_id, hash, previous_hash, 0::bigint as chain_position
                from {Qualified("operations_workflow_audit")}
                where workflow_id = @workflow_id
                  and previous_hash is null

                union all

                select successor.audit_id,
                       successor.workflow_id,
                       successor.hash,
                       successor.previous_hash,
                       predecessor.chain_position + 1
                from {Qualified("operations_workflow_audit")} as successor
                inner join audit_chain as predecessor
                    on successor.workflow_id = predecessor.workflow_id
                   and successor.previous_hash = predecessor.hash
            ),
            workflow_totals as (
                select count(*)::bigint as entry_count
                from {Qualified("operations_workflow_audit")}
                where workflow_id = @workflow_id
            )
            select audit.audit_id, audit.occurred_at_utc, audit.workflow_id, audit.fund_account_id, audit.period_id, audit.event_type,
                   audit.from_state, audit.to_state, audit.gate, audit.from_gate_status, audit.to_gate_status, audit.actor, audit.rationale,
                   audit.trace_id, audit.request_id, audit.session_id, audit.run_id,
                   audit.broker_reference_id, audit.security_reference_id, audit.ledger_reference_id, audit.reconciliation_reference_id, audit.evidence_reference_id, audit.audit_reference_id,
                   audit.hash, audit.previous_hash, audit.severity, audit.tags,
                   workflow_totals.entry_count
            from workflow_totals
            left join audit_chain
                on true
            left join {Qualified("operations_workflow_audit")} as audit
                on audit.audit_id = audit_chain.audit_id
            order by audit_chain.chain_position nulls last;
            """;
        command.Parameters.AddWithValue("workflow_id", workflowId);

        var results = new List<OperationsWorkflowAuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        long expectedCount = 0;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            expectedCount = reader.GetInt64(27);
            if (reader.IsDBNull(0))
            {
                continue;
            }

            results.Add(new OperationsWorkflowAuditRecord(
                reader.GetGuid(0),
                new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetString(24),
                reader.GetString(25),
                reader.GetFieldValue<string[]>(26)));
        }

        if (results.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Operations workflow audit chain '{workflowId}' is corrupt: " +
                $"{expectedCount} stored entries exist, but only {results.Count} are reachable from the genesis entry.");
        }

        return results;
    }

    private static string ComputeOperationsWorkflowAuditHash(OperationsWorkflowAuditAppendRequest request, string? previousHash)
    {
        var canonicalPayload = new OperationsWorkflowAuditHashPayload(
            request.AuditId,
            request.OccurredAtUtc.ToUniversalTime().ToString("O"),
            request.WorkflowId,
            request.FundAccountId,
            request.PeriodId,
            request.EventType,
            request.FromState,
            request.ToState,
            request.Gate,
            request.FromGateStatus,
            request.ToGateStatus,
            request.Actor,
            request.Rationale,
            request.TraceId,
            request.RequestId,
            request.SessionId,
            request.RunId,
            request.BrokerReferenceId,
            request.SecurityReferenceId,
            request.LedgerReferenceId,
            request.ReconciliationReferenceId,
            request.EvidenceReferenceId,
            request.AuditReferenceId,
            request.Severity,
            request.Tags.OrderBy(static tag => tag, StringComparer.Ordinal).ToArray(),
            previousHash ?? string.Empty);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            canonicalPayload,
            OperationsWorkflowAuditJsonContext.Default.OperationsWorkflowAuditHashPayload);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static (int LockKey1, int LockKey2) ComputeWorkflowAuditLockKeys(string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(workflowId.Trim()));
        return (
            BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(0, sizeof(int))),
            BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(sizeof(int), sizeof(int))));
    }

    private static OperationsWorkflowAuditRecord CreateOperationsWorkflowAuditRecord(
        OperationsWorkflowAuditAppendRequest request,
        string hash,
        string? previousHash) =>
        new(
            request.AuditId,
            request.OccurredAtUtc,
            request.WorkflowId,
            request.FundAccountId,
            request.PeriodId,
            request.EventType,
            request.FromState,
            request.ToState,
            request.Gate,
            request.FromGateStatus,
            request.ToGateStatus,
            request.Actor,
            request.Rationale,
            request.TraceId,
            request.RequestId,
            request.SessionId,
            request.RunId,
            request.BrokerReferenceId,
            request.SecurityReferenceId,
            request.LedgerReferenceId,
            request.ReconciliationReferenceId,
            request.EvidenceReferenceId,
            request.AuditReferenceId,
            hash,
            previousHash,
            request.Severity,
            request.Tags);

    private sealed record OperationsWorkflowAuditHashPayload(
        Guid AuditId,
        string OccurredAtUtc,
        string WorkflowId,
        Guid FundAccountId,
        string PeriodId,
        string EventType,
        string? FromState,
        string? ToState,
        string? Gate,
        string? FromGateStatus,
        string? ToGateStatus,
        string Actor,
        string Rationale,
        string? TraceId,
        string? RequestId,
        string? SessionId,
        string? RunId,
        string? BrokerReferenceId,
        string? SecurityReferenceId,
        string? LedgerReferenceId,
        string? ReconciliationReferenceId,
        string? EvidenceReferenceId,
        string? AuditReferenceId,
        string Severity,
        string[] Tags,
        string ChainPreviousHash);

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(OperationsWorkflowAuditHashPayload))]
    private sealed partial class OperationsWorkflowAuditJsonContext : JsonSerializerContext;
}
