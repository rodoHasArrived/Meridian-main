using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;

namespace Meridian.Storage.DirectLending;

public sealed partial class PostgresDirectLendingStateStore
{
    public async Task<OperationsWorkflowAuditRecord> AppendOperationsWorkflowAuditAsync(OperationsWorkflowAuditRecord record, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        string? previousHash = null;
        await using (var previous = connection.CreateCommand())
        {
            previous.Transaction = transaction;
            previous.CommandText =
                $"""
                select hash
                from {Qualified("operations_workflow_audit")}
                where workflow_id = @workflow_id
                order by occurred_at_utc desc, audit_id desc
                limit 1;
                """;
            previous.Parameters.AddWithValue("workflow_id", record.WorkflowId);
            previousHash = (string?)await previous.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        var computedHash = ComputeOperationsWorkflowAuditHash(record, previousHash);

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
            insert.Parameters.AddWithValue("audit_id", record.AuditId);
            insert.Parameters.AddWithValue("occurred_at_utc", record.OccurredAtUtc.UtcDateTime);
            insert.Parameters.AddWithValue("workflow_id", record.WorkflowId);
            insert.Parameters.AddWithValue("fund_account_id", record.FundAccountId);
            insert.Parameters.AddWithValue("period_id", record.PeriodId);
            insert.Parameters.AddWithValue("event_type", record.EventType);
            insert.Parameters.AddWithValue("from_state", (object?)record.FromState ?? DBNull.Value);
            insert.Parameters.AddWithValue("to_state", (object?)record.ToState ?? DBNull.Value);
            insert.Parameters.AddWithValue("gate", (object?)record.Gate ?? DBNull.Value);
            insert.Parameters.AddWithValue("from_gate_status", (object?)record.FromGateStatus ?? DBNull.Value);
            insert.Parameters.AddWithValue("to_gate_status", (object?)record.ToGateStatus ?? DBNull.Value);
            insert.Parameters.AddWithValue("actor", record.Actor);
            insert.Parameters.AddWithValue("rationale", record.Rationale);
            insert.Parameters.AddWithValue("trace_id", (object?)record.TraceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("request_id", (object?)record.RequestId ?? DBNull.Value);
            insert.Parameters.AddWithValue("session_id", (object?)record.SessionId ?? DBNull.Value);
            insert.Parameters.AddWithValue("run_id", (object?)record.RunId ?? DBNull.Value);
            insert.Parameters.AddWithValue("broker_reference_id", (object?)record.BrokerReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("security_reference_id", (object?)record.SecurityReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("ledger_reference_id", (object?)record.LedgerReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("reconciliation_reference_id", (object?)record.ReconciliationReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("evidence_reference_id", (object?)record.EvidenceReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("audit_reference_id", (object?)record.AuditReferenceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("hash", computedHash);
            insert.Parameters.AddWithValue("previous_hash", (object?)previousHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("severity", record.Severity);
            insert.Parameters.AddWithValue("tags", record.Tags.ToArray());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return record with { Hash = computedHash, PreviousHash = previousHash };
    }

    public async Task<IReadOnlyList<OperationsWorkflowAuditRecord>> GetOperationsWorkflowAuditAsync(string workflowId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select audit_id, occurred_at_utc, workflow_id, fund_account_id, period_id, event_type,
                   from_state, to_state, gate, from_gate_status, to_gate_status, actor, rationale,
                   trace_id, request_id, session_id, run_id,
                   broker_reference_id, security_reference_id, ledger_reference_id, reconciliation_reference_id, evidence_reference_id, audit_reference_id,
                   hash, previous_hash, severity, tags
            from {Qualified("operations_workflow_audit")}
            where workflow_id = @workflow_id
            order by occurred_at_utc, audit_id;
            """;
        command.Parameters.AddWithValue("workflow_id", workflowId);

        var results = new List<OperationsWorkflowAuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
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

        return results;
    }

    private static string ComputeOperationsWorkflowAuditHash(OperationsWorkflowAuditRecord record, string? previousHash)
    {
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            record.AuditId,
            OccurredAtUtc = record.OccurredAtUtc.ToUniversalTime().ToString("O"),
            record.WorkflowId,
            record.FundAccountId,
            record.PeriodId,
            record.EventType,
            record.FromState,
            record.ToState,
            record.Gate,
            record.FromGateStatus,
            record.ToGateStatus,
            record.Actor,
            record.Rationale,
            record.TraceId,
            record.RequestId,
            record.SessionId,
            record.RunId,
            record.BrokerReferenceId,
            record.SecurityReferenceId,
            record.LedgerReferenceId,
            record.ReconciliationReferenceId,
            record.EvidenceReferenceId,
            record.AuditReferenceId,
            record.Severity,
            Tags = record.Tags.OrderBy(static tag => tag, StringComparer.Ordinal).ToArray()
        });

        var bytes = Encoding.UTF8.GetBytes($"{canonicalPayload}|{previousHash ?? string.Empty}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
