using System.Globalization;
using FluentAssertions;
using Meridian.Storage.Migrations;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Migration 058 widens populated schedules transactionally without changing retained values.</summary>
[Trait("Category", "Integration")]
public sealed class SecurityEconomicSchedulePrecisionMigrationTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private const string MigrationName = "058_security_master_schedule_decimal_precision.sql";
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityEconomicSchedulePrecisionMigrationTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task Upgrade057To058_RollsBackFailureRetainsPopulatedRowsAndRerunsWithoutChangingValues()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var schema = $"sm_schedule_precision_{suffix}";
        var relativeDirectory = $"schedule-migration-fixture-{suffix}";
        var directory = Path.Combine(AppContext.BaseDirectory, relativeDirectory);
        var shippedDirectory = Path.Combine(AppContext.BaseDirectory, "SecurityMaster", "Migrations");
        Directory.CreateDirectory(directory);
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        try
        {
            // Use the real migration engine and unchanged shipped scripts to construct a populated
            // pre-058 database. The temporary directory only selects the historical endpoint.
            foreach (var path in Directory.GetFiles(shippedDirectory, "*.sql"))
            {
                var name = Path.GetFileName(path);
                if (int.Parse(name.Split('_')[0], CultureInfo.InvariantCulture) <= 57)
                    File.Copy(path, Path.Combine(directory, name));
            }
            var runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
            {
                ConnectionString = _fixture.Options.ConnectionString,
                Schema = schema,
                ScriptsSubdirectory = relativeDirectory,
                DisplayName = "Security Master schedule precision fixture",
                LockScopeName = "security_master",
                ConnectionStringSettingName = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
                TrackOrdinals = true
            });
            await runner.EnsureMigratedAsync();
            (await MigrationOrdinalAsync(connection, schema)).Should().Be(57);
            var loanId = Guid.NewGuid();
            var creditId = Guid.NewGuid();
            await using (var seed = connection.CreateCommand())
            {
                seed.CommandText = $"""
                    insert into {schema}.direct_loan_projection
                        (security_id, display_name, currency, borrower, primary_identifier_value, version)
                    values (@loan_id, 'Precision loan', 'USD', 'Borrower', 'PRECISION-LOAN', 1);
                    insert into {schema}.structured_credit_projection
                        (security_id, display_name, currency, tranche, collateral_type, original_face,
                         coupon_or_index, primary_identifier_value, version)
                    values (@credit_id, 'Precision tranche', 'USD', 'A', 'RMBS', 100, 'SOFR', 'PRECISION-CREDIT', 1);
                    insert into {schema}.direct_loan_principal_schedule_projection
                    values (@loan_id, 0, date '2027-01-01', 123456789.1234567890);
                    insert into {schema}.structured_credit_factor_schedule_projection
                    values (@credit_id, 0, date '2027-01-01', 0.1234567890);
                    """;
                seed.Parameters.AddWithValue("loan_id", loanId);
                seed.Parameters.AddWithValue("credit_id", creditId);
                await seed.ExecuteNonQueryAsync();
            }

            var beforeTypes = await ColumnTypesAsync(connection, schema);
            beforeTypes.Should().Equal("numeric(28,10)", "numeric(18,10)");
            var beforeConstraints = await ConstraintsAsync(connection, schema);
            beforeConstraints.Should().HaveCount(4, "both child tables retain their primary and foreign keys");
            File.Copy(Path.Combine(shippedDirectory, MigrationName), Path.Combine(directory, MigrationName));
            var injectedFailure = Path.Combine(directory, "059_injected_schedule_upgrade_failure.sql");
            await File.WriteAllTextAsync(injectedFailure, "select 1 / 0;\n");

            Func<Task> failedUpgrade = () => runner.EnsureMigratedAsync();
            (await failedUpgrade.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);

            (await ColumnTypesAsync(connection, schema)).Should().Equal(beforeTypes);
            (await ConstraintsAsync(connection, schema)).Should().Equal(beforeConstraints);
            (await AmountsAsync(connection, schema, principal: true)).Should().Equal(123456789.1234567890m);
            (await AmountsAsync(connection, schema, principal: false)).Should().Equal(0.1234567890m);
            (await MigrationOrdinalAsync(connection, schema)).Should().Be(57, "DDL and the migration ledger roll back together");

            File.Delete(injectedFailure);
            await runner.EnsureMigratedAsync();
            (await ColumnTypesAsync(connection, schema)).Should().Equal("numeric", "numeric");
            (await ConstraintsAsync(connection, schema)).Should().Equal(beforeConstraints);
            (await AmountsAsync(connection, schema, principal: true)).Should().Equal(123456789.1234567890m);
            (await AmountsAsync(connection, schema, principal: false)).Should().Equal(0.1234567890m);
            (await MigrationOrdinalAsync(connection, schema)).Should().Be(58);

            decimal[] principalValues = [0.00000000001m, 1_000_000_000_000_000_000m, decimal.MaxValue, 0.0000000000000000000000000001m];
            decimal[] factorValues = [0.12345678901m, 0.0000000000000000000000000001m];
            await InsertValuesAsync(connection, schema, loanId, principalValues, principal: true);
            await InsertValuesAsync(connection, schema, creditId, factorValues, principal: false);
            (await AmountsAsync(connection, schema, principal: true)).Should().Equal(new[] { 123456789.1234567890m }.Concat(principalValues));
            (await AmountsAsync(connection, schema, principal: false)).Should().Equal(new[] { 0.1234567890m }.Concat(factorValues));

            // A normal restart verifies and skips the recorded migration. Also exercise the SQL's
            // idempotence explicitly, without modifying a shipped file or its recorded checksum.
            await runner.EnsureMigratedAsync();
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await using var reapply = connection.CreateCommand();
                reapply.Transaction = transaction;
                reapply.CommandText = (await File.ReadAllTextAsync(Path.Combine(shippedDirectory, MigrationName)))
                    .Replace("__SCHEMA__", schema, StringComparison.Ordinal);
                await reapply.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            (await AmountsAsync(connection, schema, principal: true)).Should().Equal(new[] { 123456789.1234567890m }.Concat(principalValues));
            (await AmountsAsync(connection, schema, principal: false)).Should().Equal(new[] { 0.1234567890m }.Concat(factorValues));
            (await ConstraintsAsync(connection, schema)).Should().Equal(beforeConstraints);
            (await MigrationOrdinalAsync(connection, schema)).Should().Be(58);
        }
        finally
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.CommandText = $"drop schema if exists {schema} cascade;";
            await cleanup.ExecuteNonQueryAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> MigrationOrdinalAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"select max(ordinal) from {schema}.schema_migrations;";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<string>> ColumnTypesAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select format_type(attribute.atttypid, attribute.atttypmod)
            from pg_attribute attribute
            join pg_class relation on relation.oid = attribute.attrelid
            join pg_namespace namespace on namespace.oid = relation.relnamespace
            where namespace.nspname = @schema
              and ((relation.relname = 'direct_loan_principal_schedule_projection' and attribute.attname = 'amount')
                or (relation.relname = 'structured_credit_factor_schedule_projection' and attribute.attname = 'factor'))
            order by relation.relname;
            """;
        command.Parameters.AddWithValue("schema", schema);
        return await StringsAsync(command);
    }

    private static async Task<IReadOnlyList<string>> ConstraintsAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select constraint_row.conname || ':' || pg_get_constraintdef(constraint_row.oid)
            from pg_constraint constraint_row
            join pg_class relation on relation.oid = constraint_row.conrelid
            join pg_namespace namespace on namespace.oid = relation.relnamespace
            where namespace.nspname = @schema
              and relation.relname in ('direct_loan_principal_schedule_projection', 'structured_credit_factor_schedule_projection')
            order by constraint_row.conname;
            """;
        command.Parameters.AddWithValue("schema", schema);
        return await StringsAsync(command);
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(NpgsqlCommand command)
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    private static async Task<IReadOnlyList<decimal>> AmountsAsync(NpgsqlConnection connection, string schema, bool principal)
    {
        var table = principal ? "direct_loan_principal_schedule_projection" : "structured_credit_factor_schedule_projection";
        var column = principal ? "amount" : "factor";
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {column} from {schema}.{table} order by ordinal;";
        var values = new List<decimal>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetDecimal(0));
        return values;
    }

    private static async Task InsertValuesAsync(NpgsqlConnection connection, string schema, Guid securityId, decimal[] values, bool principal)
    {
        var table = principal ? "direct_loan_principal_schedule_projection" : "structured_credit_factor_schedule_projection";
        for (var index = 0; index < values.Length; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"insert into {schema}.{table} values (@security_id, @ordinal, date '2028-01-01', @value);";
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("ordinal", index + 1);
            command.Parameters.AddWithValue("value", values[index]);
            await command.ExecuteNonQueryAsync();
        }
    }
}
