using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Append-only, tenant- and company-owned reciprocal balance confirmation versions. A confirmation whose books belong to
/// two companies of the tenant is also visible to each side's company (V_ledger_079).
/// </summary>
public sealed class PostgresIntercompanyBalanceConfirmationStore : IIntercompanyBalanceConfirmationStore
{
    private readonly LedgerJournalStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly string _table;

    public PostgresIntercompanyBalanceConfirmationStore(LedgerJournalStoreOptions options, IFundScopeTenantAccessor tenants)
    {
        _options = options;
        _tenants = tenants;
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = $"\"{options.SchemaName}\".\"intercompany_balance_confirmations\"";
    }

    public async Task<IntercompanyBalanceConfirmationDto?> GetAsync(Guid confirmationId, string? companyId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await LatestAsync(connection, null, "confirmation_id=@id", command => command.Parameters.AddWithValue("id", confirmationId),
            tenant, companyId, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyBalanceConfirmationDto?> FindAsync(Guid accountingArrangementId, DateOnly asOf, string? companyId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await LatestAsync(connection, null, "accounting_arrangement_id=@arrangement and as_of=@asOf", command =>
        {
            command.Parameters.AddWithValue("arrangement", accountingArrangementId);
            command.Parameters.AddWithValue("asOf", asOf);
        }, tenant, companyId, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyBalanceConfirmationDto> AppendAsync(IntercompanyBalanceConfirmationDto next, long expectedVersion, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        if (!string.Equals(next.TenantId, tenant, StringComparison.Ordinal))
            throw new TenantScopeRejectedException("balance confirmation for another tenant");
        if (next.Version != expectedVersion + 1)
            throw new InvalidOperationException("A confirmation version must directly follow the expected version.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var fence = connection.CreateCommand())
        {
            fence.Transaction = transaction;
            fence.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0));";
            // Keyed without the company so a proposal by either side of a cross-company pair serializes with the other's.
            fence.Parameters.AddWithValue("key", $"intercompany-confirmation:{tenant}:{next.AccountingArrangementId:D}:{next.AsOf:yyyy-MM-dd}");
            await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var current = await LatestAsync(connection, transaction, "accounting_arrangement_id=@arrangement and as_of=@asOf", command =>
        {
            command.Parameters.AddWithValue("arrangement", next.AccountingArrangementId);
            command.Parameters.AddWithValue("asOf", next.AsOf);
        }, tenant, next.CompanyId, ct).ConfigureAwait(false);
        if (current is not null && current.Version >= next.Version)
        {
            // An exact replay of a retained version is idempotent; anything else lost a race.
            var retained = current.Version == next.Version ? current : await VersionAsync(connection, transaction, next, tenant, ct).ConfigureAwait(false);
            if (retained is not null && Serialize(retained) == Serialize(next))
                return retained;
            throw new InvalidOperationException("The confirmation changed since it was read. Reload it and act on the current version.");
        }
        if ((current?.Version ?? 0) != expectedVersion || (current is not null && current.ConfirmationId != next.ConfirmationId))
            throw new InvalidOperationException("The confirmation changed since it was read. Reload it and act on the current version.");
        if (next.Version == 1)
        {
            // Another company's identity for the same profile and date that either side could see already exists.
            foreach (var company in new[] { next.PartyOneCompanyId, next.PartyTwoCompanyId }.Where(item => item is not null).Distinct())
            {
                var competing = await LatestAsync(connection, transaction, "accounting_arrangement_id=@arrangement and as_of=@asOf", command =>
                {
                    command.Parameters.AddWithValue("arrangement", next.AccountingArrangementId);
                    command.Parameters.AddWithValue("asOf", next.AsOf);
                }, tenant, company, ct).ConfigureAwait(false);
                if (competing is not null && competing.ConfirmationId != next.ConfirmationId)
                    throw new InvalidOperationException("The confirmation changed since it was read. Reload it and act on the current version.");
            }
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {_table} (confirmation_id,version,tenant_id,company_id,accounting_arrangement_id,as_of,payload,
                party_one_company_id,party_two_company_id)
            values (@id,@version,@tenant,@company,@arrangement,@asOf,@payload,@partyOneCompany,@partyTwoCompany);
            """;
        command.Parameters.AddWithValue("id", next.ConfirmationId);
        command.Parameters.AddWithValue("version", next.Version);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", NpgsqlDbType.Text, (object?)next.CompanyId ?? DBNull.Value);
        command.Parameters.AddWithValue("arrangement", next.AccountingArrangementId);
        command.Parameters.AddWithValue("asOf", next.AsOf);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, Serialize(next));
        command.Parameters.AddWithValue("partyOneCompany", NpgsqlDbType.Text, next.IsCrossCompany ? (object?)next.PartyOneCompanyId ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("partyTwoCompany", NpgsqlDbType.Text, next.IsCrossCompany ? (object?)next.PartyTwoCompanyId ?? DBNull.Value : DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Deserialize(Serialize(next));
    }

    private async Task<IntercompanyBalanceConfirmationDto?> VersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IntercompanyBalanceConfirmationDto next, string tenant, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select payload from {_table} where confirmation_id=@id and version=@version and tenant_id=@tenant and coalesce(company_id,'')=@company;";
        command.Parameters.AddWithValue("id", next.ConfirmationId);
        command.Parameters.AddWithValue("version", next.Version);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", next.CompanyId ?? "");
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is string json ? Deserialize(json) : null;
    }

    private async Task<IntercompanyBalanceConfirmationDto?> LatestAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string predicate, Action<NpgsqlCommand> bind, string tenant, string? company, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The latest version of each matching confirmation decides who may see it: the proposing company, or either
        // side's company when the latest version spans two companies.
        command.CommandText = $"""
            select payload from (
                select distinct on (confirmation_id) payload, version, company_id, party_one_company_id, party_two_company_id
                from {_table}
                where {predicate} and tenant_id=@tenant
                order by confirmation_id, version desc) latest
            where coalesce(company_id,'')=@company or party_one_company_id=@company or party_two_company_id=@company
            order by version desc limit 1;
            """;
        bind(command);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company ?? "");
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is string json && Deserialize(json) is { } found
            && found.IsVisibleTo(company) ? found : null;
    }

    private string RequireTenant()
    {
        var tenant = _tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId;
        if (!TenantReadPredicate.IsResolvedTenant(tenant))
            throw new TenantScopeRejectedException("balance confirmation without a concrete tenant");
        return tenant!.Trim();
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static string Serialize(IntercompanyBalanceConfirmationDto value)
        => JsonSerializer.Serialize(value, IntercompanyBalanceConfirmationJsonContext.Default.IntercompanyBalanceConfirmationDto);
    private static IntercompanyBalanceConfirmationDto Deserialize(string json)
        => JsonSerializer.Deserialize(json, IntercompanyBalanceConfirmationJsonContext.Default.IntercompanyBalanceConfirmationDto)
            ?? throw new InvalidOperationException("Invalid retained balance confirmation.");
}
