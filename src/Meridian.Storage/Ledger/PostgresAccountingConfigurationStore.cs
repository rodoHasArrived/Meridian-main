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

    public async Task<AccountingConfigurationWorkspaceDto?> GetAsync(string fundProfileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var workspaceCommand = connection.CreateCommand();
        workspaceCommand.CommandText =
            $"""
            select ledger_book_id,
                   status,
                   configuration_version,
                   updated_at_utc,
                   validation_issues
            from {Qualified("accounting_configuration_workspaces")}
            where fund_profile_id = @fund_profile_id;
            """;
        workspaceCommand.Parameters.AddWithValue("fund_profile_id", normalizedFundProfileId);

        await using var reader = await workspaceCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var ledgerBookId = reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0);
        var status = Enum.Parse<AccountingConfigurationStatusDto>(reader.GetString(1), ignoreCase: true);
        var configurationVersion = reader.GetString(2);
        var updatedAtUtc = reader.GetDateTime(3);
        var validationIssues = Deserialize<IReadOnlyList<AccountingConfigurationValidationIssueDto>>(reader.GetString(4)) ?? [];
        await reader.DisposeAsync().ConfigureAwait(false);

        var chart = await LoadChartAsync(connection, normalizedFundProfileId, ct).ConfigureAwait(false);
        var templates = await LoadTemplatesAsync(connection, normalizedFundProfileId, ct).ConfigureAwait(false);
        var rules = await LoadRulesAsync(connection, normalizedFundProfileId, ct).ConfigureAwait(false);

        return new AccountingConfigurationWorkspaceDto(
            normalizedFundProfileId,
            ledgerBookId,
            status,
            configurationVersion,
            new DateTimeOffset(DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc)),
            LedgerBooks: [],
            ChartOfAccounts: chart,
            JournalTemplates: templates,
            PostingRules: rules,
            ValidationIssues: validationIssues,
            AuditTrail: []);
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
                evidence_links)
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
                @evidence_links);
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
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
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
                   evidence_links
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
                Deserialize<IReadOnlyList<string>>(reader.GetString(10)) ?? []));
        }

        return events;
    }

    private async Task<IReadOnlyList<ChartOfAccountsNodeDto>> LoadChartAsync(NpgsqlConnection connection, string fundProfileId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select node_id, path, account_name, account_type, parent_path, symbol, financial_account_id, is_archived
            from {Qualified("accounting_configuration_chart_nodes")}
            where fund_profile_id = @fund_profile_id
            order by path;
            """;
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);

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

    private async Task<IReadOnlyList<JournalEntryTemplateDto>> LoadTemplatesAsync(NpgsqlConnection connection, string fundProfileId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select template_id, display_name, description, lines, is_archived, version
            from {Qualified("accounting_configuration_journal_templates")}
            where fund_profile_id = @fund_profile_id
            order by template_id;
            """;
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);

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

    private async Task<IReadOnlyList<PostingRuleDto>> LoadRulesAsync(NpgsqlConnection connection, string fundProfileId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select rule_id, display_name, source_event_type, template_id, rule_version, is_archived, description
            from {Qualified("accounting_configuration_posting_rules")}
            where fund_profile_id = @fund_profile_id
            order by rule_id;
            """;
        command.Parameters.AddWithValue("fund_profile_id", fundProfileId);

        var rules = new List<PostingRuleDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
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

    private async Task UpsertWorkspaceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("accounting_configuration_workspaces")} (
                fund_profile_id,
                ledger_book_id,
                status,
                configuration_version,
                updated_at_utc,
                validation_issues)
            values (@fund_profile_id, @ledger_book_id, @status, @configuration_version, @updated_at_utc, @validation_issues)
            on conflict (fund_profile_id) do update set
                ledger_book_id = excluded.ledger_book_id,
                status = excluded.status,
                configuration_version = excluded.configuration_version,
                updated_at_utc = excluded.updated_at_utc,
                validation_issues = excluded.validation_issues;
            """;
        command.Parameters.AddWithValue("fund_profile_id", NormalizeFundProfileId(workspace.FundProfileId));
        AddUuidOrNull(command, "ledger_book_id", workspace.LedgerBookId);
        command.Parameters.AddWithValue("status", workspace.Status.ToString());
        command.Parameters.AddWithValue("configuration_version", workspace.ConfigurationVersion);
        command.Parameters.AddWithValue("updated_at_utc", workspace.UpdatedAtUtc.UtcDateTime);
        AddJson(command, "validation_issues", workspace.ValidationIssues);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ReplaceChartAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountingConfigurationWorkspaceDto workspace, CancellationToken ct)
    {
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_chart_nodes", workspace.FundProfileId, ct).ConfigureAwait(false);
        foreach (var node in workspace.ChartOfAccounts)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_chart_nodes")} (
                    fund_profile_id, node_id, path, account_name, account_type, parent_path, symbol, financial_account_id, is_archived)
                values (@fund_profile_id, @node_id, @path, @account_name, @account_type, @parent_path, @symbol, @financial_account_id, @is_archived);
                """;
            AddScope(command, workspace.FundProfileId);
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
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_journal_templates", workspace.FundProfileId, ct).ConfigureAwait(false);
        foreach (var template in workspace.JournalTemplates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_journal_templates")} (
                    fund_profile_id, template_id, display_name, description, lines, is_archived, version)
                values (@fund_profile_id, @template_id, @display_name, @description, @lines, @is_archived, @version);
                """;
            AddScope(command, workspace.FundProfileId);
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
        await DeleteScopedAsync(connection, transaction, "accounting_configuration_posting_rules", workspace.FundProfileId, ct).ConfigureAwait(false);
        foreach (var rule in workspace.PostingRules)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("accounting_configuration_posting_rules")} (
                    fund_profile_id, rule_id, display_name, source_event_type, template_id, rule_version, is_archived, description)
                values (@fund_profile_id, @rule_id, @display_name, @source_event_type, @template_id, @rule_version, @is_archived, @description);
                """;
            AddScope(command, workspace.FundProfileId);
            command.Parameters.AddWithValue("rule_id", rule.RuleId);
            command.Parameters.AddWithValue("display_name", rule.DisplayName);
            command.Parameters.AddWithValue("source_event_type", rule.SourceEventType);
            command.Parameters.AddWithValue("template_id", rule.TemplateId);
            command.Parameters.AddWithValue("rule_version", rule.RuleVersion);
            command.Parameters.AddWithValue("is_archived", rule.IsArchived);
            AddTextOrNull(command, "description", rule.Description);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task DeleteScopedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string fundProfileId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"delete from {Qualified(table)} where fund_profile_id = @fund_profile_id;";
        AddScope(command, fundProfileId);
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

    private static void AddScope(NpgsqlCommand command, string fundProfileId)
        => command.Parameters.AddWithValue("fund_profile_id", NormalizeFundProfileId(fundProfileId));

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
