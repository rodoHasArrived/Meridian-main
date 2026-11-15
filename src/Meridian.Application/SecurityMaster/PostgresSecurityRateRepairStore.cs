using System.Buffers.Binary;
using Meridian.Contracts.Integrity;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

public sealed class PostgresSecurityRateRepairStore(SecurityMasterOptions options) : ISecurityRateRepairStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Table(string name) => $"{options.Schema}.{name}";

    public async Task<SecurityRateRepairRetainedBatch> CreateAsync(SecurityRateRepairManifest manifest, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            insert into {Table("security_rate_repair_batches")}
              (batch_id, tenant_id, company_id, command_sha256, created_by, created_at, manifest_json)
            values (@id, @tenant, @company, @hash, @maker, @at, @manifest::jsonb)
            on conflict (batch_id) do nothing;
            """;
        insert.Parameters.AddWithValue("id", manifest.Request.RequestId);
        insert.Parameters.AddWithValue("tenant", manifest.TenantId);
        insert.Parameters.AddWithValue("company", (object?)manifest.CompanyId ?? DBNull.Value);
        insert.Parameters.AddWithValue("hash", manifest.CommandSha256);
        insert.Parameters.AddWithValue("maker", manifest.Maker);
        insert.Parameters.AddWithValue("at", manifest.CreatedAt.UtcDateTime);
        insert.Parameters.AddWithValue("manifest", JsonSerializer.Serialize(manifest, Json));
        var inserted = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (inserted > 0)
        {
            var progress = manifest.Items.Select(static item => new SecurityRateRepairProgress(item.SecurityId, "Selected", Guid.NewGuid())).ToArray();
            await using var state = connection.CreateCommand();
            state.CommandText = $"insert into {Table("security_rate_repair_state")} (batch_id, version, state_json) values (@id, 1, @state::jsonb)";
            state.Parameters.AddWithValue("id", manifest.Request.RequestId);
            state.Parameters.AddWithValue("state", JsonSerializer.Serialize(progress, Json));
            await state.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        var retained = await GetAsync(manifest.Request.RequestId, manifest.TenantId, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Batch identity is unavailable in the current tenant.");
        if (retained.Manifest.CommandSha256 != manifest.CommandSha256 || retained.Manifest.Maker != manifest.Maker)
            throw new SecurityRateRepairConflictException("This request identity already names a different immutable repair command.");
        return retained;
    }

    public async Task<SecurityRateRepairRetainedBatch?> GetAsync(Guid batchId, string tenantId, CancellationToken ct = default)
    {
        await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(options, ct).ConfigureAwait(false);
        await using var command = lease.Connection.CreateCommand();
        command.CommandText = $"""
            select b.manifest_json::text, s.version, s.state_json::text
            from {Table("security_rate_repair_batches")} b join {Table("security_rate_repair_state")} s using(batch_id)
            where b.batch_id = @id and b.tenant_id = @tenant;
            """;
        command.Parameters.AddWithValue("id", batchId);
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader.GetString(0), reader.GetInt64(1), reader.GetString(2)) : null;
    }

    public async Task<IReadOnlyList<SecurityRateRepairRetainedBatch>> ListAsync(string tenantId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select b.manifest_json::text, s.version, s.state_json::text
            from {Table("security_rate_repair_batches")} b join {Table("security_rate_repair_state")} s using(batch_id)
            where b.tenant_id = @tenant order by b.created_at desc limit 200;
            """;
        command.Parameters.AddWithValue("tenant", tenantId);
        var results = new List<SecurityRateRepairRetainedBatch>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(Read(reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
        return results;
    }

    public async Task<IAsyncDisposable> AcquireCommandLeaseAsync(Guid batchId, string tenantId, long expectedVersion, CancellationToken ct = default)
    {
        // A session lease serializes commands across hosts; each item still commits independently.
        // A crash closes the session, releasing the lease without a retained stuck-command flag.
        var connection = new NpgsqlConnection(options.ConnectionString);
        var key = BinaryPrimitives.ReadInt64BigEndian(Sha256Digest.ComputeBytes(Encoding.UTF8.GetBytes($"security-rate-repair:{tenantId}:{batchId:D}")));
        var acquired = false;
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "select pg_try_advisory_lock(@key)";
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                throw new SecurityRateRepairConflictException("Another command is operating on this batch; reload its retained outcome.");
            acquired = true;
            var batch = await GetAsync(batchId, tenantId, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Repair batch was not found.");
            if (batch.Version != expectedVersion)
                throw new SecurityRateRepairConflictException("The batch changed; reload its retained outcome before another command.");
            return new CommandLease(connection, key);
        }
        catch
        {
            if (acquired)
                await new CommandLease(connection, key).DisposeAsync().ConfigureAwait(false);
            else
                await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task ExecuteItemAsync(Guid batchId, string tenantId, Guid securityId,
        Func<SecurityRateRepairProgress, Task<SecurityRateRepairProgress>> operation, CancellationToken ct = default)
        => PostgresSecurityMasterMutation.ExecuteAsync(options, securityId, async () =>
        {
            SecurityRateRepairProgress[] items;
            await using (var readLease = await PostgresSecurityMasterMutation.OpenConnectionAsync(options, ct).ConfigureAwait(false))
            await using (var read = readLease.Connection.CreateCommand())
            {
                read.CommandText = $"""
                    select s.state_json::text from {Table("security_rate_repair_state")} s
                    join {Table("security_rate_repair_batches")} b using(batch_id)
                    where b.batch_id = @id and b.tenant_id = @tenant for update of s;
                    """;
                read.Parameters.AddWithValue("id", batchId);
                read.Parameters.AddWithValue("tenant", tenantId);
                var json = await read.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
                    ?? throw new KeyNotFoundException("Repair batch was not found.");
                items = JsonSerializer.Deserialize<SecurityRateRepairProgress[]>(json, Json) ?? throw new InvalidDataException("Repair progress is missing.");
            }
            var index = Array.FindIndex(items, item => item.SecurityId == securityId);
            if (index < 0)
                throw new ArgumentException("The selected security is outside the immutable repair roster.");
            var updated = await operation(items[index]).ConfigureAwait(false);
            if (updated.SecurityId != securityId || updated.WorkflowId != items[index].WorkflowId)
                throw new InvalidOperationException("Item and prepared workflow identities cannot change.");
            if (updated == items[index])
                return true;
            items[index] = updated;
            // The row lock belongs to the outer transaction, while releasing its connection lease
            // lets the nested workbench command use that same enlisted connection without deadlock.
            await using var writeLease = await PostgresSecurityMasterMutation.OpenConnectionAsync(options, ct).ConfigureAwait(false);
            await using var write = writeLease.Connection.CreateCommand();
            write.CommandText = $"update {Table("security_rate_repair_state")} set state_json = @state::jsonb, version = version + 1 where batch_id = @id";
            write.Parameters.AddWithValue("state", JsonSerializer.Serialize(items, Json));
            write.Parameters.AddWithValue("id", batchId);
            await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);

    private sealed class CommandLease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "select pg_advisory_unlock(@key)";
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private static SecurityRateRepairRetainedBatch Read(string manifest, long version, string progress)
        => new(JsonSerializer.Deserialize<SecurityRateRepairManifest>(manifest, Json) ?? throw new InvalidDataException("Repair roster is missing."),
            version, JsonSerializer.Deserialize<SecurityRateRepairProgress[]>(progress, Json) ?? throw new InvalidDataException("Repair outcomes are missing."));
}
