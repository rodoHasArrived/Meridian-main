using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterTermsCapabilityPostgresTests : IClassFixture<UnactivatedSecurityMasterDatabaseFixture>
{
    private readonly UnactivatedSecurityMasterDatabaseFixture _fixture;
    public SecurityMasterTermsCapabilityPostgresTests(UnactivatedSecurityMasterDatabaseFixture fixture) => _fixture = fixture;

    [SecurityMasterDatabaseFact]
    public async Task MigrationStartsUnactivated_ThenRoundTripsSingletonFloor_AndDistinguishesMissingFromCorruptTable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var store = new PostgresSecurityMasterTermsCapabilityStore(_fixture.Options);
        var initial = await store.ReadAsync(ct);
        initial.TableExists.Should().BeTrue();
        initial.Floor.Should().BeNull("migration 057 must not activate a deployment floor");

        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {_fixture.Options.Schema}.security_master_terms_capability_floor
                (singleton, minimum_declared_mapping_version, minimum_kernel_version, change_record, updated_by)
            values (true, @mapping, @kernel, 'ephemeral-integration-test', 'test-operator');
            """;
        command.Parameters.AddWithValue("mapping", SecurityVersionedTerms.DeclaredScheduleMappingVersion);
        command.Parameters.AddWithValue("kernel", ScheduleGenerator.KernelVersion);
        await command.ExecuteNonQueryAsync(ct);
        var active = await store.ReadAsync(ct);
        active.Floor!.MinimumDeclaredMappingVersion.Should().Be(SecurityVersionedTerms.DeclaredScheduleMappingVersion);
        active.Floor.MinimumKernelVersion.Should().Be(ScheduleGenerator.KernelVersion);
        active.Floor.ChangeRecord.Should().Be("ephemeral-integration-test");

        // The boolean primary key plus CHECK(singleton) enforce one row, not one row per value.
        var duplicate = () => command.ExecuteNonQueryAsync(ct);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.SqlState == PostgresErrorCodes.UniqueViolation);

        command.Parameters.Clear();
        command.CommandText = $"drop table {_fixture.Options.Schema}.security_master_terms_capability_floor;";
        await command.ExecuteNonQueryAsync(ct);
        var missing = await store.ReadAsync(ct);
        missing.TableExists.Should().BeFalse();
        missing.Floor.Should().BeNull();

        command.CommandText = $"create table {_fixture.Options.Schema}.security_master_terms_capability_floor (wrong_column integer);";
        await command.ExecuteNonQueryAsync(ct);
        var corrupt = () => store.ReadAsync(ct);
        await corrupt.Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.SqlState == PostgresErrorCodes.UndefinedColumn);
    }
}
