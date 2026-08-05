using FluentAssertions;
using Meridian.Tests.Storage;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.TestSupport;

public sealed class PostgresTestSchemaTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task EnvironmentScope_ExternalDatabase_DropsOwnedSchemaAndRestoresFallbackConfiguration()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var connectionVariable = $"MERIDIAN_TEST_CONNECTION_{suffix}";
        var fallbackVariable = $"MERIDIAN_TEST_FALLBACK_{suffix}";
        var schemaVariable = $"MERIDIAN_TEST_SCHEMA_{suffix}";
        const string originalSchema = "caller_owned_schema";
        await using var database = await PostgresTestServer.CreateAsync(
            "MERIDIAN_LEDGER_CONNECTION_STRING");
        Environment.SetEnvironmentVariable(fallbackVariable, database.ConnectionString);
        Environment.SetEnvironmentVariable(schemaVariable, originalSchema);
        PostgresTestSchemaEnvironmentScope? scope = null;

        try
        {
            scope = await PostgresTestSchemaEnvironmentScope.CreateIfConfiguredAsync(
                connectionVariable,
                schemaVariable,
                "scope_contract",
                fallbackVariable);
            scope.Should().NotBeNull();
            var isolatedScope = scope!;
            Environment.GetEnvironmentVariable(connectionVariable)
                .Should().Be(database.ConnectionString);
            Environment.GetEnvironmentVariable(schemaVariable).Should().Be(isolatedScope.Schema);

            await using (var connection = new NpgsqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText =
                    $"CREATE SCHEMA \"{isolatedScope.Schema}\"; " +
                    $"CREATE TABLE \"{isolatedScope.Schema}\".owned_probe(id integer PRIMARY KEY);";
                await create.ExecuteNonQueryAsync();
            }

            await isolatedScope.DisposeAsync();

            Environment.GetEnvironmentVariable(connectionVariable).Should().BeNull();
            Environment.GetEnvironmentVariable(schemaVariable).Should().Be(originalSchema);
            (await SchemaExistsAsync(database.ConnectionString, isolatedScope.Schema))
                .Should().BeFalse();

            // Repeated fixture cleanup must remain safe after the owned schema was removed.
            await isolatedScope.DisposeAsync();
        }
        finally
        {
            if (scope is not null)
            {
                await scope.DisposeAsync();
            }
            Environment.SetEnvironmentVariable(connectionVariable, null);
            Environment.SetEnvironmentVariable(fallbackVariable, null);
            Environment.SetEnvironmentVariable(schemaVariable, null);
        }
    }

    [Fact]
    public async Task EnvironmentScope_MissingConnection_DoesNotPublishOrReplaceSchema()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var connectionVariable = $"MERIDIAN_TEST_CONNECTION_{suffix}";
        var schemaVariable = $"MERIDIAN_TEST_SCHEMA_{suffix}";
        const string originalSchema = "caller_owned_schema";
        Environment.SetEnvironmentVariable(schemaVariable, originalSchema);

        try
        {
            var scope = await PostgresTestSchemaEnvironmentScope.CreateIfConfiguredAsync(
                connectionVariable,
                schemaVariable,
                "missing_connection");

            scope.Should().BeNull();
            Environment.GetEnvironmentVariable(schemaVariable).Should().Be(originalSchema);
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionVariable, null);
            Environment.SetEnvironmentVariable(schemaVariable, null);
        }
    }

    [Fact]
    public void NewSchemaName_LongValidPrefix_StaysWithinPostgresIdentifierLimit()
    {
        var schema = PostgresTestSchema.NewSchemaName("reporting_source_structure");

        schema.Should().HaveLength(PostgresTestSchema.MaxIdentifierLength);
        schema.Should().MatchRegex("^[a-z0-9_]+$");
        schema.Should().StartWith("reporting_source_structur_test_");
    }

    [Fact]
    public void NewSchemaName_RepeatedCalls_RemainUniqueAfterPrefixTruncation()
    {
        var first = PostgresTestSchema.NewSchemaName("reporting_source_structure");
        var second = PostgresTestSchema.NewSchemaName("reporting_source_structure");

        second.Should().NotBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1ledger")]
    [InlineData("unsafe-prefix")]
    [InlineData("MixedCase")]
    public void NewSchemaName_UnsafePrefix_IsRejected(string prefix)
    {
        var act = () => PostgresTestSchema.NewSchemaName(prefix);

        act.Should().Throw<ArgumentException>();
    }

    private static async Task<bool> SchemaExistsAsync(
        string connectionString,
        string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = @schema);";
        command.Parameters.AddWithValue("schema", schema);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
