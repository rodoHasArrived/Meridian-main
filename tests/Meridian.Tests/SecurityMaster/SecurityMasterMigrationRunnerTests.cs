using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterMigrationRunnerTests : IClassFixture<SecurityMasterDatabaseFixture>
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

    [SecurityMasterDatabaseFact]
    public async Task EnsureMigratedAsync_EnforcesNormalizedPrimaryIdentifierUniqueness()
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var firstId = Guid.NewGuid();

        await using (var first = connection.CreateCommand())
        {
            first.Transaction = transaction;
            first.CommandText =
                $"""
                insert into {_fixture.Options.Schema}.securities (
                    security_id, asset_class, status, display_name, currency,
                    primary_identifier_kind, primary_identifier_value,
                    normalized_primary_identifier_value, common_terms, asset_specific_terms,
                    provenance, version, effective_from)
                values (
                    @security_id, 'Equity', 'Active', 'Normalized index test A', 'USD',
                    'Isin', 'US-0378331005', 'US0378331005', jsonb_build_object(), jsonb_build_object(),
                    jsonb_build_object(), 1, now());
                """;
            first.Parameters.AddWithValue("security_id", firstId);
            await first.ExecuteNonQueryAsync();
        }

        var act = async () =>
        {
            await using var duplicate = connection.CreateCommand();
            duplicate.Transaction = transaction;
            duplicate.CommandText =
                $"""
                insert into {_fixture.Options.Schema}.securities (
                    security_id, asset_class, status, display_name, currency,
                    primary_identifier_kind, primary_identifier_value,
                    normalized_primary_identifier_value, common_terms, asset_specific_terms,
                    provenance, version, effective_from)
                values (
                    @security_id, 'Equity', 'Active', 'Normalized index test B', 'USD',
                    'Isin', 'us 0378331005', 'US0378331005', jsonb_build_object(), jsonb_build_object(),
                    jsonb_build_object(), 1, now());
                """;
            duplicate.Parameters.AddWithValue("security_id", Guid.NewGuid());
            await duplicate.ExecuteNonQueryAsync();
        };

        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        exception.Which.ConstraintName.Should().Be("ux_securities_normalized_primary_identifier");
    }

    [SecurityMasterDatabaseFact]
    public async Task Migration030_ReportsEveryCollisionInDeterministicOrderBeforeChangingIndexes()
    {
        var schema = $"sm_collision_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        try
        {
            await using (var setup = connection.CreateCommand())
            {
                setup.CommandText =
                    $"""
                    create schema {schema};
                    create table {schema}.securities (
                        security_id uuid primary key,
                        primary_identifier_kind text not null,
                        primary_identifier_value text not null,
                        normalized_primary_identifier_value text not null);
                    create unique index ux_securities_primary_identifier
                        on {schema}.securities (primary_identifier_kind, primary_identifier_value);
                    create index ix_securities_normalized_primary_identifier
                        on {schema}.securities (primary_identifier_kind, normalized_primary_identifier_value);
                    insert into {schema}.securities values
                        ('00000000-0000-0000-0000-000000000004', 'Ticker', 'ABC ', 'ABC'),
                        ('00000000-0000-0000-0000-000000000003', 'Ticker', 'abc', 'ABC'),
                        ('00000000-0000-0000-0000-000000000002', 'Isin', 'US-0378331005', 'US0378331005'),
                        ('00000000-0000-0000-0000-000000000001', 'Isin', 'us 0378331005', 'US0378331005');
                    """;
                await setup.ExecuteNonQueryAsync();
            }

            var migrationPath = Path.Combine(
                AppContext.BaseDirectory,
                "SecurityMaster",
                "Migrations",
                "032_security_master_normalized_primary_identifier_uniqueness.sql");
            var migrationSql = (await File.ReadAllTextAsync(migrationPath))
                .Replace("__SCHEMA__", schema, StringComparison.Ordinal);
            var act = async () =>
            {
                await using var migrate = connection.CreateCommand();
                migrate.CommandText = migrationSql;
                await migrate.ExecuteNonQueryAsync();
            };

            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            exception.Which.MessageText.Should().Be("Normalized primary identifier collisions block migration 032.");
            exception.Which.Detail.Should().Be(
                "Isin|US0378331005|00000000-0000-0000-0000-000000000001,00000000-0000-0000-0000-000000000002\n" +
                "Ticker|ABC|00000000-0000-0000-0000-000000000003,00000000-0000-0000-0000-000000000004");

            await using var indexes = connection.CreateCommand();
            indexes.CommandText =
                """
                select indexname
                from pg_indexes
                where schemaname = @schema
                  and tablename = 'securities'
                order by indexname;
                """;
            indexes.Parameters.AddWithValue("schema", schema);
            var indexNames = new List<string>();
            await using (var reader = await indexes.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    indexNames.Add(reader.GetString(0));
                }
            }

            indexNames.Should().Contain("ux_securities_primary_identifier");
            indexNames.Should().Contain("ix_securities_normalized_primary_identifier");
            indexNames.Should().NotContain("ux_securities_normalized_primary_identifier");
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"drop schema if exists {schema} cascade;";
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
