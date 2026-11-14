using FluentAssertions;
using Meridian.Storage.Migrations;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Ordinal-tracked migration sets are append-only: an unapplied script whose ordinal sorts below the
/// highest applied ordinal is refused before any script runs, so retired Security Master ordinals
/// 061 and 063 can never be filled in on a database that already moved past them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterMigrationOrdinalOrderTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterMigrationOrdinalOrderTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task ExistingMigratedDatabase_RerunsCleanly()
    {
        var before = await LedgerAsync(_fixture.Options.Schema);

        await new SecurityMasterMigrationRunner(_fixture.Options).EnsureMigratedAsync();

        (await LedgerAsync(_fixture.Options.Schema)).Should().Be(before);
    }

    [SecurityMasterDatabaseFact]
    public async Task ReusingRetiredOrdinal061_IsRefusedWithoutRunningAnyScript()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
        File.Exists(Path.Combine(shipped, "060_security_master_revision_field_base.sql")).Should().BeTrue();
        Directory.GetFiles(shipped, "061_*.sql").Should().BeEmpty("061 is retired and never shipped");
        Directory.GetFiles(shipped, "063_*.sql").Should().BeEmpty("063 is retired and never shipped");

        var relative = $"ordinal-order-fixture-{Guid.NewGuid():N}";
        var directory = Path.Combine(AppContext.BaseDirectory, relative);
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.GetFiles(shipped, "*.sql"))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            await File.WriteAllTextAsync(
                Path.Combine(directory, "061_reused_retired_ordinal.sql"),
                "create table __SCHEMA__.reused_retired_ordinal (id integer);\n");
            var before = await LedgerAsync(_fixture.Options.Schema);

            var act = () => Runner(relative, _fixture.Options.Schema).EnsureMigratedAsync();

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*append-only*'061_reused_retired_ordinal.sql'*sort below the highest applied migration*");
            (await LedgerAsync(_fixture.Options.Schema)).Should().Be(before);
            (await ScalarAsync(
                    $"select to_regclass('{_fixture.Options.Schema}.reused_retired_ordinal') is null"))
                .Should().Be(true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task BackfilledGap_IsRefusedButAppendedScriptApplies()
    {
        var schema = $"sm_ordinal_order_{Guid.NewGuid():N}";
        var relative = $"ordinal-order-synthetic-{Guid.NewGuid():N}";
        var directory = Path.Combine(AppContext.BaseDirectory, relative);
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "001_first.sql"), "create table __SCHEMA__.first (id integer);\n");
            await File.WriteAllTextAsync(Path.Combine(directory, "003_third.sql"), "create table __SCHEMA__.third (id integer);\n");
            await Runner(relative, schema).EnsureMigratedAsync();

            await File.WriteAllTextAsync(Path.Combine(directory, "002_backfilled.sql"), "create table __SCHEMA__.second (id integer);\n");
            await File.WriteAllTextAsync(Path.Combine(directory, "004_appended.sql"), "create table __SCHEMA__.fourth (id integer);\n");
            var refused = () => Runner(relative, schema).EnsureMigratedAsync();

            (await refused.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*'002_backfilled.sql'*'003_third.sql' (ordinal 003)*");
            (await ScalarAsync($"select count(*)::int from {schema}.schema_migrations")).Should().Be(2,
                "the refused run applies nothing, not even the appended script");

            File.Delete(Path.Combine(directory, "002_backfilled.sql"));
            await Runner(relative, schema).EnsureMigratedAsync();

            (await ScalarAsync($"select string_agg(ordinal::text, ',' order by ordinal) from {schema}.schema_migrations"))
                .Should().Be("1,3,4");
        }
        finally
        {
            await ScalarAsync($"drop schema if exists {schema} cascade; select 1");
            Directory.Delete(directory, recursive: true);
        }
    }

    private PostgresMigrationRunner Runner(string relativeDirectory, string schema) => new(new PostgresMigrationRunnerOptions
    {
        ConnectionString = _fixture.Options.ConnectionString,
        Schema = schema,
        ScriptsSubdirectory = relativeDirectory,
        DisplayName = "Security Master",
        LockScopeName = "security_master",
        ConnectionStringSettingName = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
        TrackOrdinals = true
    });

    private Task<object?> LedgerAsync(string schema) => ScalarAsync(
        $"select coalesce(jsonb_agg(to_jsonb(r) order by filename), '[]'::jsonb)::text from {schema}.schema_migrations r");

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
