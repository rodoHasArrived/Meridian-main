using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresOperatorOverridesStore : IOperatorOverridesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    // Column order consumed by ReadOverrides; keep the two in lockstep.
    private const string SelectColumns =
        "values, updated_by, updated_at, approval_status, reason_code, reviewed_by, reviewed_at, audit_trail";

    private readonly SecurityMasterOptions _options;

    public PostgresOperatorOverridesStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<OperatorOverridesDto?> GetAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {SelectColumns}
            from {Qualified("security_operator_overrides")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return ReadOverrides(reader, securityId);
    }

    public async Task<OperatorOverridesDto> PatchAsync(
        Guid securityId,
        OperatorOverridesPatchRequest request,
        string updatedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(updatedBy))
        {
            throw new ArgumentException("updatedBy must be provided.", nameof(updatedBy));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        var (current, currentAudit) = await LoadForUpdateAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal);

        if (request.SetValues is { Count: > 0 })
        {
            foreach (var (key, value) in request.SetValues)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }
                if (value is null)
                {
                    next.Remove(key);
                }
                else
                {
                    next[key] = value;
                }
            }
        }

        if (request.RemoveKeys is { Count: > 0 })
        {
            foreach (var key in request.RemoveKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    next.Remove(key);
                }
            }
        }

        var updatedAt = DateTimeOffset.UtcNow;
        var reasonCode = string.IsNullOrWhiteSpace(request.ReasonCode) ? null : request.ReasonCode.Trim();
        // Changing the values invalidates any prior review, so the record returns to Pending and any
        // recorded reviewer is cleared. The Patched entry is appended to the durable audit trail.
        var auditTrail = Append(
            currentAudit,
            new SecurityOverrideAuditEntryDto(
                EventType: "Patched",
                Actor: updatedBy,
                OccurredAt: updatedAt,
                ApprovalStatus: SecurityOverrideApprovalStatusDto.Pending,
                ReasonCode: reasonCode,
                Comment: "Operator override values changed and require reviewer approval."));

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("security_operator_overrides")}
                    (security_id, values, updated_by, updated_at, approval_status, reason_code, reviewed_by, reviewed_at, audit_trail)
                values
                    (@security_id, @values::jsonb, @updated_by, @updated_at, @approval_status, @reason_code, null, null, @audit_trail::jsonb)
                on conflict (security_id) do update
                    set values = excluded.values,
                        updated_by = excluded.updated_by,
                        updated_at = excluded.updated_at,
                        approval_status = excluded.approval_status,
                        reason_code = excluded.reason_code,
                        reviewed_by = null,
                        reviewed_at = null,
                        audit_trail = excluded.audit_trail;
                """;
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("values", JsonSerializer.Serialize(next, JsonOptions));
            command.Parameters.AddWithValue("updated_by", updatedBy);
            command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);
            command.Parameters.AddWithValue("approval_status", ToDbStatus(SecurityOverrideApprovalStatusDto.Pending));
            command.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
            command.Parameters.AddWithValue("audit_trail", SerializeAuditTrail(auditTrail));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new OperatorOverridesDto(securityId, next, updatedBy, updatedAt)
        {
            ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
            ReasonCode = reasonCode,
            AuditTrail = auditTrail
        };
    }

    public async Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
        Guid securityId,
        OperatorOverrideDecisionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reviewer))
        {
            throw new ArgumentException("reviewer must be provided.", nameof(request));
        }

        if (request.Decision is not (SecurityOverrideApprovalStatusDto.Approved or SecurityOverrideApprovalStatusDto.Rejected))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Decision,
                "Approval decision must be Approved or Rejected.");
        }

        var reviewer = request.Reviewer.Trim();

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        var existing = await LoadRowForUpdateAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            // Nothing to review — a reviewer cannot decide on overrides that were never recorded.
            throw new InvalidOperationException(
                $"No operator overrides exist for security '{securityId}'; there is nothing to review.");
        }

        if (existing.ApprovalStatus != SecurityOverrideApprovalStatusDto.Pending)
        {
            // Only a Pending overlay is reviewable; an already-decided overlay must be re-patched
            // (which resets it to Pending) before another decision can be recorded.
            throw new InvalidOperationException(
                $"Operator overrides for security '{securityId}' are '{existing.ApprovalStatus}', not Pending; " +
                "re-patch the overlay before recording another decision.");
        }

        var reviewedAt = DateTimeOffset.UtcNow;
        var reasonCode = existing.ReasonCode;
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var auditTrail = Append(
            existing.AuditTrail,
            new SecurityOverrideAuditEntryDto(
                EventType: request.Decision == SecurityOverrideApprovalStatusDto.Approved ? "Approved" : "Rejected",
                Actor: reviewer,
                OccurredAt: reviewedAt,
                ApprovalStatus: request.Decision,
                ReasonCode: reasonCode,
                Comment: comment,
                Reviewer: reviewer,
                ReviewedAt: reviewedAt));

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                update {Qualified("security_operator_overrides")}
                    set approval_status = @approval_status,
                        reason_code = @reason_code,
                        reviewed_by = @reviewed_by,
                        reviewed_at = @reviewed_at,
                        audit_trail = @audit_trail::jsonb
                where security_id = @security_id;
                """;
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("approval_status", ToDbStatus(request.Decision));
            command.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
            command.Parameters.AddWithValue("reviewed_by", reviewer);
            command.Parameters.AddWithValue("reviewed_at", reviewedAt.UtcDateTime);
            command.Parameters.AddWithValue("audit_trail", SerializeAuditTrail(auditTrail));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return existing with
        {
            ApprovalStatus = request.Decision,
            ReasonCode = reasonCode,
            ReviewedBy = reviewer,
            ReviewedAt = reviewedAt,
            AuditTrail = auditTrail
        };
    }

    public async Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
        Guid securityId,
        OperatorOverrideDecisionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await RecordApprovalDecisionAsync(
                securityId,
                new OperatorOverrideApprovalDecisionRequest(request.Decision, Comment: request.Comment),
                request.Reviewer,
                ct)
            .ConfigureAwait(false);

        return result ?? throw new InvalidOperationException(
            $"No operator overrides exist for security '{securityId:D}'.");
    }

    private async Task<(Dictionary<string, string> Values, IReadOnlyList<SecurityOverrideAuditEntryDto> AuditTrail)> LoadForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select values, audit_trail
            from {Qualified("security_operator_overrides")}
            where security_id = @security_id
            for update;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return (new Dictionary<string, string>(StringComparer.Ordinal), Array.Empty<SecurityOverrideAuditEntryDto>());
        }

        var values = DeserializeValues(reader.GetString(0));
        var auditTrail = DeserializeAuditTrail(reader.IsDBNull(1) ? null : reader.GetString(1));
        return (values, auditTrail);
    }

    private async Task<OperatorOverridesDto?> LoadRowForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {SelectColumns}
            from {Qualified("security_operator_overrides")}
            where security_id = @security_id
            for update;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return ReadOverrides(reader, securityId);
    }

    private static OperatorOverridesDto ReadOverrides(DbDataReader reader, Guid securityId)
    {
        var values = DeserializeValues(reader.GetString(0));
        var updatedBy = reader.GetString(1);
        var updatedAt = reader.GetFieldValue<DateTime>(2);
        var approvalStatus = ParseApprovalStatus(reader.IsDBNull(3) ? null : reader.GetString(3));
        var reasonCode = reader.IsDBNull(4) ? null : reader.GetString(4);
        var reviewedBy = reader.IsDBNull(5) ? null : reader.GetString(5);
        DateTimeOffset? reviewedAt = reader.IsDBNull(6)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(6), DateTimeKind.Utc));
        var auditTrail = DeserializeAuditTrail(reader.IsDBNull(7) ? null : reader.GetString(7));

        return new OperatorOverridesDto(
            securityId,
            values,
            updatedBy,
            new DateTimeOffset(DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)))
        {
            ApprovalStatus = approvalStatus,
            ReasonCode = reasonCode,
            ReviewedBy = reviewedBy,
            ReviewedAt = reviewedAt,
            AuditTrail = auditTrail
        };
    }

    private static Dictionary<string, string> DeserializeValues(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonOptions);
        return parsed is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }

    private static string SerializeAuditTrail(IReadOnlyList<SecurityOverrideAuditEntryDto> auditTrail)
        => JsonSerializer.Serialize(auditTrail, JsonOptions);

    private static IReadOnlyList<SecurityOverrideAuditEntryDto> DeserializeAuditTrail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<SecurityOverrideAuditEntryDto>();
        }

        var parsed = JsonSerializer.Deserialize<List<SecurityOverrideAuditEntryDto>>(raw, JsonOptions);
        return parsed is null ? Array.Empty<SecurityOverrideAuditEntryDto>() : parsed;
    }

    private static IReadOnlyList<SecurityOverrideAuditEntryDto> Append(
        IReadOnlyList<SecurityOverrideAuditEntryDto> existing,
        SecurityOverrideAuditEntryDto entry)
    {
        var list = new List<SecurityOverrideAuditEntryDto>(existing.Count + 1);
        list.AddRange(existing);
        list.Add(entry);
        return list;
    }

    private static SecurityOverrideApprovalStatusDto ParseApprovalStatus(string? raw)
        => Enum.TryParse<SecurityOverrideApprovalStatusDto>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : SecurityOverrideApprovalStatusDto.NotRequested;

    private static string ToDbStatus(SecurityOverrideApprovalStatusDto status) => status.ToString();

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
