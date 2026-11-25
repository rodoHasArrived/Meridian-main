using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

/// <summary>
/// PostgreSQL manual journal drafts (ledger migration 077). Mirrors <c>FileManualJournalEntryDraftStore</c>:
/// the same normalization, scope key, filters and ordering. Each row retains the complete draft DTO so
/// init-only, server-held fields round-trip exactly. Optimistic version checks belong to the workbench.
/// </summary>
public sealed class PostgresManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LedgerJournalStoreOptions _options;
    private readonly string _table;

    public PostgresManualJournalEntryDraftStore(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("A ledger connection string is required.", nameof(options));
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = $"\"{options.SchemaName}\".\"manual_journal_entry_drafts\"";
    }

    /// <summary>The schema whose drafts and command receipts this store shares.</summary>
    public LedgerJournalStoreOptions Options => _options;

    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select fund_profile_id, updated_at_utc, journal_entry_id from {_table}";
        var rows = new List<(string Fund, DateTimeOffset Updated, Guid Id)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add((NormalizeFundProfileId(reader.GetString(0)), reader.GetFieldValue<DateTimeOffset>(1), reader.GetGuid(2)));
        // Keep the spelling of the most recently updated draft, as the file store's ordered snapshot does.
        return rows
            .OrderByDescending(static row => row.Updated)
            .ThenBy(static row => row.Id)
            .Select(static row => row.Fund)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
        => Order(await QueryAsync(fundProfileId, ledgerBookId, null, tenantId, companyId, ct).ConfigureAwait(false));

    public async Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
        => Order(await QueryAsync(fundProfileId, null, journalEntryId, tenantId, companyId, ct).ConfigureAwait(false))
            .FirstOrDefault();

    public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
        => SaveBatchAsync([draft], ct);

    public async Task SaveBatchAsync(IReadOnlyList<ManualJournalEntryDraftDto> drafts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        var normalized = drafts
            .Select(draft => draft ?? throw new ArgumentException("Manual journal entry draft batches cannot contain null drafts.", nameof(drafts)))
            .Select(draft => draft with
            {
                FundProfileId = NormalizeFundProfileId(draft.FundProfileId),
                TenantId = NormalizeOptional(draft.TenantId),
                CompanyId = NormalizeOptional(draft.CompanyId)
            })
            .ToArray();
        if (normalized.Length == 0)
            return;

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var draft in normalized)
        {
            await using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = $"""
                insert into {_table}
                    (fund_key, tenant_key, company_key, journal_entry_id, fund_profile_id, tenant_id, company_id,
                     ledger_book_id, version, updated_at_utc, payload)
                values (@fund_key, @tenant_key, @company_key, @id, @fund, @tenant, @company, @book, @version, @updated, @payload)
                on conflict (fund_key, tenant_key, company_key, journal_entry_id) do update set
                    fund_profile_id = excluded.fund_profile_id,
                    tenant_id = excluded.tenant_id,
                    company_id = excluded.company_id,
                    ledger_book_id = excluded.ledger_book_id,
                    version = excluded.version,
                    updated_at_utc = excluded.updated_at_utc,
                    payload = excluded.payload,
                    recorded_at = now()
                """;
            upsert.Parameters.AddWithValue("fund_key", FundKey(draft.FundProfileId));
            upsert.Parameters.AddWithValue("tenant_key", TenantKey(draft.TenantId));
            upsert.Parameters.AddWithValue("company_key", CompanyKey(draft.CompanyId));
            upsert.Parameters.AddWithValue("id", draft.JournalEntryId);
            upsert.Parameters.AddWithValue("fund", draft.FundProfileId);
            upsert.Parameters.AddWithValue("tenant", (object?)draft.TenantId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("company", (object?)draft.CompanyId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("book", (object?)draft.LedgerBookId ?? DBNull.Value);
            upsert.Parameters.AddWithValue("version", draft.Version);
            upsert.Parameters.AddWithValue("updated", draft.UpdatedAtUtc.ToUniversalTime());
            upsert.Parameters.AddWithValue("payload", JsonSerializer.Serialize(draft, JsonOptions));
            await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ManualJournalEntryDraftDto>> QueryAsync(
        string fundProfileId, Guid? ledgerBookId, Guid? journalEntryId, string? tenantId, string? companyId, CancellationToken ct)
    {
        var tenant = NormalizeOptional(tenantId);
        var company = NormalizeOptional(companyId);
        var filter = new List<string> { "fund_key = @fund_key" };
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("fund_key", FundKey(fundProfileId));
        if (ledgerBookId is { } book)
        {
            filter.Add("ledger_book_id = @book");
            command.Parameters.AddWithValue("book", book);
        }
        if (journalEntryId is { } id)
        {
            filter.Add("journal_entry_id = @id");
            command.Parameters.AddWithValue("id", id);
        }
        if (tenant is not null)
        {
            filter.Add("tenant_key = @tenant_key");
            command.Parameters.AddWithValue("tenant_key", TenantKey(tenant));
        }
        if (company is not null)
        {
            filter.Add("company_key = @company_key");
            command.Parameters.AddWithValue("company_key", CompanyKey(company));
        }
        command.CommandText = $"select payload, journal_entry_id from {_table} where {string.Join(" and ", filter)}";
        var result = new List<ManualJournalEntryDraftDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var draft = JsonSerializer.Deserialize<ManualJournalEntryDraftDto>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidDataException("A manual journal draft payload is empty.");
            if (draft.JournalEntryId != reader.GetGuid(1))
                throw new InvalidDataException("A manual journal draft payload contradicts its database identity.");
            result.Add(draft);
        }
        return result;
    }

    // Order in memory so ties break exactly as the file store does (Guid comparison, not uuid collation).
    private static IReadOnlyList<ManualJournalEntryDraftDto> Order(IEnumerable<ManualJournalEntryDraftDto> drafts)
        => drafts.OrderByDescending(static item => item.UpdatedAtUtc).ThenBy(static item => item.JournalEntryId).ToArray();

    private static string FundKey(string fundProfileId) => NormalizeFundProfileId(fundProfileId).ToLowerInvariant();

    private static string TenantKey(string? tenantId) => NormalizeOptional(tenantId)?.ToLowerInvariant() ?? "tenant:any";

    private static string CompanyKey(string? companyId) => NormalizeOptional(companyId)?.ToLowerInvariant() ?? "company:any";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
