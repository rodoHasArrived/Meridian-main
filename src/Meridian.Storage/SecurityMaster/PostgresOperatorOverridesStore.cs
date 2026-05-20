using System.Collections.Generic;
using System.Data;
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
            select values, updated_by, updated_at
            from {Qualified("security_operator_overrides")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var values = DeserializeValues(reader.GetString(0));
        var updatedBy = reader.GetString(1);
        var updatedAt = reader.GetFieldValue<DateTime>(2);
        return new OperatorOverridesDto(
            securityId,
            values,
            updatedBy,
            new DateTimeOffset(DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)));
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

        var current = await LoadValuesAsync(connection, transaction, securityId, ct).ConfigureAwait(false);
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
        var serialized = JsonSerializer.Serialize(next, JsonOptions);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("security_operator_overrides")} (security_id, values, updated_by, updated_at)
                values (@security_id, @values::jsonb, @updated_by, @updated_at)
                on conflict (security_id) do update
                    set values = excluded.values,
                        updated_by = excluded.updated_by,
                        updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("values", serialized);
            command.Parameters.AddWithValue("updated_by", updatedBy);
            command.Parameters.AddWithValue("updated_at", updatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new OperatorOverridesDto(securityId, next, updatedBy, updatedAt)
        {
            ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending,
            ReasonCode = string.IsNullOrWhiteSpace(request.ReasonCode) ? null : request.ReasonCode.Trim(),
            AuditTrail =
            [
                new SecurityOverrideAuditEntryDto(
                    EventType: "Patched",
                    Actor: updatedBy,
                    OccurredAt: updatedAt,
                    ApprovalStatus: SecurityOverrideApprovalStatusDto.Pending,
                    ReasonCode: string.IsNullOrWhiteSpace(request.ReasonCode) ? null : request.ReasonCode.Trim(),
                    Comment: "Operator override values changed and require reviewer approval.")
            ]
        };
    }

    private async Task<Dictionary<string, string>> LoadValuesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select values
            from {Qualified("security_operator_overrides")}
            where security_id = @security_id
            for update;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var raw = (string?)await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return raw is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : DeserializeValues(raw);
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
