using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

/// <summary>Immutable, tenant-owned reviewer attributions over exact retained journal lines.</summary>
public sealed class PostgresLegacyIntercompanyAttributionStore : ILegacyIntercompanyAttributionStore
{
    private readonly LedgerJournalStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly LegacyIntercompanyAttributionValidator _validator;
    private readonly string _table;

    public PostgresLegacyIntercompanyAttributionStore(LedgerJournalStoreOptions options, IFundScopeTenantAccessor tenants,
        LegacyIntercompanyAttributionValidator validator)
    {
        _options = options;
        _tenants = tenants;
        _validator = validator;
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = Qualified("legacy_intercompany_attributions");
    }

    public async Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> ListAsync(Guid ledgerBookId, CancellationToken ct = default)
        => LegacyIntercompanyAttributionRules.Resolve(await HistoryAsync(ledgerBookId, ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> HistoryAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var company = await RequireBookAsync(connection, transaction, ledgerBookId, tenant, ct).ConfigureAwait(false);
        return await ReadAsync(connection, transaction, ledgerBookId, tenant, company, ct).ConfigureAwait(false);
    }

    public async Task<LegacyIntercompanyAttributionDto> AppendAsync(LegacyIntercompanyAttributionDto attribution,
        Guid? expectedPreviousAttributionId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        await using var sourceAuthority = await _validator.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        await _validator.ValidateAsync(attribution, ct).ConfigureAwait(false);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var fence = connection.CreateCommand())
        {
            fence.Transaction = transaction;
            fence.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0));";
            fence.Parameters.AddWithValue("key", $"legacy-intercompany:{tenant}:{attribution.LedgerBookId:D}");
            await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var company = await RequireBookAsync(connection, transaction, attribution.LedgerBookId, tenant, ct).ConfigureAwait(false);
        if (attribution.TenantId != tenant || attribution.CompanyId != company)
            throw new TenantScopeRejectedException("legacy attribution differing from its authoritative book owner");
        var history = await ReadAsync(connection, transaction, attribution.LedgerBookId, tenant, company, ct).ConfigureAwait(false);
        var retained = history.SingleOrDefault(x => x.AttributionId == attribution.AttributionId);
        if (retained is not null)
        {
            if (Serialize(retained) != Serialize(attribution) || retained.PreviousAttributionId != expectedPreviousAttributionId)
                throw new InvalidOperationException("The attribution id was already used for a different reviewed assertion.");
            return retained;
        }
        LegacyIntercompanyAttributionRules.ValidateAppend(attribution, expectedPreviousAttributionId, history);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {_table} (attribution_id,revision,ledger_book_id,journal_entry_id,line_id,tenant_id,company_id,payload)
            values (@id,@revision,@book,@journal,@line,@tenant,@company,@payload);
            """;
        command.Parameters.AddWithValue("id", attribution.AttributionId);
        command.Parameters.AddWithValue("revision", attribution.Revision);
        command.Parameters.AddWithValue("book", attribution.LedgerBookId);
        command.Parameters.AddWithValue("journal", attribution.JournalEntryId);
        command.Parameters.AddWithValue("line", attribution.LineId);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", NpgsqlDbType.Text, (object?)company ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, Serialize(attribution));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Deserialize(Serialize(attribution));
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        _ = RequireTenant();
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"lock table {_table} in share mode;";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new Lease(connection, transaction);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<string?> RequireBookAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid book, string tenant, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select nullif(t.company_id,'all') from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} t on lower(trim(b.fund_profile_id))=t.fund_profile_id
            where b.ledger_book_id=@book and lower(b.tenant_id)=lower(@tenant) and lower(t.tenant_id)=lower(@tenant);
            """;
        command.Parameters.AddWithValue("book", book);
        command.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new TenantScopeRejectedException("legacy attribution for an unavailable or foreign book");
        return reader.IsDBNull(0) ? null : reader.GetString(0);
    }

    private async Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> ReadAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid book, string tenant, string? company, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select payload from {_table} where ledger_book_id=@book and tenant_id=@tenant and coalesce(company_id,'')=@company order by journal_entry_id,line_id,revision;";
        command.Parameters.AddWithValue("book", book);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company ?? "");
        var result = new List<LegacyIntercompanyAttributionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(Deserialize(reader.GetString(0)));
        return result;
    }

    private string RequireTenant()
    {
        var tenant = _tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId;
        if (!TenantReadPredicate.IsResolvedTenant(tenant))
            throw new TenantScopeRejectedException("legacy attribution without a concrete tenant");
        return tenant!.Trim();
    }
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private string Qualified(string table) => $"\"{_options.SchemaName}\".\"{table}\"";
    private static string Serialize(LegacyIntercompanyAttributionDto value)
        => JsonSerializer.Serialize(value, LegacyIntercompanyAttributionJsonContext.Default.LegacyIntercompanyAttributionDto);
    private static LegacyIntercompanyAttributionDto Deserialize(string json)
        => JsonSerializer.Deserialize(json, LegacyIntercompanyAttributionJsonContext.Default.LegacyIntercompanyAttributionDto)
            ?? throw new InvalidOperationException("Invalid retained legacy attribution.");
    private sealed class Lease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        { try { await transaction.DisposeAsync().ConfigureAwait(false); } finally { await connection.DisposeAsync().ConfigureAwait(false); } }
    }
}
