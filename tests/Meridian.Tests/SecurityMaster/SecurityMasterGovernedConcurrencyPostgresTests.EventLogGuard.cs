using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterGovernedConcurrencyPostgresTests
{
    [SecurityMasterDatabaseFact]
    public async Task SecurityEvents_RejectUpdateDeleteAndTruncate()
    {
        var id = await SeedAsync();
        var schema = fixture.Options.Schema;
        string[] statements =
        [
            $"update {schema}.security_events set actor = 'tampered' where security_id = @id",
            $"delete from {schema}.security_events where security_id = @id",
            $"truncate {schema}.security_events"
        ];

        foreach (var statement in statements)
        {
            await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(statement, connection);
            command.Parameters.AddWithValue("id", id);

            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

            failure.SqlState.Should().Be("23514", statement);
            failure.MessageText.Should().Contain("append-only", statement);
        }
    }
}
