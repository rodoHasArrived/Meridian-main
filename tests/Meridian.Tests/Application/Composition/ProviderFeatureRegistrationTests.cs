using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Core.Config;
using Meridian.Contracts.Api;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.ProviderSdk;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class ProviderFeatureRegistrationTests : IDisposable
{
    private readonly string? _originalRobinhoodAccessToken;
    private readonly string? _originalAlpacaKeyId;
    private readonly string? _originalAlpacaSecretKey;
    private readonly List<string> _tempFiles = new();

    public ProviderFeatureRegistrationTests()
    {
        _originalRobinhoodAccessToken = Environment.GetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN");
        _originalAlpacaKeyId = Environment.GetEnvironmentVariable("ALPACA_KEY_ID");
        _originalAlpacaSecretKey = Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY");
    }

    [Fact]
    public async Task Register_AddsRobinhoodOptionsProvider_WhenEnabledAndTokenPresent()
    {
        Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", "test-token");

        var configPath = WriteConfig(new AppConfig(
            Backfill: new BackfillConfig(
                Providers: new BackfillProvidersConfig(
                    Robinhood: new RobinhoodConfig(Enabled: true)))));

        var services = CreateServices(configPath);

        await using var provider = services.BuildServiceProvider();
        var optionsProviders = provider.GetServices<IOptionsChainProvider>();
        var resolvedProvider = provider.GetRequiredService<IOptionsChainProvider>();

        optionsProviders.Should().Contain(x => x is RobinhoodOptionsChainProvider);
        resolvedProvider.Should().BeOfType<RobinhoodOptionsChainProvider>();
    }

    [Fact]
    public async Task Register_SkipsRobinhoodOptionsProvider_WhenTokenMissing()
    {
        Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", null);

        var configPath = WriteConfig(new AppConfig(
            Backfill: new BackfillConfig(
                Providers: new BackfillProvidersConfig(
                    Robinhood: new RobinhoodConfig(Enabled: true)))));

        var services = CreateServices(configPath);

        await using var provider = services.BuildServiceProvider();
        provider.GetServices<IOptionsChainProvider>().Should().NotContain(x => x is RobinhoodOptionsChainProvider);
        provider.GetRequiredService<IOptionsChainProvider>().Should().NotBeOfType<RobinhoodOptionsChainProvider>();
    }

    [Fact]
    public async Task Register_MergesRobinhoodIntoRuntimeProviderCatalog_WhenEnabledAndTokenPresent()
    {
        Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", "test-token");

        var configPath = WriteConfig(new AppConfig(
            Backfill: new BackfillConfig(
                Providers: new BackfillProvidersConfig(
                    Robinhood: new RobinhoodConfig(Enabled: true)))));

        var services = CreateServices(configPath);

        await using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ProviderRegistry>();

        var robinhood = ProviderCatalog.Get("robinhood");

        robinhood.Should().NotBeNull();
        robinhood!.Capabilities.SupportsOptionsChain.Should().BeTrue();
        robinhood.Capabilities.SupportsBrokerage.Should().BeTrue();
        robinhood.CredentialFields.Should().Contain(field =>
            string.Equals(field.EnvironmentVariable, "ROBINHOOD_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase));
        robinhood.DataTypes.Should().Contain("OptionsChain");
        robinhood.DataTypes.Should().Contain("Brokerage");
    }

    [Fact]
    public async Task Register_CreatesAlpacaStreamingClient_WhenCredentialsComeFromEnvironmentOnly()
    {
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", "AKXXXXXXXXXXXXXXXX");
        Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", "secretxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

        var configPath = WriteConfig(new AppConfig(DataSource: DataSourceKind.Alpaca, Alpaca: null));
        var services = CreateServices(configPath);

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        await using var client = registry.CreateStreamingClient("alpaca");

        client.Should().BeOfType<AlpacaMarketDataClient>();
    }

    [Fact]
    public async Task Register_IBBootstrapWithAlternativeCredentials_DoesNotResolveRuntimeSelector()
    {
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", "AKXXXXXXXXXXXXXXXX");
        Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", "secretxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

        var configPath = WriteConfig(new AppConfig(
            DataSource: DataSourceKind.IB,
            Symbols: [new SymbolConfig("SPY")]));
        var services = CreateServices(configPath);
        var selectorResolutionCount = 0;
        services.RemoveAll<ConfigurationService>();
        services.AddSingleton(_ => new ConfigurationService(
            ibGatewayAvailabilityProbe: static () => false,
            providerSelectorAccessor: () =>
            {
                selectorResolutionCount++;
                throw new InvalidOperationException(
                    "Provider bootstrap must not resolve the runtime routing graph.");
            }));

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ProviderRegistry>().Should().NotBeNull();
        selectorResolutionCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfigurationService_ExplicitRuntimeSelection_StillUsesRegisteredSelector()
    {
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", "AKXXXXXXXXXXXXXXXX");
        Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", "secretxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        var selector = new RecordingProviderSelector("alpaca");
        await using var service = new ConfigurationService(providerSelector: selector);

        var selected = service.GetBestRealTimeProvider();

        selected.Should().NotBeNull();
        selected!.Name.Should().BeEquivalentTo("alpaca");
        selector.CallCount.Should().Be(1);
    }

    private static ServiceCollection CreateServices(string configPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new CollectorFeatureRegistration().Register(services, options);
        new ProviderFeatureRegistration().Register(services, options);

        return services;
    }

    private string WriteConfig(AppConfig config)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(config, AppConfigJsonOptions.Write);
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", _originalRobinhoodAccessToken);
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", _originalAlpacaKeyId);
        Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", _originalAlpacaSecretKey);
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;

        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class RecordingProviderSelector(string providerFamilyId) : IBestOfBreedProviderSelector
    {
        public int CallCount { get; private set; }

        public Task<ProviderRouteResult> SelectAsync(
            ProviderRouteContext context,
            CancellationToken ct = default)
        {
            CallCount++;
            var decision = new ProviderRouteDecision(
                ConnectionId: providerFamilyId,
                ProviderFamilyId: providerFamilyId,
                Capability: context.Capability,
                SafetyMode: ProviderSafetyMode.HealthAwareFailover,
                ScopeRank: 0,
                Priority: 0,
                IsHealthy: true,
                ReasonCodes: [],
                FallbackConnectionIds: []);
            return Task.FromResult(new ProviderRouteResult(
                context,
                decision,
                [decision],
                []));
        }
    }
}
