using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

/// <summary>Append-only accrual-schedule versions (ledger migration 075), serialized per schedule.</summary>
public sealed class PostgresIntercompanyAccrualScheduleStore : IIntercompanyAccrualScheduleStore
{
    private readonly LedgerJournalStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly string _table;

    public PostgresIntercompanyAccrualScheduleStore(LedgerJournalStoreOptions options, IFundScopeTenantAccessor tenants)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = $"\"{options.SchemaName}\".\"intercompany_accrual_schedules\"";
    }

    public async Task<IntercompanyAccrualScheduleDto?> GetAsync(string tenantId, Guid scheduleId, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var found = await LatestAsync(connection, null, "tenant_id=@tenant and schedule_id=@id", command =>
        {
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("id", scheduleId);
        }, ct).ConfigureAwait(false);
        return found.FirstOrDefault();
    }

    public async Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListAsync(string tenantId, Guid? ledgerBookId = null, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await LatestAsync(connection, null, ledgerBookId is null ? "tenant_id=@tenant" : "tenant_id=@tenant and ledger_book_id=@book", command =>
        {
            command.Parameters.AddWithValue("tenant", tenant);
            if (ledgerBookId is { } book)
                command.Parameters.AddWithValue("book", book);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return (await LatestAsync(connection, null, "true", _ => { }, ct).ConfigureAwait(false))
            .Where(item => item.Status == IntercompanyAccrualScheduleStatusDto.Active).ToArray();
    }

    public async Task<IntercompanyAccrualScheduleDto> AppendAsync(IntercompanyAccrualScheduleDto next, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        var tenant = Scope(next.TenantId);
        IntercompanyAccrualScheduleStoreRules.RequireTenant(tenant, next);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var fence = connection.CreateCommand())
        {
            fence.Transaction = transaction;
            fence.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0))";
            fence.Parameters.AddWithValue("key", $"intercompany-accrual-schedule:{_options.SchemaName}:{next.ScheduleId:D}");
            await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var current = (await LatestAsync(connection, transaction, "schedule_id=@id",
            command => command.Parameters.AddWithValue("id", next.ScheduleId), ct).ConfigureAwait(false)).FirstOrDefault();
        if (current is not null)
            IntercompanyAccrualScheduleStoreRules.RequireTenant(tenant, current);
        if (IntercompanyAccrualScheduleStoreRules.IsIdenticalRetry(current, next, expectedVersion))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return current!;
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"insert into {_table} (schedule_id,version,tenant_id,company_id,ledger_book_id,status,payload) values (@id,@version,@tenant,@company,@book,@status,@payload)";
            insert.Parameters.AddWithValue("id", next.ScheduleId);
            insert.Parameters.AddWithValue("version", next.Version);
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("company", (object?)next.CompanyId ?? DBNull.Value);
            insert.Parameters.AddWithValue("book", next.LedgerBookId);
            insert.Parameters.AddWithValue("status", next.Status.ToString());
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, IntercompanyAccrualScheduleStoreRules.Serialize(next));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return next;
    }

    private async Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> LatestAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string filter, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select distinct on (schedule_id) payload::text, version, schedule_id from {_table} where {filter} order by schedule_id, version desc";
        bind(command);
        var result = new List<IntercompanyAccrualScheduleDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schedule = IntercompanyAccrualScheduleStoreRules.Deserialize(reader.GetString(0));
            if (schedule.Version != reader.GetInt64(1) || schedule.ScheduleId != reader.GetGuid(2))
                throw new InvalidDataException("A schedule payload contradicts its immutable database identity.");
            result.Add(schedule);
        }
        return result;
    }

    private string Scope(string tenantId)
    {
        var tenant = IntercompanyAccrualScheduleStoreRules.Tenant(tenantId);
        if (_tenants.ResolveCallerTenant() is { Length: > 0 } active && IntercompanyAccrualScheduleStoreRules.Tenant(active) != tenant)
            throw new UnauthorizedAccessException("The caller is scoped to a different tenant.");
        return tenant;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
