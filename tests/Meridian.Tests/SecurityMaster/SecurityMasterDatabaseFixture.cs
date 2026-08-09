using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterDatabaseFixture : IAsyncLifetime
{
    private const string EnvVar = "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING";

    // Resolved in InitializeAsync; null only before that runs or if it failed to start.
    private PostgresTestServer? _server;

    // Stable once InitializeAsync completes. Initialised with a placeholder so the property
    // is never null at runtime (xUnit calls InitializeAsync before any test method executes).
    public SecurityMasterOptions Options { get; private set; } = new() { PreloadProjectionCache = false };

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(EnvVar).ConfigureAwait(false);
        try
        {
            Options = new SecurityMasterOptions
            {
                ConnectionString = _server.ConnectionString,
                Schema = _server.CreateSchemaName("sm"),
                PreloadProjectionCache = false
            };

            var runner = new SecurityMasterMigrationRunner(Options);
            await runner.EnsureMigratedAsync().ConfigureAwait(false);
        }
        catch
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        await _server.DisposeAsync().ConfigureAwait(false);
    }
}
