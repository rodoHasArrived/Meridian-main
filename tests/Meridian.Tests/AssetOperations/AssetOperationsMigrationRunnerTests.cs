using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.AssetOperations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Meridian.Tests.AssetOperations;

[Trait("Category", "Integration")]
public sealed class AssetOperationsMigrationRunnerTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING";
    private PostgreSqlContainer? _container;
    private AssetOperationsOptions _options = new();

    public async Task InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            _options = new AssetOperationsOptions
            {
                ConnectionString = externalConnectionString,
                Schema = $"asset_ops_test_{Guid.NewGuid():N}"
            };
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("meridian_test")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();
        await _container.StartAsync().ConfigureAwait(false);

        _options = new AssetOperationsOptions
        {
            ConnectionString = _container.GetConnectionString(),
            Schema = $"asset_ops_test_{Guid.NewGuid():N}"
        };
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    [AssetOperationsDatabaseFact]
    public async Task EnsureMigratedAsync_CreatesGenericSecurityIdLineageTablesIdempotently()
    {
        var runner = new AssetOperationsMigrationRunner(_options);

        await runner.EnsureMigratedAsync();
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
