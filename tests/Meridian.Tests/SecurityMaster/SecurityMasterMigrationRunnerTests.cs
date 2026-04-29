using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
[Collection(nameof(SecurityMasterDatabaseCollection))]
public sealed class SecurityMasterMigrationRunnerTests
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterMigrationRunnerTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task EnsureMigratedAsync_CreatesCoreTables()
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from information_schema.tables
            where table_schema = @schema
              and table_name in ('security_events', 'securities', 'security_identifiers', 'security_aliases', 'security_snapshots', 'projection_checkpoint');
            """;
        command.Parameters.AddWithValue("schema", _fixture.Options.Schema);

        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        count.Should().Be(6);
    }
}
