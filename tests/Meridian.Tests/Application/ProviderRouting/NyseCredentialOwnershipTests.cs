using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Infrastructure.DataSources;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Application.ProviderRouting;

// Direct-construction tests change process-wide posture; no other composition may run concurrently.
[Collection("Sequential")]
public sealed class NyseCredentialOwnershipTests : IDisposable
{
    private static readonly string[] PostureVariables =
    [
        "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "MERIDIAN_ENVIRONMENT", "MERIDIAN_DEPLOYMENT_ENVIRONMENT",
        "MERIDIAN_MODE", "MERIDIAN_API_DEPLOYMENT_MODE", "MDC_PACKAGED_BUILD", "MERIDIAN_CUSTOMER_BUILD"
    ];
    private readonly Dictionary<string, string?> _originalEnvironment = PostureVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-nyse-ownership", Guid.NewGuid().ToString("N"));

    public NyseCredentialOwnershipTests()
    {
        foreach (var name in PostureVariables)
            Environment.SetEnvironmentVariable(name, null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
    }

    [Theory]
    [InlineData("production")]
    [InlineData("packaged")]
    [InlineData("customer")]
    public async Task FinalHostPosture_RefusesEveryNyseSurfaceBeforeReadingCredentialsOrCreatingAClient(string posture)
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        configuration.Setup(value => value[It.IsAny<string>()]).Returns((string _) => null);
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        await using var provider = await CreateHostAsync(posture, configuration.Object, http.Object);

        Action options = () => provider.GetRequiredService<NYSEOptions>();
        Action direct = () => provider.GetServices<IDataSource>().ToArray();
        Action streaming = () => provider.GetRequiredService<ProviderFactory>().CreateNyseStreamingClient();

        options.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        direct.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        streaming.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        configuration.Verify(value => value.GetSection(It.IsAny<string>()), Times.Never);
        http.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Production")]
    [InlineData("DOTNET_ENVIRONMENT", "Production")]
    [InlineData("MERIDIAN_MODE", "Live")]
    [InlineData("MERIDIAN_API_DEPLOYMENT_MODE", "ProductionApi")]
    [InlineData("MDC_PACKAGED_BUILD", "1")]
    [InlineData("MDC_PACKAGED_BUILD", "yes")]
    [InlineData("MERIDIAN_CUSTOMER_BUILD", "true")]
    public void UnregisteredProductionPolicy_RefusesPublicRegistrationAndConstruction(string flag, string value)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable(flag, value);
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        configuration.Setup(item => item[It.IsAny<string>()]).Returns((string _) => null);
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddSingleton(http.Object);
        services.AddNYSEDataSource(configuration.Object);
        // Supplying an options instance cannot bypass the direct-source admission check.
        services.AddSingleton(new NYSEOptions { ApiKey = "synthetic-unassigned-key", ApiSecret = "synthetic-unassigned-secret" });
        using var provider = services.BuildServiceProvider();

        Action registered = () => provider.GetServices<IDataSource>().ToArray();
        Action constructed = () => new NYSEDataSource(new NYSEOptions(), http.Object);
        var publisher = new TestMarketEventPublisher();
        Action streaming = () => new NyseMarketDataClient(new TradeDataCollector(publisher, null),
            new MarketDepthCollector(publisher, requireExplicitSubscription: false), new QuoteCollector(publisher), http.Object);

        registered.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        constructed.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        streaming.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        configuration.Verify(item => item.GetSection(It.IsAny<string>()), Times.Never);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public void PublicRegistration_ProductionConfigurationCannotBeOverriddenByADevelopmentPolicy()
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        configuration.Setup(item => item[It.IsAny<string>()]).Returns((string name) =>
            name == "ASPNETCORE_ENVIRONMENT" ? "Production" : null);
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddSingleton(new NyseCredentialAccessPolicy(false));
        services.AddSingleton(http.Object);
        services.AddNYSEDataSource(configuration.Object);
        using var provider = services.BuildServiceProvider();

        Action resolve = () => provider.GetRequiredService<NYSEOptions>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("NYSE activation is unavailable*");
        configuration.Verify(item => item.GetSection(It.IsAny<string>()), Times.Never);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Development_RetainsNyseOptionsDirectSourceAndUnifiedStreamingWithoutDispatch()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NYSE:ApiKey"] = "synthetic-development-key",
            ["NYSE:ApiSecret"] = "synthetic-development-secret"
        }).Build();
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        await using var provider = await CreateHostAsync("development", configuration, http.Object);

        provider.GetRequiredService<NYSEOptions>().ApiKey.Should().Be("synthetic-development-key");
        provider.GetServices<IDataSource>().Should().ContainSingle().Which.Should().BeOfType<NYSEDataSource>();
        await using var streaming = provider.GetRequiredService<ProviderFactory>().CreateNyseStreamingClient();
        streaming.Should().BeOfType<NyseMarketDataClient>();
        http.VerifyNoOtherCalls();
    }

    private async Task<ServiceProvider> CreateHostAsync(string posture, IConfiguration providerConfiguration, IHttpClientFactory http)
    {
        Directory.CreateDirectory(_root);
        var store = new ConfigStore(Path.Combine(_root, "appsettings.json"), new(false));
        await store.SaveAsync(new AppConfig(DataRoot: _root));
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(new ConfigurationService(ibGatewayAvailabilityProbe: () => false));
        services.AddSingleton<IProviderCredentialStore>(Mock.Of<IProviderCredentialStore>());
        services.AddSingleton(providerConfiguration);
        services.AddSingleton(http);
        services.AddSingleton(new CompositionConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MDC_PACKAGED_BUILD"] = posture == "packaged" ? "true" : "false",
            ["MERIDIAN_CUSTOMER_BUILD"] = posture == "customer" ? "yes" : "false"
        }).Build()));
        new ProviderFeatureRegistration().Register(services, new CompositionOptions { ConfigPath = store.ConfigPath });
        services.DeclareMeridianDeploymentPosture(posture == "production"
            ? MeridianDeploymentPosture.ProductionApi : MeridianDeploymentPosture.LocalWorkstation);
        var publisher = new TestMarketEventPublisher();
        services.AddSingleton(new TradeDataCollector(publisher, null));
        services.AddSingleton(new MarketDepthCollector(publisher, requireExplicitSubscription: false));
        services.AddSingleton(new QuoteCollector(publisher));
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        foreach (var entry in _originalEnvironment)
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
