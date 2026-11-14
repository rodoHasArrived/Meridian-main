using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Persists versioned security reconstruction snapshots in PostgreSQL.
/// </summary>
public sealed class PostgresSecurityMasterSnapshotStore : ISecurityMasterSnapshotStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterSnapshotStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task<SecuritySnapshotRecord?> LoadAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id, version, snapshot_timestamp, payload::text
            from {Qualified("security_snapshots")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new SecuritySnapshotRecord(
            reader.GetGuid(0),
            reader.GetInt64(1),
            new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
            JsonDocument.Parse(reader.GetString(3)).RootElement.Clone());
    }

    public async Task SaveAsync(SecuritySnapshotRecord snapshot, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("security_snapshots")} (
                security_id, version, snapshot_timestamp, payload)
            values (
                @security_id, @version, @snapshot_timestamp, @payload::jsonb)
            on conflict (security_id) do update set
                version = excluded.version,
                snapshot_timestamp = excluded.snapshot_timestamp,
                payload = excluded.payload;
            """;
        command.Parameters.AddWithValue("security_id", snapshot.SecurityId);
        command.Parameters.AddWithValue("version", snapshot.Version);
        command.Parameters.AddWithValue("snapshot_timestamp", snapshot.SnapshotTimestamp.UtcDateTime);
        command.Parameters.AddWithValue(
            "payload",
            JsonSerializer.Serialize(snapshot.Payload, SecurityMasterJsonContext.HighPerformanceOptions));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> ListSecurityIdsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select security_id from {Qualified("security_snapshots")} order by security_id;";
        var securityIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            securityIds.Add(reader.GetGuid(0));
        }

        return securityIds;
    }

    public async Task<SecuritySnapshotReplaceOutcome> ReplaceAsync(SecuritySnapshotRecord snapshot, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // One statement: the row lock taken by the update decides between a replacement, an equal
        // retained payload and a version a concurrent write has since moved (or a removed row).
        command.CommandText =
            $"""
            with current as (
                select version, payload = @payload::jsonb as is_equal
                from {Qualified("security_snapshots")}
                where security_id = @security_id
                for update
            ), replaced as (
                update {Qualified("security_snapshots")} s
                set snapshot_timestamp = @snapshot_timestamp,
                    payload = @payload::jsonb
                from current
                where s.security_id = @security_id
                  and current.version = @version
                  and not current.is_equal
                returning 1
            )
            select case
                when exists (select 1 from replaced) then 0
                when exists (select 1 from current where version = @version) then 1
                else 2
            end;
            """;
        command.Parameters.AddWithValue("security_id", snapshot.SecurityId);
        command.Parameters.AddWithValue("version", snapshot.Version);
        command.Parameters.AddWithValue("snapshot_timestamp", snapshot.SnapshotTimestamp.UtcDateTime);
        command.Parameters.AddWithValue(
            "payload",
            JsonSerializer.Serialize(snapshot.Payload, SecurityMasterJsonContext.HighPerformanceOptions));

        var outcome = Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        return (SecuritySnapshotReplaceOutcome)outcome;
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
