using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

public sealed class PostgresAccountingConfigurationStore : IAccountingConfigurationStore, IAccountingActionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly LedgerJournalStoreOptions _options;

    public PostgresAccountingConfigurationStore(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var configurationScopeId = ConfigurationScopeId(ledgerBookId);
        var tenantScopeId = TenantScopeId(tenantId);
        var companyScopeId = CompanyScopeId(companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var workspaceCommand = connection.CreateCommand();
        workspaceCommand.CommandText =
            $"""
            select tenant_id,
                   company_id,
                   ledger_book_id,
                   status,
                   configuration_version,
                   updated_at_utc,
                   validation_issues
            from {Qualified("accounting_configuration_workspaces")}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and fund_profile_id = @fund_profile_id
              and configuration_scope_id = @configuration_scope_id;
            """;
        workspaceCommand.Parameters.AddWithValue("tenant_id", tenantScopeId);
        workspaceCommand.Parameters.AddWithValue("company_id", companyScopeId);
        workspaceCommand.Parameters.AddWithValue("fund_profile_id", normalizedFundProfileId);
        workspaceCommand.Parameters.AddWithValue("configuration_scope_id", configurationScopeId);

        await using var reader = await workspaceCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var loadedTenantId = ScopeToNullable(reader.GetString(0));
        var loadedCompanyId = ScopeToNullable(reader.GetString(1));
        var loadedLedgerBookId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        var status = Enum.Parse<AccountingConfigurationStatusDto>(reader.GetString(3), ignoreCase: true);
        var configurationVersion = reader.GetString(4);
        var updatedAtUtc = reader.GetDateTime(5);
        var validationIssues = Deserialize<IReadOnlyList<AccountingConfigurationValidationIssueDto>>(reader.GetString(6)) ?? [];
        await reader.DisposeAsync().ConfigureAwait(false);

        var chart = await LoadChartAsync(connection, tenantScopeId, companyScopeId, normalizedFundProfileId, configurationScopeId, ct).ConfigureAwait(false);
        var templates = await LoadTemplatesAsync(connection, tenantScopeId, companyScopeId, normalizedFundProfileId, configurationScopeId, ct).ConfigureAwait(false);
        var rules = await LoadRulesAsync(connection, tenantScopeId, companyScopeId, normalizedFundProfileId, configurationScopeId, ct).ConfigureAwait(false);
        var testCases = await LoadRuleTestCasesAsync(connection, tenantScopeId, companyScopeId, normalizedFundProfileId, configurationScopeId, ct).ConfigureAwait(false);

        return new AccountingConfigurationWorkspaceDto(
            normalizedFundProfileId,
            loadedLedgerBookId,
            status,
            configurationVersion,
            new DateTimeOffset(DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc)),
            LedgerBooks: [],
            ChartOfAccounts: chart,
            JournalTemplates: templates,
            PostingRules: rules,
            ValidationIssues: validationIssues,
            AuditTrail: [],
            RuleTestCases: testCases,
            TenantId: loadedTenantId,
            CompanyId: loadedCompanyId);
    }

    public async Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(workspace);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await UpsertWorkspaceAsync(connection, transaction, workspace, ct).ConfigureAwait(false);
        await ReplaceChartAsync(connection, transaction, workspace, ct).ConfigureAwait(false);
        await ReplaceTemplatesAsync(connection, transaction, workspace, ct).ConfigureAwait(false);
        await ReplaceRulesAsync(connection, transaction, workspace, ct).ConfigureAwait(false);
        await ReplaceRuleTestCasesAsync(connection, transaction, workspace, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(auditEvent);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("accounting_action_audit_events")} (
                audit_event_id,
                recorded_at_utc,
                actor,
                action,
                fund_profile_id,
                ledger_book_id,
                correlation_id,
                before_hash,
                after_hash,
                validation_issues,
                evidence_links,
                company_id,
                report_group_principal_ids)
            values (
                @audit_event_id,
                @recorded_at_utc,
                @actor,
                @action,
                @fund_profile_id,
                @ledger_book_id,
                @correlation_id,
                @before_hash,
                @after_hash,
                @validation_issues,
                @evidence_links,
                @company_id,
                @report_group_principal_ids);
            """;
        command.Parameters.AddWithValue("audit_event_id", auditEvent.AuditEventId);
        command.Parameters.AddWithValue("recorded_at_utc", auditEvent.RecordedAtUtc.UtcDateTime);
        AddTextOrNull(command, "actor", auditEvent.Actor);
        AddTextOrNull(command, "action", auditEvent.Action);
        AddTextOrNull(command, "fund_profile_id", auditEvent.FundProfileId);
        AddUuidOrNull(command, "ledger_book_id", auditEvent.LedgerBookId);
        AddTextOrNull(command, "correlation_id", auditEvent.CorrelationId);
        AddTextOrNull(command, "before_hash", auditEvent.BeforeHash);
        AddTextOrNull(command, "after_hash", auditEvent.AfterHash);
        AddJson(command, "validation_issues", auditEvent.ValidationIssues);
        AddJson(command, "evidence_links", auditEvent.EvidenceLinks);
        AddTextOrNull(command, "company_id", auditEvent.CompanyId);
        AddJson(command, "report_group_principal_ids", auditEvent.ReportGroupPrincipalIds ?? []);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select audit_event_id,
                   recorded_at_utc,
                   actor,
                   action,
                   fund_profile_id,
                   ledger_book_id,
                   correlation_id,
                   before_hash,
                   after_hash,
                   validation_issues,
                   evidence_links,
                   company_id,
                   report_group_principal_ids
            from {Qualified("accounting_action_audit_events")}
            where 1 = 1
            """;

        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            command.CommandText += " and fund_profile_id = @fund_profile_id";
            command.Parameters.AddWithValue("fund_profile_id", fundProfileId.Trim());
        }

        if (ledgerBookId.HasValue)
        {
            command.CommandText += " and ledger_book_id = @ledger_book_id";
            command.Parameters.AddWithValue("ledger_book_id", ledgerBookId.Value);
        }

        if (!string.IsNullOrWhiteSpace(companyId))
        {
            command.CommandText += " and company_id = @company_id";
            command.Parameters.AddWithValue("company_id", companyId.Trim());
        }

        command.CommandText += " order by recorded_at_utc desc, audit_event_id;";

        var events = new List<AccountingActionAuditEventDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            events.Add(new AccountingActionAuditEventDto(
                reader.GetGuid(0),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                Deserialize<IReadOnlyList<AccountingConfigurationValidationIssueDto>>(reader.GetString(9)) ?? [],
                Deserialize<IReadOnlyList<string>>(reader.GetString(10)) ?? [],
                reader.IsDBNull(11) ? null : reader.GetString(11),
                Deserialize<IReadOnlyList<string>>(reader.GetString(12)) ?? []));
        }

        return events;
    }

    private async Task<IReadOnlyList<ChartOfAccountsNodeDto>> LoadChartAsync(
        NpgsqlConnection connection,
        string tenantScopeId,
        string companyScopeId,
        string fundProfileId,
        string configurationScopeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select node_id, path, account_name, account_type, parent_path, symbol, financial_account_id, is_archived
            from {Qualified("accounting_configuration_chart_nodes")}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and fund_profile_id = @fund_profile_id
              and configuration_scope_id = @configuration_scope_id
            order by path;
            """;
        command.Parameters.AddWithValue("tenant_id", tenantScopeId);
        command.Parameters.AddWithValue("company_id", companyScopeId);
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);
        command.Parameters.AddWithValue("configuration_scope_id", configurationScopeId);

        var nodes = new List<ChartOfAccountsNodeDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            nodes.Add(new ChartOfAccountsNodeDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetBoolean(7)));
        }

        return nodes;
    }

    private async Task<IReadOnlyList<JournalEntryTemplateDto>> LoadTemplatesAsync(
        NpgsqlConnection connection,
        string tenantScopeId,
        string companyScopeId,
        string fundProfileId,
        string configurationScopeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select template_id, display_name, description, lines, is_archived, version
            from {Qualified("accounting_configuration_journal_templates")}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and fund_profile_id = @fund_profile_id
              and configuration_scope_id = @configuration_scope_id
            order by template_id;
            """;
        command.Parameters.AddWithValue("tenant_id", tenantScopeId);
        command.Parameters.AddWithValue("company_id", companyScopeId);
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);
        command.Parameters.AddWithValue("configuration_scope_id", configurationScopeId);

        var templates = new List<JournalEntryTemplateDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            templates.Add(new JournalEntryTemplateDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Deserialize<IReadOnlyList<JournalEntryTemplateLineDto>>(reader.GetString(3)) ?? [],
                reader.GetBoolean(4),
                reader.GetString(5)));
        }

        return templates;
    }

    private async Task<IReadOnlyList<PostingRuleDto>> LoadRulesAsync(
        NpgsqlConnection connection,
        string tenantScopeId,
        string companyScopeId,
        string fundProfileId,
        string configurationScopeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select rule_id, display_name, source_event_type, template_id, rule_version, is_archived, description, rule_payload
            from {Qualified("accounting_configuration_posting_rules")}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and fund_profile_id = @fund_profile_id
              and configuration_scope_id = @configuration_scope_id
            order by rule_id;
            """;
        command.Parameters.AddWithValue("tenant_id", tenantScopeId);
        command.Parameters.AddWithValue("company_id", companyScopeId);
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);
        command.Parameters.AddWithValue("configuration_scope_id", configurationScopeId);

        var rules = new List<PostingRuleDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(7) &&
                Deserialize<PostingRuleDto>(reader.GetString(7)) is { } richRule)
            {
                rules.Add(richRule);
                continue;
            }

            rules.Add(new PostingRuleDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rules;
    }

    private async Task<IReadOnlyList<AccountingRuleTestCaseDto>> LoadRuleTestCasesAsync(
        NpgsqlConnection connection,
        string tenantScopeId,
        string companyScopeId,
        string fundProfileId,
        string configurationScopeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select test_case_payload
            from {Qualified("accounting_configuration_rule_test_cases")}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and fund_profile_id = @fund_profile_id
              and configuration_scope_id = @configuration_scope_id
            order by display_name, test_case_id;
            """;
        command.Parameters.AddWithValue("tenant_id", tenantScopeId);
        command.Parameters.AddWithValue("company_id", companyScopeId);
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);
        command.Parameters.AddWithValue("configuration_scope_id", configurationScopeId);

        var testCases = new List<AccountingRuleTestCaseDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (Deserialize<AccountingRuleTestCaseDto>(reader.GetString(0)) is { } testCase)
            {
                testCases.Add(testCase);
            }
        }

        return testCases;
    }

    private async Task UpsertWorkspaceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("accounting_configuration_workspaces")} (
                tenant_id,
                company_id,
                fund_profile_id,
                configuration_scope_id,
                ledger_book_id,
                status,
                configuration_version,
                updated_at_utc,
                validation_issues)
            values (@tenant_id, @company_id, @fund_profile_id, @configuration_scope_id, @ledger_book_id, @status, @configuration_version, @updated_at_utc, @validation_issues)
            on conflict (tenant_id, company_id, fund_profile_id, configuration_scope_id) do update set
                ledger_book_id = excluded.ledger_book_id,
                status = excluded.status,
                configuration_version = excluded.configuration_version,
                updated_at_utc = excluded.updated_at_utc,
                validation_issues = excluded.validation_issues;
            """;
        command.Parameters.AddWithValue("tenant_id", TenantScopeId(workspace.TenantId));
        command.Parameters.AddWithValue("company_id", CompanyScopeId(workspace.CompanyId));
        command.Parameters.AddWithValue("fund_profile_id", NormalizeFundProfileId(workspace.FundProfileId));
        command.Parameters.AddWithValue("configuration_scope_id", ConfigurationScopeId(workspace.LedgerBookId));
        AddUuidOrNull(command, "ledger_book_id", workspace.LedgerBookId);
        command.Parameters.AddWithValue("status", workspace.Status.ToString());
        command.Parameters.AddWithValue("configuration_version", workspace.ConfigurationVersion);
        command.Parameters.AddWithValue("updated_at_utc", workspace.UpdatedAtUtc.UtcDateTime);
        AddJson(command, "validation_issues", workspace.ValidationIssues);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ReplaceChartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_chart_nodes", workspace, ct).ConfigureAwait(false);
        foreach (var node in workspace.ChartOfAccounts)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_chart_nodes")} (
                    tenant_id, company_id, fund_profile_id, configuration_scope_id, node_id, path, account_name, account_type, parent_path, symbol, financial_account_id, is_archived)
                values (@tenant_id, @company_id, @fund_profile_id, @configuration_scope_id, @node_id, @path, @account_name, @account_type, @parent_path, @symbol, @financial_account_id, @is_archived);
                """;
            AddScope(command, workspace);
            command.Parameters.AddWithValue("node_id", node.NodeId);
            command.Parameters.AddWithValue("path", node.Path);
            command.Parameters.AddWithValue("account_name", node.AccountName);
            command.Parameters.AddWithValue("account_type", node.AccountType);
            AddTextOrNull(command, "parent_path", node.ParentPath);
            AddTextOrNull(command, "symbol", node.Symbol);
            AddTextOrNull(command, "financial_account_id", node.FinancialAccountId);
            command.Parameters.AddWithValue("is_archived", node.IsArchived);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceTemplatesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_journal_templates", workspace, ct).ConfigureAwait(false);
        foreach (var template in workspace.JournalTemplates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_journal_templates")} (
                    tenant_id, company_id, fund_profile_id, configuration_scope_id, template_id, display_name, description, lines, is_archived, version)
                values (@tenant_id, @company_id, @fund_profile_id, @configuration_scope_id, @template_id, @display_name, @description, @lines, @is_archived, @version);
                """;
            AddScope(command, workspace);
            command.Parameters.AddWithValue("template_id", template.TemplateId);
            command.Parameters.AddWithValue("display_name", template.DisplayName);
            command.Parameters.AddWithValue("description", template.Description);
            AddJson(command, "lines", template.Lines);
            command.Parameters.AddWithValue("is_archived", template.IsArchived);
            command.Parameters.AddWithValue("version", template.Version);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceRulesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_posting_rules", workspace, ct).ConfigureAwait(false);
        foreach (var rule in workspace.PostingRules)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_posting_rules")} (
                    tenant_id, company_id, fund_profile_id, configuration_scope_id, rule_id, display_name, source_event_type, template_id, rule_version, is_archived, description, rule_payload)
                values (@tenant_id, @company_id, @fund_profile_id, @configuration_scope_id, @rule_id, @display_name, @source_event_type, @template_id, @rule_version, @is_archived, @description, @rule_payload);
                """;
            AddScope(command, workspace);
            command.Parameters.AddWithValue("rule_id", rule.RuleId);
            command.Parameters.AddWithValue("display_name", rule.DisplayName);
            command.Parameters.AddWithValue("source_event_type", rule.SourceEventType);
            command.Parameters.AddWithValue("template_id", rule.TemplateId ?? string.Empty);
            command.Parameters.AddWithValue("rule_version", rule.RuleVersion);
            command.Parameters.AddWithValue("is_archived", rule.IsArchived);
            AddTextOrNull(command, "description", rule.Description);
            AddJson(command, "rule_payload", rule);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ReplaceRuleTestCasesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_rule_test_cases", workspace, ct).ConfigureAwait(false);
        foreach (var testCase in workspace.RuleTestCases)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_rule_test_cases")} (
                    tenant_id, company_id, fund_profile_id, configuration_scope_id, test_case_id, display_name, test_case_payload)
                values (@tenant_id, @company_id, @fund_profile_id, @configuration_scope_id, @test_case_id, @display_name, @test_case_payload);
                """;
            AddScope(command, workspace);
            command.Parameters.AddWithValue("test_case_id", testCase.TestCaseId);
            command.Parameters.AddWithValue("display_name", testCase.DisplayName);
            AddJson(command, "test_case_payload", testCase);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task DeleteScopedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        AccountingConfigurationWorkspaceDto workspace,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"delete from {Qualified(table)} where tenant_id = @tenant_id and company_id = @company_id and fund_profile_id = @fund_profile_id and configuration_scope_id = @configuration_scope_id;";
        AddScope(command, workspace);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

    private string Qualified(string tableName)
        => $"{ValidateIdentifier(_options.SchemaName)}.{ValidateIdentifier(tableName)}";

    private static void AddScope(NpgsqlCommand command, AccountingConfigurationWorkspaceDto workspace)
    {
        command.Parameters.AddWithValue("tenant_id", TenantScopeId(workspace.TenantId));
        command.Parameters.AddWithValue("company_id", CompanyScopeId(workspace.CompanyId));
        command.Parameters.AddWithValue("fund_profile_id", NormalizeFundProfileId(workspace.FundProfileId));
        command.Parameters.AddWithValue("configuration_scope_id", ConfigurationScopeId(workspace.LedgerBookId));
    }

    private static void AddTextOrNull(NpgsqlCommand command, string name, string? value)
        => command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private static void AddUuidOrNull(NpgsqlCommand command, string name, Guid? value)
        => command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);

    private static void AddJson<T>(NpgsqlCommand command, string name, T value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = JsonSerializer.Serialize(value, JsonOptions);
    }

    private static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions);

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string ConfigurationScopeId(Guid? ledgerBookId)
        => ledgerBookId.HasValue ? ledgerBookId.Value.ToString("D") : "fund";

    private static string TenantScopeId(string? tenantId)
        => NormalizeScopeId(tenantId);

    private static string CompanyScopeId(string? companyId)
        => NormalizeScopeId(companyId);

    private static string NormalizeScopeId(string? value)
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
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new InvalidOperationException($"PostgreSQL identifier '{value}' contains an invalid character.");
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
