using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lifecycle = Meridian.Ui.Shared.Services.ProviderConnectionLifecycleService;
using WorkstationConfigStore = Meridian.Ui.Shared.Services.ConfigStore;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderCredentialProductionOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-production-ownership", Guid.NewGuid().ToString("N"));
    private ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));
    private static ProviderCredentialOwnershipPolicy Required => new(true);

    [Theory]
    [InlineData("production")]
    [InlineData("packaged-workstation")]
    [InlineData("packaged-unspecified")]
    public void Composition_ResolvesTheFinalDeclaredPostureInsteadOfRegistrationOrder(string posture)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MDC_PACKAGED_BUILD"] = posture.StartsWith("packaged", StringComparison.Ordinal) ? "true" : "false"
        }).Build();
        services.AddSingleton(new CompositionConfiguration(configuration));
        services.AddProviderCredentialOwnershipPolicy();
        services.DeclareMeridianDeploymentPosture(posture == "production"
            ? MeridianDeploymentPosture.ProductionApi : posture == "packaged-unspecified"
                ? MeridianDeploymentPosture.Unspecified : MeridianDeploymentPosture.LocalWorkstation);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderCredentialOwnershipPolicy>().RequireExplicitOwnership.Should().BeTrue();
    }

    [Theory]
    [InlineData("MDC_PACKAGED_BUILD", "true")]
    [InlineData("MDC_PACKAGED_BUILD", "1")]
    [InlineData("MDC_PACKAGED_BUILD", "yes")]
    [InlineData("MERIDIAN_CUSTOMER_BUILD", "yes")]
    public void DirectConstruction_PackagedFlagsUseTheHostPoliciesTruthyValues(string flag, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            [flag] = value
        }).Build();
        ProviderCredentialOwnershipPolicy.ForCurrentEnvironment(new(configuration)).RequireExplicitOwnership.Should().BeTrue();
    }

    [Fact]
    public async Task Production_UnownedSetupAndLegacyExecutionRefuseBeforeAnyVaultAccess()
    {
        await InitializeAsync();
        var vault = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);
        var setup = new ProviderSetupService(Config, vault.Object, new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()), Required);
        var result = await setup.ConfigureAsync(new("alpaca", "Unowned", "unassigned-key", "unassigned-secret", null,
            ["streaming"], "paper"));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("authorized connection");
        var legacy = new LegacyProviderRuntimeCredentialStore(Config, vault.Object, Required);
        var read = () => legacy.ReadForProviderAsync("alpaca");
        await read.Should().ThrowAsync<ProviderCredentialOwnershipRequiredException>();
        var fallback = new Mock<IProviderCredentialResolver>(MockBehavior.Strict);
        var resolver = new StoredProviderCredentialResolver(vault.Object, fallback.Object, Config, Required);
        Action resolve = () => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider),
            new Dictionary<string, string?> { ["ALPACA_KEY_ID"] = "unassigned-config-key" });
        resolve.Should().Throw<ProviderCredentialOwnershipRequiredException>();
        vault.VerifyNoOtherCalls();
        fallback.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Production_ScopedSetupAndExecutionSelectOnlyTheBoundAccount()
    {
        await InitializeAsync();
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Credentials("unassigned"));
        var setup = new ProviderSetupService(Config, vault, new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()), Required);
        var scope = new ProviderCredentialScope("tenant-a", "owned", "account-a", "paper");
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new(scope.ConnectionId, "alpaca", "Owned",
            ExternalAccountId: scope.ExternalAccountId), scope.TenantId, scope.Environment);
        (await setup.ConfigureForConnectionAsync(new("alpaca", "Owned", "selected", "selected-secret", null, [], "paper"),
            scope.ConnectionId, scope.TenantId, "operator")).Success.Should().BeTrue();
        await new ProviderRuntimeCredentialService(Config, vault).BindAsync("alpaca", scope.ConnectionId, scope.TenantId);
        var resolver = new StoredProviderCredentialResolver(vault, new EnvironmentCredentialResolver(), Config, Required);
        resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("selected");
        await vault.DeleteScopedAsync("alpaca", scope);
        resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).IsConfigured("ALPACA_KEY_ID").Should().BeFalse();
        (await vault.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("unassigned");
    }

    [Fact]
    public async Task Production_CompatibilityLifecycleCannotReportOrMutateUnownedCredentials()
    {
        await InitializeAsync();
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveAsync(Credentials("unassigned"));
        var lifecycle = new Lifecycle(vault, new WorkstationConfigStore(Config.ConfigPath),
            NullLogger<Lifecycle>.Instance, ownershipPolicy: Required);
        var rows = await lifecycle.GetConnectionsForTenantAsync("tenant-a");
        var row = rows.Single(value => value.ProviderId == "alpaca");
        row.CredentialState.Should().Be(ProviderCredentialStateDto.Missing);
        row.CredentialSource.Should().Be(ProviderCredentialSourceDto.None);
        var save = () => lifecycle.SaveCredentialsAsync("alpaca", new(new Dictionary<string, string?> { ["KeyId"] = "replacement" }));
        await save.Should().ThrowAsync<UnauthorizedAccessException>();
        (await vault.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("unassigned");
    }

    [Fact]
    public void Production_DefaultSymbolSearchCannotConstructAmbientCredentialProviders()
    {
        Action construct = () => new SymbolSearchService(ownershipPolicy: Required);
        construct.Should().Throw<ProviderCredentialOwnershipRequiredException>();
    }

    [Fact]
    public async Task Production_HostCompositionCannotSelectConfigurationFallbackWithoutAVault()
    {
        await InitializeAsync();
        var services = new ServiceCollection();
        services.AddSingleton(Config);
        services.AddSingleton(new CompositionConfiguration(new ConfigurationBuilder().AddInMemoryCollection().Build()));
        services.AddSingleton(new ConfigurationService());
        new ProviderFeatureRegistration().Register(services, new CompositionOptions { ConfigPath = Config.ConfigPath });
        services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
        using var provider = services.BuildServiceProvider();
        Action resolve = () => provider.GetRequiredService<IProviderCredentialResolver>();
        resolve.Should().Throw<InvalidOperationException>().WithMessage("*require a credential vault*");
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
    }

    private static ProviderCredentialSaveRequest Credentials(string key) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = key + "-secret" }, "paper", "operator");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
