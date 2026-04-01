using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Testcontainers.PostgreSql;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterDatabaseFixture : IAsyncLifetime
{
    private const string EnvVar = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING";

    // Non-null only when no external connection string is supplied.
    private readonly PostgreSqlContainer? _container;

    // Stable once InitializeAsync completes. Initialised with a placeholder when using a
    // container so that the property is never null at runtime (xUnit calls InitializeAsync
    // before any test method executes).
    public SecurityMasterOptions Options { get; private set; }

    public SecurityMasterDatabaseFixture()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable(EnvVar);

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            Options = new SecurityMasterOptions
            {
                ConnectionString = externalConnectionString,
                Schema = $"sm_test_{Guid.NewGuid():N}",
                PreloadProjectionCache = false
            };
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("meridian_test")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .Build();

            // Placeholder — overwritten in InitializeAsync once the container is running.
            Options = new SecurityMasterOptions { PreloadProjectionCache = false };
        }
    }

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync().ConfigureAwait(false);

            Options = new SecurityMasterOptions
            {
                ConnectionString = _container.GetConnectionString(),
                Schema = $"sm_test_{Guid.NewGuid():N}",
                PreloadProjectionCache = false
            };
        }

        var runner = new SecurityMasterMigrationRunner(Options);
        await runner.EnsureMigratedAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            var runner = new SecurityMasterMigrationRunner(Options);
            await runner.ResetSchemaAsync().ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(nameof(SecurityMasterDatabaseCollection), DisableParallelization = true)]
public sealed class SecurityMasterDatabaseCollection : ICollectionFixture<SecurityMasterDatabaseFixture>
{
}
