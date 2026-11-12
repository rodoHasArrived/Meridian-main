using System.Data;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Npgsql;

namespace Meridian.Storage.FundStructure;

/// <summary>Tenant-bound immutable history with cross-process authority locks.</summary>
public sealed class PostgresEntityLifecycleStore(FundStructureStoreOptions options) : IEntityLifecycleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string Q(string table) => $"{options.Schema}.{table}";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(options.ConnectionString);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tenantId, IEnumerable<Guid> entityIds, bool shared, CancellationToken ct)
    {
        foreach (var id in entityIds.Distinct().OrderBy(value => value))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = shared ? "select pg_advisory_xact_lock_shared(hashtextextended(@key, 0));"
                : "select pg_advisory_xact_lock(hashtextextended(@key, 0));";
            command.Parameters.AddWithValue("key", $"entity-lifecycle:{tenantId}:{id:D}");
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<IAsyncDisposable> AcquireReadLeaseAsync(string tenantId, IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        tenantId = InMemoryEntityLifecycleStore.NormalizeTenant(tenantId);
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            await LockAsync(connection, transaction, tenantId, entityIds, shared: true, ct).ConfigureAwait(false);
            return new EntityLifecycleLeaseGate.Lease(async () =>
            {
                try { await transaction.DisposeAsync().ConfigureAwait(false); }
                finally { await connection.DisposeAsync().ConfigureAwait(false); }
            });
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<EntityLifecycleRevisionDto?> GetLatestAsync(string tenantId, Guid entityId, CancellationToken ct = default)
        => (await GetHistoryAsync(tenantId, entityId, ct).ConfigureAwait(false)).LastOrDefault();

    public async Task<EntityLifecycleRevisionDto?> GetRevisionAsync(string tenantId, Guid entityId, long revision, CancellationToken ct = default)
        => (await GetHistoryAsync(tenantId, entityId, ct).ConfigureAwait(false)).SingleOrDefault(row => row.Revision == revision);

    public async Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(string tenantId, Guid entityId, CancellationToken ct = default)
    {
        tenantId = InMemoryEntityLifecycleStore.NormalizeTenant(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadHistoryAsync(connection, null, tenantId, entityId, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<EntityLifecycleRevisionDto>> ReadHistoryAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, string tenantId, Guid entityId, CancellationToken ct)
    {
        var rows = new List<EntityLifecycleRevisionDto>();
        await using (var baseline = connection.CreateCommand())
        {
            baseline.Transaction = transaction;
            baseline.CommandText = $"""
                select b.lifecycle_status, b.captured_at_utc
                from {Q("entity_lifecycle_legacy_baseline")} b
                join {Q("legal_entity")} e on e.entity_id = b.entity_id
                where b.entity_id = @entity and lower(trim(e.tenant_id)) = @tenant;
                """;
            baseline.Parameters.AddWithValue("entity", entityId);
            baseline.Parameters.AddWithValue("tenant", tenantId);
            await using var reader = await baseline.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var captured = reader.GetFieldValue<DateTimeOffset>(1);
                // Stable identity derived from the entity; this baseline does not assert earlier history.
                rows.Add(EntityLifecycleRevisionIntegrity.Seal(new EntityLifecycleRevisionDto(tenantId, entityId, 1,
                    captured, "migration", "Legacy status snapshot at lifecycle cutover", $"migration:entity-lifecycle:{entityId:D}",
                    string.Empty, string.Empty, [new(entityId, captured, null,
                        Enum.Parse<LegalEntityLifecycleStatusDto>(reader.GetString(0)), LegalEntityLifecycleEventKindDto.Other)], LegacySnapshot: true)));
            }
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select r.payload::text from {Q("entity_lifecycle_revision")} r
            join {Q("legal_entity")} e on e.entity_id = r.entity_id
            where r.tenant_id = @tenant and r.entity_id = @entity and lower(trim(e.tenant_id)) = @tenant
            order by r.revision;
            """;
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("entity", entityId);
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = JsonSerializer.Deserialize<EntityLifecycleRevisionDto>(reader.GetString(0), JsonOptions)
                    ?? throw new InvalidOperationException("Retained lifecycle payload is empty.");
                if (row.TenantId != tenantId || row.EntityId != entityId)
                    throw new InvalidOperationException("Retained lifecycle payload scope does not match its storage key.");
                EntityLifecycleRevisionIntegrity.ValidateRevision(row, rows.LastOrDefault(), rows.LastOrDefault()?.Revision ?? 0);
                rows.Add(row);
            }
        }
        return rows;
    }

    public async Task<EntityLifecycleRevisionDto> AppendAsync(EntityLifecycleRevisionDto revision, long expectedRevision, CancellationToken ct = default)
    {
        var tenant = InMemoryEntityLifecycleStore.NormalizeTenant(revision.TenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        // Posting authority leases lock the graph before entity advisory locks. Writers must
        // take their table intent lock in the same order, before waiting on the entity lease.
        await using (var tableLock = connection.CreateCommand())
        {
            tableLock.Transaction = transaction;
            tableLock.CommandText = $"lock table {Q("legal_entity")} in row exclusive mode;";
            await tableLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        // Successor graph changes are rare; serialize writers so two concurrent mergers cannot
        // each validate against the other's predecessor and introduce a cycle.
        await using (var graphLock = connection.CreateCommand())
        {
            graphLock.Transaction = transaction;
            graphLock.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0));";
            graphLock.Parameters.AddWithValue("key", $"entity-lifecycle-successor-graph:{tenant}");
            await graphLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var ids = revision.Timeline.Where(state => state.SuccessorEntityId.HasValue).Select(state => state.SuccessorEntityId!.Value)
            .Append(revision.EntityId).Distinct().OrderBy(id => id).ToArray();
        await LockAsync(connection, transaction, tenant, ids, shared: false, ct).ConfigureAwait(false);
        foreach (var id in ids)
        {
            await using var owner = connection.CreateCommand();
            owner.Transaction = transaction;
            owner.CommandText = $"select entity_id from {Q("legal_entity")} where entity_id = @entity and lower(trim(tenant_id)) = @tenant for update;";
            owner.Parameters.AddWithValue("entity", id);
            owner.Parameters.AddWithValue("tenant", tenant);
            if (await owner.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                throw new InvalidOperationException("Lifecycle entities and successors must belong to the calling tenant.");
        }
        var history = await ReadHistoryAsync(connection, transaction, tenant, revision.EntityId, ct).ConfigureAwait(false);
        EntityLifecycleRevisionIntegrity.ValidateRevision(revision, history.LastOrDefault(), expectedRevision);
        await EntityLifecycleStoreRules.ValidateSuccessorsAsync(revision, async id =>
            (await ReadHistoryAsync(connection, transaction, tenant, id, ct).ConfigureAwait(false)).LastOrDefault()).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {Q("entity_lifecycle_revision")}
                    (tenant_id, entity_id, revision, recorded_at_utc, recorded_by, content_hash, previous_hash, payload)
                values (@tenant, @entity, @revision, @recorded, @actor, @hash, @previous, @payload::jsonb);
                """;
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("entity", revision.EntityId);
            insert.Parameters.AddWithValue("revision", revision.Revision);
            insert.Parameters.AddWithValue("recorded", revision.RecordedAtUtc);
            insert.Parameters.AddWithValue("actor", revision.RecordedBy);
            insert.Parameters.AddWithValue("hash", revision.ContentHash);
            insert.Parameters.AddWithValue("previous", revision.PreviousHash);
            insert.Parameters.AddWithValue("payload", JsonSerializer.Serialize(revision, JsonOptions));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var current = revision.Timeline.SingleOrDefault(state => state.EffectiveFrom <= revision.RecordedAtUtc
            && (!state.EffectiveTo.HasValue || revision.RecordedAtUtc < state.EffectiveTo.Value));
        if (current is not null)
        {
            await using var projection = connection.CreateCommand();
            projection.Transaction = transaction;
            projection.CommandText = $"update {Q("legal_entity")} set lifecycle_status = @status, updated_at = now() where entity_id = @entity and lower(trim(tenant_id)) = @tenant;";
            projection.Parameters.AddWithValue("status", current.Status.ToString());
            projection.Parameters.AddWithValue("entity", revision.EntityId);
            projection.Parameters.AddWithValue("tenant", tenant);
            await projection.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return revision with { Timeline = revision.Timeline.ToArray() };
    }
}
