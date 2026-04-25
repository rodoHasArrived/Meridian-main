using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

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
}
