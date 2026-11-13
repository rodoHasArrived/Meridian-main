using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.FundStructure;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.FundStructure;

/// <summary>Tenant-scoped append-only history, serialized by agreement and protected by database immutability.</summary>
public sealed class PostgresIntercompanyAccountingArrangementStore : IIntercompanyAccountingArrangementStore
{
    private readonly FundStructureStoreOptions _options;
    public PostgresIntercompanyAccountingArrangementStore(FundStructureStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Regex.IsMatch(options.Schema, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Fund-structure schema is invalid.", nameof(options));
        _options = new() { ConnectionString = options.ConnectionString, Schema = options.Schema.ToLowerInvariant() };
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await LockAuthorityAsync(connection, transaction, ct).ConfigureAwait(false);
            var retainedTransaction = transaction;
            return new IntercompanyAccountingArrangementAuthorityLease(async () =>
            {
                try
                { await retainedTransaction.DisposeAsync().ConfigureAwait(false); }
                finally { await connection.DisposeAsync().ConfigureAwait(false); }
            });
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task LockAuthorityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@scope, 0))";
        command.Parameters.AddWithValue("scope", "intercompany-accounting-arrangement-authority:" + _options.Schema);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAccountingArrangementHistoryDto?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadAsync(connection, null, IntercompanyAccountingArrangementStoreRules.Tenant(tenantId), agreementId, ct).ConfigureAwait(false);
    }

    private async Task<IntercompanyAccountingArrangementHistoryDto?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string tenant, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select payload::text, aggregate_version, request_id from {_options.Schema}.intercompany_accounting_arrangement_events where tenant_id=@tenant and arrangement_id=@id order by aggregate_version";
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var events = new List<IntercompanyAccountingArrangementEventDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var item = JsonSerializer.Deserialize(reader.GetString(0), IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementEventDto)
                ?? throw new InvalidDataException("Agreement event payload is missing.");
            if (item.Snapshot.ArrangementId != id || item.Snapshot.Version != reader.GetInt64(1) || item.RequestId != reader.GetGuid(2))
                throw new InvalidDataException("Agreement payload contradicts its immutable database identity.");
            events.Add(item);
        }
        if (events.Count == 0)
            return null;
        var history = new IntercompanyAccountingArrangementHistoryDto(events);
        IntercompanyAccountingArrangementStoreRules.ValidateHistory(history);
        return history;
    }

    public async Task<IReadOnlyList<IntercompanyAccountingArrangementDto>> ListAsync(string tenantId, Guid? organizationId = null, CancellationToken ct = default)
    {
        var tenant = IntercompanyAccountingArrangementStoreRules.Tenant(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var ids = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"select distinct arrangement_id from {_options.Schema}.intercompany_accounting_arrangement_events where tenant_id=@tenant order by arrangement_id";
            command.Parameters.AddWithValue("tenant", tenant);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                ids.Add(reader.GetGuid(0));
        }
        var results = new List<IntercompanyAccountingArrangementDto>();
        foreach (var id in ids)
        {
            var history = await ReadAsync(connection, null, tenant, id, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Agreement history disappeared.");
            if (organizationId is null || history.Current.OrganizationId == organizationId)
                results.Add(history.Current);
        }
        return results.OrderBy(x => x.ArrangementReference).ThenBy(x => x.ArrangementId).ToArray();
    }

    public Task<IntercompanyAccountingArrangementDto> AppendAsync(string tenantId, long expectedVersion, IntercompanyAccountingArrangementEventDto item, CancellationToken ct = default)
        => MutateAsync(tenantId, item.Snapshot.ArrangementId, item.RequestId, item.RequestDigest, (old, _) =>
        {
            if ((old?.Current.Version ?? 0) != expectedVersion)
                throw new InvalidOperationException("The agreement version changed.");
            return Task.FromResult(item);
        }, ct);

    public async Task<IntercompanyAccountingArrangementDto> MutateAsync(string tenantId, Guid agreementId, Guid requestId, string requestDigest,
        Func<IntercompanyAccountingArrangementHistoryDto?, CancellationToken, Task<IntercompanyAccountingArrangementEventDto>> build, CancellationToken ct = default)
    {
        var tenant = IntercompanyAccountingArrangementStoreRules.Tenant(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAuthorityAsync(connection, transaction, ct).ConfigureAwait(false);
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "select pg_advisory_xact_lock(hashtextextended(@scope, 0))";
            guard.Parameters.AddWithValue("scope", "intercompany-accounting-arrangement:" + tenant + ":" + agreementId.ToString("D"));
            await guard.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var old = await ReadAsync(connection, transaction, tenant, agreementId, ct).ConfigureAwait(false);
        if (IntercompanyAccountingArrangementStoreRules.Retry(old, requestId, requestDigest) is { } retry)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return retry;
        }
        var item = await build(old, ct).ConfigureAwait(false);
        if (item.Snapshot.ArrangementId != agreementId || item.RequestId != requestId || item.RequestDigest != requestDigest)
            throw new InvalidOperationException("The agreement mutation changed its locked identity.");
        var result = IntercompanyAccountingArrangementStoreRules.Append(old, old?.Current.Version ?? 0, item);
        if (result.Changed)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"insert into {_options.Schema}.intercompany_accounting_arrangement_events (tenant_id,arrangement_id,aggregate_version,request_id,payload) values (@tenant,@id,@version,@request,@payload)";
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("id", item.Snapshot.ArrangementId);
            command.Parameters.AddWithValue("version", item.Snapshot.Version);
            command.Parameters.AddWithValue("request", item.RequestId);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result.History.Events[^1], IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementEventDto));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result.Result;
    }
}
