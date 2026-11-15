using Meridian.Contracts.Integrity;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using NpgsqlTypes;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    public Task<AutomatedPostingGrantDto> ActivateAsync(ActivateAutomatedPostingGrantRequest request,
        string actor, string tenantId, CancellationToken ct = default)
        => ActivateGrantAsync(request, null, actor, tenantId, ct);

    public Task<AutomatedPostingGrantDto> ActivateWithPolicyAsync(ActivateAutomatedPostingGrantRequest request,
        AccountingPolicyDto policySnapshot, string actor, string tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policySnapshot);
        return ActivateGrantAsync(request, policySnapshot, actor, tenantId, ct);
    }

    private async Task<AutomatedPostingGrantDto> ActivateGrantAsync(ActivateAutomatedPostingGrantRequest request,
        AccountingPolicyDto? policySnapshot, string actor, string tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationsOriginGuard.RequireHumanOperator(request.ActionOrigin, "activate automatic fill posting");
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequireGrantCaller(tenantId, actor);
        var reference = new AutomatedPostingGrantReferenceDto(request.GrantId, 1, tenantId,
            request.FinancialAccountId, request.LedgerBookId, request.PeriodId, request.PolicyId,
            request.PolicyVersion, request.MappingRevision, request.SourceMode, request.CompanyId);
        ValidateGrantReference(reference);
        if (policySnapshot is not null)
        {
            if (policySnapshot.PolicyId != reference.PolicyId || policySnapshot.Version != reference.PolicyVersion)
                throw new LedgerValidationException("The retained automatic policy must match the exact grant policy/version.");
            foreach (var source in new[] { "execution.trade-fill", "execution.trade-commission", "execution.trade-fee" })
            {
                var rules = policySnapshot.RulePack?.Rules.Where(rule => rule.SourceEventType == source).ToArray() ?? [];
                if (rules.Length != 1 || string.IsNullOrWhiteSpace(rules[0].RuleId) || string.IsNullOrWhiteSpace(rules[0].RuleVersion)
                    || !rules[0].RequiresEvidence || rules[0].RequiresApproval || !rules[0].AllowsAutoPosting)
                    throw new LedgerValidationException("The retained automatic policy requires exact evidence-backed automatic rules.");
            }
        }
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        var existing = await LoadAutomatedGrantAsync(connection, transaction, reference.GrantId, tenantId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!existing.IsActive || existing.Reference != reference || existing.ActivatedBy != actor
                || PolicyContent(existing.PolicySnapshot) != PolicyContent(policySnapshot))
                throw new LedgerValidationException("A retained grant cannot be reactivated or rebound; create a new grant identity.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return existing;
        }
        var mappingFingerprint = await ValidateGrantBookAndConfigurationAsync(connection, transaction, reference, ct).ConfigureAwait(false);
        if (policySnapshot is not null)
        {
            var book = await LoadLedgerBookAsync(connection, transaction, reference.LedgerBookId, ct).ConfigureAwait(false);
            var period = await LoadPeriodAsync(connection, transaction, reference.PeriodId, forUpdate: true, ct: ct).ConfigureAwait(false);
            if (book is null || period is null || policySnapshot.AccountingBasis != book.AccountingBasis
                || policySnapshot.FundProfileId is { } policyFund && policyFund != book.FundProfileId
                || policySnapshot.FundStructureNodeId is { } policyNode && policyNode != book.FundStructureNodeId
                || policySnapshot.InstrumentId is not null || policySnapshot.SourceEventId is not null
                || policySnapshot.EffectiveFrom > period.StartDate || policySnapshot.EffectiveTo is { } end && end < period.EndDate)
                throw new LedgerValidationException("The retained policy must cover the grant's exact basis and entire period.");
        }
        var auditHead = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
        var now = GrantTimestamp();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("automated_posting_grants")}
                (grant_id, version, tenant_id, company_id, financial_account_id, ledger_book_id, period_id,
                 policy_id, policy_version, mapping_revision, mapping_fingerprint, source_mode, is_active, activated_by, activated_at_utc,
                 policy_snapshot, policy_snapshot_sha256)
            values (@id, 1, @tenant, @company, @account, @book, @period,
                    @policy, @policy_version, @mapping, @mapping_fingerprint, 'Synthetic', true, @actor, @at, @policy_snapshot, @policy_snapshot_sha256)
            on conflict (grant_id) do nothing
            """;
        AddGrantParameters(command, reference);
        command.Parameters.AddWithValue("mapping_fingerprint", mappingFingerprint);
        var policyJson = policySnapshot is null ? null : JsonSerializer.Serialize(policySnapshot, AutomatedGrantPolicyJsonContext.Default.AccountingPolicyDto);
        command.Parameters.AddWithValue("policy_snapshot", NpgsqlDbType.Jsonb, (object?)policyJson ?? DBNull.Value);
        command.Parameters.AddWithValue("policy_snapshot_sha256", (object?)(policyJson is null ? null : PolicyHash(policyJson)) ?? DBNull.Value);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("at", now);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new LedgerValidationException("The grant identity is already retained; choose a new grant identity.");
        await AppendLedgerAuditAsync(connection, transaction, auditHead, "automation-grant", reference.GrantId, 1,
            "AutomationGrantActivated", actor, null, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new AutomatedPostingGrantDto(reference, true, actor, now) { PolicySnapshot = policySnapshot };
    }

    public async Task<AutomatedPostingGrantDto> RevokeAsync(Guid grantId, long expectedVersion,
        string actor, string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequireGrantCaller(tenantId, actor);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        var current = await LoadAutomatedGrantAsync(connection, transaction, grantId, tenantId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The automated posting grant is unavailable in this tenant.");
        if (current.Reference.Version != expectedVersion)
            throw new LedgerValidationException("The automated posting grant version has changed.");
        if (!current.IsActive)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return current;
        }
        var auditHead = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
        var now = GrantTimestamp();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            update {Qualified("automated_posting_grants")}
            set is_active = false, version = version + 1, revoked_by = @actor, revoked_at_utc = @at
            where grant_id = @id and tenant_id = @tenant and version = @version
            """;
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("at", now);
        command.Parameters.AddWithValue("id", grantId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("version", expectedVersion);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new LedgerValidationException("The automated posting grant version has changed.");
        await AppendLedgerAuditAsync(connection, transaction, auditHead, "automation-grant", grantId, expectedVersion + 1,
            "AutomationGrantRevoked", actor, null, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return current with
        {
            Reference = current.Reference with { Version = expectedVersion + 1 },
            IsActive = false,
            RevokedBy = actor,
            RevokedAtUtc = now
        };
    }

    public async Task<AutomatedPostingGrantDto?> GetAsync(Guid grantId, string tenantId, CancellationToken ct = default)
    {
        RequireGrantCaller(tenantId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadAutomatedGrantAsync(connection, null, grantId, tenantId, ct).ConfigureAwait(false);
    }

    private void RequireGrantCaller(string tenantId, string? actor = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId != tenantId.Trim() || tenantId.Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Automatic posting requires one exact tenant.");
        var ambient = RequireWriteTenant();
        if (ambient is not null && !string.Equals(ambient, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The automated posting grant does not belong to the caller's tenant.");
        if (actor is not null && string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("A retained operator identity is required.");
    }

    public async Task ValidateCurrentAsync(AutomatedPostingGrantReferenceDto reference, CancellationToken ct = default)
    {
        ValidateGrantReference(reference);
        RequireGrantCaller(reference.TenantId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var retained = await LoadAutomatedGrantAsync(connection, transaction, reference.GrantId, reference.TenantId, ct).ConfigureAwait(false);
        if (retained is null || !retained.IsActive || retained.Reference != reference || retained.PolicySnapshot is null)
            throw new LedgerValidationException("The exact retained automatic grant and policy are unavailable.");
        var current = await ValidateGrantBookAndConfigurationAsync(connection, transaction, reference, ct).ConfigureAwait(false);
        await using var fingerprint = connection.CreateCommand();
        fingerprint.Transaction = transaction;
        fingerprint.CommandText = $"select mapping_fingerprint from {Qualified("automated_posting_grants")} where grant_id = @id";
        fingerprint.Parameters.AddWithValue("id", reference.GrantId);
        if (await fingerprint.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string previous || previous != current)
            throw new LedgerValidationException("Automatic posting mapping content changed; activate a new reviewed grant.");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        if (retained.PolicySnapshot.EffectiveFrom > date || retained.PolicySnapshot.EffectiveTo is { } end && end < date)
            throw new LedgerValidationException("The retained automatic policy is not effective today.");
        foreach (var source in new[] { "execution.trade-fill", "execution.trade-commission", "execution.trade-fee" })
        {
            var rules = retained.PolicySnapshot.RulePack?.Rules.Where(rule => rule.SourceEventType == source).ToArray() ?? [];
            if (rules.Length != 1 || !rules[0].RequiresEvidence || rules[0].RequiresApproval || !rules[0].AllowsAutoPosting)
                throw new LedgerValidationException("The retained automatic policy does not authorize execution posting.");
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static void ValidateGrantReference(AutomatedPostingGrantReferenceDto reference)
    {
        if (reference.GrantId == Guid.Empty || reference.LedgerBookId == Guid.Empty || reference.PeriodId == Guid.Empty
            || reference.SourceMode != "Synthetic" || string.IsNullOrWhiteSpace(reference.FinancialAccountId)
            || string.IsNullOrWhiteSpace(reference.PolicyId) || string.IsNullOrWhiteSpace(reference.PolicyVersion)
            || string.IsNullOrWhiteSpace(reference.MappingRevision) || reference.CompanyId == "all")
            throw new LedgerValidationException("An exact synthetic account, book, period, policy and mapping scope is required.");
    }

    private async Task ValidateAutomatedPostingGrantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write, CancellationToken ct)
    {
        if (write.PostingCommand?.ActionOrigin != OperationsActionOriginDto.AuthorizedSystem)
            return;
        var requested = write.PostingCommand.AutomationGrant!;
        RequireGrantCaller(requested.TenantId);
        var retained = await LoadAutomatedGrantAsync(connection, transaction, requested.GrantId, requested.TenantId, ct)
            .ConfigureAwait(false);
        if (retained is null || !retained.IsActive || retained.Reference != requested)
            throw new LedgerValidationException("Automatic posting grant is missing, revoked, stale or outside its exact scope.");
        var policy = retained.PolicySnapshot
            ?? throw new LedgerValidationException("Automatic posting requires a retained operator-authorized policy snapshot.");
        var effectiveDate = write.Entry.Metadata.EffectiveDate ?? write.PostingCommand.EffectiveDate;
        var rules = policy.RulePack?.Rules.Where(rule => rule.SourceEventType == write.PostingCommand.SourceEventType).ToArray() ?? [];
        if (policy.PolicyId != requested.PolicyId || policy.Version != requested.PolicyVersion || policy.AccountingBasis != write.AccountingBasis
            || policy.EffectiveFrom > effectiveDate || policy.EffectiveTo is { } end && end < effectiveDate
            || rules.Length != 1 || rules[0].RuleId != write.RuleId || rules[0].RuleVersion != write.RuleVersion
            || !rules[0].RequiresEvidence || rules[0].RequiresApproval || !rules[0].AllowsAutoPosting)
            throw new LedgerValidationException("Automatic posting differs from its retained effective policy and exact automatic rule authority.");
        var currentFingerprint = await ValidateGrantBookAndConfigurationAsync(connection, transaction, requested, ct).ConfigureAwait(false);
        await using var fingerprint = connection.CreateCommand();
        fingerprint.Transaction = transaction;
        fingerprint.CommandText = $"select mapping_fingerprint from {Qualified("automated_posting_grants")} where grant_id = @id";
        fingerprint.Parameters.AddWithValue("id", requested.GrantId);
        if (await fingerprint.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string retainedFingerprint
            || retainedFingerprint != currentFingerprint)
            throw new LedgerValidationException("Automatic posting mapping content changed; activate a new reviewed grant.");
    }

    private static void ValidateAutomaticLotScope(AtomicTaxLotJournalCommand command)
    {
        if (command.Journal.PostingCommand?.ActionOrigin == OperationsActionOriginDto.AuthorizedSystem
            && (command.Direction != OpenLotDirection.Long
                || command.MutationKind is not (AtomicTaxLotMutationKind.Acquisition or AtomicTaxLotMutationKind.Disposal)
                || command.Journal.PostingCommand.SourceEventType != "execution.trade-fill"
                || command.AcquisitionLot?.Acquisition is { QuantityBasis: not LotQuantityBasis.Units }))
            throw new LedgerValidationException("Automatic fill grants authorize only long equity acquisitions and disposals.");
    }

    private async Task<string> ValidateGrantBookAndConfigurationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        AutomatedPostingGrantReferenceDto reference, CancellationToken ct)
    {
        await using var scope = connection.CreateCommand();
        scope.Transaction = transaction;
        scope.CommandText = $"""
            select b.fund_profile_id
            from {Qualified("ledger_books")} b
            join {Qualified("accounting_periods")} p on p.ledger_book_id = b.ledger_book_id
            join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
            where b.ledger_book_id = @book and p.period_id = @period
              and b.tenant_id = @tenant and p.tenant_id = @tenant and t.tenant_id = @tenant
              and t.company_id = coalesce(@company, 'all')
              and b.base_currency = 'USD' and p.status = 'Open'
              and b.accounting_policy_id = @policy and b.accounting_policy_version = @policy_version
            """;
        AddGrantParameters(scope, reference);
        var fundProfileId = await scope.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new LedgerValidationException("Automatic posting requires its exact tenant-owned open USD book and retained policy.");
        await using var config = connection.CreateCommand();
        config.Transaction = transaction;
        config.CommandText = $"""
            select configuration_version from {Qualified("accounting_configuration_workspaces")}
            where tenant_id = @tenant and company_id = @company and fund_profile_id = @fund
              and configuration_scope_id = @scope and ledger_book_id = @book and status = 'Active'
            for share
            """;
        config.Parameters.AddWithValue("tenant", reference.TenantId);
        config.Parameters.AddWithValue("company", reference.CompanyId ?? "all");
        config.Parameters.AddWithValue("fund", fundProfileId);
        config.Parameters.AddWithValue("scope", reference.LedgerBookId.ToString("D"));
        config.Parameters.AddWithValue("book", reference.LedgerBookId);
        if (await config.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string version || version != reference.MappingRevision)
            throw new LedgerValidationException("Automatic posting requires the current retained accounting mapping revision.");

        // ConfigurationVersion is a display revision; several edits can share it. Retain the exact
        // mapped content too, with canonical database JSON and stable child ordering.
        config.CommandText = $"""
            select jsonb_build_object('workspace', to_jsonb(w),
                'chart', {MappingRows("accounting_configuration_chart_nodes", "node_id")},
                'templates', {MappingRows("accounting_configuration_journal_templates", "template_id")},
                'rules', {MappingRows("accounting_configuration_posting_rules", "rule_id")})::text
            from {Qualified("accounting_configuration_workspaces")} w
            where w.tenant_id = @tenant and w.company_id = @company and w.fund_profile_id = @fund
              and w.configuration_scope_id = @scope and w.ledger_book_id = @book
            """;
        var content = await config.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
            ?? throw new LedgerValidationException("The retained mapping is unavailable.");
        return Sha256Digest.ComputeUtf8(content);
    }

    private string MappingRows(string table, string key)
        => $"""
            (select coalesce(jsonb_agg(to_jsonb(n) order by n.{key}), '[]'::jsonb)
             from {Qualified(table)} n where n.tenant_id = w.tenant_id and n.company_id = w.company_id
               and n.fund_profile_id = w.fund_profile_id and n.configuration_scope_id = w.configuration_scope_id)
            """;

    private async Task<AutomatedPostingGrantDto?> LoadAutomatedGrantAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid grantId, string tenantId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select version, tenant_id, financial_account_id, ledger_book_id, period_id, policy_id,
                   policy_version, mapping_revision, source_mode, company_id, is_active,
                   activated_by, activated_at_utc, revoked_by, revoked_at_utc, policy_snapshot, policy_snapshot_sha256
            from {Qualified("automated_posting_grants")} where grant_id = @id and tenant_id = @tenant
            {(transaction is null ? "" : "for update")}
            """;
        command.Parameters.AddWithValue("id", grantId);
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var reference = new AutomatedPostingGrantReferenceDto(grantId, reader.GetInt64(0), reader.GetString(1),
            reader.GetString(2), reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9));
        var policy = reader.IsDBNull(15) ? null : JsonSerializer.Deserialize(reader.GetString(15), AutomatedGrantPolicyJsonContext.Default.AccountingPolicyDto);
        if ((policy is null) != reader.IsDBNull(16) || (policy is not null && reader.GetString(16) != PolicyHash(
                JsonSerializer.Serialize(policy, AutomatedGrantPolicyJsonContext.Default.AccountingPolicyDto))))
            throw new LedgerValidationException("The retained automatic policy snapshot failed its integrity hash.");
        return new AutomatedPostingGrantDto(reference, reader.GetBoolean(10), reader.GetString(11),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(12), DateTimeKind.Utc)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(14), DateTimeKind.Utc)))
        { PolicySnapshot = policy };
    }

    private static void AddGrantParameters(NpgsqlCommand command, AutomatedPostingGrantReferenceDto reference)
    {
        command.Parameters.AddWithValue("id", reference.GrantId);
        command.Parameters.AddWithValue("tenant", reference.TenantId);
        command.Parameters.AddWithValue("company", (object?)reference.CompanyId ?? DBNull.Value);
        command.Parameters.AddWithValue("account", reference.FinancialAccountId);
        command.Parameters.AddWithValue("book", reference.LedgerBookId);
        command.Parameters.AddWithValue("period", reference.PeriodId);
        command.Parameters.AddWithValue("policy", reference.PolicyId);
        command.Parameters.AddWithValue("policy_version", reference.PolicyVersion);
        command.Parameters.AddWithValue("mapping", reference.MappingRevision);
    }

    private static string? PolicyContent(AccountingPolicyDto? policy) => policy is null ? null
        : JsonSerializer.Serialize(policy with { CreatedAt = default }, AutomatedGrantPolicyJsonContext.Default.AccountingPolicyDto);

    private static string PolicyHash(string json) => Sha256Digest.ComputeUtf8(json);

    private static DateTimeOffset GrantTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AccountingPolicyDto))]
internal sealed partial class AutomatedGrantPolicyJsonContext : JsonSerializerContext
{
}
