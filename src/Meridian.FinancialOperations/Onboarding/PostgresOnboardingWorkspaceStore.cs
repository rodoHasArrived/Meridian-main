using System.Data;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.FinancialOperations.Onboarding;

/// <summary>
/// Retains onboarding workspaces, including shadow-mode state, in the ledger database
/// (<c>V_ledger_069__onboarding_workspaces</c>). Every save runs the shared retention rules against the row it
/// replaces, inside one transaction holding that row. A partial unique index enforces that at most one
/// workspace per tenant holds a ledger book in shadow mode or promoted it, across processes and hosts.
/// </summary>
public sealed class PostgresOnboardingWorkspaceStore : IOnboardingWorkspaceStore
{
    private const string ActiveShadowBookIndex = "ux_onboarding_workspaces_active_shadow_book";
    private readonly LedgerJournalStoreOptions _options;
    private readonly string _table;

    public PostgresOnboardingWorkspaceStore(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = $"\"{options.SchemaName}\".\"onboarding_workspaces\"";
    }

    public async Task<IReadOnlyList<OnboardingWorkspaceDto>> ListAsync(string tenantId, string companyId, CancellationToken ct = default)
    {
        RequireScope(tenantId, companyId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select payload_json from {_table} where tenant_id = @tenant and company_id = @company order by updated_at desc, workspace_id;";
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        return await ReadAllAsync(command, tenantId, companyId, ct).ConfigureAwait(false);
    }

    public async Task<OnboardingWorkspaceDto?> GetAsync(string tenantId, string companyId, string workspaceId, CancellationToken ct = default)
    {
        RequireScope(tenantId, companyId);
        if (string.IsNullOrWhiteSpace(workspaceId))
            return null;
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select payload_json from {_table} where workspace_id = @workspace and tenant_id = @tenant and company_id = @company;";
        command.Parameters.AddWithValue("workspace", workspaceId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("company", companyId);
        return (await ReadAllAsync(command, tenantId, companyId, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<OnboardingWorkspaceDto>> ListByLedgerBookAsync(
        string tenantId, string? companyId, Guid ledgerBookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new OnboardingValidationException("A tenant is required for onboarding storage.");
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select payload_json from {_table}
            where tenant_id = @tenant and ledger_book_id = @book and (@company::text is null or company_id = @company::text)
            order by created_at, workspace_id;
            """;
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("book", ledgerBookId);
        command.Parameters.Add(new NpgsqlParameter("company", NpgsqlDbType.Text) { Value = (object?)companyId ?? DBNull.Value });
        return await ReadAllAsync(command, tenantId, companyId, ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(OnboardingWorkspaceDto workspace, int? expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RequireScope(workspace.Scope?.TenantId!, workspace.Scope?.CompanyId!);
        // Serialize before awaiting so callers cannot mutate nested lists during persistence.
        var retained = OnboardingWorkspaceRetention.Detach(workspace);
        OnboardingWorkspaceRetention.VerifyIntegrity(retained);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var gate = connection.CreateCommand())
        {
            // Serializes writers of one workspace, including its first insert, where no row exists to lock yet.
            gate.Transaction = transaction;
            gate.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0));";
            gate.Parameters.AddWithValue("key", $"onboarding-workspace:{retained.WorkspaceId}");
            await gate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        OnboardingWorkspaceDto? current = null;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"select payload_json from {_table} where workspace_id = @workspace for update;";
            read.Parameters.AddWithValue("workspace", retained.WorkspaceId);
            if (await read.ExecuteScalarAsync(ct).ConfigureAwait(false) is string json)
            {
                current = OnboardingWorkspaceRetention.Deserialize(json);
                OnboardingWorkspaceRetention.VerifyIntegrity(current);
            }
        }

        OnboardingWorkspaceRetention.RequireValidTransition(current, retained, expectedVersion);

        await using (var save = connection.CreateCommand())
        {
            save.Transaction = transaction;
            save.CommandText = $"""
                insert into {_table} (workspace_id, tenant_id, company_id, ledger_book_id, version, shadow_state,
                    created_at, updated_at, payload_json)
                values (@workspace, @tenant, @company, @book, @version, @shadow, @created, @updated, @payload)
                on conflict (workspace_id) do update set
                    version = excluded.version, shadow_state = excluded.shadow_state,
                    updated_at = excluded.updated_at, payload_json = excluded.payload_json
                where {_table}.version = @previous_version
                  and {_table}.tenant_id = excluded.tenant_id and {_table}.company_id = excluded.company_id;
                """;
            save.Parameters.AddWithValue("workspace", retained.WorkspaceId);
            save.Parameters.AddWithValue("tenant", retained.Scope.TenantId);
            save.Parameters.AddWithValue("company", retained.Scope.CompanyId);
            save.Parameters.Add(new NpgsqlParameter("book", NpgsqlDbType.Uuid) { Value = retained.Scope.LedgerBookId == Guid.Empty ? DBNull.Value : (object)retained.Scope.LedgerBookId });
            save.Parameters.AddWithValue("version", retained.Version);
            save.Parameters.Add(new NpgsqlParameter("shadow", NpgsqlDbType.Text)
            {
                Value = (object?)retained.ShadowDesignation?.State ?? DBNull.Value
            });
            save.Parameters.AddWithValue("created", retained.CreatedAtUtc);
            save.Parameters.AddWithValue("updated", retained.UpdatedAtUtc);
            save.Parameters.AddWithValue("payload", OnboardingWorkspaceRetention.Serialize(retained));
            save.Parameters.AddWithValue("previous_version", current?.Version ?? 0);
            try
            {
                if (await save.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new OnboardingConcurrencyException("The onboarding workspace changed. Refresh before saving.");
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(ex.ConstraintName, ActiveShadowBookIndex, StringComparison.Ordinal))
            {
                // Another workspace committed a designation of this book first; nothing of this save is kept.
                throw new OnboardingConcurrencyException(OnboardingWorkspaceRetention.ShadowBookHeldMessage);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<OnboardingWorkspaceDto>> ReadAllAsync(NpgsqlCommand command,
        string tenantId, string? companyId, CancellationToken ct)
    {
        var workspaces = new List<OnboardingWorkspaceDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var workspace = OnboardingWorkspaceRetention.Deserialize(reader.GetString(0));
            OnboardingWorkspaceRetention.VerifyIntegrity(workspace);
            // The indexed columns select rows; the retained document must agree with them.
            if (workspace.Scope.TenantId != tenantId || (companyId is not null && workspace.Scope.CompanyId != companyId))
                throw new InvalidDataException("Retained onboarding storage contains invalid or ambiguous scope.");
            workspaces.Add(workspace);
        }

        return workspaces;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
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

    private static void RequireScope(string tenantId, string companyId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            throw new OnboardingValidationException("A tenant and company are required for onboarding storage.");
    }
}
