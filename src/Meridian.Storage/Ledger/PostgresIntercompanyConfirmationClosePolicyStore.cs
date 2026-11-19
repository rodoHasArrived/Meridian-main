using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

/// <summary>Append-only confirmation close-policy versions (ledger migration 078), serialized per policy.</summary>
public sealed class PostgresIntercompanyConfirmationClosePolicyStore : IIntercompanyConfirmationClosePolicyStore
{
    private readonly LedgerJournalStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly string _table;

    public PostgresIntercompanyConfirmationClosePolicyStore(LedgerJournalStoreOptions options, IFundScopeTenantAccessor tenants)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = $"\"{options.SchemaName}\".\"intercompany_confirmation_close_policies\"";
    }

    public async Task<IntercompanyConfirmationClosePolicyDto?> GetAsync(string tenantId, Guid policyId, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var found = await LatestAsync(connection, null, "tenant_id=@tenant and policy_id=@id", command =>
        {
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("id", policyId);
        }, ct).ConfigureAwait(false);
        return found.FirstOrDefault();
    }

    public async Task<IReadOnlyList<IntercompanyConfirmationClosePolicyDto>> ListAsync(string tenantId, Guid? ledgerBookId = null, CancellationToken ct = default)
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

    public async Task<IntercompanyConfirmationClosePolicyDto> AppendAsync(IntercompanyConfirmationClosePolicyDto next, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        var tenant = Scope(next.TenantId);
        IntercompanyConfirmationClosePolicyStoreRules.RequireTenant(tenant, next);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var fence = connection.CreateCommand())
        {
            fence.Transaction = transaction;
            fence.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0))";
            fence.Parameters.AddWithValue("key", $"intercompany-confirmation-close-policy:{_options.SchemaName}:{next.PolicyId:D}");
            await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var current = (await LatestAsync(connection, transaction, "policy_id=@id",
            command => command.Parameters.AddWithValue("id", next.PolicyId), ct).ConfigureAwait(false)).FirstOrDefault();
        if (current is not null)
            IntercompanyConfirmationClosePolicyStoreRules.RequireTenant(tenant, current);
        if (IntercompanyConfirmationClosePolicyStoreRules.IsIdenticalRetry(current, next, expectedVersion))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return current!;
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"insert into {_table} (policy_id,version,tenant_id,company_id,ledger_book_id,status,payload) values (@id,@version,@tenant,@company,@book,@status,@payload)";
            insert.Parameters.AddWithValue("id", next.PolicyId);
            insert.Parameters.AddWithValue("version", next.Version);
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("company", (object?)next.CompanyId ?? DBNull.Value);
            insert.Parameters.AddWithValue("book", next.LedgerBookId);
            insert.Parameters.AddWithValue("status", next.Status.ToString());
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, IntercompanyConfirmationClosePolicyStoreRules.Serialize(next));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return next;
    }

    private async Task<IReadOnlyList<IntercompanyConfirmationClosePolicyDto>> LatestAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string filter, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select distinct on (policy_id) payload::text, version, policy_id from {_table} where {filter} order by policy_id, version desc";
        bind(command);
        var result = new List<IntercompanyConfirmationClosePolicyDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schedule = IntercompanyConfirmationClosePolicyStoreRules.Deserialize(reader.GetString(0));
            if (schedule.Version != reader.GetInt64(1) || schedule.PolicyId != reader.GetGuid(2))
                throw new InvalidDataException("A close-policy payload contradicts its immutable database identity.");
            result.Add(schedule);
        }
        return result;
    }

    private string Scope(string tenantId)
    {
        var tenant = IntercompanyConfirmationClosePolicyStoreRules.Tenant(tenantId);
        if (_tenants.ResolveCallerTenant() is { Length: > 0 } active && IntercompanyConfirmationClosePolicyStoreRules.Tenant(active) != tenant)
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
