using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.TestSupport;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingArtifactStoreTests :
    IClassFixture<ReportingArtifactDatabaseFixture>,
    IAsyncLifetime
{
    private readonly ReportingArtifactDatabaseFixture _database;

    public ReportingArtifactStoreTests(ReportingArtifactDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _database.ResetAsync();

    [ReportingDatabaseFact]
    public async Task StoreAsync_PersistsContentAddressedBytesIdempotently()
    {
        var tenantId = NewTenantId();
        var content = Encoding.UTF8.GetBytes($"immutable-report-{Guid.NewGuid():N}");

        var first = await _database.Store.StoreAsync(new ReportingArtifactWriteRequest(tenantId, content));
        var second = await _database.Store.StoreAsync(new ReportingArtifactWriteRequest(tenantId, content));
        var retained = await _database.Store.ReadAsync(first.Identity);

        first.AlreadyExisted.Should().BeFalse();
        second.AlreadyExisted.Should().BeTrue();
        second.Identity.Should().Be(first.Identity);
        first.ByteSize.Should().Be(content.LongLength);
        retained.ByteSize.Should().Be(content.LongLength);
        retained.Content.Should().Equal(content);
        (await _database.CountRowsAsync(first.Identity)).Should().Be(1);
    }

    [ReportingDatabaseFact]
    public async Task StoreAsync_UsesTenantScopedContentAddresses()
    {
        var content = Encoding.UTF8.GetBytes($"shared-report-{Guid.NewGuid():N}");
        var first = await _database.Store.StoreAsync(new ReportingArtifactWriteRequest(NewTenantId(), content));
        var second = await _database.Store.StoreAsync(new ReportingArtifactWriteRequest(NewTenantId(), content));

        first.Identity.TenantId.Should().NotBe(second.Identity.TenantId);
        first.Identity.ContentHashSha256.Should().Be(second.Identity.ContentHashSha256);
        (await _database.Store.ReadAsync(first.Identity)).Content.Should().Equal(content);
        (await _database.Store.ReadAsync(second.Identity)).Content.Should().Equal(content);

        var missingTenantIdentity = first.Identity with { TenantId = NewTenantId() };
        Func<Task> crossTenantRead = () => _database.Store.ReadAsync(missingTenantIdentity);
        await crossTenantRead.Should().ThrowAsync<ReportingArtifactNotFoundException>();
    }

    [ReportingDatabaseFact]
    public async Task ReadAsync_FailsClosedWhenArtifactIsMissing()
    {
        var identity = new ReportingArtifactIdentity(NewTenantId(), new string('0', 64));

        Func<Task> read = () => _database.Store.ReadAsync(identity);

        var failure = await read.Should().ThrowAsync<ReportingArtifactNotFoundException>();
        failure.Which.Identity.Should().Be(identity);
    }

    [ReportingDatabaseFact]
    public async Task ReadAsync_FailsClosedWhenRetainedBytesDoNotMatchContentAddress()
    {
        var content = Encoding.UTF8.GetBytes($"corruption-proof-{Guid.NewGuid():N}");
        var stored = await _database.Store.StoreAsync(new ReportingArtifactWriteRequest(NewTenantId(), content));
        var corrupted = content.Select(static value => (byte)(value ^ 0x5A)).ToArray();
        await _database.CorruptBytesAsync(stored.Identity, corrupted);

        Func<Task> read = () => _database.Store.ReadAsync(stored.Identity);
        Func<Task> idempotentWrite = () => _database.Store.StoreAsync(
            new ReportingArtifactWriteRequest(stored.Identity.TenantId, content));

        await read.Should().ThrowAsync<ReportingArtifactIntegrityException>()
            .WithMessage("*SHA-256*");
        await idempotentWrite.Should().ThrowAsync<ReportingArtifactIntegrityException>()
            .WithMessage("*SHA-256*");
    }

    [ReportingDatabaseFact]
    public async Task DatabaseGuards_RejectArtifactUpdatesAndDeletes()
    {
        var content = Encoding.UTF8.GetBytes($"write-once-{Guid.NewGuid():N}");
        var stored = await _database.Store.StoreAsync(new ReportingArtifactWriteRequest(NewTenantId(), content));

        Func<Task> update = () => _database.ExecuteMutationAsync(
            $"update {_database.QualifiedArtifactTable} set stored_at_utc = stored_at_utc where tenant_id = @tenant_id and content_hash_sha256 = @hash;",
            stored.Identity);
        Func<Task> delete = () => _database.ExecuteMutationAsync(
            $"delete from {_database.QualifiedArtifactTable} where tenant_id = @tenant_id and content_hash_sha256 = @hash;",
            stored.Identity);

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55000");
        (await _database.Store.ReadAsync(stored.Identity)).Content.Should().Equal(content);
    }

    [ReportingDatabaseFact]
    public async Task MigrationRunner_IsConcurrentAndIdempotentWithChecksummedLedger()
    {
        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => new ReportingMigrationRunner(_database.Options).EnsureMigratedAsync()));

        (await _database.HasMigrationAsync("001_reporting_artifact_blobs.sql")).Should().BeTrue();
    }

    private static string NewTenantId() => $"tenant-{Guid.NewGuid():N}";
}

public sealed class ReportingArtifactDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "MERIDIAN_REPORTING_CONNECTION_STRING";
    private PostgresTestServer? _server;

    public ReportingArtifactStoreOptions Options { get; private set; } = null!;

    public PostgresReportingArtifactStore Store { get; private set; } = null!;

    public string QualifiedArtifactTable => $"\"{Options.Schema}\".\"reporting_artifact_blobs\"";

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(ConnectionStringVariable).ConfigureAwait(false);
        Options = new ReportingArtifactStoreOptions
        {
            ConnectionString = _server.ConnectionString,
            Schema = _server.CreateSchemaName("reporting_artifact")
        };

        try
        {
            await new ReportingMigrationRunner(Options).EnsureMigratedAsync().ConfigureAwait(false);
            Store = new PostgresReportingArtifactStore(Options);
        }
        catch
        {
            await _server.DisposeAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Drops and recreates this class fixture's private schema after each test method so a
    /// destructive or interrupted scenario cannot leak rows or disabled triggers to the next one.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_server is null)
        {
            throw new InvalidOperationException("The reporting database fixture is not initialized.");
        }

        var migrationRunner = new ReportingMigrationRunner(Options);
        await migrationRunner.ResetSchemaAsync().ConfigureAwait(false);
        await migrationRunner.EnsureMigratedAsync().ConfigureAwait(false);
        Store = new PostgresReportingArtifactStore(Options);
    }

    public async Task<long> CountRowsAsync(ReportingArtifactIdentity identity)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select count(*) from {QualifiedArtifactTable} where tenant_id = @tenant_id and content_hash_sha256 = @hash;";
        AddIdentityParameters(command, identity);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
    }

    public async Task<long> CountMigrationRowsAsync()
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from \"{Options.Schema}\".\"reporting_schema_migrations\";";
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
    }

    public async Task<bool> HasMigrationAsync(string scriptName)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select exists(select 1 from \"{Options.Schema}\".\"reporting_schema_migrations\" where filename = @script_name);";
        command.Parameters.AddWithValue("script_name", NpgsqlDbType.Text, scriptName);
        return (bool)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? false);
    }

    public async Task ExecuteMutationAsync(string sql, ReportingArtifactIdentity identity)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddIdentityParameters(command, identity);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task CorruptBytesAsync(ReportingArtifactIdentity identity, byte[] corrupted)
    {
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await using (var disable = connection.CreateCommand())
        {
            disable.CommandText = $"alter table {QualifiedArtifactTable} disable trigger user;";
            await disable.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        try
        {
            await using var corrupt = connection.CreateCommand();
            corrupt.CommandText =
                $"update {QualifiedArtifactTable} set content = @content where tenant_id = @tenant_id and content_hash_sha256 = @hash;";
            corrupt.Parameters.AddWithValue("content", NpgsqlDbType.Bytea, corrupted);
            AddIdentityParameters(corrupt, identity);
            await corrupt.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await using var enable = connection.CreateCommand();
            enable.CommandText = $"alter table {QualifiedArtifactTable} enable trigger user;";
            await enable.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(Options.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static void AddIdentityParameters(NpgsqlCommand command, ReportingArtifactIdentity identity)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Text, identity.ContentHashSha256);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ReportingDatabaseFactAttribute : FactAttribute
{
    private const string DisableDockerVariable = "MERIDIAN_DISABLE_DOCKER_TESTS";
    private const string ConnectionStringVariable = "MERIDIAN_REPORTING_CONNECTION_STRING";

    public ReportingDatabaseFactAttribute()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            return;
        }

        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Reporting PostgreSQL tests are skipped because {DisableDockerVariable}=true.";
            return;
        }

        if (!ReportingDockerAvailability.IsDockerAvailable())
        {
            Skip = "Reporting PostgreSQL tests are skipped because Docker is unavailable. " +
                   $"Start Docker, set {ConnectionStringVariable}, or set {DisableDockerVariable}=true.";
        }
    }
}

file static class ReportingDockerAvailability
{
    public static bool IsDockerAvailable()
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
