using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterDatabaseFixture : IAsyncLifetime
{
    private readonly SecurityMasterMigrationRunner? _migrationRunner;

    public SecurityMasterDatabaseFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            IsConfigured = false;
            Options = new SecurityMasterOptions();
            return;
        }

        IsConfigured = true;
        Options = new SecurityMasterOptions
        {
            ConnectionString = connectionString,
            Schema = $"security_master_test_{Guid.NewGuid():N}",
            PreloadProjectionCache = false
        };

        _migrationRunner = new SecurityMasterMigrationRunner(Options);
    }

    public SecurityMasterOptions Options { get; }
    public bool IsConfigured { get; }

    public Task InitializeAsync()
        => _migrationRunner is null ? Task.CompletedTask : _migrationRunner.EnsureMigratedAsync();

    public async Task DisposeAsync()
    {
        if (_migrationRunner is not null)
        {
            await _migrationRunner.ResetSchemaAsync().ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(nameof(SecurityMasterDatabaseCollection), DisableParallelization = true)]
public sealed class SecurityMasterDatabaseCollection : ICollectionFixture<SecurityMasterDatabaseFixture>
{
}
