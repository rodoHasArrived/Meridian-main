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
