using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed class PostgresOperationsContinuityStore :
    IOperationsContinuityRepository,
    IOperationsWorkflowAuditStore,
    IOperationsContinuityWorkflowStartCommitStore,
    IOperationsContinuityTransactionalCommitStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly LedgerJournalStoreOptions _options;
    private readonly ITransactionalLedgerJournalStore _ledgerJournalStore;
    private readonly IOperationsStatusDerivationService _statusDerivation;

    // SEC-005 slice 4c: ambient caller-tenant resolver. Optional so existing construction sites (tests,
    // non-web hosts) keep compiling and stay fail-open; the workstation host injects an
    // IHttpContextAccessor-backed implementation. Null (no tenant in scope) => reads are not scoped.
    private readonly IFundScopeTenantAccessor? _tenantAccessor;

    public PostgresOperationsContinuityStore(
        LedgerJournalStoreOptions options,
        ITransactionalLedgerJournalStore ledgerJournalStore,
        IOperationsStatusDerivationService statusDerivation,
        IFundScopeTenantAccessor? tenantAccessor = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ledgerJournalStore = ledgerJournalStore ?? throw new ArgumentNullException(nameof(ledgerJournalStore));
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
        _tenantAccessor = tenantAccessor;
    }

    // SEC-005 slice 4c: the caller's tenant for the current ambient scope, or null (fail-open).
    private string? ResolveCallerTenant() => _tenantAccessor?.ResolveCallerTenant();

    public async Task SaveAsync(OperationsContinuityWorkflow workflow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        await UpsertWorkflowAsync(connection, transaction, workflow, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<OperationsContinuityWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select workflow_json::text
            from {Qualified("operations_continuity_workflows")}
            where workflow_id = @workflow_id
            """;
        command.Parameters.AddWithValue("workflow_id", workflowId);
        // SEC-005 slice 4c: scope by the workflow's stamped tenant_id so a foreign workflow GUID resolves to
        // not-found rather than leaking the workflow. Fail-open for a tenantless caller / unbound workflow.
        var callerTenant = ResolveCallerTenant();
        if (TenantReadPredicate.ShouldFilter(callerTenant))
        {
            command.CommandText += TenantReadPredicate.FilterClause("tenant_id");
            command.Parameters.AddWithValue(
                TenantReadPredicate.ParameterName,
                TenantReadPredicate.NormalizeParameter(callerTenant!));
        }

        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : DeserializeWorkflow(json, workflowId);
    }

    public async Task<IReadOnlyList<OperationsContinuityWorkflow>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default,
        Guid? ledgerBookId = null)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select workflow_id,
                   workflow_json::text
            from {Qualified("operations_continuity_workflows")}
            where 1 = 1
            """;

        if (fundAccountId.HasValue)
        {
            command.CommandText += " and fund_account_id = @fund_account_id";
            command.Parameters.AddWithValue("fund_account_id", fundAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(periodId))
        {
            command.CommandText += " and period_id = @period_id";
            command.Parameters.AddWithValue("period_id", periodId.Trim());
        }

        if (ledgerBookId.HasValue)
        {
            command.CommandText += " and workflow_json ->> 'ledgerBookId' = @ledger_book_id";
            command.Parameters.AddWithValue("ledger_book_id", ledgerBookId.Value.ToString("D"));
        }

        if (status.HasValue)
        {
            command.CommandText += " and derived_status = @derived_status";
            command.Parameters.AddWithValue("derived_status", status.Value.ToString());
        }

        // SEC-005 slice 4c: scope by the workflow's stamped tenant_id, closing the slice-3b residual where
        // the close-cockpit / workflow-summary reach this store by fund_account_id / ledgerBookId without a
        // fund_profile_id. Fail-open — applied only for a tenant-scoped caller; a null-tenant (unbound)
        // workflow or a tenantless caller passes, so single-company-per-deployment behavior is unchanged.
        var callerTenant = ResolveCallerTenant();
        if (TenantReadPredicate.ShouldFilter(callerTenant))
        {
            command.CommandText += TenantReadPredicate.FilterClause("tenant_id");
            command.Parameters.AddWithValue(
                TenantReadPredicate.ParameterName,
                TenantReadPredicate.NormalizeParameter(callerTenant!));
        }

        command.CommandText += " order by updated_at_utc desc, workflow_id;";

        var results = new List<OperationsContinuityWorkflow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(DeserializeWorkflow(reader.GetString(1), reader.GetGuid(0)));
        }

        return results;
    }

    public async Task<OperationsWorkflowAuditDto> AppendAsync(
        OperationsWorkflowAuditDraft draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        var audit = await AppendAuditAsync(connection, transaction, draft, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return audit;
    }

    public async Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select audit_json::text
            from {Qualified("operations_continuity_audit")}
            where workflow_id = @workflow_id
            order by occurred_at_utc, audit_id;
            """;
        command.Parameters.AddWithValue("workflow_id", workflowId);

        var results = new List<OperationsWorkflowAuditDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(DeserializeAudit(reader.GetString(0), workflowId));
        }

        return results;
    }

    public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowStartAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(auditDraft);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        await UpsertWorkflowAsync(connection, transaction, workflow, ct).ConfigureAwait(false);
        var audit = await AppendAuditAsync(connection, transaction, auditDraft, ct).ConfigureAwait(false);
        workflow.Touch(audit.OccurredAtUtc);
        await UpsertWorkflowAsync(connection, transaction, workflow, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new OperationsContinuityTransactionalCommitResult(workflow, audit);
    }

    public async Task<OperationsContinuityTransactionalCommitResult> CommitLedgerPostingAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        LedgerJournalEntryWrite journalEntry,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(auditDraft);
        ArgumentNullException.ThrowIfNull(journalEntry);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        await _ledgerJournalStore.AppendAsync(connection, transaction, journalEntry, ct).ConfigureAwait(false);
        var audit = await AppendAuditAsync(connection, transaction, auditDraft, ct).ConfigureAwait(false);
        workflow.Touch(audit.OccurredAtUtc);
        await UpsertWorkflowAsync(connection, transaction, workflow, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new OperationsContinuityTransactionalCommitResult(workflow, audit);
    }

    private async Task<OperationsWorkflowAuditDto> AppendAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperationsWorkflowAuditDraft draft,
        CancellationToken ct)
    {
        await AcquireWorkflowAuditLockAsync(connection, transaction, draft.WorkflowId, ct).ConfigureAwait(false);
        var previousHash = await LoadPreviousAuditHashAsync(connection, transaction, draft.WorkflowId, ct).ConfigureAwait(false);
        var audit = OperationsWorkflowAuditHashing.Create(draft, previousHash, DateTimeOffset.UtcNow);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("operations_continuity_audit")} (
                audit_id,
                occurred_at_utc,
                workflow_id,
                fund_account_id,
                period_id,
                event_type,
                from_state,
                to_state,
                gate,
                from_gate_status,
                to_gate_status,
                actor,
                rationale,
                correlation_id,
                references_json,
                previous_hash,
                current_hash,
                audit_json)
            values (
                @audit_id,
                @occurred_at_utc,
                @workflow_id,
                @fund_account_id,
                @period_id,
                @event_type,
                @from_state,
                @to_state,
                @gate,
                @from_gate_status,
                @to_gate_status,
                @actor,
                @rationale,
                @correlation_id,
                cast(@references_json as jsonb),
                @previous_hash,
                @current_hash,
                cast(@audit_json as jsonb));
            """;
        command.Parameters.AddWithValue("audit_id", audit.AuditId);
        command.Parameters.AddWithValue("occurred_at_utc", audit.OccurredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("workflow_id", audit.WorkflowId);
        command.Parameters.AddWithValue("fund_account_id", audit.FundAccountId);
        command.Parameters.AddWithValue("period_id", audit.PeriodId);
        command.Parameters.AddWithValue("event_type", audit.EventType);
        command.Parameters.AddWithValue("from_state", audit.FromState.ToString());
        command.Parameters.AddWithValue("to_state", audit.ToState.ToString());
        command.Parameters.AddWithValue("gate", (object?)audit.Gate?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("from_gate_status", (object?)audit.FromGateStatus?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("to_gate_status", (object?)audit.ToGateStatus?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("actor", audit.Actor);
        command.Parameters.AddWithValue("rationale", (object?)audit.Rationale ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)audit.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("references_json", JsonSerializer.Serialize(audit.References, JsonOptions));
        command.Parameters.AddWithValue("previous_hash", (object?)audit.PreviousHash ?? DBNull.Value);
        command.Parameters.AddWithValue("current_hash", audit.CurrentHash);
        command.Parameters.AddWithValue("audit_json", JsonSerializer.Serialize(audit, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return audit;
    }

    private static async Task AcquireWorkflowAuditLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid workflowId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(@workflow_lock_key);";
        command.Parameters.AddWithValue("workflow_lock_key", CreateWorkflowAuditLockKey(workflowId));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static long CreateWorkflowAuditLockKey(Guid workflowId)
    {
        var hash = SHA256.HashData(workflowId.ToByteArray());
        return BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long)));
    }

    private async Task<string?> LoadPreviousAuditHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid workflowId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select current_hash
            from {Qualified("operations_continuity_audit")}
            where workflow_id = @workflow_id
            order by occurred_at_utc desc, audit_id desc
            limit 1
            for update;
            """;
        command.Parameters.AddWithValue("workflow_id", workflowId);

        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private async Task UpsertWorkflowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperationsContinuityWorkflow workflow,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("operations_continuity_workflows")} (
                workflow_id,
                fund_account_id,
                period_id,
                security_master_snapshot_id,
                broker_source,
                derived_status,
                version,
                created_at_utc,
                updated_at_utc,
                workflow_json,
                updated_at,
                tenant_id)
            values (
                @workflow_id,
                @fund_account_id,
                @period_id,
                @security_master_snapshot_id,
                @broker_source,
                @derived_status,
                @version,
                @created_at_utc,
                @updated_at_utc,
                cast(@workflow_json as jsonb),
                now(),
                -- SEC-005 slice 4c: inherit the partition tenant from the workflow's ledger book (stamped by
                -- V_ledger_020 from the authoritative registry). A workflow with no book / an unbound book
                -- resolves null and stays fail-open until it is attributed.
                (select b.tenant_id
                 from {Qualified("ledger_books")} b
                 where b.ledger_book_id = @tenant_ledger_book_id))
            on conflict (workflow_id) do update
            set fund_account_id = excluded.fund_account_id,
                period_id = excluded.period_id,
                security_master_snapshot_id = excluded.security_master_snapshot_id,
                broker_source = excluded.broker_source,
                derived_status = excluded.derived_status,
                version = excluded.version,
                created_at_utc = excluded.created_at_utc,
                updated_at_utc = excluded.updated_at_utc,
                workflow_json = excluded.workflow_json,
                updated_at = now(),
                -- Preserve an already-stamped owner (first-owner-wins) but fill a null from the re-resolved
                -- book tenant, so a workflow first saved while its book was unbound is stamped on a later save.
                tenant_id = coalesce(operations_continuity_workflows.tenant_id, excluded.tenant_id)
            where operations_continuity_workflows.version < excluded.version;
            """;
        command.Parameters.AddWithValue("workflow_id", workflow.WorkflowId);
        command.Parameters.AddWithValue("fund_account_id", workflow.FundAccountId);
        command.Parameters.AddWithValue("period_id", workflow.PeriodId);
        command.Parameters.AddWithValue("security_master_snapshot_id", (object?)workflow.SecurityMasterSnapshotId ?? DBNull.Value);
        command.Parameters.AddWithValue("broker_source", workflow.BrokerSource);
        command.Parameters.AddWithValue("derived_status", _statusDerivation.Derive(workflow).ToString());
        command.Parameters.AddWithValue("version", workflow.Version);
        command.Parameters.AddWithValue("created_at_utc", workflow.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("updated_at_utc", workflow.UpdatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("workflow_json", JsonSerializer.Serialize(workflow, JsonOptions));
        command.Parameters.AddWithValue("tenant_ledger_book_id", (object?)workflow.LedgerBookId ?? DBNull.Value);

        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Operations continuity workflow '{workflow.WorkflowId}' was not saved because the stored version is newer or equal.");
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("LedgerJournalStoreOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table)
        => $"{ValidateIdentifier(_options.SchemaName, nameof(_options.SchemaName))}.{ValidateIdentifier(table, nameof(table))}";

    private static OperationsContinuityWorkflow DeserializeWorkflow(string json, Guid workflowId)
        => JsonSerializer.Deserialize<OperationsContinuityWorkflow>(json, JsonOptions)
           ?? throw new InvalidOperationException($"Unable to deserialize operations continuity workflow '{workflowId}'.");

    private static OperationsWorkflowAuditDto DeserializeAudit(string json, Guid workflowId)
        => JsonSerializer.Deserialize<OperationsWorkflowAuditDto>(json, JsonOptions)
           ?? throw new InvalidOperationException($"Unable to deserialize operations continuity audit event for workflow '{workflowId}'.");

    private static string ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{parameterName} is required.");
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new InvalidOperationException($"{parameterName} contains an invalid identifier character.");
            }
        }

        return value;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
