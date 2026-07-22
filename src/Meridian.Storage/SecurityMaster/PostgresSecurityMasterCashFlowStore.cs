using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed class PostgresSecurityMasterCashFlowStore : ISecurityMasterCashFlowStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterCashFlowStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<SecurityCashFlowSourceDto?> GetSourceAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select source_kind, last_updated_utc, is_client_override, client_confirmed_by, client_confirmed_at
            from {Qualified("security_cashflow_source_assignments")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var sourceKind = SecurityMasterEnumReads.ParseOrFallback(reader.GetString(0), StructuredCashFlowSourceKind.Unknown);
        var lastUpdated = reader.IsDBNull(1)
            ? (DateTimeOffset?)null
            : reader.GetFieldValue<DateTimeOffset>(1);
        var isClientOverride = reader.GetBoolean(2);
        var clientConfirmedBy = reader.IsDBNull(3) ? null : reader.GetString(3);
        var clientConfirmedAt = reader.IsDBNull(4)
            ? (DateTimeOffset?)null
            : reader.GetFieldValue<DateTimeOffset>(4);

        return new SecurityCashFlowSourceDto(
            securityId, sourceKind, lastUpdated, isClientOverride, clientConfirmedBy, clientConfirmedAt);
    }

    public async Task UpsertSourceAsync(SecurityCashFlowSourceDto source, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_cashflow_source_assignments")}
                (security_id, source_kind, last_updated_utc, is_client_override, client_confirmed_by, client_confirmed_at)
            values (@security_id, @source_kind, @last_updated_utc, @is_client_override, @client_confirmed_by, @client_confirmed_at)
            on conflict (security_id) do update
                set source_kind = excluded.source_kind,
                    last_updated_utc = excluded.last_updated_utc,
                    is_client_override = excluded.is_client_override,
                    client_confirmed_by = excluded.client_confirmed_by,
                    client_confirmed_at = excluded.client_confirmed_at;
            """;
        command.Parameters.AddWithValue("security_id", source.SecurityId);
        command.Parameters.AddWithValue("source_kind", source.SourceKind.ToString());
        command.Parameters.AddWithValue("last_updated_utc", (object?)source.LastUpdatedUtc?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("is_client_override", source.IsClientOverride);
        command.Parameters.AddWithValue("client_confirmed_by", (object?)source.ClientConfirmedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("client_confirmed_at", (object?)source.ClientConfirmedAt?.UtcDateTime ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
