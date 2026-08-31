using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Npgsql;
using NpgsqlTypes;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

public sealed class PostgresAccountingConfigurationStore : IAccountingConfigurationStore, IAccountingActionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly LedgerJournalStoreOptions _options;

    public PostgresAccountingConfigurationStore(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string HeadTable => Qualified("accounting_action_audit_chain_head");

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

    /// <summary>
    /// Appends an audit event and extends the tamper-evident chain over it (W9-GOV-008 criterion 3).
    /// </summary>
    /// <remarks>
    /// <para>The head is a locked row advanced in the same transaction as the insert, following
    /// <c>PostgresReportingArtifactAuditStore</c> rather than inventing a second scheme: the digest
    /// shape is the one <see cref="AccountingAuditChain"/> defines, so the file posture and this one
    /// carry identical tamper-evidence and an operator does not have to learn two models depending
    /// on which store a deployment happens to have configured.</para>
    ///
    /// <para>Fails closed. The head is verified against the final retained event before the append,
    /// so a mutated, reordered or truncated history cannot acquire valid-looking successors — and
    /// because the verification and the advance share one transaction, two concurrent appenders
    /// cannot both chain off the same predecessor and fork the chain.</para>
    /// </remarks>
    /// <exception cref="AccountingAuditChainIntegrityException">The retained chain does not verify.</exception>
    public async Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(auditEvent);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        var head = await LockAndReadChainHeadAsync(connection, transaction, ct).ConfigureAwait(false);
        await VerifyChainHeadAsync(connection, transaction, head, ct).ConfigureAwait(false);

        // Digest the event as it will actually be stored, not as it arrived. AddTextOrNull trims and
        // nulls blank text, so hashing the raw DTO records a digest of a value the row does not hold:
        // the append commits, and the next append -- which recomputes the payload digest from the
        // retained row -- reports EventMutated and refuses, permanently stopping the chain over an
        // event nobody touched. Normalizing once, here, is what keeps "what was hashed" and "what was
        // written" the same string.
        var normalized = NormalizeForPersistence(auditEvent);
        var payloadHash = AccountingAuditChain.ComputePayloadHash(normalized);

        // Idempotent on the event id, matching the file posture. audit_event_id is the primary key,
        // so a repeat would raise a unique violation rather than corrupt anything here -- but the
        // repeat is what RecoverPendingAuditAsync does after a crash between a mutation and its
        // audit, and letting it throw makes recovery fail on the one path written to complete it.
        // Read under the head lock taken above, so a concurrent append cannot slip in behind it.
        var retainedHash = await ReadRetainedPayloadHashAsync(
            connection, transaction, normalized.AuditEventId, ct).ConfigureAwait(false);
        if (retainedHash is not null)
        {
            if (!string.Equals(retainedHash, payloadHash, StringComparison.Ordinal))
            {
                // Two distinct events claiming one identity. Appending is impossible (the key is
                // taken) and ignoring it would drop an audit record, so it is named instead.
                throw new InvalidOperationException(
                    $"Audit event '{normalized.AuditEventId.ToString("D", CultureInfo.InvariantCulture)}' "
                    + "is already retained with different content.");
            }

            return;
        }

        var entryHash = AccountingAuditChain.ComputeEntryHash(head.NextSequence, head.LastHash, payloadHash);
        auditEvent = normalized;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                tenant_id,
                company_id,
                report_group_principal_ids,
                chain_sequence,
                payload_hash,
                previous_hash,
                entry_hash)
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
                @tenant_id,
                @company_id,
                @report_group_principal_ids,
                @chain_sequence,
                @payload_hash,
                @previous_hash,
                @entry_hash);
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
        AddTextOrNull(command, "tenant_id", auditEvent.TenantId);
        AddTextOrNull(command, "company_id", auditEvent.CompanyId);
        AddJson(command, "report_group_principal_ids", auditEvent.ReportGroupPrincipalIds ?? []);
        command.Parameters.AddWithValue("chain_sequence", NpgsqlDbType.Bigint, head.NextSequence);
        command.Parameters.AddWithValue("payload_hash", NpgsqlDbType.Text, payloadHash);
        command.Parameters.AddWithValue(
            "previous_hash", NpgsqlDbType.Text, (object?)head.LastHash ?? DBNull.Value);
        command.Parameters.AddWithValue("entry_hash", NpgsqlDbType.Text, entryHash);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await AdvanceChainHeadAsync(connection, transaction, head.NextSequence, entryHash, ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The payload digest of a retained audit event, or <c>null</c> when the id is not yet taken.
    /// </summary>
    /// <remarks>
    /// Recomputed from the retained row rather than read from its <c>payload_hash</c> column, so an
    /// event written before chaining -- which has no stored digest -- is compared on the same terms
    /// as a chained one.
    /// </remarks>
    private async Task<string?> ReadRetainedPayloadHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditEventId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {AuditEventColumns}
            from {Qualified("accounting_action_audit_events")}
            where audit_event_id = @audit_event_id;
            """;
        command.Parameters.AddWithValue("audit_event_id", auditEventId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return AccountingAuditChain.ComputePayloadHash(ReadAuditEvent(reader));
    }

    private sealed record AccountingAuditChainHead(
        int SchemaVersion,
        long NextSequence,
        string? LastHash,
        long GenesisSequence,
        long PreChainEventCount);

    private async Task<AccountingAuditChainHead> LockAndReadChainHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select schema_version,
                   next_sequence,
                   last_hash,
                   genesis_sequence,
                   pre_chain_event_count
            from {HeadTable}
            where chain_id = 1
            for update;
            """;
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, ct)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // A missing head is not an empty chain: it means the row that records where the chain
            // starts is gone, so nothing can say which retained events were ever covered.
            throw new AccountingAuditChainIntegrityException(new AccountingAuditChainVerification(
                AccountingAuditChainStatus.AnchorMissing,
                LinksChecked: 0,
                PreChainEventCount: 0,
                "The accounting audit chain head row is missing; append failed closed."));
        }

        return new AccountingAuditChainHead(
            reader.GetInt32(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    /// <summary>
    /// Verifies the head against the final chained event before building on it.
    /// </summary>
    /// <remarks>
    /// Deliberately checks both directions. A head that claims events which are not there is a
    /// truncated tail; chained events past the head are a forked or rewound chain. Either read as
    /// "fine" if only one direction were checked.
    /// </remarks>
    private async Task VerifyChainHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AccountingAuditChainHead head,
        CancellationToken ct)
    {
        // Before anything is compared: a chain written under hashing rules this build does not
        // implement cannot be checked by it. The head records schema_version for exactly this, and
        // the file posture already refuses on it -- here it was selected by nobody, so a v2 chain
        // would have been verified with v1 rules, reported EventMutated over events nobody touched,
        // and, had it passed, taken a v1 link on top of a v2 history that no build could verify
        // again. Refusing is the whole purpose of the column.
        if (head.SchemaVersion != AccountingAuditChainState.CurrentSchemaVersion)
        {
            throw ChainFailure(
                AccountingAuditChainStatus.UnsupportedSchemaVersion,
                head,
                $"Chain schema version {head.SchemaVersion.ToString(CultureInfo.InvariantCulture)} "
                + $"is not version {AccountingAuditChainState.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {AuditEventColumns},
                   chain_sequence,
                   payload_hash,
                   previous_hash,
                   entry_hash
            from {Qualified("accounting_action_audit_events")}
            where chain_sequence is not null
            order by chain_sequence desc
            limit 1;
            """;

        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, ct)
            .ConfigureAwait(false);
        var hasChainedEvent = await reader.ReadAsync(ct).ConfigureAwait(false);

        if (head.NextSequence == head.GenesisSequence)
        {
            if (head.LastHash is not null)
            {
                throw ChainFailure(
                    AccountingAuditChainStatus.BrokenLink, head,
                    "An empty accounting audit chain carries a predecessor hash.");
            }

            if (hasChainedEvent)
            {
                throw ChainFailure(
                    AccountingAuditChainStatus.AnchorMismatch, head,
                    "Chained accounting audit events exist while the chain head records none.");
            }

            return;
        }

        if (!hasChainedEvent)
        {
            throw ChainFailure(
                AccountingAuditChainStatus.MissingEvent, head,
                "The accounting audit chain head points past every retained chained event.");
        }

        var finalEvent = ReadAuditEvent(reader);
        var sequence = reader.GetInt64(14);
        var payloadHash = reader.GetString(15);
        var previousHash = reader.IsDBNull(16) ? null : reader.GetString(16);
        var retainedHash = reader.GetString(17);

        if (sequence != head.NextSequence - 1)
        {
            throw ChainFailure(
                AccountingAuditChainStatus.AnchorMismatch, head,
                $"The chain head expects sequence {(head.NextSequence - 1).ToString(CultureInfo.InvariantCulture)} "
                + $"but the final retained event is {sequence.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (head.LastHash is null)
        {
            throw ChainFailure(
                AccountingAuditChainStatus.BrokenLink, head,
                "A non-empty accounting audit chain is missing its predecessor hash.");
        }

        // Recomputed from the retained event, not taken from the stored payload_hash. Deriving the
        // entry hash from a column the same edit could have rewritten checks only that the row is
        // self-consistent: an actor, action or evidence list edited together with nothing else would
        // still satisfy it, and this append would then extend tampered history while reporting that
        // it had verified the chain.
        if (!string.Equals(
                AccountingAuditChain.ComputePayloadHash(finalEvent),
                payloadHash,
                StringComparison.Ordinal))
        {
            throw ChainFailure(
                AccountingAuditChainStatus.EventMutated, head,
                $"The final chained accounting audit event at sequence "
                + $"{sequence.ToString(CultureInfo.InvariantCulture)} no longer matches its recorded digest.");
        }

        var computed = AccountingAuditChain.ComputeEntryHash(sequence, previousHash, payloadHash);
        if (!string.Equals(retainedHash, head.LastHash, StringComparison.Ordinal)
            || !string.Equals(retainedHash, computed, StringComparison.Ordinal))
        {
            throw ChainFailure(
                AccountingAuditChainStatus.BrokenLink, head,
                "The accounting audit chain head or its final event failed hash verification.");
        }
    }

    private async Task AdvanceChainHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long sequence,
        string entryHash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // Guarded by the sequence this append read under the row lock: if anything advanced the head
        // in between, this updates nothing and the append fails rather than forking the chain.
        command.CommandText =
            $"""
            update {HeadTable}
            set next_sequence = @next_sequence,
                last_hash = @last_hash
            where chain_id = 1
              and next_sequence = @expected_sequence;
            """;
        command.Parameters.AddWithValue("next_sequence", NpgsqlDbType.Bigint, sequence + 1);
        command.Parameters.AddWithValue("last_hash", NpgsqlDbType.Text, entryHash);
        command.Parameters.AddWithValue("expected_sequence", NpgsqlDbType.Bigint, sequence);

        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new AccountingAuditChainIntegrityException(new AccountingAuditChainVerification(
                AccountingAuditChainStatus.AnchorMismatch,
                LinksChecked: 0,
                PreChainEventCount: 0,
                "The accounting audit chain head could not be advanced atomically.",
                sequence));
        }
    }

    private static AccountingAuditChainIntegrityException ChainFailure(
        AccountingAuditChainStatus status,
        AccountingAuditChainHead head,
        string detail)
        => new(new AccountingAuditChainVerification(
            status,
            LinksChecked: (int)Math.Min(int.MaxValue, Math.Max(0, head.NextSequence - head.GenesisSequence)),
            PreChainEventCount: (int)Math.Min(int.MaxValue, head.PreChainEventCount),
            detail,
            head.NextSequence));

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
                   tenant_id,
                   company_id,
                   report_group_principal_ids
            from {Qualified("accounting_action_audit_events")}
            where 1 = 1
            """;

        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            // FundProfileId is matched case-insensitively everywhere it is consumed (the file store's
            // audit list and the publish-time run resolver both use OrdinalIgnoreCase), so the audit
            // query must too — otherwise a case-variant fund (e.g. FUND-X vs fund-x) is wrongly treated
            // as having no history, which would let the tenant guard mis-classify a foreign fund as
            // unknown.
            command.CommandText += " and lower(fund_profile_id) = lower(@fund_profile_id)";
            command.Parameters.AddWithValue("fund_profile_id", fundProfileId.Trim());
        }

        if (ledgerBookId.HasValue)
        {
            command.CommandText += " and ledger_book_id = @ledger_book_id";
            command.Parameters.AddWithValue("ledger_book_id", ledgerBookId.Value);
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            command.CommandText += " and tenant_id = @tenant_id";
            command.Parameters.AddWithValue("tenant_id", tenantId.Trim());
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
            events.Add(ReadAuditEvent(reader));
        }

        return events;
    }

    /// <summary>
    /// The audit-event column list, in the order <see cref="ReadAuditEvent"/> expects.
    /// </summary>
    private const string AuditEventColumns =
        """
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
        tenant_id,
        company_id,
        report_group_principal_ids
        """;

    /// <summary>
    /// Materializes one audit event from <see cref="AuditEventColumns"/>.
    /// </summary>
    /// <remarks>
    /// Shared with the chain verification paths deliberately. Recomputing a payload digest is only
    /// meaningful if the event it digests was read exactly the way the reading path reads it, so a
    /// second hand-rolled projection here would be a way for verification and retrieval to disagree
    /// about the same row.
    /// </remarks>
    private static AccountingActionAuditEventDto ReadAuditEvent(NpgsqlDataReader reader)
        => new(
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
            reader.IsDBNull(12) ? null : reader.GetString(12),
            Deserialize<IReadOnlyList<string>>(reader.GetString(13)) ?? [],
            reader.IsDBNull(11) ? null : reader.GetString(11));

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
        // Npgsql truncates this to the microsecond timestamptz holds, so the row cannot carry the
        // 100ns tick it was handed. That is the reason AccountingConfigurationService digests
        // UpdatedAtUtc through AccountingAuditChain.ToRetainedPrecision: it compares a digest taken
        // before this write against one taken after a reload, and hashing the untruncated tick made
        // the two disagree on every interrupted mutation (Codex review finding on PR #2871). The
        // reduction belongs on the digest rather than here, where it would be a no-op.
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

    /// <summary>
    /// Applies <see cref="AddTextOrNull"/>'s rule to the event itself, so the payload digest covers
    /// the values the row will hold.
    /// </summary>
    /// <remarks>
    /// The chain digest and the parameter binding must agree on every field, or an event stored with
    /// a trimmed actor is read back as mutated. The collections need no equivalent: the digest
    /// already folds null and empty together, which is how <c>ReadAuditEvent</c> materializes them.
    /// </remarks>
    private static AccountingActionAuditEventDto NormalizeForPersistence(
        AccountingActionAuditEventDto auditEvent)
        => auditEvent with
        {
            // RecordedAtUtc is deliberately not touched here. It looks like it should be -- a
            // timestamptz holds microseconds while DateTimeOffset carries 100ns ticks, and
            // AccountingAuditChain digests the truncated form -- but Npgsql truncates identically
            // when it encodes the parameter, so the row already holds the instant that was hashed.
            // AnEventRecordedFinerThanAMicrosecond_DoesNotMakeTheNextAppendReportTampering pins
            // that, because it is a property of the driver rather than of this class.
            Actor = NormalizeOptional(auditEvent.Actor) ?? auditEvent.Actor,
            Action = NormalizeOptional(auditEvent.Action) ?? auditEvent.Action,
            FundProfileId = NormalizeOptional(auditEvent.FundProfileId),
            CorrelationId = NormalizeOptional(auditEvent.CorrelationId),
            BeforeHash = NormalizeOptional(auditEvent.BeforeHash) ?? auditEvent.BeforeHash,
            AfterHash = NormalizeOptional(auditEvent.AfterHash) ?? auditEvent.AfterHash,
            TenantId = NormalizeOptional(auditEvent.TenantId),
            CompanyId = NormalizeOptional(auditEvent.CompanyId),
        };


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
