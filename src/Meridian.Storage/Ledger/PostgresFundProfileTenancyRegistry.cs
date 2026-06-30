using Meridian.Contracts.Tenancy;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>
/// PostgreSQL-backed <see cref="IFundProfileTenancyRegistry"/> — the storage-enforced fund→tenant
/// ownership source of truth for shared/multi-tenant deployments. Fund profiles are keyed
/// case-insensitively (the stored id is normalized to a trimmed, lower-invariant value, consistent with
/// how FundProfileId is compared elsewhere). Binding is first-owner-wins via
/// <c>insert ... on conflict (fund_profile_id) do nothing</c>.
/// </summary>
public sealed class PostgresFundProfileTenancyRegistry : IFundProfileTenancyRegistry
{
    private readonly LedgerJournalStoreOptions _options;

    public PostgresFundProfileTenancyRegistry(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<FundProfileOwnership> BindAsync(
        string fundProfileId, string tenantId, string? companyId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fund = NormalizeFund(fundProfileId);
        if (string.IsNullOrEmpty(fund))
        {
            throw new ArgumentException("fundProfileId is required.", nameof(fundProfileId));
        }

        var tenant = NormalizeRequired(tenantId, nameof(tenantId));
        var company = NormalizeScope(companyId);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Single-statement upsert-and-return: a no-op SET on conflict (company_id to its existing value)
        // lets RETURNING surface the EXISTING owner's row without changing tenant_id, so first-owner-wins
        // is preserved and a fresh insert returns the caller. This avoids a multi-statement reader.
        command.CommandText =
            $"""
            insert into {Qualified("fund_profile_tenancy")} (fund_profile_id, tenant_id, company_id)
            values (@fund_profile_id, @tenant_id, @company_id)
            on conflict (fund_profile_id) do update set company_id = fund_profile_tenancy.company_id
            returning tenant_id, company_id;
            """;
        command.Parameters.AddWithValue("fund_profile_id", fund);
        command.Parameters.AddWithValue("tenant_id", tenant);
        command.Parameters.AddWithValue("company_id", company);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new FundProfileOwnership(fund, reader.GetString(0), ScopeToNullable(reader.GetString(1)));
        }

        // RETURNING always yields the row, so this is unreachable; return the caller's claim as a safe
        // fallback.
        return new FundProfileOwnership(fund, tenant, ScopeToNullable(company));
    }

    public async Task<FundProfileOwnership?> ResolveAsync(string fundProfileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fund = NormalizeFund(fundProfileId);
        if (string.IsNullOrEmpty(fund))
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tenant_id, company_id from {Qualified("fund_profile_tenancy")}
            where fund_profile_id = @fund_profile_id;
            """;
        command.Parameters.AddWithValue("fund_profile_id", fund);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new FundProfileOwnership(fund, reader.GetString(0), ScopeToNullable(reader.GetString(1)))
            : null;
    }

    public async Task<bool> IsAccessibleAsync(
        string fundProfileId, string tenantId, string? companyId = null, CancellationToken ct = default)
    {
        var owner = await ResolveAsync(fundProfileId, ct).ConfigureAwait(false);
        return owner is null || owner.IsHeldBy(tenantId);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("LedgerJournalStoreOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string Qualified(string tableName)
        => $"{ValidateIdentifier(_options.SchemaName)}.{ValidateIdentifier(tableName)}";

    private static string NormalizeFund(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeScope(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim();

    private static string? ScopeToNullable(string value)
        => string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) ? null : value;

    private static string ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("A PostgreSQL identifier is required.");
        }

        foreach (var c in value)
        {
            var isAsciiAlphanumeric = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
            if (!isAsciiAlphanumeric && c != '_')
            {
                throw new InvalidOperationException($"Invalid PostgreSQL identifier '{value}'.");
            }
        }

        return value;
    }
}
