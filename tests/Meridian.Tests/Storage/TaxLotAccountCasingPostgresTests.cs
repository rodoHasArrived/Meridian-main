using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.Migrations;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// <see cref="LedgerAccount"/> compares <see cref="LedgerAccount.FinancialAccountId"/> with
/// <see cref="StringComparison.OrdinalIgnoreCase"/>. These tests prove the PostgreSQL ledger store
/// agrees: the lot-label uniqueness boundary, the open-lot and label lookups, and the
/// <c>V_ledger_051</c> upgrade pre-check all treat account ids that differ only by casing as one
/// logical account while the stored bytes keep their original casing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TaxLotAccountCasingPostgresTests
{
    private const string CasingMigration = "V_ledger_051__tax_lot_account_case_insensitive_keys.sql";
    private const string MigrationLedger = "ledger_journal_schema_migrations";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-12T14:00:00Z");

    [LedgerDatabaseFact]
    public async Task SaveTaxLotAsync_SameLotLabelUnderCaseVariantAccountId_IsRejectedAsDuplicate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var bookId = await SaveBookAsync(database, ct);

        var upper = BuildLot(bookId, new LedgerAccount("Investments", LedgerAccountType.Asset, "AAPL", "BROKER-A"), "lot-1");
        var lower = BuildLot(bookId, new LedgerAccount("Investments", LedgerAccountType.Asset, "AAPL", "broker-a"), "lot-1");
        upper.Account.Should().Be(lower.Account, "the domain already treats these as one account");

        var saved = await database.JournalStore.SaveTaxLotAsync(upper, ct);
        saved.Account.FinancialAccountId.Should().Be("BROKER-A", "stored casing is preserved");

        var duplicate = () => database.JournalStore.SaveTaxLotAsync(lower, ct);
        var failure = await duplicate.Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        failure.Which.ConstraintName.Should().Be("ux_tax_lots_book_account_lot");

        var retained = await database.JournalStore.GetTaxLotsByIdsAsync(bookId, [upper.TaxLotRecordId, lower.TaxLotRecordId], ct);
        retained.Should().ContainSingle().Which.Should().BeEquivalentTo(saved);

        // A genuinely different account id (not merely a case variant) still admits the same label.
        var other = BuildLot(bookId, new LedgerAccount("Investments", LedgerAccountType.Asset, "AAPL", "broker-b"), "lot-1");
        (await database.JournalStore.SaveTaxLotAsync(other, ct)).LotId.Should().Be("lot-1");
    }

    [LedgerDatabaseFact]
    public async Task OpenLotAndLabelLookups_FindLotsStoredUnderCaseVariantAccountId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var bookId = await SaveBookAsync(database, ct);
        var stored = new LedgerAccount("Investments", LedgerAccountType.Asset, "MSFT", "Broker-A");
        var queried = new LedgerAccount("Investments", LedgerAccountType.Asset, "MSFT", "BROKER-a");
        var unrelated = new LedgerAccount("Investments", LedgerAccountType.Asset, "MSFT", "broker-b");

        var first = await database.JournalStore.SaveTaxLotAsync(BuildLot(bookId, stored, "lot-1"), ct);
        var second = await database.JournalStore.SaveTaxLotAsync(
            BuildLot(bookId, stored, "lot-2") with { AcquiredDate = new DateOnly(2026, 2, 1) }, ct);
        var closed = await database.JournalStore.SaveTaxLotAsync(
            BuildLot(bookId, stored, "lot-closed") with { OpenQuantity = 0m }, ct);
        var elsewhere = await database.JournalStore.SaveTaxLotAsync(BuildLot(bookId, unrelated, "lot-1"), ct);

        var open = await database.JournalStore.ListOpenTaxLotsAsync(bookId, queried, ct);
        open.Should().BeEquivalentTo(new[] { first, second }, options => options.WithStrictOrdering(),
            "the open-lot lookup must match the logical account regardless of casing and keep acquisition order");
        open.Should().OnlyContain(lot => lot.Account.FinancialAccountId == "Broker-A", "lookups do not rewrite stored casing");

        var byLabel = await database.JournalStore.GetTaxLotsByLotIdsAsync(bookId, queried, ["lot-1", "lot-closed"], ct);
        byLabel.Should().BeEquivalentTo(new[] { first, closed }, "label lookups include closed lots and ignore account-id casing");
        byLabel.Should().NotContain(lot => lot.TaxLotRecordId == elsewhere.TaxLotRecordId);

        (await database.JournalStore.ListOpenTaxLotsAsync(bookId, unrelated, ct))
            .Should().ContainSingle().Which.TaxLotRecordId.Should().Be(elsewhere.TaxLotRecordId);
    }

    [LedgerDatabaseFact]
    public Task CasingMigration_RaisesWhenRetainedLotsCollideUnderCaseInsensitiveKey_ThenAppliesOnceResolved()
        => AssertCasingMigrationPreCheckAsync("tax_lots", "lot-label");

    [LedgerDatabaseFact]
    public Task CasingMigration_RaisesWhenRetainedPoliciesCollideUnderCaseInsensitiveKey_ThenAppliesOnceResolved()
        => AssertCasingMigrationPreCheckAsync("tax_lot_policies", "effective-date");

    private static async Task AssertCasingMigrationPreCheckAsync(string table, string groupLabel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        await using var fixture = await MigrationFixture.CreateAsync(ct);
        var conflicting = await fixture.SeedCaseVariantRowsAsync(table, ct);
        var receipts = await fixture.ReceiptsAsync(ct);
        var indexes = await fixture.IndexDefinitionsAsync(ct);
        indexes.Should().Contain("COALESCE(financial_account_id, ''::text)").And.NotContain("lower(COALESCE(financial_account_id");

        await fixture.WriteCasingMigrationAsync(ct);
        var upgrade = () => fixture.Runner().EnsureMigratedAsync(ct);
        var failure = await upgrade.Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        failure.Which.MessageText.Should().Contain($"1 {table} {groupLabel} group(s) spanning 2 row(s)")
            .And.Contain("lower(financial_account_id)");

        (await fixture.ReceiptsAsync(ct)).Should().Be(receipts, "a refused pre-check must leave no receipt");
        (await fixture.IndexDefinitionsAsync(ct)).Should().Be(indexes, "a refused pre-check must leave the V_ledger_009 indexes intact");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.{table}", ct)).Should().Be("2", "no data is rewritten");

        // The operator resolves the collision; the migration then converts the key without touching rows.
        await fixture.ScalarAsync($"delete from {fixture.Schema}.{table} where {IdColumn(table)} = '{conflicting}'", ct);
        await fixture.Runner().EnsureMigratedAsync(ct);
        (await fixture.IndexDefinitionsAsync(ct)).Should().Contain("lower(COALESCE(financial_account_id, ''::text))")
            .And.Contain("ux_tax_lots_book_account_lot").And.Contain("ix_tax_lots_book_account_open")
            .And.Contain("ux_tax_lot_policies_book_account_effective");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.{MigrationLedger} where filename = '{CasingMigration}'", ct))
            .Should().Be("1");
        (await fixture.ScalarAsync($"select financial_account_id from {fixture.Schema}.{table}", ct))
            .Should().Be("BROKER-A", "the surviving row keeps its original casing");

        // Reinserting the case variant is now refused by the rebuilt unique index.
        var reinsert = () => fixture.SeedCaseVariantRowsAsync(table, ct, onlyVariant: true);
        var violation = await reinsert.Should().ThrowAsync<PostgresException>();
        violation.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public void CasingMigration_PreChecksBeforeRebuildingEveryFinancialAccountKey()
    {
        var sql = File.ReadAllText(Path.Combine(MigrationFixture.MigrationDirectory(), CasingMigration));

        sql.Should().NotContainEquivalentOf("truncate").And.NotContainEquivalentOf("delete from").And.NotContainEquivalentOf("update ");
        var preCheck = sql.IndexOf("raise exception", StringComparison.OrdinalIgnoreCase);
        var firstDrop = sql.IndexOf("drop index if exists", StringComparison.OrdinalIgnoreCase);
        preCheck.Should().BePositive().And.BeLessThan(firstDrop, "operators must be told about collisions before any DDL runs");
        foreach (var index in new[] { "ux_tax_lots_book_account_lot", "ix_tax_lots_book_account_open", "ux_tax_lot_policies_book_account_effective" })
        {
            sql.Should().Contain($"drop index if exists __SCHEMA__.{index};");
            sql.Should().Contain($"index {index}");
        }

        sql.Split("create ").Skip(1).Should().HaveCount(3)
            .And.OnlyContain(definition => definition.Contains("lower(coalesce(financial_account_id, ''))", StringComparison.Ordinal));
        sql.Should().NotContain("if not exists", "drop-then-create is the reapply idiom; a stale index must never survive");
    }

    private static string IdColumn(string table) => table == "tax_lots" ? "tax_lot_record_id" : "policy_record_id";

    private static async Task<Guid> SaveBookAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        var bookId = Guid.NewGuid();
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            bookId, $"fund-{bookId:N}", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Account casing", "USD", Now, Now), ct);
        return bookId;
    }

    private static LedgerTaxLotRecord BuildLot(Guid bookId, LedgerAccount account, string lotId)
        => new(
            Guid.NewGuid(),
            bookId,
            account,
            lotId,
            new DateOnly(2026, 1, 15),
            OriginalQuantity: 100m,
            OpenQuantity: 100m,
            UnitCost: 12.5m,
            "USD",
            Now,
            Now);

    private sealed class MigrationFixture : IAsyncDisposable
    {
        private readonly PostgresTestServer _server;
        private readonly string _scripts;
        private readonly string _casingSql;

        public string Schema { get; }

        private MigrationFixture(PostgresTestServer server, string scripts, string schema, string casingSql)
            => (_server, _scripts, Schema, _casingSql) = (server, scripts, schema, casingSql);

        public static async Task<MigrationFixture> CreateAsync(CancellationToken ct)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
            var scripts = Path.Combine(Path.GetTempPath(), $"meridian-lot-casing-migrations-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(scripts);
                var source = MigrationDirectory();
                foreach (var file in Directory.GetFiles(source, "*.sql"))
                {
                    // Only the predecessor schema: 051 is written later, after conflicting rows exist.
                    if (StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(file), CasingMigration) < 0)
                        File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                }
                var sql = await File.ReadAllTextAsync(Path.Combine(source, CasingMigration), ct);
                var fixture = new MigrationFixture(server, scripts, server.CreateSchemaName("lot_casing"), sql);
                await fixture.Runner().EnsureMigratedAsync(ct);
                return fixture;
            }
            catch
            {
                try
                { await server.DisposeAsync(); }
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

        public Task WriteCasingMigrationAsync(CancellationToken ct)
            => File.WriteAllTextAsync(Path.Combine(_scripts, CasingMigration), _casingSql, ct);

        public Task<string> ReceiptsAsync(CancellationToken ct)
            => ScalarAsync($"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text from {Schema}.{MigrationLedger} r", ct);

        public Task<string> IndexDefinitionsAsync(CancellationToken ct)
            => ScalarAsync($"""
                select coalesce(string_agg(indexdef, E'\n' order by indexname), '')
                from pg_indexes
                where schemaname = '{Schema}' and indexname in (
                    'ux_tax_lots_book_account_lot', 'ix_tax_lots_book_account_open',
                    'ux_tax_lot_policies_book_account_effective')
                """, ct);

        /// <summary>
        /// Seeds two rows that are distinct under V_ledger_009's byte-wise key and identical under the
        /// case-insensitive key, returning the id of the case-variant row an operator would remove.
        /// </summary>
        public async Task<Guid> SeedCaseVariantRowsAsync(string table, CancellationToken ct, bool onlyVariant = false)
        {
            var variant = Guid.NewGuid();
            // The VALUES literals must be typed: an untyped text literal cannot be inserted into the uuid key.
            var rows = onlyVariant
                ? $"('{variant}'::uuid, 'broker-a')"
                : $"('{Guid.NewGuid()}'::uuid, 'BROKER-A'), ('{variant}'::uuid, 'broker-a')";
            var bookSetup = onlyVariant
                ? $"select ledger_book_id into b from {Schema}.ledger_books limit 1;"
                : $"""
                   b := gen_random_uuid();
                   insert into {Schema}.ledger_books (ledger_book_id, fund_profile_id, fund_structure_node_id,
                       fund_structure_node_kind, display_name, base_currency, created_at, updated_at)
                   values (b, b::text, gen_random_uuid(), 'Fund', 'Casing pre-check', 'USD', now(), now());
                   """;
            var insert = table == "tax_lots"
                ? $"""
                   insert into {Schema}.tax_lots (tax_lot_record_id, ledger_book_id, account_name, account_type, symbol,
                       financial_account_id, lot_id, acquired_date, original_quantity, open_quantity, unit_cost, currency,
                       created_at, updated_at)
                   select id, b, 'Investments', 'Asset', 'AAPL', account_id, 'lot-1', '2026-01-15', 100, 100, 12.5, 'USD',
                       now(), now()
                   from (values {rows}) as seed(id, account_id);
                   """
                : $"""
                   insert into {Schema}.tax_lot_policies (policy_record_id, ledger_book_id, account_name, account_type, symbol,
                       financial_account_id, relief_method, policy_id, effective_date, created_at, updated_at)
                   select id, b, 'Investments', 'Asset', 'AAPL', account_id, 'Fifo', 'policy-v1', '2026-01-01', now(), now()
                   from (values {rows}) as seed(id, account_id);
                   """;
            await ScalarAsync($$"""
                do $seed$
                declare
                    b uuid;
                begin
                    {{bookSetup}}
                    {{insert}}
                end
                $seed$;
                """, ct);
            return variant;
        }

        public async Task<string> ScalarAsync(string sql, CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(_server.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToString(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        public async ValueTask DisposeAsync()
        {
            try
            { await _server.DisposeAsync(); }
            finally { Directory.Delete(_scripts, recursive: true); }
        }

        public static string MigrationDirectory()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "src", "Meridian.Storage", "Ledger", "Migrations");
                if (File.Exists(Path.Combine(path, CasingMigration)))
                    return path;
            }
            throw new DirectoryNotFoundException("Unable to locate retained Ledger migrations through 051.");
        }
    }
}
