using System.Data;
using System.Text.Json;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Storage.Tenancy;

/// <summary>Counts only: startup diagnostics must not expose retained records or credentials.</summary>
public sealed record TenantCutoverFinding(string Store, string Table, string Reason, int Count);

public sealed record TenantCutoverReadiness(IReadOnlyList<TenantCutoverFinding> Findings)
{
    public bool IsReady => Findings.Count == 0;
}

/// <summary>A retained row and the owning rows its persisted references require to agree.</summary>
public sealed record TenantCutoverRecord(string Store, string Table, string Identity, string? TenantId,
    IReadOnlyList<string> OwnerReferences, bool RequiresOwner = false);

/// <summary>
/// Read-only upgrade guard over every nullable tenant column affected by the strict posture.
/// It neither invents tenant attribution nor changes records while the host starts.
/// </summary>
public static class PostgresTenantCutoverInspector
{
    private sealed record GraphTable(string Table, string Id, bool IsNode, string[] References);
    private static readonly GraphTable[] GraphTables =
    [
        new("organization", "organization_id", true, ["business_ids"]),
        new("business", "business_id", true, ["organization_id", "client_ids", "fund_ids", "investment_portfolio_ids"]),
        new("client", "client_id", true, ["business_id", "investment_portfolio_ids"]),
        new("fund", "fund_id", true, ["business_id", "sleeve_ids", "vehicle_ids", "entity_ids", "investment_portfolio_ids", "account_ids"]),
        new("sleeve", "sleeve_id", true, ["fund_id", "investment_portfolio_ids", "account_ids"]),
        new("vehicle", "vehicle_id", true, ["fund_id", "legal_entity_id", "investment_portfolio_ids", "account_ids"]),
        new("legal_entity", "entity_id", true, []),
        new("investment_portfolio", "investment_portfolio_id", true, ["business_id", "client_id", "fund_id", "sleeve_id", "vehicle_id", "entity_id", "account_ids"]),
        new("fund_structure_linked_account", "account_id", true, []),
        new("ownership_link", "ownership_link_id", false, ["parent_node_id", "child_node_id"]),
        new("fund_structure_assignment", "assignment_id", false, ["node_id"])
    ];

    public static async Task<TenantCutoverReadiness> InspectAsync(
        FundStructureStoreOptions? fund, LedgerJournalStoreOptions? ledger, FundAccountStoreOptions? accounts,
        CancellationToken ct = default)
    {
        var rows = new List<TenantCutoverRecord>();
        var findings = new List<TenantCutoverFinding>();
        IReadOnlyList<JsonElement> retainedQuarantine = [];
        if (fund is not null)
        {
            var schema = Schema(fund.Schema);
            await using var connection = new NpgsqlConnection(fund.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            await SetReadOnlyAsync(connection, transaction, ct).ConfigureAwait(false);
            foreach (var table in GraphTables)
                foreach (var row in await ReadAsync(connection, transaction, schema, table.Table, ct).ConfigureAwait(false))
                    rows.Add(new("fund-structure", table.Table,
                        (table.IsNode ? "node:" : table.Table + ":") + row.GetProperty(table.Id).GetGuid().ToString("D"),
                        Tenant(row), References(row, table.References, "node:"), !table.IsNode));
            retainedQuarantine = await ReadAsync(connection, transaction, schema, "fund_structure_tenant_quarantine", ct).ConfigureAwait(false);
            var open = retainedQuarantine.Count(row => !row.TryGetProperty("resolved_at_utc", out var value) || value.ValueKind == JsonValueKind.Null);
            if (open > 0)
                findings.Add(new("fund-structure", "fund_structure_tenant_quarantine", "UnresolvedQuarantine", open));
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        if (ledger is not null)
        {
            var schema = Schema(ledger.SchemaName);
            await using var connection = new NpgsqlConnection(ledger.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            await SetReadOnlyAsync(connection, transaction, ct).ConfigureAwait(false);
            foreach (var row in await ReadAsync(connection, transaction, schema, "fund_profile_tenancy", ct).ConfigureAwait(false))
                rows.Add(new("ledger", "fund_profile_tenancy", "profile:" + OptionalString(row, "fund_profile_id")?.Trim().ToLowerInvariant(), Tenant(row), []));
            foreach (var row in await ReadAsync(connection, transaction, schema, "ledger_books", ct).ConfigureAwait(false))
            {
                List<string> owners = ["profile:" + OptionalString(row, "fund_profile_id")?.Trim().ToLowerInvariant()];
                if (fund is not null)
                    owners.AddRange(References(row, ["fund_structure_node_id"], "node:"));
                rows.Add(new("ledger", "ledger_books", "book:" + row.GetProperty("ledger_book_id").GetGuid().ToString("D"), Tenant(row), owners, true));
            }
            foreach (var row in await ReadAsync(connection, transaction, schema, "accounting_periods", ct).ConfigureAwait(false))
                rows.Add(new("ledger", "accounting_periods", "period:" + row.GetProperty("period_id").GetGuid().ToString("D"), Tenant(row),
                    References(row, ["ledger_book_id"], "book:"), true));
            foreach (var row in await ReadAsync(connection, transaction, schema, "operations_continuity_workflows", ct).ConfigureAwait(false))
                rows.Add(new("ledger", "operations_continuity_workflows", "workflow:" + row.GetProperty("workflow_id").GetGuid().ToString("D"), Tenant(row),
                    References(row.GetProperty("workflow_json"), ["ledgerBookId"], "book:")));
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        if (accounts is not null)
        {
            var schema = Schema(accounts.Schema);
            await using var connection = new NpgsqlConnection(accounts.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
            await SetReadOnlyAsync(connection, transaction, ct).ConfigureAwait(false);
            foreach (var row in await ReadAsync(connection, transaction, schema, "account_definition", ct).ConfigureAwait(false))
            {
                var id = row.GetProperty("account_id").GetGuid().ToString("D");
                var owners = References(row, ["fund_id", "sleeve_id", "vehicle_id", "entity_id"], "node:").ToList();
                if (rows.Any(node => node.Identity == "node:" + id))
                    owners.Add("node:" + id);
                rows.Add(new("fund-accounts", "account_definition", "account:" + id, Tenant(row), owners));
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        foreach (var resolved in retainedQuarantine.Where(row => row.TryGetProperty("resolved_at_utc", out var value) && value.ValueKind != JsonValueKind.Null))
        {
            var id = resolved.GetProperty("node_id").GetGuid().ToString("D");
            var retained = rows.Where(row => row.Identity.EndsWith(":" + id, StringComparison.Ordinal)).ToArray();
            var tenant = OptionalString(resolved, "resolved_tenant_id");
            if (retained.Length == 0 || retained.Any(row => !SameTenant(tenant, row.TenantId)))
                findings.Add(new("fund-structure", "fund_structure_tenant_quarantine", "ResolutionOwnerMismatch", 1));
        }
        findings.AddRange(Evaluate(rows).Findings);
        return new(findings.GroupBy(item => (item.Store, item.Table, item.Reason))
            .Select(group => new TenantCutoverFinding(group.Key.Store, group.Key.Table, group.Key.Reason, group.Sum(item => item.Count)))
            .OrderBy(item => item.Store, StringComparer.Ordinal).ThenBy(item => item.Table, StringComparer.Ordinal)
            .ThenBy(item => item.Reason, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Pure readiness policy; known references must resolve to exactly one matching owner.</summary>
    public static TenantCutoverReadiness Evaluate(IReadOnlyList<TenantCutoverRecord> rows)
    {
        var owners = rows.GroupBy(row => row.Identity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var findings = new List<TenantCutoverFinding>();
        foreach (var row in rows)
        {
            if (Normalize(row.TenantId) is null)
                findings.Add(new(row.Store, row.Table, "MissingTenantAttribution", 1));
            if (owners[row.Identity].Length != 1)
                findings.Add(new(row.Store, row.Table, "AmbiguousRecordIdentity", 1));
            if (row.RequiresOwner && row.OwnerReferences.Count == 0 ||
                row.OwnerReferences.Any(reference => !owners.TryGetValue(reference, out var found) || found.Length != 1 || Normalize(found[0].TenantId) is null))
                findings.Add(new(row.Store, row.Table, "MissingOwnerAuthority", 1));
            else if (Normalize(row.TenantId) is not null && row.OwnerReferences.Any(reference => !SameTenant(row.TenantId, owners[reference][0].TenantId)))
                findings.Add(new(row.Store, row.Table, "TenantAuthorityMismatch", 1));
        }
        return new(findings.GroupBy(item => (item.Store, item.Table, item.Reason))
            .Select(group => new TenantCutoverFinding(group.Key.Store, group.Key.Table, group.Key.Reason, group.Sum(item => item.Count))).ToArray());
    }

    private static async Task SetReadOnlyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '30s'; SET LOCAL lock_timeout = '5s'", connection, transaction);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, string table, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT to_jsonb(r)::text FROM \"{schema}\".\"{table}\" r", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<JsonElement>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(reader.GetString(0));
            rows.Add(document.RootElement.Clone());
        }
        return rows;
    }

    private static IReadOnlyList<string> References(JsonElement row, string[] properties, string prefix)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!row.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                continue;
            IEnumerable<JsonElement> values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };
            foreach (var item in values)
                references.Add(item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id)
                    ? prefix + id.ToString("D") : "invalid-reference");
        }
        return references.Order(StringComparer.Ordinal).ToArray();
    }

    private static string? OptionalString(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Tenant(JsonElement row) => OptionalString(row, "tenant_id");
    private static string? Normalize(string? tenant)
        => string.IsNullOrWhiteSpace(tenant) || tenant.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null : tenant.Trim();
    private static bool SameTenant(string? first, string? second)
        => Normalize(first) is { } owner && Normalize(second) is { } expected && string.Equals(owner, expected, StringComparison.OrdinalIgnoreCase);
    private static string Schema(string schema)
        => !string.IsNullOrWhiteSpace(schema) && (char.IsAsciiLetter(schema[0]) || schema[0] == '_') &&
           schema.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? schema.ToLowerInvariant() : throw new ArgumentException("A schema must be a PostgreSQL identifier.", nameof(schema));
}
