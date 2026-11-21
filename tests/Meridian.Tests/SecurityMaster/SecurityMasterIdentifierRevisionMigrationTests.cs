using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Migrations;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 062 applied to a populated pre-062 schema (migration 062 review, finding F5): the
/// Baseline backfill is the migration's own statement, run by the runner, not a hand-inserted row.
/// It must copy every identifier row once, leave <c>security_identifiers</c> untouched, roll back
/// whole on failure and be stable across a retry and a restart.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterIdentifierRevisionMigrationTests
{
    private const string RevisionMigration = "062_security_master_identifier_revisions.sql";

    [SecurityMasterDatabaseTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Upgrade_RefusesMissingDeploymentAttestation_WithoutPublishingMigration(bool writersStopped, bool legacyWritersRetired)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        var receipts = await fixture.ReceiptsAsync();

        var migrate = () => fixture.Runner(writersStopped, legacyWritersRetired).EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*migration 062 requires stopped writers*");
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
        (await fixture.ScalarAsync($"select to_regclass('{fixture.Schema}.security_identifier_revisions') is null")).Should().Be("True");
    }

    [SecurityMasterDatabaseTheory]
    [InlineData("securities")]
    [InlineData("security_identifiers")]
    public async Task Upgrade_RefusesConcurrentWriter_AndReleasesLocksForRetry(string table)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await using var writer = new NpgsqlConnection(fixture.ConnectionString);
        await writer.OpenAsync();
        await using var writerTransaction = await writer.BeginTransactionAsync();
        await using (var command = writer.CreateCommand())
        {
            command.Transaction = writerTransaction;
            command.CommandText = $"select * from {fixture.Schema}.{table} for update;";
            await command.ExecuteNonQueryAsync();
        }

        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Stop every Security Master writer*");
        await writerTransaction.RollbackAsync();

        // In the child-table case the runner acquired the parent lock before refusing. Its
        // failed transaction must release that lock, leaving neither a receipt nor a blocker.
        await using (var retryLockTransaction = await writer.BeginTransactionAsync())
        {
            await using var command = writer.CreateCommand();
            command.Transaction = retryLockTransaction;
            command.CommandText = $"lock table {fixture.Schema}.securities, {fixture.Schema}.security_identifiers in exclusive mode nowait;";
            await command.ExecuteNonQueryAsync();
            await retryLockTransaction.RollbackAsync();
        }
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_identifier_revisions")).Should().Be("6");
        // Apply conditions gate a pending upgrade, not every subsequent startup.
        await fixture.Runner(writersStopped: false, legacyWritersRetired: false).EnsureMigratedAsync();
    }

    [SecurityMasterDatabaseFact]
    public async Task FreshInstall_DefaultProductionRunner_DoesNotRequireUpgradeAttestation()
    {
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        var options = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("identifier_revision_fresh")
        };
        var runner = new SecurityMasterMigrationRunner(options);
        await runner.EnsureMigratedAsync();
        await runner.EnsureMigratedAsync();
    }

    [SecurityMasterDatabaseFact]
    public async Task Upgrade_LargeBaselineRefusesDefaultTimeout_BeforePublishingHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandTimeout = 120;
            seed.CommandText = $"""
                insert into {fixture.Schema}.security_identifiers (
                    security_id, identifier_kind, identifier_value, normalized_identifier_value,
                    is_primary, valid_from, source, manual_override)
                select '11111111-1111-1111-1111-111111111111', 'ProviderSymbol',
                       'TIMEOUT-' || n, 'TIMEOUT-' || n, false, '2026-01-01T00:00:00Z', 'Synthetic', false
                from generate_series(1, 1000001) n;
                """;
            await seed.ExecuteNonQueryAsync();
        }

        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Raise Command Timeout above 30 seconds*");
        (await fixture.ScalarAsync($"select to_regclass('{fixture.Schema}.security_identifier_revisions') is null")).Should().Be("True");
    }

    [SecurityMasterDatabaseFact]
    public async Task PopulatedUpgrade_BackfillsOneBaselinePerIdentifierRow_AcrossFailureRetryAndRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        var identifiers = await fixture.IdentifiersAsync();
        var receipts = await fixture.ReceiptsAsync();

        // Fail after the table, the backfill and the guards exist: the whole batch rolls back.
        await fixture.WriteMigrationAsync(fixture.MigrationSql + "\ndo $$ begin raise exception 'injected identifier revision migration failure'; end $$;\n");
        var migrate = () => fixture.Runner().EnsureMigratedAsync();
        await migrate.Should().ThrowAsync<PostgresException>().WithMessage("*injected identifier revision migration failure*");
        (await fixture.ScalarAsync($"select to_regclass('{fixture.Schema}.security_identifier_revisions') is null")).Should().Be("True");
        (await fixture.ReceiptsAsync()).Should().Be(receipts);
        (await fixture.IdentifiersAsync()).Should().Be(identifiers);

        await fixture.WriteMigrationAsync(fixture.MigrationSql);
        await fixture.Runner().EnsureMigratedAsync();

        (await fixture.IdentifiersAsync()).Should().Be(identifiers, "the migration changes nothing in security_identifiers");
        (await fixture.ScalarAsync($"select checksum from {fixture.Schema}.schema_migrations where filename = '{RevisionMigration}'"))
            .Should().Be(Sha256Digest.ComputeUtf8(fixture.MigrationSql));
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_identifiers")).Should().Be("6");
        (await fixture.ScalarAsync($"select count(*) from {fixture.Schema}.security_identifier_revisions")).Should().Be("6");
        // Row for row: every identifier row has exactly one Baseline revision repeating its values,
        // available from the migration and not before.
        (await fixture.ScalarAsync($"""
            select count(*) from {fixture.Schema}.security_identifiers c
            join {fixture.Schema}.security_identifier_revisions r
              on r.security_id = c.security_id and r.identifier_kind = c.identifier_kind
             and r.identifier_value = c.identifier_value and r.valid_from = c.valid_from
            where r.revision = 1 and r.change_kind = 'Baseline'
              and r.recorded_by = 'security-master.migration-062'
              and r.history_available_from is not null and r.history_available_from = r.recorded_at
              and (r.normalized_identifier_value, r.provider, r.normalized_provider, r.is_primary, r.valid_to,
                   r.source, r.confidence, r.manual_override)
                  is not distinct from
                  (c.normalized_identifier_value, c.provider, c.normalized_provider, c.is_primary, c.valid_to,
                   c.source, c.confidence, c.manual_override)
            """)).Should().Be("6");
        (await fixture.ScalarAsync($"select count(distinct recorded_at) from {fixture.Schema}.security_identifier_revisions")).Should().Be("1");

        var history = await fixture.ScalarAsync(
            $"select jsonb_agg(to_jsonb(r) order by to_jsonb(r)::text)::text from {fixture.Schema}.security_identifier_revisions r");
        foreach (var mutation in new[]
                 {
                     "update {0}.security_identifier_revisions set recorded_by = 'rewrite'",
                     "delete from {0}.security_identifier_revisions",
                     "truncate {0}.security_identifier_revisions"
                 })
        {
            var mutate = () => fixture.ScalarAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, mutation, fixture.Schema));
            await mutate.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");
        }

        // A restart finds nothing pending and changes nothing.
        var upgradedReceipts = await fixture.ReceiptsAsync();
        await fixture.Runner().EnsureMigratedAsync();
        (await fixture.ReceiptsAsync()).Should().Be(upgradedReceipts);
        (await fixture.ScalarAsync(
            $"select jsonb_agg(to_jsonb(r) order by to_jsonb(r)::text)::text from {fixture.Schema}.security_identifier_revisions r"))
            .Should().Be(history);
        (await fixture.IdentifiersAsync()).Should().Be(identifiers);
    }

    private sealed class Fixture(PostgresTestServer server, string scripts, string schema, string migrationSql) : IAsyncDisposable
    {
        public string Schema { get; } = schema;
        public string MigrationSql { get; } = migrationSql;
        public string ConnectionString => server.ConnectionString;

        /// <summary>A schema migrated through the last migration before 062.</summary>
        public static async Task<Fixture> CreateAsync()
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
            var scripts = Path.Combine(Path.GetTempPath(), $"meridian-identifier-revision-upgrade-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(scripts);
                var source = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
                File.Exists(Path.Combine(source, RevisionMigration)).Should().BeTrue();
                foreach (var file in Directory.GetFiles(source, "*.sql"))
                {
                    if (StringComparer.Ordinal.Compare(Path.GetFileName(file), RevisionMigration) < 0)
                        File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                }

                var fixture = new Fixture(server, scripts, server.CreateSchemaName("identifier_revision_upgrade"),
                    await File.ReadAllTextAsync(Path.Combine(source, RevisionMigration)));
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

        public PostgresMigrationRunner Runner(bool writersStopped = true, bool legacyWritersRetired = true) => new(new PostgresMigrationRunnerOptions
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { CommandTimeout = 30 }.ConnectionString,
            Schema = Schema,
            ScriptsSubdirectory = scripts,
            DisplayName = "Security Master",
            LockScopeName = "security_master",
            ConnectionStringSettingName = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
            TrackOrdinals = true,
            BeforeScriptAsync = (fileName, connection, transaction, isUpgrade, ct) =>
                SecurityMasterMigrationRunner.CheckIdentifierHistoryApplyConditionsAsync(new SecurityMasterOptions
                {
                    Schema = Schema,
                    IdentifierHistoryMigrationWritersStopped = writersStopped,
                    IdentifierHistoryMigrationLegacyWritersRetired = legacyWritersRetired
                }, fileName, connection, transaction, isUpgrade, ct)
        });

        public Task WriteMigrationAsync(string sql) => File.WriteAllTextAsync(Path.Combine(scripts, RevisionMigration), sql);

        public Task<string> ReceiptsAsync() => ScalarAsync(
            $"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text from {Schema}.schema_migrations r");

        public Task<string> IdentifiersAsync() => ScalarAsync(
            $"select jsonb_agg(to_jsonb(r) order by to_jsonb(r)::text)::text from {Schema}.security_identifiers r");

        public async Task<string> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        /// <summary>
        /// Two securities and six identifier rows covering the shapes the backfill must copy: open and
        /// expired rows, a provider-scoped row, one value on two securities, and non-default
        /// source, confidence and manual-override metadata.
        /// </summary>
        public Task SeedAsync() => ScalarAsync($$"""
            insert into {{Schema}}.securities (security_id, asset_class, status, display_name, currency,
                primary_identifier_kind, primary_identifier_value, normalized_primary_identifier_value, common_terms, asset_specific_terms, provenance, version, effective_from)
            values ('11111111-1111-1111-1111-111111111111', 'Equity', 'Active', 'Upgrade one', 'USD', 'Ticker', 'UPG1', 'UPG1', '{}', '{}', '{}', 1, '2026-01-01T00:00:00Z'),
                   ('22222222-2222-2222-2222-222222222222', 'Equity', 'Active', 'Upgrade two', 'USD', 'Ticker', 'UPG2', 'UPG2', '{}', '{}', '{}', 1, '2026-01-01T00:00:00Z');
            insert into {{Schema}}.security_identifiers (
                security_id, identifier_kind, identifier_value, normalized_identifier_value, provider, normalized_provider,
                is_primary, valid_from, valid_to, source, confidence, manual_override)
            values
                ('11111111-1111-1111-1111-111111111111', 'Ticker', 'UPG1', 'UPG1', null, null, true, '2026-01-01T00:00:00Z', null, 'SecurityMaster', null, false),
                ('11111111-1111-1111-1111-111111111111', 'Isin', 'US0000000001', 'US0000000001', null, null, false, '2026-01-01T00:00:00Z', null, 'OperatorDesk', 0.8750, true),
                ('11111111-1111-1111-1111-111111111111', 'Ticker', 'OLD1', 'OLD1', null, null, false, '2025-01-01T00:00:00Z', '2026-01-01T00:00:00Z', 'SecurityMaster', null, false),
                ('11111111-1111-1111-1111-111111111111', 'ProviderSymbol', 'UPG1.O', 'UPG1.O', 'Refinitiv', 'REFINITIV', false, '2026-01-01T00:00:00.123456Z', null, 'Refinitiv', 0.5000, false),
                ('22222222-2222-2222-2222-222222222222', 'Ticker', 'UPG2', 'UPG2', null, null, true, '2026-01-01T00:00:00Z', null, 'SecurityMaster', null, false),
                ('22222222-2222-2222-2222-222222222222', 'ProviderSymbol', 'UPG1.O', 'UPG1.O', 'Bloomberg', 'BLOOMBERG', false, '2026-01-01T00:00:00Z', null, 'Bloomberg', null, false);
            """);

        public async ValueTask DisposeAsync()
        {
            try
            { await server.DisposeAsync(); }
            finally
            {
                if (Directory.Exists(scripts))
                    Directory.Delete(scripts, recursive: true);
            }
        }
    }
}

// Keep the same environment and Docker availability policy as the surrounding database facts.
file sealed class SecurityMasterDatabaseTheoryAttribute : TheoryAttribute
{
    public SecurityMasterDatabaseTheoryAttribute() => Skip = new SecurityMasterDatabaseFactAttribute().Skip;
}
