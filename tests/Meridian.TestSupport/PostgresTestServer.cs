using Testcontainers.PostgreSql;

namespace Meridian.TestSupport;

/// <summary>
/// Resolves a PostgreSQL endpoint for integration tests, centralising the single
/// container-or-external-connection lifecycle that each Meridian test module previously
/// hand-rolled. When the configured environment variable supplies a connection string the
/// external database is used and container startup is short-circuited; otherwise a
/// disposable <c>postgres</c> Testcontainers container is started.
/// </summary>
/// <remarks>
/// Per-run schema isolation, migration setup, and schema teardown remain the responsibility
/// of the owning module. Callers inspect <see cref="UsesExternalConnection"/> to decide
/// whether a run-scoped schema must be dropped on teardown.
/// </remarks>
public sealed class PostgresTestServer : IAsyncDisposable
{
    private readonly PostgreSqlContainer? _container;

    private PostgresTestServer(string connectionString, PostgreSqlContainer? container)
    {
        ConnectionString = connectionString;
        _container = container;
    }

    /// <summary>The resolved connection string (external database or container).</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// <see langword="true"/> when an external connection string was supplied through the
    /// environment variable, so no container backs this server. Modules use this to decide
    /// whether run-scoped schemas must be dropped on teardown: external databases persist
    /// across runs, whereas containers are discarded whole.
    /// </summary>
    public bool UsesExternalConnection => _container is null;

    /// <summary>
    /// Creates a server from the connection string in
    /// <paramref name="connectionStringEnvironmentVariable"/> when that variable is set;
    /// otherwise starts a Testcontainers PostgreSQL container using
    /// <paramref name="containerOptions"/> (or the defaults) and returns its connection string.
    /// </summary>
    public static async Task<PostgresTestServer> CreateAsync(
        string connectionStringEnvironmentVariable,
        PostgresTestContainerOptions? containerOptions = null,
        CancellationToken ct = default)
    {
        var externalConnectionString =
            Environment.GetEnvironmentVariable(connectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            return new PostgresTestServer(externalConnectionString, container: null);
        }

        var options = containerOptions ?? new PostgresTestContainerOptions();
        var container = new PostgreSqlBuilder(options.Image)
            .WithDatabase(options.Database)
            .WithUsername(options.Username)
            .WithPassword(options.Password)
            .Build();

        await container.StartAsync(ct).ConfigureAwait(false);
        return new PostgresTestServer(container.GetConnectionString(), container);
    }

    /// <summary>Disposes the backing container, if any. A no-op for external connections.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
