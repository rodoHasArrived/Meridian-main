using Meridian.Contracts.Integrity;
using System.Data;
using System.Text.Json;
using Meridian.Storage.FundAccounts;
using Npgsql;

namespace Meridian.Storage.FundStructure;

/// <summary>
/// Explicit maintenance session. Ledger evidence is read under SHARE locks; fund rows are read
/// under SHARE ROW EXCLUSIVE locks. Normal INSERT/UPDATE/DELETE operations therefore cannot
/// change either input between validation and the fund transaction's commit, including phantoms.
/// </summary>
public sealed class PostgresFundStructureTenantBackfillStore : IFundStructureTenantBackfillStore
{
    private readonly string _fundConnection;
    private readonly string _ledgerConnection;
    private readonly string _fundSchema;
    private readonly string _ledgerSchema;
    private readonly int _lockTimeoutSeconds;
    private readonly FundAccountStoreOptions? _fundAccounts;

    private sealed record Table(string Name, string IdColumn, string Kind, bool IsNode,
        string[] Parents, string[] Children);

    private static readonly Table[] Tables =
    [
        new("organization", "organization_id", "Organization", true, [], ["business_ids"]),
        new("business", "business_id", "Business", true, ["organization_id"], ["client_ids", "fund_ids", "investment_portfolio_ids"]),
        new("client", "client_id", "Client", true, ["business_id"], ["investment_portfolio_ids"]),
        new("fund", "fund_id", "Fund", true, ["business_id"], ["sleeve_ids", "vehicle_ids", "entity_ids", "investment_portfolio_ids", "account_ids"]),
        new("sleeve", "sleeve_id", "Sleeve", true, ["fund_id"], ["investment_portfolio_ids", "account_ids"]),
        new("vehicle", "vehicle_id", "Vehicle", true, ["fund_id", "legal_entity_id"], ["investment_portfolio_ids", "account_ids"]),
        new("legal_entity", "entity_id", "Entity", true, [], []),
        new("investment_portfolio", "investment_portfolio_id", "InvestmentPortfolio", true,
            ["business_id", "client_id", "fund_id", "sleeve_id", "vehicle_id", "entity_id"], ["account_ids"]),
        new("fund_structure_linked_account", "account_id", "Account", true, [], []),
        new("ownership_link", "ownership_link_id", "OwnershipLink", false, ["parent_node_id"], ["child_node_id"]),
        new("fund_structure_assignment", "assignment_id", "Assignment", false, ["node_id"], [])
    ];

    public PostgresFundStructureTenantBackfillStore(
        FundStructureStoreOptions fundOptions, string ledgerConnectionString, string ledgerSchema,
        int lockTimeoutSeconds = 5, FundAccountStoreOptions? fundAccounts = null)
    {
        ArgumentNullException.ThrowIfNull(fundOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(fundOptions.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerConnectionString);
        _fundSchema = ValidateSchema(fundOptions.Schema);
        _ledgerSchema = ValidateSchema(ledgerSchema);
        if (string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(fundOptions.ConnectionString).Database) ||
            string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(ledgerConnectionString).Database))
            throw new ArgumentException("Both connection settings require an explicit database name.");
        if (lockTimeoutSeconds is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(lockTimeoutSeconds));
        _fundConnection = fundOptions.ConnectionString;
        _ledgerConnection = ledgerConnectionString;
        _lockTimeoutSeconds = lockTimeoutSeconds;
        if (fundAccounts is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fundAccounts.ConnectionString);
            if (string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(fundAccounts.ConnectionString).Database))
                throw new ArgumentException("The fund account connection requires an explicit database name.");
            _fundAccounts = new() { ConnectionString = fundAccounts.ConnectionString, Schema = ValidateSchema(fundAccounts.Schema) };
        }
    }

    public async Task<FundStructureTenantBackfillReceipt?> FindReceiptAsync(Guid runId, CancellationToken ct = default)
    {
        await using var fund = new NpgsqlConnection(_fundConnection);
        await fund.OpenAsync(ct).ConfigureAwait(false);
        return await ReadReceiptAsync(fund, null, _fundSchema, runId, ct).ConfigureAwait(false);
    }

    public async Task<IFundStructureTenantBackfillSession> OpenSessionAsync(CancellationToken ct = default)
    {
        var sameDatabase = string.Equals(ConnectionIdentity(_fundConnection), ConnectionIdentity(_ledgerConnection), StringComparison.Ordinal);
        // One connection/transaction is mandatory for apply: separate source locks can disappear
        // on connection loss before another database commits. Separate databases support preview only.
        var ledger = new NpgsqlConnection(sameDatabase ? _fundConnection : _ledgerConnection);
        var fund = sameDatabase ? ledger : new NpgsqlConnection(_fundConnection);
        var sameAccountDatabase = _fundAccounts is null || string.Equals(ConnectionIdentity(_fundConnection),
            ConnectionIdentity(_fundAccounts.ConnectionString), StringComparison.Ordinal);
        var accounts = _fundAccounts is null ? null : sameAccountDatabase ? fund : new NpgsqlConnection(_fundAccounts.ConnectionString);
        NpgsqlTransaction? ledgerTransaction = null;
        NpgsqlTransaction? fundTransaction = null;
        NpgsqlTransaction? accountsTransaction = null;
        try
        {
            await ledger.OpenAsync(ct).ConfigureAwait(false);
            ledgerTransaction = await ledger.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            await ExecuteAsync(ledger, ledgerTransaction, $"SET LOCAL lock_timeout = '{_lockTimeoutSeconds}s'", ct).ConfigureAwait(false);
            await ExecuteAsync(ledger, ledgerTransaction,
                $"LOCK TABLE {Q(_ledgerSchema, "fund_profile_tenancy")}, {Q(_ledgerSchema, "ledger_event_audit_events")} IN SHARE MODE", ct).ConfigureAwait(false);
            await ExecuteAsync(ledger, ledgerTransaction,
                $"LOCK TABLE {Q(_ledgerSchema, "accounting_periods")}, {Q(_ledgerSchema, "ledger_books")}, {Q(_ledgerSchema, "operations_continuity_workflows")} IN SHARE ROW EXCLUSIVE MODE", ct).ConfigureAwait(false);

            if (sameDatabase)
                fundTransaction = ledgerTransaction;
            else
            {
                await fund.OpenAsync(ct).ConfigureAwait(false);
                fundTransaction = await fund.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            }
            await ExecuteAsync(fund, fundTransaction, $"SET LOCAL lock_timeout = '{_lockTimeoutSeconds}s'", ct).ConfigureAwait(false);
            var fundTables = Tables.Select(table => table.Name)
                .Concat(["fund_structure_tenant_quarantine", "fund_structure_tenant_backfill_receipt"])
                .Order(StringComparer.Ordinal).Select(table => Q(_fundSchema, table));
            await ExecuteAsync(fund, fundTransaction,
                $"LOCK TABLE {string.Join(", ", fundTables)} IN SHARE ROW EXCLUSIVE MODE", ct).ConfigureAwait(false);
            if (accounts is not null)
            {
                if (sameAccountDatabase)
                    accountsTransaction = fundTransaction;
                else
                {
                    await accounts.OpenAsync(ct).ConfigureAwait(false);
                    accountsTransaction = await accounts.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
                }
                await ExecuteAsync(accounts, accountsTransaction, $"SET LOCAL lock_timeout = '{_lockTimeoutSeconds}s'", ct).ConfigureAwait(false);
                await ExecuteAsync(accounts, accountsTransaction,
                    $"LOCK TABLE {Q(_fundAccounts!.Schema, "account_definition")} IN SHARE ROW EXCLUSIVE MODE", ct).ConfigureAwait(false);
            }

            var rows = new List<FundStructureTenantBackfillRow>();
            foreach (var table in Tables)
            {
                var retainedRows = await ReadJsonRowsAsync(fund, fundTransaction,
                    $"SELECT to_jsonb(r)::text FROM {Q(_fundSchema, table.Name)} r ORDER BY {table.IdColumn}", ct).ConfigureAwait(false);
                foreach (var row in retainedRows)
                {
                    rows.Add(new(table.Name, row.GetProperty(table.IdColumn).GetGuid(), table.Kind, table.IsNode,
                        ReadOptionalString(row, "tenant_id"), ReadReferences(row, table.Parents),
                        ReadReferences(row, table.Children), row));
                }
            }

            // Mirror the normal service's legacy snapshot: unknown link endpoints and assignment
            // targets are Accounts. Preview records them without writing; apply materializes only
            // reviewed attributable accounts in the same transaction as their stamps and receipt.
            var knownNodes = rows.Where(row => row.IsNode).Select(row => row.Id).ToHashSet();
            var inferredAccounts = rows.Where(row => row.Kind is "OwnershipLink" or "Assignment")
                .SelectMany(row => row.Parents.Concat(row.Children)).Where(id => !knownNodes.Contains(id))
                .Distinct().Order().ToArray();
            foreach (var accountId in inferredAccounts)
                rows.Add(new("fund_structure_linked_account", accountId, "Account", true, null, [], [],
                    JsonSerializer.SerializeToElement(new { account_id = accountId }), IsInferred: true));

            var evidence = new List<FundStructureTenantBackfillEvidence>();
            await using (var command = new NpgsqlCommand($"""
                SELECT to_jsonb(b)::text, to_jsonb(t)::text
                FROM {Q(_ledgerSchema, "ledger_books")} b
                LEFT JOIN {Q(_ledgerSchema, "fund_profile_tenancy")} t
                    ON lower(trim(b.fund_profile_id)) = lower(trim(t.fund_profile_id))
                ORDER BY b.ledger_book_id, t.fund_profile_id
                """, ledger, ledgerTransaction))
            await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var book = Parse(reader.GetString(0));
                    JsonElement? registry = reader.IsDBNull(1) ? null : Parse(reader.GetString(1));
                    evidence.Add(new(book.GetProperty("ledger_book_id").GetGuid(),
                        book.GetProperty("fund_profile_id").GetString()!, book.GetProperty("fund_structure_node_id").GetGuid(),
                        registry is { } tenantRow ? ReadOptionalString(tenantRow, "tenant_id") : null,
                        registry is { } companyRow ? ReadOptionalString(companyRow, "company_id") : null, book, registry));
                }
            }

            var books = evidence.GroupBy(item => item.BookId).ToDictionary(group => group.Key, group => group.First());
            foreach (var book in books.Values)
                rows.Add(new("ledger.ledger_books", book.BookId, "LedgerBook", false, ReadOptionalString(book.RetainedBook, "tenant_id"),
                    [book.NodeId], [], book.RetainedBook));
            var periods = await ReadJsonRowsAsync(ledger, ledgerTransaction, $"""
                SELECT (to_jsonb(p) || jsonb_build_object('tenant_backfill_audit_protected', EXISTS (
                    SELECT 1 FROM {Q(_ledgerSchema, "ledger_event_audit_events")} e
                    WHERE e.subject_kind = 'period' AND e.subject_id = p.period_id)))::text
                FROM {Q(_ledgerSchema, "accounting_periods")} p ORDER BY period_id
                """, ct).ConfigureAwait(false);
            foreach (var period in periods)
                rows.Add(new("ledger.accounting_periods", period.GetProperty("period_id").GetGuid(), "AccountingPeriod", false,
                    ReadOptionalString(period, "tenant_id"), BookOwner(period, "ledger_book_id", books), [], period));
            var workflows = await ReadJsonRowsAsync(ledger, ledgerTransaction,
                $"SELECT to_jsonb(w)::text FROM {Q(_ledgerSchema, "operations_continuity_workflows")} w ORDER BY workflow_id", ct).ConfigureAwait(false);
            foreach (var workflow in workflows)
                rows.Add(new("ledger.operations_continuity_workflows", workflow.GetProperty("workflow_id").GetGuid(), "OperationsWorkflow", false,
                    ReadOptionalString(workflow, "tenant_id"), BookOwner(workflow.GetProperty("workflow_json"), "ledgerBookId", books), [], workflow));
            if (accounts is not null)
            {
                var definitions = await ReadJsonRowsAsync(accounts, accountsTransaction!,
                    $"SELECT to_jsonb(a)::text FROM {Q(_fundAccounts!.Schema, "account_definition")} a ORDER BY account_id", ct).ConfigureAwait(false);
                foreach (var account in definitions)
                {
                    var id = account.GetProperty("account_id").GetGuid();
                    var parents = ReadReferences(account, ["fund_id", "sleeve_id", "vehicle_id", "entity_id"]).ToList();
                    if (knownNodes.Contains(id) || inferredAccounts.Contains(id))
                        parents.Add(id);
                    rows.Add(new("fund_accounts.account_definition", id, "AccountDefinition", false,
                        ReadOptionalString(account, "tenant_id"), parents.Distinct().Order().ToArray(), [], account));
                }
            }

            var quarantine = await ReadJsonRowsAsync(fund, fundTransaction,
                $"SELECT to_jsonb(q)::text FROM {Q(_fundSchema, "fund_structure_tenant_quarantine")} q ORDER BY node_id", ct).ConfigureAwait(false);
            var schema = await ReadSchemaAsync(fund, fundTransaction, _fundSchema, ct).ConfigureAwait(false);
            var ledgerSchema = await ReadSchemaAsync(ledger, ledgerTransaction, _ledgerSchema, ct).ConfigureAwait(false);
            var fundIdentity = await ReadDatabaseIdentityAsync(fund, fundTransaction, ct).ConfigureAwait(false);
            var ledgerIdentity = sameDatabase ? fundIdentity : await ReadDatabaseIdentityAsync(ledger, ledgerTransaction, ct).ConfigureAwait(false);
            var accountSchema = accounts is null ? "" : await ReadSchemaAsync(accounts, accountsTransaction!, _fundAccounts!.Schema, ct).ConfigureAwait(false);
            var accountIdentity = accounts is null ? "" : await ReadDatabaseIdentityAsync(accounts, accountsTransaction!, ct).ConfigureAwait(false);
            var identity = Hash($"{ConnectionIdentity(_ledgerConnection)}|{_ledgerSchema}|{ledgerIdentity}|{ConnectionIdentity(_fundConnection)}|{_fundSchema}|{fundIdentity}|{_fundAccounts?.Schema}|{accountIdentity}");
            var snapshot = new FundStructureTenantBackfillSnapshot(identity, Hash(schema + "\n" + ledgerSchema + "\n" + accountSchema), rows, evidence, quarantine, sameDatabase && sameAccountDatabase);
            return new Session(fund, fundTransaction, ledger, ledgerTransaction, accounts, accountsTransaction,
                _fundSchema, _ledgerSchema, _fundAccounts?.Schema, snapshot);
        }
        catch
        {
            try
            {
                await DisposeConnectionAsync(fund, fundTransaction ?? (sameDatabase ? ledgerTransaction : null)).ConfigureAwait(false);
            }
            finally
            {
                if (!sameDatabase)
                    await DisposeConnectionAsync(ledger, ledgerTransaction).ConfigureAwait(false);
                if (accounts is not null && !sameAccountDatabase)
                    await DisposeConnectionAsync(accounts, accountsTransaction).ConfigureAwait(false);
            }
            throw;
        }
    }

    private sealed class Session(
        NpgsqlConnection fund, NpgsqlTransaction fundTransaction,
        NpgsqlConnection ledger, NpgsqlTransaction ledgerTransaction,
        NpgsqlConnection? accounts, NpgsqlTransaction? accountsTransaction,
        string schema, string ledgerSchema, string? accountsSchema, FundStructureTenantBackfillSnapshot snapshot) : IFundStructureTenantBackfillSession
    {
        private bool _committed;
        public FundStructureTenantBackfillSnapshot Snapshot { get; } = snapshot;

        public Task<FundStructureTenantBackfillReceipt?> FindReceiptAsync(Guid runId, CancellationToken ct)
            => ReadReceiptAsync(fund, fundTransaction, schema, runId, ct);

        public async Task<FundStructureTenantBackfillReceipt> CommitAsync(
            Guid runId, string planHash, string operatorId, string reviewReference, JsonElement plan,
            IReadOnlyList<FundStructureTenantBackfillStamp> stamps,
            IReadOnlyList<FundStructureTenantBackfillException> exceptions, CancellationToken ct)
            => await CommitReviewedAsync(runId, planHash, operatorId, reviewReference, plan, stamps, exceptions, [], ct).ConfigureAwait(false);

        public async Task<FundStructureTenantBackfillReceipt> CommitReviewedAsync(
            Guid runId, string planHash, string operatorId, string reviewReference, JsonElement plan,
            IReadOnlyList<FundStructureTenantBackfillStamp> stamps,
            IReadOnlyList<FundStructureTenantBackfillException> exceptions,
            IReadOnlyList<FundStructureTenantQuarantineResolution> resolutions, CancellationToken ct)
        {
            if (_committed)
                throw new InvalidOperationException("Backfill session has already committed.");
            if (!Snapshot.SupportsAtomicApply)
                throw new InvalidOperationException("Separate databases support preview only; atomic apply requires co-located schemas.");
            foreach (var stamp in stamps)
            {
                var (table, targetSchema) = stamp.Table switch
                {
                    "ledger.ledger_books" => (new Table("ledger_books", "ledger_book_id", "LedgerBook", false, [], []), ledgerSchema),
                    "ledger.accounting_periods" => (new Table("accounting_periods", "period_id", "AccountingPeriod", false, [], []), ledgerSchema),
                    "ledger.operations_continuity_workflows" => (new Table("operations_continuity_workflows", "workflow_id", "OperationsWorkflow", false, [], []), ledgerSchema),
                    "fund_accounts.account_definition" when accountsSchema is not null => (new Table("account_definition", "account_id", "AccountDefinition", false, [], []), accountsSchema),
                    _ => (Tables.Single(table => table.Name == stamp.Table), schema)
                };
                ArgumentException.ThrowIfNullOrWhiteSpace(stamp.TenantId);
                var retainedRow = Snapshot.Rows.Single(row => row.Table == stamp.Table && row.Id == stamp.Id);
                if (retainedRow.IsInferred)
                {
                    if (table.Name != "fund_structure_linked_account" || retainedRow.Kind != "Account")
                        throw new InvalidOperationException("Only retained legacy Account references can be materialized.");
                    await using var insert = new NpgsqlCommand(
                        $"INSERT INTO {Q(schema, table.Name)} (account_id) VALUES (@id)", fund, fundTransaction);
                    insert.Parameters.AddWithValue("id", stamp.Id);
                    await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await using var command = new NpgsqlCommand($"""
                    UPDATE {Q(targetSchema, table.Name)} SET tenant_id = @tenant
                    WHERE {table.IdColumn} = @id AND (tenant_id IS NULL OR length(trim(tenant_id)) = 0)
                    """, fund, fundTransaction);
                command.Parameters.AddWithValue("id", stamp.Id);
                command.Parameters.AddWithValue("tenant", stamp.TenantId);
                if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("A backfill row no longer permits first attribution.");
            }

            foreach (var resolution in resolutions)
            {
                await using var command = new NpgsqlCommand($"""
                    UPDATE {Q(schema, "fund_structure_tenant_quarantine")}
                    SET resolved_at_utc = now(), resolved_tenant_id = @tenant, resolution_note = @note
                    WHERE node_id = @id AND resolved_at_utc IS NULL
                    """, fund, fundTransaction);
                command.Parameters.AddWithValue("id", resolution.NodeId);
                command.Parameters.AddWithValue("tenant", resolution.TenantId);
                command.Parameters.AddWithValue("note", $"Reviewed backfill {runId:D}; operator={operatorId}; review={reviewReference}; plan={planHash}");
                if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The reviewed quarantine release no longer matches retained state.");
            }

            foreach (var exception in exceptions)
            {
                await using var command = new NpgsqlCommand($"""
                    INSERT INTO {Q(schema, "fund_structure_tenant_quarantine")}
                        (node_id, node_kind, reason, candidate_tenant_ids)
                    VALUES (@id, @kind, @reason, @candidates::jsonb)
                    ON CONFLICT (node_id) DO UPDATE SET node_kind = excluded.node_kind,
                        reason = excluded.reason, candidate_tenant_ids = excluded.candidate_tenant_ids
                    WHERE fund_structure_tenant_quarantine.resolved_at_utc IS NULL
                    """, fund, fundTransaction);
                command.Parameters.AddWithValue("id", exception.NodeId);
                command.Parameters.AddWithValue("kind", exception.NodeKind);
                command.Parameters.AddWithValue("reason", exception.Reason);
                command.Parameters.AddWithValue("candidates", JsonSerializer.Serialize(exception.CandidateTenantIds));
                if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("A retained quarantine resolution requires a separate review.");
            }

            await using (var command = new NpgsqlCommand($"""
                INSERT INTO {Q(schema, "fund_structure_tenant_backfill_receipt")}
                    (run_id, plan_hash, operator_id, review_reference, stamped_rows, quarantined_rows, plan)
                VALUES (@id, @hash, @operator, @review, @stamps, @exceptions, @plan::jsonb)
                """, fund, fundTransaction))
            {
                command.Parameters.AddWithValue("id", runId);
                command.Parameters.AddWithValue("hash", planHash);
                command.Parameters.AddWithValue("operator", operatorId);
                command.Parameters.AddWithValue("review", reviewReference);
                command.Parameters.AddWithValue("stamps", stamps.Count);
                command.Parameters.AddWithValue("exceptions", exceptions.Count);
                command.Parameters.AddWithValue("plan", plan.GetRawText());
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var receipt = await FindReceiptAsync(runId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Backfill receipt was not retained.");
            await fundTransaction.CommitAsync(ct).ConfigureAwait(false);
            _committed = true;
            // For apply, ledger evidence and fund mutations share this exact transaction.
            return receipt;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DisposeConnectionAsync(fund, fundTransaction).ConfigureAwait(false);
            }
            finally
            {
                if (!ReferenceEquals(fund, ledger))
                {
                    await DisposeConnectionAsync(ledger, ledgerTransaction).ConfigureAwait(false);
                }
                if (accounts is not null && !ReferenceEquals(accounts, fund))
                    await DisposeConnectionAsync(accounts, accountsTransaction).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyList<Guid> BookOwner(JsonElement row, string property,
        IReadOnlyDictionary<Guid, FundStructureTenantBackfillEvidence> books)
        => row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
            Guid.TryParse(value.GetString(), out var id) && books.TryGetValue(id, out var book) ? [book.NodeId] : [];

    private static async Task<FundStructureTenantBackfillReceipt?> ReadReceiptAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string schema, Guid runId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT to_jsonb(r)::text FROM {Q(schema, "fund_structure_tenant_backfill_receipt")} r WHERE run_id = @id",
            connection, transaction);
        command.Parameters.AddWithValue("id", runId);
        var raw = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (raw is null)
            return null;
        var row = Parse(raw);
        return new(row.GetProperty("run_id").GetGuid(), row.GetProperty("plan_hash").GetString()!,
            row.GetProperty("operator_id").GetString()!, row.GetProperty("review_reference").GetString()!,
            row.GetProperty("applied_at_utc").GetDateTimeOffset(), row.GetProperty("stamped_rows").GetInt32(),
            row.GetProperty("quarantined_rows").GetInt32(), row.GetProperty("plan").Clone());
    }

    private static async ValueTask DisposeConnectionAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        try
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadJsonRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<JsonElement>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(Parse(reader.GetString(0)));
        return rows;
    }

    private static async Task<string> ReadSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
                'serverVersion', current_setting('server_version_num'),
                'columns', (SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY table_name, ordinal_position), '[]'::jsonb)
                    FROM information_schema.columns c WHERE table_schema = @schema),
                'constraints', (SELECT coalesce(jsonb_agg(jsonb_build_array(c.conrelid, c.conname, pg_get_constraintdef(c.oid))
                    ORDER BY c.conrelid, c.conname), '[]'::jsonb)
                    FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = @schema),
                'triggers', (SELECT coalesce(jsonb_agg(jsonb_build_array(t.tgrelid, t.tgname, pg_get_triggerdef(t.oid), pg_get_functiondef(t.tgfoid))
                    ORDER BY t.tgrelid, t.tgname), '[]'::jsonb)
                    FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = @schema AND NOT t.tgisinternal))::text
            """, connection, transaction);
        command.Parameters.AddWithValue("schema", schema);
        return (string)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static async Task<string> ReadDatabaseIdentityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_array(current_database(), (SELECT oid FROM pg_database WHERE datname = current_database()),
                inet_server_addr()::text, inet_server_port(), pg_postmaster_start_time(), current_user)::text
            """, connection, transaction);
        return (string)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static IReadOnlyList<Guid> ReadReferences(JsonElement row, IEnumerable<string> properties)
    {
        var references = new HashSet<Guid>();
        foreach (var property in properties)
        {
            if (!row.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in value.EnumerateArray())
                    references.Add(id.GetGuid());
            }
            else
                references.Add(value.GetGuid());
        }
        return references.Order().ToArray();
    }

    private static string? ReadOptionalString(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    private static string ValidateSchema(string schema)
    {
        // Match the migration runner's ASCII identifier contract, then the existing stores'
        // unquoted-name folding. Quoting the folded identifier keeps maintenance SQL safe.
        if (string.IsNullOrWhiteSpace(schema) || !(char.IsAsciiLetter(schema[0]) || schema[0] == '_') ||
            !schema.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            throw new ArgumentException("A schema must be a PostgreSQL identifier.", nameof(schema));
        return schema.ToLowerInvariant();
    }
    private static string Q(string schema, string table) => $"\"{schema}\".\"{table}\"";
    private static string Hash(string value) => Sha256Digest.ComputeUtf8(value);
    private static string ConnectionIdentity(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}/{builder.Database}";
    }
}
