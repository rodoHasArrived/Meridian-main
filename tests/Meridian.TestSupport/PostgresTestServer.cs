using System.Collections.Concurrent;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Meridian.TestSupport;

/// <summary>
/// Resolves a PostgreSQL endpoint for integration tests, centralising the single
/// container-or-external-connection lifecycle that each Meridian test module previously
/// hand-rolled. When the configured environment variable supplies a connection string the
/// external database is used and container startup is short-circuited; otherwise a
/// disposable <c>postgres</c> Testcontainers container is started. Schemas allocated through
/// <see cref="CreateSchemaName"/> are owned by this server and dropped when an external
/// database is released.
/// </summary>
/// <remarks>
/// Migration setup remains the responsibility of the owning module. Test fixtures should
/// allocate schemas through <see cref="CreateSchemaName"/> so teardown is deterministic even
/// when migration or test setup fails before the module-specific cleanup path can run.
/// </remarks>
public sealed class PostgresTestServer : IAsyncDisposable
{
    private readonly PostgreSqlContainer? _container;
    private readonly ConcurrentDictionary<string, byte> _ownedSchemas = new(StringComparer.Ordinal);

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
    /// Allocates a unique schema name owned by this server. On an externally supplied database,
    /// every allocated schema is dropped with <c>CASCADE</c> during <see cref="DisposeAsync"/>.
    /// A container-backed server obtains the same isolation and releases the schemas when the
    /// disposable container is removed.
    /// </summary>
    public string CreateSchemaName(string prefix)
    {
        var schema = PostgresTestSchema.NewSchemaName(prefix);
        ValidateIdentifier(schema);
        _ownedSchemas.TryAdd(schema, 0);
        return schema;
    }

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

        try
        {
            await container.StartAsync(ct).ConfigureAwait(false);
            return new PostgresTestServer(container.GetConnectionString(), container);
        }
        catch
        {
            await container.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Drops owned schemas on an external database, then disposes the backing container when one
    /// was created. Schema removal is idempotent so fixture-specific reset paths may coexist while
    /// callers migrate to server-owned cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (UsesExternalConnection && !_ownedSchemas.IsEmpty)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            foreach (var schema in _ownedSchemas.Keys.Order(StringComparer.Ordinal))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            _ownedSchemas.Clear();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (identifier.Length == 0 ||
            !identifier.All(static c =>
                char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                $"Unsafe schema identifier: '{identifier}'",
                nameof(identifier));
        }
    }
}
