using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Migrations;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterAliasMigrationTests
{
    private const string AliasMigration = "037_security_master_alias_revisions.sql";
    private const string GenerationMigration = "036_security_master_workbench_generations.sql";

    [SecurityMasterDatabaseFact]
    public async Task FreshDatabase_AppliesDistinctGenerationAndAliasMigrationsOnce()
    {
        await using var fixture = await Fixture.CreateAsync(includeAlias: true);
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.schema_migrations where ordinal in (36, 37)"))
            .Should().Be("2");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_workbench_generations")).Should().Be("0");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_alias_revisions")).Should().Be("0");
        var receipts = await fixture.ReceiptsAsync();
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
    }

    [SecurityMasterDatabaseFact]
    public async Task Populated036Upgrade_PreservesGenerationAndAliasFactsAcrossFailureRetryAndRestart()
    {
        await using var fixture = await Fixture.CreateAsync(includeAlias: false);
        await fixture.SeedAsync();
        var receipts = await fixture.ReceiptsAsync();
        var history = await fixture.HistoryAsync();

        // Fail after the new table, backfill and guards exist. Transaction rollback must remove
        // all 037 effects without changing the retained 036 fence or its migration receipt.
        await fixture.WriteAliasAsync(fixture.AliasSql + "\ndo $$ begin raise exception 'injected alias migration failure'; end $$;\n");
        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<PostgresException>().WithMessage("*injected alias migration failure*");
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
        (await fixture.HistoryAsync()).Should().Be(history);
        (await fixture.ScalarAsync($"select to_regclass('{fixture.Schema}.security_alias_revisions') is null")).Should().Be("True");

        await fixture.WriteAliasAsync(fixture.AliasSql);
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.HistoryAsync()).Should().Be(history);
        (await fixture.ReceiptsAsync(excludeAlias: true)).Should().Be(receipts);
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

    private sealed class Fixture(PostgresTestServer server, string scripts, string schema, string aliasSql) : IAsyncDisposable
    {
        public string Schema { get; } = schema;
        public string AliasSql { get; } = aliasSql;

        public static async Task<Fixture> CreateAsync(bool includeAlias)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
            var scripts = Path.Combine(Path.GetTempPath(), $"meridian-alias-upgrade-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(scripts);
                var source = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
                File.Exists(Path.Combine(source, GenerationMigration)).Should().BeTrue();
                foreach (var file in Directory.GetFiles(source, "*.sql"))
                {
                    if (StringComparer.Ordinal.Compare(Path.GetFileName(file), AliasMigration) < 0 || includeAlias)
                        File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                }
                var fixture = new Fixture(server, scripts, server.CreateSchemaName("alias_upgrade"),
                    await File.ReadAllTextAsync(Path.Combine(source, AliasMigration)));
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
        public Task<string> ReceiptsAsync(bool excludeAlias = false) => ScalarAsync(
            $"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text from {Schema}.schema_migrations r" +
            (excludeAlias ? $" where filename <> '{AliasMigration}'" : ""));
        public async Task<string> HistoryAsync()
        {
            var rows = new List<string>();
            foreach (var table in new[] { "securities", "security_aliases", "security_workbench_generations" })
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

        public async Task SeedAsync() => await ScalarAsync($$"""
            insert into {{Schema}}.securities (security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value, common_terms, asset_specific_terms, provenance, version, effective_from)
            values ('11111111-1111-1111-1111-111111111111', 'Equity', 'Active', 'Upgrade', 'USD', 'Ticker', 'UPGRADE', 'UPGRADE',
                '{}', '{}', '{}', 1, '2026-01-01T00:00:00Z');
            insert into {{Schema}}.security_aliases (alias_id, security_id, alias_kind, alias_value, normalized_alias_value, scope, created_by, created_at, valid_from, is_enabled)
            values ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'Ticker', 'OLD.O', 'OLD.O',
                'Operations', 'original-actor', '2026-01-02T00:00:00Z', '2026-01-01T00:00:00Z', true);
            insert into {{Schema}}.security_workbench_generations values ('11111111-1111-1111-1111-111111111111', 7);
            """);

        public async ValueTask DisposeAsync()
        {
            try
            { await server.DisposeAsync(); }
            finally { if (Directory.Exists(scripts)) Directory.Delete(scripts, recursive: true); }
        }
    }
}
