using FluentAssertions;
using Meridian.Storage.Migrations;

namespace Meridian.Tests.Storage;

public sealed class PostgresMigrationRunnerValidationTests
{
    private static PostgresMigrationRunnerOptions Options(
        string schema = "meridian_test",
        string ledgerTable = "schema_migrations",
        string ledgerKeyColumn = "filename") => new()
        {
            ConnectionString = "Host=localhost;Database=meridian",
            Schema = schema,
            ScriptsSubdirectory = Path.Combine("DoesNotExist", "Migrations"),
            DisplayName = "Test",
            LockScopeName = "test",
            ConnectionStringSettingName = "TestOptions.ConnectionString",
            LedgerTableName = ledgerTable,
            LedgerKeyColumn = ledgerKeyColumn,
        };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad-schema")]
    [InlineData("schema;drop schema public")]
    [InlineData("1_starts_with_digit")]
    [InlineData("schéma")]
    public void Constructor_RejectsUnsupportedSchemaIdentifiers(string schema)
    {
        var act = () => new PostgresMigrationRunner(Options(schema: schema));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*PostgreSQL identifier*");
    }

    [Theory]
    [InlineData("bad table")]
    [InlineData("table;--")]
    public void Constructor_RejectsUnsupportedLedgerTableNames(string table)
    {
        var act = () => new PostgresMigrationRunner(Options(ledgerTable: table));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("bad column")]
    [InlineData("col;--")]
    public void Constructor_RejectsUnsupportedLedgerKeyColumns(string column)
    {
        var act = () => new PostgresMigrationRunner(Options(ledgerKeyColumn: column));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("meridian_banking")]
    [InlineData("_leading_underscore")]
    [InlineData("Schema2")]
    public void Constructor_AcceptsValidIdentifiers(string schema)
    {
        var act = () => new PostgresMigrationRunner(Options(schema: schema));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("same-checksum")]
    [InlineData("old-checksum")]
    public void ResolveAppliedMigrationAction_ReapplyExecutesExplicitlyRepeatableScriptsEveryStartup(string? appliedChecksum)
    {
        var action = PostgresMigrationRunner.ResolveAppliedMigrationAction(
            appliedChecksum,
            "same-checksum",
            MigrationDriftPolicy.Reapply,
            isRepeatableMigration: true);

        action.Should().Be(PostgresMigrationRunner.AppliedMigrationAction.ExecuteAndUpdateChecksum);
    }

    [Fact]
    public void ResolveAppliedMigrationAction_ReapplySkipsUnchangedNonRepeatableScripts()
    {
        var action = PostgresMigrationRunner.ResolveAppliedMigrationAction(
            "same-checksum",
            "same-checksum",
            MigrationDriftPolicy.Reapply);

        action.Should().Be(PostgresMigrationRunner.AppliedMigrationAction.Skip);
    }

    [Fact]
    public void ResolveAppliedMigrationAction_ReapplyExecutesChangedNonRepeatableScripts()
    {
        var action = PostgresMigrationRunner.ResolveAppliedMigrationAction(
            "old-checksum",
            "new-checksum",
            MigrationDriftPolicy.Reapply);

        action.Should().Be(PostgresMigrationRunner.AppliedMigrationAction.ExecuteAndUpdateChecksum);
    }

    [Fact]
    public void ResolveAppliedMigrationAction_ThrowPolicySkipsUnchangedScripts()
    {
        var action = PostgresMigrationRunner.ResolveAppliedMigrationAction(
            "same-checksum",
            "same-checksum",
            MigrationDriftPolicy.Throw);

        action.Should().Be(PostgresMigrationRunner.AppliedMigrationAction.Skip);
    }

    [Fact]
    public void ResolveAppliedMigrationAction_ThrowPolicyAdoptsMissingChecksumsWithoutExecuting()
    {
        var action = PostgresMigrationRunner.ResolveAppliedMigrationAction(
            null,
            "same-checksum",
            MigrationDriftPolicy.Throw);

        action.Should().Be(PostgresMigrationRunner.AppliedMigrationAction.UpdateChecksum);
    }

    [Fact]
    public void ResolveAppliedMigrationAction_ThrowPolicyFailsOnChecksumDrift()
    {
        var action = PostgresMigrationRunner.ResolveAppliedMigrationAction(
            "old-checksum",
            "new-checksum",
            MigrationDriftPolicy.Throw);

        action.Should().Be(PostgresMigrationRunner.AppliedMigrationAction.Throw);
    }

    [Fact]
    public void FindUnappliedScriptBelowHighestApplied_AllowsAFreshSchema()
    {
        var refusal = PostgresMigrationRunner.FindUnappliedScriptBelowHighestApplied(
            [("001_a.sql", 1), ("002_b.sql", 2)],
            new Dictionary<string, int>(StringComparer.Ordinal),
            "Test");

        refusal.Should().BeNull();
    }

    [Fact]
    public void FindUnappliedScriptBelowHighestApplied_AllowsAppendedScriptsAndHistoricalGaps()
    {
        // 061 and 063 were never shipped: the gap itself is not an error, only filling it in.
        var refusal = PostgresMigrationRunner.FindUnappliedScriptBelowHighestApplied(
            [("060_a.sql", 60), ("062_b.sql", 62), ("064_c.sql", 64), ("069_new.sql", 69)],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["060_a.sql"] = 60,
                ["062_b.sql"] = 62,
                ["064_c.sql"] = 64,
            },
            "Test");

        refusal.Should().BeNull();
    }

    [Fact]
    public void FindUnappliedScriptBelowHighestApplied_AllowsAppliedScriptsWhoseFilesWereRemoved()
    {
        var refusal = PostgresMigrationRunner.FindUnappliedScriptBelowHighestApplied(
            [("064_c.sql", 64)],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["060_a.sql"] = 60,
                ["064_c.sql"] = 64,
            },
            "Test");

        refusal.Should().BeNull();
    }

    [Fact]
    public void FindUnappliedScriptBelowHighestApplied_RefusesAReusedRetiredOrdinal()
    {
        var refusal = PostgresMigrationRunner.FindUnappliedScriptBelowHighestApplied(
            [("060_a.sql", 60), ("061_reused.sql", 61), ("062_b.sql", 62), ("063_reused.sql", 63), ("064_c.sql", 64)],
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["060_a.sql"] = 60,
                ["062_b.sql"] = 62,
                ["064_c.sql"] = 64,
            },
            "Security Master");

        refusal.Should().NotBeNull();
        refusal.Should().StartWith("Security Master migration ordinals are append-only");
        refusal.Should().Contain("'061_reused.sql', '063_reused.sql'");
        refusal.Should().Contain("'064_c.sql' (ordinal 064)");
        refusal.Should().Contain("database/migration-reservations.json");
    }

    [Fact]
    public void FindUnappliedScriptBelowHighestApplied_AllowsAnUnappliedScriptAboveTheHighestEvenWhenTheLedgerHasGaps()
    {
        var refusal = PostgresMigrationRunner.FindUnappliedScriptBelowHighestApplied(
            [("001_a.sql", 1), ("005_b.sql", 5)],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["001_a.sql"] = 1 },
            "Test");

        refusal.Should().BeNull();
    }

    [Fact]
    public async Task EnsureMigratedAsync_ThrowsWhenConnectionStringMissing()
    {
        var runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = "",
            Schema = "meridian_test",
            // The scripts directory must exist for the run to reach the connection check, so use
            // a directory that ships with the test base output.
            ScriptsSubdirectory = ".",
            DisplayName = "Test",
            LockScopeName = "test",
            ConnectionStringSettingName = "TestOptions.ConnectionString",
        });

        var act = () => runner.EnsureMigratedAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("TestOptions.ConnectionString is not configured.");
    }

    [Fact]
    public async Task EnsureMigratedAsync_ThrowsWhenScriptsDirectoryMissingAndConfiguredToThrow()
    {
        var runner = new PostgresMigrationRunner(Options());

        var act = () => runner.EnsureMigratedAsync();

        (await act.Should().ThrowAsync<DirectoryNotFoundException>())
            .WithMessage("Test migration directory was not found at*");
    }
}
