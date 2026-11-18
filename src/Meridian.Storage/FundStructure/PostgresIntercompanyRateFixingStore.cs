using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.FundStructure;

/// <summary>Tenant-scoped append-only fixing versions (fund-structure migration 009), serialized per fixing.</summary>
public sealed class PostgresIntercompanyRateFixingStore : IIntercompanyRateFixingStore
{
    private readonly FundStructureStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;

    public PostgresIntercompanyRateFixingStore(FundStructureStoreOptions options, IFundScopeTenantAccessor tenants)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Regex.IsMatch(options.Schema, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Fund-structure schema is invalid.", nameof(options));
        _options = new() { ConnectionString = options.ConnectionString, Schema = options.Schema.ToLowerInvariant() };
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    public async Task<IntercompanyRateFixingDto?> GetAsync(string tenantId, Guid fixingId, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadAsync(connection, null, tenant, fixingId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IntercompanyRateFixingDto>> ListAsync(string tenantId, string indexReference, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select distinct on (fixing_id) payload::text from {_options.Schema}.intercompany_rate_fixings
            where tenant_id=@tenant and index_key=@index and fixing_date between @from and @to
            order by fixing_id, version desc
            """;
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("index", indexReference);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var result = new List<IntercompanyRateFixingDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(Deserialize(reader.GetString(0), tenant));
        return result.OrderBy(item => item.FixingDate).ToArray();
    }

    public async Task<IntercompanyRateFixingDto> AppendAsync(IntercompanyRateFixingDto next, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        var tenant = Scope(next.TenantId);
        IntercompanyRateFixingStoreRules.RequireTenant(tenant, next);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "select pg_advisory_xact_lock(hashtextextended(@scope, 0))";
            guard.Parameters.AddWithValue("scope", $"intercompany-rate-fixing:{_options.Schema}:{tenant}:{next.FixingId:D}");
            await guard.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var current = await ReadAsync(connection, transaction, tenant, next.FixingId, ct).ConfigureAwait(false);
        if (IntercompanyRateFixingStoreRules.IsIdenticalRetry(current, next, expectedVersion))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return current!;
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                insert into {_options.Schema}.intercompany_rate_fixings (tenant_id, fixing_id, version, index_key, fixing_date, payload)
                values (@tenant, @id, @version, @index, @date, @payload)
                """;
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("id", next.FixingId);
            insert.Parameters.AddWithValue("version", next.Version);
            insert.Parameters.AddWithValue("index", next.IndexReference);
            insert.Parameters.AddWithValue("date", next.FixingDate);
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, IntercompanyRateFixingStoreRules.Serialize(next));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return next;
    }

    private async Task<IntercompanyRateFixingDto?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string tenant,
        Guid fixingId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select payload::text, version from {_options.Schema}.intercompany_rate_fixings where tenant_id=@tenant and fixing_id=@id order by version desc limit 1";
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", fixingId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var fixing = Deserialize(reader.GetString(0), tenant);
        if (fixing.FixingId != fixingId || fixing.Version != reader.GetInt64(1))
            throw new InvalidDataException("A fixing payload contradicts its immutable database identity.");
        return fixing;
    }

    private static IntercompanyRateFixingDto Deserialize(string json, string tenant)
    {
        var fixing = JsonSerializer.Deserialize(json, IntercompanyRateFixingJsonContext.Default.IntercompanyRateFixingDto)
            ?? throw new InvalidDataException("A retained fixing payload is empty.");
        IntercompanyRateFixingStoreRules.RequireTenant(tenant, fixing);
        return fixing;
    }

    private string Scope(string tenantId)
    {
        var tenant = IntercompanyRateFixingStoreRules.Tenant(tenantId);
        if (_tenants.ResolveCallerTenant() is { Length: > 0 } active && !string.Equals(IntercompanyRateFixingStoreRules.Tenant(active), tenant, StringComparison.Ordinal))
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
