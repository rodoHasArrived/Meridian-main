using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Initializes a fixture-owned ephemeral Security Master schema and records this test process's
/// exact capability floor. This is test provisioning, never production deployment authority.
/// </summary>
internal static class SecurityMasterTestCapabilityFloor
{
    public static async Task InitializeAndActivateAsync(
        PostgresTestServer server,
        SecurityMasterOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(options);
        // Ownership comes from the actual server's allocation ledger, not a guessed prefix.
        // Even when tests use an externally supplied database, only their fresh schema may change.
        if (!server.OwnsSchema(options.Schema)
            || !string.Equals(server.ConnectionString, options.ConnectionString, StringComparison.Ordinal))
            throw new InvalidOperationException("Capability test provisioning requires this test server's owned schema and connection.");

        // This includes migration 057 before the activation statement can reach its empty table.
        await new SecurityMasterMigrationRunner(options).EnsureMigratedAsync(ct).ConfigureAwait(false);
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        using var identifierBuilder = new NpgsqlCommandBuilder();
        var schema = identifierBuilder.QuoteIdentifier(options.Schema);
        command.CommandText = $"""
            insert into {schema}.security_master_terms_capability_floor (
                singleton, minimum_declared_mapping_version, minimum_kernel_version, change_record, updated_by)
            values (true, @mapping, @kernel, 'ephemeral-integration-test-schema', 'fixture-provisioning')
            on conflict (singleton) do nothing;
            """;
        command.Parameters.AddWithValue("mapping", SecurityVersionedTerms.DeclaredScheduleMappingVersion);
        command.Parameters.AddWithValue("kernel", ScheduleGenerator.KernelVersion);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Reused fixture setup may observe an existing floor, but must never lower or overwrite it.
        var state = await new PostgresSecurityMasterTermsCapabilityStore(options).ReadAsync(ct).ConfigureAwait(false);
        if (!state.TableExists || state.Floor is not { } floor
            || !string.Equals(floor.MinimumDeclaredMappingVersion, SecurityVersionedTerms.DeclaredScheduleMappingVersion, StringComparison.Ordinal)
            || !string.Equals(floor.MinimumKernelVersion, ScheduleGenerator.KernelVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The fixture schema already has a different or invalid capability floor; it was not overwritten.");
    }
}
