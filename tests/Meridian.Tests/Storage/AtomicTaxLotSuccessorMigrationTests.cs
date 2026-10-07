using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Migrations;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// Guards a populated ledger upgrade for approved exchange/refunding successors: historical
/// posting evidence survives widening, failed DDL restores the prior schema, and retry/restart
/// retain the migration receipts. Fixtures exercise database-valid history, not posting approval.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AtomicTaxLotSuccessorMigrationTests
{
    private const string SuccessorMigration = "V_ledger_042__atomic_lot_successors.sql";
    private const string MigrationLedger = "ledger_journal_schema_migrations";

    [LedgerDatabaseFact]
    public async Task SuccessorUpgrade_PopulatedLedger_PreservesHistoryAcrossUpgradeReapplyAndRestart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct);
        await fixture.SeedAsync(corporateAction: false, ct);
        var history = await fixture.HistoryAsync(ct);
        var receipts = await fixture.ReceiptsAsync(excludeSuccessor: true, ct);

        await fixture.WriteSuccessorAsync(fixture.SuccessorSql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);

        (await fixture.HistoryAsync(ct)).Should().Be(history,
            "042 must preserve every retained legacy column, including fingerprints and nullable metadata");
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.atomic_tax_lot_posting_batches " +
            "where corporate_action_instruction is not null", ct)).Should().Be("0");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.tax_lot_mutations", ct)).Should().Be("11");
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(fixture.SuccessorSql));

        await fixture.SeedAsync(corporateAction: true, ct);
        var withSuccessors = await fixture.HistoryAsync(ct);
        var constraints = await fixture.ConstraintsAsync(ct);
        var instructions = await fixture.ScalarAsync($"select jsonb_agg(corporate_action_instruction " +
            $"order by mutation_batch_id)::text from {fixture.Schema}.atomic_tax_lot_posting_batches " +
            "where mutation_kind = 'CorporateAction'", ct);

        // Reapply through the production drift policy, with only a test-local comment changed.
        // Receipt update follows successful DDL; a fresh runner then exercises unchanged startup.
        var reapplySql = fixture.SuccessorSql + "\n-- test-local checksum drift for explicit 042 reapply\n";
        await fixture.WriteSuccessorAsync(reapplySql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(reapplySql));
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.HistoryAsync(ct)).Should().Be(withSuccessors);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select jsonb_agg(corporate_action_instruction " +
            $"order by mutation_batch_id)::text from {fixture.Schema}.atomic_tax_lot_posting_batches " +
            "where mutation_kind = 'CorporateAction'", ct)).Should().Be(instructions);

        var allReceipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(withSuccessors);
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(allReceipts);
    }

    [LedgerDatabaseFact]
    public async Task SuccessorUpgrade_FailureAfterConstraintDrops_RestoresSchemaAndReceiptsThenRetries()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct);
        await fixture.SeedAsync(corporateAction: false, ct);
        var history = await fixture.HistoryAsync(ct);
        var constraints = await fixture.ConstraintsAsync(ct);
        var receipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        const string finalCheck = "alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_cost check (";
        var faultOffset = fixture.SuccessorSql.IndexOf(finalCheck, StringComparison.Ordinal);
        faultOffset.Should().BeGreaterThan(0);
        var failedSql = fixture.SuccessorSql.Insert(faultOffset,
            "do $$ begin raise exception 'injected successor migration DDL failure'; end $$;\n");
        await fixture.WriteSuccessorAsync(failedSql, ct);

        var upgrade = () => fixture.Runner().EnsureMigratedAsync(ct);
        await upgrade.Should().ThrowAsync<PostgresException>()
            .WithMessage("*injected successor migration DDL failure*");
        (await fixture.ConstraintsAsync(ct)).Should().Be(constraints,
            "the transaction must restore checks dropped earlier in the failed migration");
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(receipts,
            "a failed migration must neither append a receipt nor change an earlier checksum or timestamp");
        (await fixture.ScalarAsync("select count(*) from information_schema.columns " +
            $"where table_schema = '{fixture.Schema}' and table_name = 'atomic_tax_lot_posting_batches' " +
            "and column_name = 'corporate_action_instruction'", ct)).Should().Be("0");

        await fixture.WriteSuccessorAsync(fixture.SuccessorSql, ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: true, ct)).Should().Be(receipts);
        (await fixture.ChecksumAsync(ct)).Should().Be(Sha256Digest.ComputeUtf8(fixture.SuccessorSql));
        var upgradedReceipts = await fixture.ReceiptsAsync(excludeSuccessor: false, ct);
        var upgradedConstraints = await fixture.ConstraintsAsync(ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.HistoryAsync(ct)).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeSuccessor: false, ct)).Should().Be(upgradedReceipts);
        (await fixture.ConstraintsAsync(ct)).Should().Be(upgradedConstraints);
    }

    private sealed class MigrationFixture : IAsyncDisposable
    {
        private readonly PostgresTestServer _server;
        private readonly string _scripts;
        public string Schema { get; }
        public string SuccessorSql { get; }

        private MigrationFixture(PostgresTestServer server, string scripts, string schema, string successorSql)
            => (_server, _scripts, Schema, SuccessorSql) = (server, scripts, schema, successorSql);

        public static async Task<MigrationFixture> CreateAsync(CancellationToken ct)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
            var scripts = Path.Combine(Path.GetTempPath(), $"meridian-successor-migrations-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(scripts);
                var source = MigrationDirectory();
                foreach (var file in Directory.GetFiles(source, "*.sql"))
                {
                    // This is the actual predecessor schema; later migrations must not leak into setup.
                    if (StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(file), SuccessorMigration) < 0)
                        File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                }
                var sql = await File.ReadAllTextAsync(Path.Combine(source, SuccessorMigration), ct);
                var fixture = new MigrationFixture(server, scripts, server.CreateSchemaName("successor_upgrade"), sql);
                await fixture.Runner().EnsureMigratedAsync(ct);
                return fixture;
            }
            catch
            {
                try { await server.DisposeAsync(); }
                finally { if (Directory.Exists(scripts)) Directory.Delete(scripts, recursive: true); }
                throw;
            }
        }

        public PostgresMigrationRunner Runner() => new(new PostgresMigrationRunnerOptions
        {
            ConnectionString = _server.ConnectionString,
            Schema = Schema,
            ScriptsSubdirectory = _scripts,
            DisplayName = "Ledger",
            LockScopeName = "ledger",
            ConnectionStringSettingName = "MERIDIAN_LEDGER_CONNECTION_STRING",
            LedgerTableName = MigrationLedger,
            DriftPolicy = MigrationDriftPolicy.Reapply,
            RepeatableMigrationFileNames = new HashSet<string>(StringComparer.Ordinal)
            { "V_ledger_038__audit_safe_tenant_attribution.sql" }
        });

        public Task WriteSuccessorAsync(string sql, CancellationToken ct)
            => File.WriteAllTextAsync(Path.Combine(_scripts, SuccessorMigration), sql, ct);

        public Task<string> ChecksumAsync(CancellationToken ct)
            => ScalarAsync($"select checksum from {Schema}.{MigrationLedger} where filename = '{SuccessorMigration}'", ct);

        public Task<string> ReceiptsAsync(bool excludeSuccessor, CancellationToken ct)
            => ScalarAsync($"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text " +
                $"from {Schema}.{MigrationLedger} r" +
                (excludeSuccessor ? $" where filename <> '{SuccessorMigration}'" : ""), ct);

        public Task<string> ConstraintsAsync(CancellationToken ct)
            => ScalarAsync($"""
                select coalesce(jsonb_agg(jsonb_build_object('table', t.relname, 'name', c.conname,
                    'definition', pg_get_constraintdef(c.oid), 'validated', c.convalidated)
                    order by t.relname, c.conname), '[]'::jsonb)::text
                from pg_constraint c join pg_class t on t.oid = c.conrelid
                join pg_namespace n on n.oid = t.relnamespace
                where n.nspname = '{Schema}' and c.conname in (
                    'ck_atomic_tax_lot_batch_kind', 'ck_atomic_tax_lot_successor_instruction',
                    'ck_tax_lot_mutations_kind', 'ck_tax_lot_mutations_quantities', 'ck_tax_lot_mutations_cost')
                """, ct);

        public async Task<string> HistoryAsync(CancellationToken ct)
        {
            var rows = new List<string>();
            foreach (var table in new[] { "ledger_books", "accounting_periods", "journal_entries", "journal_legs",
                         "journal_entry_integrity_seals", "tax_lots", "atomic_tax_lot_posting_batches", "tax_lot_mutations" })
                rows.Add(await ScalarAsync($"select coalesce(jsonb_agg(to_jsonb(r) - 'corporate_action_instruction' " +
                    $"order by to_jsonb(r)::text), '[]'::jsonb)::text from {Schema}.{table} r", ct));
            return string.Join("\n", rows);
        }

        public async Task<string> ScalarAsync(string sql, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(_server.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToString(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        public async Task SeedAsync(bool corporateAction, CancellationToken ct)
        {
            // Values deliberately cover each pre-042 predicate branch, including legal NULL fields,
            // zero acquisition cost and each adjusted-current-basis discrete relief method.
            var cases = corporateAction
                ? """
                  ('close', 'CorporateAction', 'CorporateAction', 10, -10, 5, 55, null, null, '{}'::jsonb, null::integer, null::numeric),
                  ('open', 'CorporateAction', 'CorporateAction', 0, 10, 5, 55, null, null, null::jsonb, null::integer, null::numeric)
                  """
                : """
                  ('zero-acquisition', 'Acquisition', 'Acquisition', 0, 1, 0, 0, null, null, null::jsonb, null::integer, null::numeric),
                  ('acquisition', 'Acquisition', 'Acquisition', 0, 10, 5, 50, null, null, null::jsonb, null::integer, null::numeric),
                  ('disposal', 'Disposal', 'Disposal', 10, -2, 5, 10, null, null, null::jsonb, null::integer, null::numeric),
                  ('redistribution', 'Disposal', 'BasisRedistribution', 10, 0, 5, 55, 'AvErAgEcOsT', null, '{}'::jsonb, null::integer, null::numeric),
                  ('amortization', 'Amortization', 'Amortization', 10, 0, 5, 2, null, null, null::jsonb, null::integer, null::numeric),
                  ('average-disposal', 'Disposal', 'Disposal', 10, -2, 5, 9, 'AverageCost', 'policy-v1', '{}'::jsonb, 1, 100.125),
                  ('fifo-current', 'Disposal', 'Disposal', 10, -2, 5, 7, 'Fifo', 'policy-v1', '{"acquisition":{},"basisAdjustment":{}}'::jsonb, null::integer, null::numeric),
                  ('lifo-current', 'Disposal', 'Disposal', 10, -2, 5, 7, 'Lifo', 'policy-v1', '{"acquisition":{},"basisAdjustment":{}}'::jsonb, null::integer, null::numeric),
                  ('hifo-current', 'Disposal', 'Disposal', 10, -2, 5, 7, 'Hifo', 'policy-v1', '{"acquisition":{},"basisAdjustment":{}}'::jsonb, null::integer, null::numeric),
                  ('specific-current', 'Disposal', 'Disposal', 10, -2, 5, 7, 'SpecificId', 'policy-v1', '{"acquisition":{},"basisAdjustment":{}}'::jsonb, null::integer, null::numeric),
                  ('zero-current', 'Disposal', 'Disposal', 10, -2, 5, 0, 'fIfO', null, '{"acquisition":{},"basisAdjustment":{}}'::jsonb, null::integer, null::numeric)
                  """;
            var instructionColumn = corporateAction ? ", corporate_action_instruction" : "";
            var instructionValue = corporateAction ? ", '{\"fixture\":\"successor-reapply\"}'::jsonb" : "";
            var sql = $$"""
                do $seed$
                declare
                    b uuid := gen_random_uuid(); p uuid := gen_random_uuid(); s uuid := gen_random_uuid();
                    position_id uuid := gen_random_uuid(); j uuid; batch uuid; lot uuid; ev uuid; item record;
                begin
                    insert into {{Schema}}.ledger_books (ledger_book_id, fund_profile_id, fund_structure_node_id,
                        fund_structure_node_kind, display_name, base_currency, created_at, updated_at)
                    values (b, b::text, gen_random_uuid(), 'Fund', 'Migration history', 'USD', now(), now());
                    insert into {{Schema}}.accounting_periods (period_id, fiscal_year, period_no, label, start_date,
                        end_date, status, opened_at, ledger_book_id)
                    values (p, 2026, 1, '2026', '2026-01-01', '2026-12-31', 'Open', now(), b);
                    for item in select * from (values {{cases}}) as c(
                        label, batch_kind, kind, qty_before, delta, unit_cost, basis, relief, policy,
                        snapshot, allocation_version, sale_price)
                    loop
                        j := gen_random_uuid(); batch := gen_random_uuid(); lot := gen_random_uuid(); ev := gen_random_uuid();
                        insert into {{Schema}}.journal_entries (journal_entry_id, aggregate_id, period_id, occurred_at, description)
                        values (j, b, p, now(), item.label);
                        insert into {{Schema}}.journal_legs (entry_id, journal_entry_id, line_no, aggregate_id, period_id,
                            occurred_at, account_name, account_type, debit, credit, description)
                        values (gen_random_uuid(), j, 0, b, p, now(), 'Investment', 'Asset', 1, 0, item.label),
                            (gen_random_uuid(), j, 1, b, p, now(), 'Cash', 'Asset', 0, 1, item.label);
                        insert into {{Schema}}.tax_lots (tax_lot_record_id, ledger_book_id, account_name, account_type,
                            lot_id, acquired_date, original_quantity, open_quantity, unit_cost, currency, created_at, updated_at)
                        values (lot, b, 'Investment', 'Asset', item.label, '2026-01-01',
                            greatest(1, item.qty_before, item.qty_before + item.delta), item.qty_before + item.delta,
                            item.unit_cost, 'USD', now(), now());
                        insert into {{Schema}}.atomic_tax_lot_posting_batches (mutation_batch_id, ledger_book_id, period_id,
                            journal_entry_id, source_event_id, idempotency_key, canonical_fingerprint, expected_period_version,
                            mutation_kind, retained_evidence, relief_method, policy_revision, created_at, security_id,
                            book_position_id, proceeds_allocation_version, disposal_sale_price{{instructionColumn}})
                        values (batch, b, p, j, ev, item.label, 'sha256:' || repeat('a', 64), 0,
                            item.batch_kind, '[{"evidence":"retained-before-upgrade"}]'::jsonb, item.relief, item.policy,
                            now(), s, position_id, item.allocation_version, item.sale_price{{instructionValue}});
                        insert into {{Schema}}.tax_lot_mutations (mutation_record_id, mutation_batch_id, mutation_kind,
                            tax_lot_record_id, lot_id, selection_ordinal, quantity_before, quantity_delta, quantity_after,
                            unit_cost, cost_basis, expected_version, result_version, selection_evidence_id, retained_evidence,
                            journal_entry_id, source_event_id, relief_method, policy_revision, lot_snapshot_before,
                            lot_snapshot_after, recorded_at, security_id, book_position_id)
                        values (gen_random_uuid(), batch, item.kind, lot, item.label, 0, item.qty_before, item.delta,
                            item.qty_before + item.delta, item.unit_cost, item.basis, 0, 1, 'retained-evidence',
                            '[{"evidence":"retained-before-upgrade"}]'::jsonb, j, ev, item.relief, item.policy,
                            item.snapshot, '{}'::jsonb, now(), s, position_id);
                    end loop;
                end
                $seed$;
                """;
            await ScalarAsync(sql, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try { await _server.DisposeAsync(); }
            finally { Directory.Delete(_scripts, recursive: true); }
        }

        private static string MigrationDirectory()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "src", "Meridian.Storage", "Ledger", "Migrations");
                if (File.Exists(Path.Combine(path, SuccessorMigration))) return path;
            }
            throw new DirectoryNotFoundException("Unable to locate retained Ledger migrations through 042.");
        }
    }
}
