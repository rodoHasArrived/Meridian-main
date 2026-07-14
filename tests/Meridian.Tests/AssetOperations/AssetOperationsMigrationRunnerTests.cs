using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.AssetOperations;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.AssetOperations;

[Trait("Category", "Integration")]
public sealed class AssetOperationsMigrationRunnerTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING";
    private PostgresTestServer? _server;
    private AssetOperationsOptions _options = new();

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(ConnectionStringVariable).ConfigureAwait(false);
        _options = new AssetOperationsOptions
        {
            ConnectionString = _server.ConnectionString,
            Schema = PostgresTestSchema.NewSchemaName("asset_ops")
        };
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [AssetOperationsDatabaseFact]
    public async Task EnsureMigratedAsync_CreatesGenericSecurityIdLineageTablesIdempotently()
    {
        var runner = new AssetOperationsMigrationRunner(_options);

        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync()));
        await runner.EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from information_schema.columns
            where table_schema = @schema
              and table_name in (
                'asset_operation_subjects',
                'asset_terms_versions',
                'asset_lifecycle_events',
                'asset_cash_flow_projection_runs',
                'asset_projected_cash_flows',
                'asset_actual_activity',
                'asset_reconciliation_runs',
                'asset_reconciliation_results',
                'asset_ledger_projections',
                'asset_operations_readiness',
                'asset_workflow_audit')
              and column_name in ('security_id', 'source_domain', 'source_entity_id', 'payload');
            """;
        command.Parameters.AddWithValue("schema", _options.Schema);

        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        count.Should().Be(44);

        await using var typedCommand = connection.CreateCommand();
        typedCommand.CommandText =
            """
            select count(*)
            from information_schema.tables
            where table_schema = @schema
              and table_name in (
                'instrument_role_projections',
                'book_position_projections',
                'position_economic_state_projections');
            """;
        typedCommand.Parameters.AddWithValue("schema", _options.Schema);
        var typedTableCount = (long)(await typedCommand.ExecuteScalarAsync() ?? 0L);
        typedTableCount.Should().Be(3);

        await using var migrationLedgerCommand = connection.CreateCommand();
        migrationLedgerCommand.CommandText =
            $"select count(*) from \"{_options.Schema}\".asset_operations_schema_migrations;";
        ((long)(await migrationLedgerCommand.ExecuteScalarAsync() ?? 0L)).Should().Be(3);

        await using var guardColumnCommand = connection.CreateCommand();
        guardColumnCommand.CommandText =
            """
            select count(*)
            from information_schema.columns
            where table_schema = @schema
              and (
                (table_name = 'instrument_role_projections' and column_name in (
                    'approval_rationale', 'source_domain', 'source_entity_id', 'source_content_hash'))
                or
                (table_name = 'book_position_projections' and column_name in (
                    'position_side', 'position_status', 'approval_rationale', 'source_domain', 'source_entity_id',
                    'source_content_hash', 'projection_run_id', 'projection_event_id'))
                or
                (table_name = 'position_economic_state_projections' and column_name in (
                    'ledger_book_id', 'owner_scope_id', 'owner_scope_kind', 'approval_rationale',
                    'source_domain', 'source_entity_id', 'source_content_hash',
                    'projection_run_id', 'projection_event_id'))
              );
            """;
        guardColumnCommand.Parameters.AddWithValue("schema", _options.Schema);
        ((long)(await guardColumnCommand.ExecuteScalarAsync() ?? 0L)).Should().Be(21);

        await using var constraintCommand = connection.CreateCommand();
        constraintCommand.CommandText =
            """
            select count(*)
            from information_schema.table_constraints
            where constraint_schema = @schema
              and constraint_name in (
                'fk_book_position_projection_role_scope',
                'fk_position_economic_state_position_scope');
            """;
        constraintCommand.Parameters.AddWithValue("schema", _options.Schema);
        ((long)(await constraintCommand.ExecuteScalarAsync() ?? 0L)).Should().Be(2);

        await using var noBalanceCommand = connection.CreateCommand();
        noBalanceCommand.CommandText =
            """
            select count(*)
            from information_schema.columns
            where table_schema = @schema
              and table_name in (
                'instrument_role_projections',
                'book_position_projections',
                'position_economic_state_projections')
              and column_name like '%balance%';
            """;
        noBalanceCommand.Parameters.AddWithValue("schema", _options.Schema);
        ((long)(await noBalanceCommand.ExecuteScalarAsync() ?? 0L)).Should().Be(0);
    }

    [AssetOperationsDatabaseFact]
    public async Task EnsureMigratedAsync_BackfillsSlice2TypedRowsWithoutReplacingTheirPayloads()
    {
        var runner = new AssetOperationsMigrationRunner(_options);
        await runner.EnsureMigratedAsync();
        var projection = InstrumentPositionProjectionFixture.Create();
        var lineageOnlyState = projection.PositionEconomicStates.Single() with { SourceEvent = null };
        projection = projection with
        {
            BookPositions =
            [
                projection.BookPositions.Single() with
                {
                    CurrentEconomicState = lineageOnlyState,
                    ProjectionLineage = lineageOnlyState.ProjectionLineage
                }
            ],
            PositionEconomicStates = [lineageOnlyState]
        };
        var store = new PostgresAssetOperationsProjectionStore(_options);
        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);

        await using (var connection = new NpgsqlConnection(_options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var degrade = connection.CreateCommand();
            degrade.CommandText =
                $"""
                alter table "{_options.Schema}"."book_position_projections"
                    alter column position_side drop not null,
                    alter column position_status drop not null;
                alter table "{_options.Schema}"."position_economic_state_projections"
                    alter column ledger_book_id drop not null,
                    alter column owner_scope_id drop not null,
                    alter column owner_scope_kind drop not null;
                update "{_options.Schema}"."book_position_projections"
                set position_side = null,
                    position_status = null,
                    approval_rationale = '',
                    source_domain = null,
                    source_entity_id = null,
                    source_content_hash = null,
                    projection_run_id = null,
                    projection_event_id = null;
                update "{_options.Schema}"."position_economic_state_projections"
                set ledger_book_id = null,
                    owner_scope_id = null,
                    owner_scope_kind = null,
                    source_event_id = null,
                    approval_rationale = '',
                    source_domain = null,
                    source_entity_id = null,
                    source_content_hash = null,
                    projection_run_id = null,
                    projection_event_id = null;
                delete from "{_options.Schema}".asset_operations_schema_migrations
                where migration_id = '003_instrument_position_projection_guards.sql';
                """;
            await degrade.ExecuteNonQueryAsync();
        }

        await runner.EnsureMigratedAsync();

        await using var verifyConnection = new NpgsqlConnection(_options.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText =
            $"""
            select p.position_side,
                   p.position_status,
                   s.ledger_book_id,
                   s.owner_scope_id,
                   s.owner_scope_kind,
                   s.source_event_id,
                   p.payload::text,
                   p.approval_rationale,
                   s.approval_rationale
            from "{_options.Schema}"."book_position_projections" p
            join "{_options.Schema}"."position_economic_state_projections" s
              on s.position_id = p.position_id
            where p.position_id = @position_id;
            """;
        verify.Parameters.AddWithValue("position_id", InstrumentPositionProjectionFixture.PositionId);
        await using var reader = await verify.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be(BookPositionSides.Long);
        reader.GetString(1).Should().Be("Active");
        reader.GetGuid(2).Should().Be(InstrumentPositionProjectionFixture.LedgerBookId);
        reader.GetString(3).Should().Be("fund-alpha");
        reader.GetString(4).Should().Be(FundStructureNodeKindDto.Fund.ToString());
        reader.GetGuid(5).Should().Be(lineageOnlyState.ProjectionLineage!.TriggerEvent.EventId);
        reader.GetString(6).Should().Contain(InstrumentPositionProjectionFixture.PositionId.ToString("D"));
        reader.GetString(7).Should().Contain("rationale was not captured");
        reader.GetString(8).Should().Contain("rationale was not captured");
    }

    [AssetOperationsDatabaseFact]
    public async Task EnsureMigratedAsync_ShouldRejectConflictingLegacyProjectionRunLineage()
    {
        var runner = new AssetOperationsMigrationRunner(_options);
        await runner.EnsureMigratedAsync();
        var projection = InstrumentPositionProjectionFixture.Create();
        var store = new PostgresAssetOperationsProjectionStore(_options);
        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);

        await using (var connection = new NpgsqlConnection(_options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seedConflict = connection.CreateCommand();
            seedConflict.CommandText =
                $"""
                update "{_options.Schema}"."book_position_projections"
                set projection_run_id = null,
                    projection_event_id = null;
                update "{_options.Schema}"."position_economic_state_projections"
                set payload = jsonb_set(
                        payload,
                        array['projectionLineage', 'projectionEventId'],
                        to_jsonb(@conflicting_event_id::text),
                        false),
                    projection_run_id = null,
                    projection_event_id = null;
                delete from "{_options.Schema}".asset_operations_schema_migrations
                where migration_id = '003_instrument_position_projection_guards.sql';
                """;
            seedConflict.Parameters.AddWithValue("conflicting_event_id", Guid.NewGuid());
            await seedConflict.ExecuteNonQueryAsync();
        }

        var act = () => runner.EnsureMigratedAsync();

        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.Which.MessageText.Should().Contain("conflicting retained lineage");
    }

}

public sealed class AssetOperationsMigrationRunnerValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("asset-operations")]
    [InlineData("asset_operations;drop schema public")]
    [InlineData("1_asset_operations")]
    public void Constructor_ShouldRejectUnsupportedSchemaIdentifiers(string schema)
    {
        var act = () => new AssetOperationsMigrationRunner(new AssetOperationsOptions
        {
            ConnectionString = "Host=localhost;Database=meridian",
            Schema = schema
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*PostgreSQL identifier*");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AssetOperationsDatabaseFactAttribute : FactAttribute
{
    private const string DisableDockerVariable = "MERIDIAN_DISABLE_DOCKER_TESTS";
    private const string ConnectionStringVariable = "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING";

    public AssetOperationsDatabaseFactAttribute()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Asset Operations PostgreSQL tests are skipped because {DisableDockerVariable}=true.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            return;
        }

        if (!IsDockerAvailable())
        {
            Skip = "Asset Operations PostgreSQL tests are skipped because Docker is unavailable. " +
                   $"Start Docker or set {ConnectionStringVariable} to an external Postgres instance.";
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".",
                    "docker_engine",
                    System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                pipe.Connect(250);
                return pipe.IsConnected;
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch
        {
            return false;
        }
    }
}
