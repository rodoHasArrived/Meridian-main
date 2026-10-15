using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Migrations;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterAliasMigrationTests
{
    private const string AliasMigration = "038_security_master_alias_revisions.sql";
    private const string GenerationMigration = "036_security_master_workbench_generations.sql";
    private const string ClosePriceMigration = "037_security_master_close_price_snapshots.sql";

    [SecurityMasterDatabaseFact]
    public async Task FreshDatabase_AppliesDistinctGenerationClosePriceAndAliasMigrationsOnce()
    {
        await using var fixture = await Fixture.CreateAsync(includeAlias: true);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.schema_migrations where ordinal in (36, 37, 38)"))
            .Should().Be("3");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_workbench_generations")).Should().Be("0");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_close_price_snapshots")).Should().Be("0");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_alias_revisions")).Should().Be("0");
        var receipts = await fixture.ReceiptsAsync();
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
    }

    [SecurityMasterDatabaseFact]
    public Task Populated036Upgrade_PreservesGenerationAndAliasFactsAcrossFailureRetryAndRestart() =>
        AssertPopulatedUpgradeAsync(includeClosePrices: false);

    [SecurityMasterDatabaseFact]
    public Task Populated037Upgrade_PreservesClosePricesAndAliasFactsAcrossFailureRetryAndRestart() =>
        AssertPopulatedUpgradeAsync(includeClosePrices: true);

    private static async Task AssertPopulatedUpgradeAsync(bool includeClosePrices)
    {
        await using var fixture = await Fixture.CreateAsync(includeAlias: false, includeClosePrices: includeClosePrices);
        await fixture.SeedAsync();
        var baselineOrdinal = includeClosePrices ? 37 : 36;
        var receipts = await fixture.ReceiptsAsync();
        var history = await fixture.HistoryAsync();

        // Fail after the new table, backfill and guards exist. Transaction rollback must remove
        // the entire pending migration batch without changing baseline facts or receipts. An
        // upgrade from 036 rolls back both 037 and 038; an existing 037 remains unchanged.
        await fixture.WriteClosePricesAsync();
        await fixture.WriteAliasAsync(fixture.AliasSql + "\ndo $$ begin raise exception 'injected alias migration failure'; end $$;\n");
        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<PostgresException>().WithMessage("*injected alias migration failure*");
        (await fixture.ReceiptsAsync(throughOrdinal: baselineOrdinal)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.schema_migrations where ordinal = 37"))
            .Should().Be(includeClosePrices ? "1" : "0");
        (await fixture.HistoryAsync()).Should().Be(history);
        foreach (var table in new[] { "security_close_price_snapshots", "security_close_price_approvals" })
            (await fixture.ScalarAsync($"select to_regclass('{fixture.Schema}.{table}') is null"))
                .Should().Be(includeClosePrices ? "False" : "True");
        (await fixture.ScalarAsync($"select to_regclass('{fixture.Schema}.security_alias_revisions') is null")).Should().Be("True");

        await fixture.WriteAliasAsync(fixture.AliasSql);
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.HistoryAsync()).Should().Be(history);
        (await fixture.ReceiptsAsync(throughOrdinal: baselineOrdinal)).Should().Be(receipts);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.schema_migrations where ordinal in (37, 38)"))
            .Should().Be("2");
        (await fixture.ScalarAsync($"select checksum from {fixture.Schema}.schema_migrations where filename = '{ClosePriceMigration}'"))
            .Should().Be(fixture.ClosePriceChecksum);
        (await fixture.ScalarAsync($"select checksum from {fixture.Schema}.schema_migrations where filename = '{AliasMigration}'"))
            .Should().Be(Sha256Digest.ComputeUtf8(fixture.AliasSql));
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_alias_revisions r join {fixture.Schema}.security_aliases a using(alias_id) " +
            "where r.revision = 1 and r.alias_value = a.alias_value and r.recorded_at = a.created_at and r.recorded_by = a.created_by " +
            "and r.history_available_from is not null and r.history_available_from > r.recorded_at and not r.is_retired"))
            .Should().Be("1");
        var aliasHistory = await fixture.ScalarAsync($"select to_jsonb(r)::text from {fixture.Schema}.security_alias_revisions r");
        foreach (var mutation in new[] { "update {0}.security_alias_revisions set reason = 'rewrite'", "delete from {0}.security_alias_revisions", "truncate {0}.security_alias_revisions" })
        {
            var mutate = () => fixture.ScalarAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, mutation, fixture.Schema));
            await mutate.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");
        }
        var upgradedReceipts = await fixture.ReceiptsAsync();
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.ReceiptsAsync()).Should().Be(upgradedReceipts);
        (await fixture.HistoryAsync()).Should().Be(history);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_alias_revisions")).Should().Be("1");
        (await fixture.ScalarAsync($"select to_jsonb(r)::text from {fixture.Schema}.security_alias_revisions r")).Should().Be(aliasHistory);
    }

    private sealed class Fixture(PostgresTestServer server, string scripts, string schema, string aliasSql,
        string closePriceSql, bool includeClosePrices) : IAsyncDisposable
    {
        public string Schema { get; } = schema;
        public string AliasSql { get; } = aliasSql;
        public string ClosePriceChecksum => Sha256Digest.ComputeUtf8(closePriceSql);

        public static async Task<Fixture> CreateAsync(bool includeAlias, bool includeClosePrices = true)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
            var scripts = Path.Combine(Path.GetTempPath(), $"meridian-alias-upgrade-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(scripts);
                var source = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
                File.Exists(Path.Combine(source, GenerationMigration)).Should().BeTrue();
                File.Exists(Path.Combine(source, ClosePriceMigration)).Should().BeTrue();
                foreach (var file in Directory.GetFiles(source, "*.sql"))
                {
                    if ((!includeClosePrices && Path.GetFileName(file) == ClosePriceMigration) ||
                        (!includeAlias && Path.GetFileName(file) == AliasMigration))
                        continue;
                    if (StringComparer.Ordinal.Compare(Path.GetFileName(file), AliasMigration) < 0 || includeAlias)
                        File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                }
                var fixture = new Fixture(server, scripts, server.CreateSchemaName("alias_upgrade"),
                    await File.ReadAllTextAsync(Path.Combine(source, AliasMigration)),
                    await File.ReadAllTextAsync(Path.Combine(source, ClosePriceMigration)), includeClosePrices);
                await fixture.Runner().EnsureMigratedAsync();
                return fixture;
            }
            catch
            {
                await server.DisposeAsync();
                if (Directory.Exists(scripts))
                    Directory.Delete(scripts, recursive: true);
                throw;
            }
        }

        public PostgresMigrationRunner Runner() => new(new PostgresMigrationRunnerOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = Schema,
            ScriptsSubdirectory = scripts,
            DisplayName = "Security Master",
            LockScopeName = "security_master",
            ConnectionStringSettingName = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
            TrackOrdinals = true
        });

        public Task WriteAliasAsync(string sql) => File.WriteAllTextAsync(Path.Combine(scripts, AliasMigration), sql);
        public Task WriteClosePricesAsync() => File.WriteAllTextAsync(Path.Combine(scripts, ClosePriceMigration), closePriceSql);
        public Task<string> ReceiptsAsync(int? throughOrdinal = null) => ScalarAsync(
            $"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text from {Schema}.schema_migrations r" +
            (throughOrdinal is { } ordinal ? $" where ordinal <= {ordinal}" : ""));
        public async Task<string> HistoryAsync()
        {
            var rows = new List<string>();
            foreach (var table in new[] { "securities", "security_aliases", "security_workbench_generations" })
                rows.Add(await ScalarAsync($"select jsonb_agg(to_jsonb(r) order by to_jsonb(r)::text)::text from {Schema}.{table} r"));
            if (includeClosePrices)
                foreach (var table in new[] { "security_close_price_snapshots", "security_close_price_approvals" })
                    rows.Add(await ScalarAsync($"select jsonb_agg(to_jsonb(r) order by to_jsonb(r)::text)::text from {Schema}.{table} r"));
            return string.Join("\n", rows);
        }

        public async Task<string> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        public async Task SeedAsync()
        {
            await ScalarAsync($$"""
            insert into {{Schema}}.securities (security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value, common_terms, asset_specific_terms, provenance, version, effective_from)
            values ('11111111-1111-1111-1111-111111111111', 'Equity', 'Active', 'Upgrade', 'USD', 'Ticker', 'UPGRADE', 'UPGRADE',
                '{}', '{}', '{}', 1, '2026-01-01T00:00:00Z');
            insert into {{Schema}}.security_aliases (alias_id, security_id, alias_kind, alias_value, normalized_alias_value, scope, created_by, created_at, valid_from, is_enabled)
            values ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'Ticker', 'OLD.O', 'OLD.O',
                'Operations', 'original-actor', '2026-01-02T00:00:00Z', '2026-01-01T00:00:00Z', true);
            insert into {{Schema}}.security_workbench_generations values ('11111111-1111-1111-1111-111111111111', 7);
            """);
            if (includeClosePrices)
                await ScalarAsync($$"""
                    with snapshot as (
                        insert into {{Schema}}.security_close_price_snapshots
                            (snapshot_id, account_id, book_id, valuation_date, version, payload, payload_sha256, recorded_at)
                        values ('33333333-3333-3333-3333-333333333333', 'upgrade-account', 'upgrade-book', '2026-01-03', 1,
                            '{"Blockers":[],"CreatedBy":"maker"}',
                            encode(sha256(convert_to('{"Blockers":[],"CreatedBy":"maker"}', 'UTF8')), 'hex'), '2026-01-03T00:00:00Z')
                        returning snapshot_id, payload_sha256
                    ), approval as (
                        select snapshot_id, jsonb_build_object('SnapshotId', snapshot_id, 'SnapshotSha256', payload_sha256,
                            'Reviewer', 'reviewer', 'Rationale', 'retained independent review')::text as payload from snapshot
                    )
                    insert into {{Schema}}.security_close_price_approvals (snapshot_id, payload, payload_sha256, recorded_at)
                    select snapshot_id, payload, encode(sha256(convert_to(payload, 'UTF8')), 'hex'), '2026-01-04T00:00:00Z'
                    from approval;
                    """);
        }

        public async ValueTask DisposeAsync()
        {
            try
            { await server.DisposeAsync(); }
            finally { if (Directory.Exists(scripts)) Directory.Delete(scripts, recursive: true); }
        }
    }
}
