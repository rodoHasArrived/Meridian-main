using System.Text.Json;
using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class SecurityRevisionWorkflowPostgresTests
{
    [LedgerDatabaseFact]
    public async Task MissingPurpose_RetainsCloseUniqueness_WhileDistinctRevisionSubjectsCoexist()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        var account = Guid.NewGuid();
        var period = Guid.NewGuid().ToString("D");
        await InsertAsync(connection, database.Options.SchemaName, account, period, "{}");
        (await FluentActions.Awaiting(() => InsertAsync(connection, database.Options.SchemaName, account, period, "{}"))
            .Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        var subject = Guid.NewGuid();
        await InsertAsync(connection, database.Options.SchemaName, account, period, Subject(subject));
        await InsertAsync(connection, database.Options.SchemaName, account, period, Subject(Guid.NewGuid()));
        (await FluentActions.Awaiting(() => InsertAsync(connection, database.Options.SchemaName, Guid.NewGuid(), period, Subject(subject)))
            .Should().ThrowAsync<PostgresException>("one revision cannot acquire a second approval workflow in another account"))
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        var uppercase = Subject(subject).Replace(subject.ToString("D"), subject.ToString("D").ToUpperInvariant(), StringComparison.Ordinal);
        (await FluentActions.Awaiting(() => InsertAsync(connection, database.Options.SchemaName, Guid.NewGuid(), period, uppercase))
            .Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [LedgerDatabaseFact]
    public async Task NullUnknownAndIncompletePurposes_RefuseAtTheDatabaseBoundary()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var invalid in new[]
        {
            """{"purpose":null}""", """{"purpose":"UnknownPurpose"}""",
            """{"purpose":"SecurityMasterRevision"}""",
            Subject(Guid.NewGuid(), "subjectMaker"), Subject(Guid.NewGuid(), "subjectEvidenceSha256"),
            Subject(Guid.NewGuid()).Replace("SecurityMasterRevision", "Close", StringComparison.Ordinal)
        })
        {
            (await FluentActions.Awaiting(() => InsertAsync(connection, database.Options.SchemaName,
                Guid.NewGuid(), Guid.NewGuid().ToString("D"), invalid)).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }

    [LedgerDatabaseFact]
    public async Task RetainedSubjectAndAccountingScope_CannotBeRewritten()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        var id = await InsertAsync(connection, database.Options.SchemaName, Guid.NewGuid(),
            Guid.NewGuid().ToString("D"), Subject(Guid.NewGuid()));
        foreach (var change in new[]
        {
            "workflow_json = jsonb_set(workflow_json, '{subjectMaker}', '\"forged\"'::jsonb)",
            "workflow_json = jsonb_set(workflow_json, '{purpose}', '\"Close\"'::jsonb)",
            "workflow_json = jsonb_set(workflow_json, '{ledgerBookId}', to_jsonb(gen_random_uuid()::text))",
            "fund_account_id = gen_random_uuid()",
            "period_id = 'fabricated-period'"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"update {database.Options.SchemaName}.operations_continuity_workflows set {change} where workflow_id = @id";
            command.Parameters.AddWithValue("id", id);
            (await command.Invoking(command => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }

    private static string Subject(Guid revision, string? missing = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["purpose"] = "SecurityMasterRevision",
            ["subjectRevisionId"] = revision,
            ["subjectMaker"] = "maker",
            ["subjectEvidenceId"] = "retained-document",
            ["subjectEvidenceSha256"] = new string('a', 64),
            ["ledgerBookId"] = Guid.NewGuid()
        };
        if (missing is not null)
            fields.Remove(missing);
        return JsonSerializer.Serialize(fields);
    }

    private static async Task<Guid> InsertAsync(NpgsqlConnection connection, string schema, Guid account, string period, string json)
    {
        var id = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {schema}.operations_continuity_workflows
              (workflow_id, fund_account_id, period_id, broker_source, derived_status, version, created_at_utc, updated_at_utc, workflow_json)
            values (@id, @account, @period, 'synthetic', 'InProgress', 1, now(), now(), @json::jsonb);
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("account", account);
        command.Parameters.AddWithValue("period", period);
        command.Parameters.AddWithValue("json", json);
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
