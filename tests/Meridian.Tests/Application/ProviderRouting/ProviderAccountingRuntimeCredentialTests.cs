using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.AccountingSystem;
using Meridian.Core.Config;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.DataIntegration.Credentials;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderAccountingRuntimeCredentialTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-accounting-credential-recovery", Guid.NewGuid().ToString("N"));
    private CoreConfigStore Config => new(Path.Combine(_root, "appsettings.json"));

    [Theory]
    [InlineData("quickbooks")]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task WorkstationAccounting_AfterRollbackCannotReactivateUnassignedCredentials(string providerId)
    {
        var (store, scope) = await SetupAsync(providerId);
        var commands = new ProviderRuntimeCredentialService(Config, store);
        var preview = await commands.GetMigrationPreviewAsync(providerId, scope.ConnectionId, scope.TenantId);
        await commands.TransferAsync(providerId, scope.ConnectionId, scope.TenantId, "transfer",
            preview.AuditMetadata["credentialGeneration"], "operator", false);
        await commands.BindAsync(providerId, scope.ConnectionId, scope.TenantId);
        await commands.TransferAsync(providerId, scope.ConnectionId, scope.TenantId, "transfer", null, "operator", true);

        // Resolve the real workstation registrations after restart. Metadata reads never call a provider.
        using var services = CreateServices(new FileProviderCredentialStore(_root, _ => null));
        var adapter = services.GetServices<IAccountingSystemProvider>().Single(value => value.ProviderId == providerId);
        Func<Task> metadata = () => ((IAccountingSystemConnectionMetadataProvider)adapter).GetConnectionMetadataAsync();

        await metadata.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        (await store.ReadForProviderAsync(providerId)).Should().NotBeNull("the operator may explicitly review a new transfer");
        (await store.ReadScopedAsync(providerId, scope)).Should().BeNull();
    }

    [Theory]
    [InlineData("quickbooks")]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task WorkstationAccounting_SelectedScopeCannotEnableAnImportWithoutTenantAwareBinding(string providerId)
    {
        var (store, scope) = await SetupAsync(providerId);
        var commands = new ProviderRuntimeCredentialService(Config, store);
        var preview = await commands.GetMigrationPreviewAsync(providerId, scope.ConnectionId, scope.TenantId);
        await commands.TransferAsync(providerId, scope.ConnectionId, scope.TenantId, "transfer",
            preview.AuditMetadata["credentialGeneration"], "operator", false);
        await commands.BindAsync(providerId, scope.ConnectionId, scope.TenantId);

        using var services = CreateServices(store);
        var adapter = services.GetServices<IAccountingSystemProvider>().Single(value => value.ProviderId == providerId);
        Func<Task> metadata = () => ((IAccountingSystemConnectionMetadataProvider)adapter).GetConnectionMetadataAsync();

        await metadata.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        (await store.ReadForProviderAsync(providerId)).Should().BeNull();
    }

    [Theory]
    [InlineData("quickbooks")]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task WorkstationAccounting_EntirelyLegacyProviderRetainsItsExistingConfiguration(string providerId)
    {
        var (store, _) = await SetupAsync(providerId, owned: false);
        using var services = CreateServices(store);
        var adapter = services.GetServices<IAccountingSystemProvider>().Single(value => value.ProviderId == providerId);

        var metadata = await ((IAccountingSystemConnectionMetadataProvider)adapter).GetConnectionMetadataAsync();

        metadata.HasLocalConfig.Should().BeTrue();
        metadata.HasRefreshToken.Should().BeTrue();
    }

    [Theory]
    [InlineData("quickbooks")]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task WorkstationAccounting_ExistingAdapterRechecksNewOwnershipBeforeReadingLegacyCredentials(string providerId)
    {
        var (store, scope) = await SetupAsync(providerId, owned: false);
        using var services = CreateServices(store);
        var adapter = (IAccountingSystemConnectionMetadataProvider)services.GetServices<IAccountingSystemProvider>()
            .Single(value => value.ProviderId == providerId);
        (await adapter.GetConnectionMetadataAsync()).HasLocalConfig.Should().BeTrue();
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            scope.ConnectionId, providerId, "Owned accounting connection", ExternalAccountId: scope.ExternalAccountId), scope.TenantId, scope.Environment);

        Func<Task> metadata = () => adapter.GetConnectionMetadataAsync();

        await metadata.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
    }

    [Theory]
    [InlineData("quickbooks")]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task WorkstationAccounting_ProviderListReportsOwnedProviderBlockedWithoutHidingOtherProviders(string providerId)
    {
        var (store, _) = await SetupAsync(providerId);
        using var services = CreateServices(store);
        var integration = new AccountingSystemIntegrationService(services.GetServices<IAccountingSystemProvider>());

        var providers = await integration.ListProvidersAsync();

        var selected = providers.Single(provider => provider.ProviderId == providerId);
        selected.State.Should().Be(AccountingSystemProviderStateDto.Disabled);
        selected.StatusDetail.Should().Contain("tenant-aware");
        providers.Single(provider => provider.ProviderId == "quickbooks-fixture").State.Should()
            .Be(AccountingSystemProviderStateDto.Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkstationAccounting_ReturningTokenRotationRechecksOwnershipBeforeSaving(bool ownershipIntroduced)
    {
        var (store, scope) = await SetupAsync("quickbooks", owned: false);
        using var services = CreateServices(store);
        var connectionStore = services.GetRequiredService<IQuickBooksOnlineConnectionStore>();
        var requestCredentials = (await connectionStore.ReadAsync())!;
        if (ownershipIntroduced)
            await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
                scope.ConnectionId, "quickbooks", "Owned accounting connection", ExternalAccountId: scope.ExternalAccountId), scope.TenantId, scope.Environment);

        // Simulate a provider response returning after configuration changed; no token exchange runs.
        Func<Task> save = () => connectionStore.SaveRefreshTokenAsync("synthetic-rotated-refresh", requestCredentials);
        if (ownershipIntroduced)
            await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant-aware*");
        else
            await save();

        var retained = (await store.ReadForProviderAsync("quickbooks"))!;
        retained.Get("RefreshToken").Should().Be(ownershipIntroduced ? "synthetic-refresh" : "synthetic-rotated-refresh");
        if (ownershipIntroduced)
            retained.CredentialGeneration.Should().Be(requestCredentials.CredentialSnapshot!.CredentialGeneration);
        else
            retained.CredentialGeneration.Should().NotBe(requestCredentials.CredentialSnapshot!.CredentialGeneration);
    }

    [Fact]
    public async Task WorkstationAccounting_ProviderListDoesNotTranslateMissingConfigurationIntoAnOwnedProviderStatus()
    {
        var (store, _) = await SetupAsync("quickbooks", owned: false);
        using var services = CreateServices(store);
        var integration = new AccountingSystemIntegrationService(services.GetServices<IAccountingSystemProvider>());
        File.Delete(Config.ConfigPath);

        Func<Task> list = () => integration.ListProvidersAsync();

        await list.Should().ThrowAsync<IOException>();
    }

    private ServiceProvider CreateServices(IProviderCredentialStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Config);
        services.AddSingleton(store);
        services.AddWorkstationSharedServices(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private async Task<(FileProviderCredentialStore Store, ProviderCredentialScope Scope)> SetupAsync(string providerId, bool owned = true)
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        var account = providerId == "xero" ? "12345678-1234-1234-1234-123456789abc" : "account-a";
        var environment = providerId == "xero" ? "production" : "sandbox";
        var scope = new ProviderCredentialScope("tenant-a", "owned", account, environment);
        if (owned)
            await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
                scope.ConnectionId, providerId, "Owned accounting connection", ExternalAccountId: account), scope.TenantId, environment);
        var fields = new Dictionary<string, string?>
        {
            ["ClientId"] = "synthetic-client",
            ["ClientSecret"] = "synthetic-secret",
            ["RefreshToken"] = "synthetic-refresh",
            [providerId switch { "quickbooks" => "RealmId", "xero" => "TenantId", _ => "AccountId" }] = account
        };
        if (providerId == "netsuite")
        {
            fields["SubsidiaryId"] = "1";
            fields["AccountingBookId"] = "1";
        }
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveAsync(new(providerId, fields, environment, "operator"));
        return (store, scope);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
