namespace Meridian.TestSupport;

/// <summary>
/// Gives a process-configuration test fixture a uniquely named PostgreSQL schema whenever its
/// domain connection string is supplied by the host. The caller must serialize environment
/// mutation for the scope's lifetime.
/// </summary>
public sealed class PostgresTestSchemaEnvironmentScope : IAsyncDisposable
{
    private readonly string _connectionStringEnvironmentVariable;
    private readonly string? _originalConnectionString;
    private readonly bool _restoreConnectionString;
    private readonly string _schemaEnvironmentVariable;
    private readonly string? _originalSchema;
    private PostgresTestServer? _server;

    private PostgresTestSchemaEnvironmentScope(
        PostgresTestServer server,
        string connectionStringEnvironmentVariable,
        string? originalConnectionString,
        bool restoreConnectionString,
        string schemaEnvironmentVariable,
        string? originalSchema,
        string schema)
    {
        _server = server;
        _connectionStringEnvironmentVariable = connectionStringEnvironmentVariable;
        _originalConnectionString = originalConnectionString;
        _restoreConnectionString = restoreConnectionString;
        _schemaEnvironmentVariable = schemaEnvironmentVariable;
        _originalSchema = originalSchema;
        Schema = schema;
    }

    public string Schema { get; }

    /// <summary>
    /// Returns <see langword="null"/> when the domain connection is not configured. Otherwise,
    /// allocates and publishes a unique schema that is dropped when the scope is disposed.
    /// </summary>
    public static async Task<PostgresTestSchemaEnvironmentScope?> CreateIfConfiguredAsync(
        string connectionStringEnvironmentVariable,
        string schemaEnvironmentVariable,
        string schemaPrefix,
        string? fallbackConnectionStringEnvironmentVariable = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringEnvironmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaEnvironmentVariable);

        var originalConnectionString =
            Environment.GetEnvironmentVariable(connectionStringEnvironmentVariable);
        var effectiveConnectionString = originalConnectionString;
        if (string.IsNullOrWhiteSpace(effectiveConnectionString) &&
            !string.IsNullOrWhiteSpace(fallbackConnectionStringEnvironmentVariable))
        {
            effectiveConnectionString =
                Environment.GetEnvironmentVariable(fallbackConnectionStringEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            return null;
        }

        var publishedFallbackConnection = string.IsNullOrWhiteSpace(originalConnectionString);
        if (publishedFallbackConnection)
        {
            Environment.SetEnvironmentVariable(
                connectionStringEnvironmentVariable,
                effectiveConnectionString);
        }

        PostgresTestServer? server = null;
        try
        {
            server = await PostgresTestServer.CreateAsync(
                    connectionStringEnvironmentVariable,
                    ct: ct)
                .ConfigureAwait(false);
            var schema = server.CreateSchemaName(schemaPrefix);
            var originalSchema = Environment.GetEnvironmentVariable(schemaEnvironmentVariable);
            Environment.SetEnvironmentVariable(schemaEnvironmentVariable, schema);
            return new PostgresTestSchemaEnvironmentScope(
                server,
                connectionStringEnvironmentVariable,
                originalConnectionString,
                publishedFallbackConnection,
                schemaEnvironmentVariable,
                originalSchema,
                schema);
        }
        catch
        {
            if (publishedFallbackConnection)
            {
                Environment.SetEnvironmentVariable(
                    connectionStringEnvironmentVariable,
                    originalConnectionString);
            }
            if (server is not null)
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var server = Volatile.Read(ref _server);
        if (server is null)
        {
            return;
        }

        Environment.SetEnvironmentVariable(_schemaEnvironmentVariable, _originalSchema);
        if (_restoreConnectionString)
        {
            Environment.SetEnvironmentVariable(
                _connectionStringEnvironmentVariable,
                _originalConnectionString);
        }

        await server.DisposeAsync().ConfigureAwait(false);
        Interlocked.CompareExchange(ref _server, null, server);
    }
}
