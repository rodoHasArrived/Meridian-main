using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

/// <summary>Append-only account mappings with book-authoritative tenant/company scope.</summary>
public sealed class PostgresIntercompanyAccountBindingStore : IIntercompanyAccountBindingStore
{
    private readonly LedgerJournalStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly string _table;

    public PostgresIntercompanyAccountBindingStore(LedgerJournalStoreOptions options,
        IFundScopeTenantAccessor tenants)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = Qualified("intercompany_account_binding_revisions");
    }

    public async Task<IReadOnlyList<IntercompanyAccountBindingDto>> ListAsync(Guid ledgerBookId,
        DateOnly asOf, CancellationToken ct = default)
        => IntercompanyAccountBindingRules.Resolve(await HistoryAsync(ledgerBookId, ct).ConfigureAwait(false), asOf);

    public async Task<IReadOnlyList<IntercompanyAccountBindingDto>> HistoryAsync(Guid ledgerBookId,
        CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var scope = await RequireBookAsync(connection, transaction, ledgerBookId, tenant, ct).ConfigureAwait(false);
        return await ReadHistoryAsync(connection, transaction, ledgerBookId, scope, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAccountBindingDto?> GetRevisionAsync(Guid revisionId,
        CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select payload from {_table} where revision_id = @id and lower(tenant_id) = lower(@tenant);";
        command.Parameters.AddWithValue("id", revisionId);
        command.Parameters.AddWithValue("tenant", tenant);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (json is null)
            return null;
        var revision = Deserialize(json);
        // Recheck retained book ownership, rather than trusting just a caller-supplied row scope.
        await RequireBookAsync(connection, null, revision.LedgerBookId, tenant, ct).ConfigureAwait(false);
        return revision;
    }

    public async Task<IntercompanyAccountBindingDto> AppendAsync(IntercompanyAccountBindingDto revision,
        Guid? expectedPreviousRevisionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var tenant = RequireTenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var authority = connection.CreateCommand())
        {
            authority.Transaction = transaction;
            authority.CommandText = $"lock table {Qualified("ledger_books")} in share mode; "
                + $"lock table {Qualified("accounting_configuration_chart_nodes")} in share mode; "
                + "select pg_advisory_xact_lock(hashtextextended(@lock_key, 0));";
            authority.Parameters.AddWithValue("lock_key", $"intercompany-account-binding:{tenant.ToLowerInvariant()}:{revision.LedgerBookId:D}");
            await authority.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var scope = await RequireBookAsync(connection, transaction, revision.LedgerBookId, tenant, ct).ConfigureAwait(false);
        if (!string.Equals(scope.TenantId, revision.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(scope.CompanyId, revision.CompanyId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Account binding scope must match the book's authoritative owner.");
        var history = await ReadHistoryAsync(connection, transaction, revision.LedgerBookId, scope, ct).ConfigureAwait(false);
        var retained = history.SingleOrDefault(x => x.RevisionId == revision.RevisionId);
        if (retained is not null)
        {
            if (Serialize(retained) != Serialize(revision) || retained.PreviousRevisionId != expectedPreviousRevisionId)
                throw new InvalidOperationException("The account binding revision identity was already used for a different request.");
            return retained;
        }
        IntercompanyAccountBindingRules.ValidateAppend(revision, expectedPreviousRevisionId, history);
        await RequireChartNodeAsync(connection, transaction, revision, scope, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {_table} (revision_id, binding_id, revision_number, ledger_book_id,
                tenant_id, company_id, chart_account_node_id, effective_from, effective_to, recorded_at_utc, payload)
            values (@revision, @binding, @number, @book, @tenant, @company, @node, @from, @to, @recorded, @payload);
            """;
        command.Parameters.AddWithValue("revision", revision.RevisionId);
        command.Parameters.AddWithValue("binding", revision.BindingId);
        command.Parameters.AddWithValue("number", revision.RevisionNumber);
        command.Parameters.AddWithValue("book", revision.LedgerBookId);
        command.Parameters.AddWithValue("tenant", revision.TenantId);
        command.Parameters.AddWithValue("company", NpgsqlDbType.Text, (object?)revision.CompanyId ?? DBNull.Value);
        command.Parameters.AddWithValue("node", revision.ChartAccountNodeId);
        command.Parameters.AddWithValue("from", revision.EffectiveFrom);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, (object?)revision.EffectiveTo ?? DBNull.Value);
        command.Parameters.AddWithValue("recorded", revision.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, Serialize(revision));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Deserialize(Serialize(revision));
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        _ = RequireTenant();
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"lock table {Qualified("accounting_configuration_chart_nodes")} in share mode; "
                + $"lock table {_table} in share mode;";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new AuthorityLease(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<BookScope> RequireBookAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid bookId, string tenant, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select b.fund_profile_id, t.tenant_id, nullif(t.company_id, 'all')
            from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} t on lower(trim(b.fund_profile_id)) = t.fund_profile_id
            where b.ledger_book_id = @book and lower(trim(b.tenant_id)) = lower(@tenant)
                and lower(trim(t.tenant_id)) = lower(@tenant);
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new TenantScopeRejectedException("intercompany account bindings for an unavailable or foreign book");
        return new BookScope(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<IReadOnlyList<IntercompanyAccountBindingDto>> ReadHistoryAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid book, BookScope scope, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select payload from {_table} where ledger_book_id = @book and lower(tenant_id) = lower(@tenant)
                and lower(coalesce(company_id, '')) = lower(@company)
            order by binding_id, revision_number;
            """;
        command.Parameters.AddWithValue("book", book);
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId ?? string.Empty);
        var result = new List<IntercompanyAccountBindingDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(Deserialize(reader.GetString(0)));
        return result;
    }

    private async Task RequireChartNodeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IntercompanyAccountBindingDto revision, BookScope scope, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select path, account_name, account_type, symbol, financial_account_id, is_archived, intercompany_role
            from {Qualified("accounting_configuration_chart_nodes")}
            where lower(tenant_id) = lower(@tenant) and lower(company_id) = lower(@company)
                and fund_profile_id = @fund and configuration_scope_id = @book and node_id = @node;
            """;
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId ?? "all");
        command.Parameters.AddWithValue("fund", scope.FundProfileId);
        command.Parameters.AddWithValue("book", revision.LedgerBookId.ToString("D"));
        command.Parameters.AddWithValue("node", revision.ChartAccountNodeId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)
            || reader.GetString(0) != revision.AccountPath || reader.GetString(1) != revision.AccountName
            || reader.GetString(2) != revision.AccountType || !reader.IsDBNull(3) || !reader.IsDBNull(4)
            || reader.GetBoolean(5) != revision.IsArchived || reader.IsDBNull(6)
            || reader.GetString(6) != revision.Role.ToString())
            throw new InvalidOperationException("Account bindings require the exact current, unscoped chart node and compatible configured role.");
    }

    private string RequireTenant()
    {
        var tenant = _tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId;
        if (!TenantReadPredicate.IsResolvedTenant(tenant))
            throw new TenantScopeRejectedException("intercompany account binding authority without an owning tenant");
        return tenant!.Trim();
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private string Qualified(string table) => $"\"{_options.SchemaName}\".\"{table}\"";
    private static string Serialize(IntercompanyAccountBindingDto value)
        => JsonSerializer.Serialize(value, IntercompanyAccountContractsJsonContext.Default.IntercompanyAccountBindingDto);
    private static IntercompanyAccountBindingDto Deserialize(string value)
        => JsonSerializer.Deserialize(value, IntercompanyAccountContractsJsonContext.Default.IntercompanyAccountBindingDto)
            ?? throw new InvalidOperationException("Retained intercompany account binding is invalid.");
    private sealed record BookScope(string FundProfileId, string TenantId, string? CompanyId);

    private sealed class AuthorityLease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
