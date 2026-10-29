using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderRuntimeCredentialServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-runtime-ownership", Guid.NewGuid().ToString("N"));
    private ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));
    private static ProviderCredentialSaveRequest Credentials(string key) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = key + "-secret" }, "paper");

    private async Task<FileProviderCredentialStore> SetupAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            "owned", "alpaca", "Owned account", ExternalAccountId: "account-a"), "tenant-a", "paper");
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Credentials("legacy"));
        await store.SaveScopedAsync(Credentials("owned"), new("tenant-a", "owned", "account-a", "paper"));
        return store;
    }

    [Fact]
    public async Task HostResolver_RequiresExplicitBindingAndCannotFallbackAfterDeletion()
    {
        var store = await SetupAsync();
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Config);
        Action resolve = () => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
        resolve.Should().Throw<InvalidOperationException>();
        await new ProviderRuntimeCredentialService(Config, store).BindAsync("alpaca", "OWNED", "tenant-a");
        resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("owned");
        await store.DeleteScopedAsync("alpaca", new("tenant-a", "owned", "account-a", "paper"));
        var missing = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider),
            new Dictionary<string, string?> { ["ALPACA_KEY_ID"] = "caller-key" });
        missing.IsConfigured("ALPACA_KEY_ID").Should().BeFalse();
        missing.CredentialEnvironment.Should().Be("paper");
        await new ProviderConnectionService(Config).DeleteForTenantAsync("owned", "tenant-a");
        resolve.Should().Throw<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("disabled")]
    [InlineData("duplicate")]
    [InlineData("account")]
    [InlineData("environment")]
    public async Task BindingAndMigration_RejectInvalidRetainedOwnership(string conflict)
    {
        var store = await SetupAsync();
        await Config.UpdateAsync(config =>
        {
            var section = config.ProviderConnections!;
            var connection = section.Connections!.Single();
            var changed = conflict switch
            {
                "foreign" => connection with { TenantId = "tenant-b" },
                "disabled" => connection with { Enabled = false },
                "account" => connection with { ExternalAccountId = null },
                "environment" => connection with { CredentialEnvironment = null },
                _ => connection
            };
            return config with { ProviderConnections = section with { Connections = conflict == "duplicate" ? [changed, changed] : [changed] } };
        });
        var service = new ProviderRuntimeCredentialService(Config, store);
        Func<Task> bind = () => service.BindAsync("alpaca", "owned", "tenant-a");
        await bind.Should().ThrowAsync<UnauthorizedAccessException>();
        Func<Task> migrate = () => service.TransferAsync("alpaca", "owned", "tenant-a", "operation-a", "generation", "operator", false);
        await migrate.Should().ThrowAsync<UnauthorizedAccessException>();
        Config.LoadRequired().ProviderConnections!.Connections!.Length.Should().Be(conflict == "duplicate" ? 2 : 1);
    }

    [Fact]
    public async Task ScopedCommand_RechecksOwnershipAfterWaitingForConfigurationLease()
    {
        var store = await SetupAsync();
        var service = new ProviderRuntimeCredentialService(Config, store);
        Task<ProviderCredentialMigrationReceipt> pending;
        using (await Config.AcquireWriteLeaseAsync())
        {
            pending = service.TransferAsync("alpaca", "owned", "tenant-a", "operation-a", "generation", "operator", false);
            // Simulate the lease holder removing ownership before the waiting command resumes.
            await File.WriteAllTextAsync(Config.ConfigPath, "{}");
        }
        Func<Task> migrate = () => pending.WaitAsync(TimeSpan.FromSeconds(5));
        await migrate.Should().ThrowAsync<UnauthorizedAccessException>();
        (await store.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy");
    }

    [Fact]
    public async Task RuntimeBinding_RoundTripsWithSourceGeneratedConfigSerialization()
    {
        var store = await SetupAsync();
        await new ProviderRuntimeCredentialService(Config, store).BindAsync("alpaca", "owned", "tenant-a");
        Config.LoadRequired().ProviderConnections!.RuntimeCredentials.Should().ContainSingle()
            .Which.Should().Be(new ProviderRuntimeCredentialBinding("alpaca", "tenant-a", "owned"));
    }

    [Fact]
    public async Task RemovingAnUnboundOwner_CannotReactivateLegacyCredentials()
    {
        var store = await SetupAsync();
        await new ProviderConnectionService(Config).DeleteForTenantAsync("owned", "tenant-a");
        var current = Config.LoadRequired();
        current.ProviderConnections!.OwnershipRequiredProviders.Should().Contain("alpaca");
        Action resolve = () => new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Config)
            .CreateContext(typeof(AlpacaHistoricalDataProvider));
        resolve.Should().Throw<InvalidOperationException>();
        (await store.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
