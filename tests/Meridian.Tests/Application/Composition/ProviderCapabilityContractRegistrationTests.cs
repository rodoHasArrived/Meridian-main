using System.Text;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Domain.Events;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class ProviderCapabilityContractRegistrationTests : IDisposable
{
    public void Dispose()
    {
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;
    }

    [Fact]
    public async Task DataSourceDeclarations_ShouldMatchImplementedContracts_AndRegistrationPaths()
    {
        var configPath = TestConfigWriter.WriteTempConfig(new AppConfig());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();

        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new CollectorFeatureRegistration().Register(services, options);
        new ProviderFeatureRegistration().Register(services, options);

        await using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<ProviderRegistry>();
        var dataSourceRegistry = serviceProvider.GetRequiredService<DataSourceRegistry>();

        var mismatches = new List<string>();

        foreach (var source in dataSourceRegistry.Sources.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            ValidateCapabilityClaim(
                mismatches,
                source.Id,
                "realtime",
                typeof(IMarketDataClient),
                source.IsRealtime,
                source.ImplementationType,
                registry.SupportedStreamingSources.Contains(source.Id, StringComparer.OrdinalIgnoreCase),
                "DataSourceAttribute + ProviderRegistry.RegisterStreamingFactory");

            ValidateCapabilityClaim(
                mismatches,
                source.Id,
                "historical",
                typeof(IHistoricalDataProvider),
                source.IsHistorical,
                source.ImplementationType,
                registry.GetProviders<IHistoricalDataProvider>().Any(p => string.Equals(p.ProviderId, source.Id, StringComparison.OrdinalIgnoreCase)),
                "DataSourceAttribute + ProviderFactory.CreateBackfillProviders");
        }

        foreach (var provider in registry.GetAllProviderMetadata().OrderBy(p => p.ProviderId, StringComparer.Ordinal))
        {
            var providerType = provider.GetType();
            var capabilities = provider.ProviderCapabilities;

            ValidateCapabilityClaim(mismatches, provider.ProviderId, "supportsStreaming", typeof(IMarketDataClient), capabilities.SupportsStreaming, providerType, registry.SupportedStreamingSources.Contains(provider.ProviderId, StringComparer.OrdinalIgnoreCase), "ProviderCapabilities + ProviderRegistry.RegisterStreamingFactory");
            ValidateCapabilityClaim(mismatches, provider.ProviderId, "supportsBackfill", typeof(IHistoricalDataProvider), capabilities.SupportsBackfill, providerType, true, "ProviderCapabilities + ProviderFactory.CreateBackfillProviders");
            ValidateCapabilityClaim(mismatches, provider.ProviderId, "supportsSymbolSearch", typeof(ISymbolSearchProvider), capabilities.SupportsSymbolSearch, providerType, registry.GetProviders<ISymbolSearchProvider>().Any(p => string.Equals(p.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase)), "ProviderCapabilities + ProviderFactory.CreateSymbolSearchProviders");
            ValidateCapabilityClaim(mismatches, provider.ProviderId, "supportsCorporateActions", typeof(ICorporateActionProvider), providerType.GetInterfaces().Contains(typeof(ICorporateActionProvider)), providerType, serviceProvider.GetServices<ICorporateActionProvider>().Any(p => string.Equals(p.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase)), "Implemented contract + DI service registration");
        }

        if (mismatches.Count != 0)
        {
            var diagnostic = new StringBuilder()
                .AppendLine("Provider capability/contract/registration mismatches:")
                .AppendLine(string.Join(Environment.NewLine, mismatches))
                .ToString();

            Assert.Fail(diagnostic);
        }

        File.Delete(configPath);
    }

    private static void ValidateCapabilityClaim(
        List<string> mismatches,
        string providerId,
        string claimedCapability,
        Type expectedInterface,
        bool claimed,
        Type implementationType,
        bool isRegistered,
        string registrationSource)
    {
        if (!claimed)
        {
            return;
        }

        var implementsContract = expectedInterface.IsAssignableFrom(implementationType);
        if (implementsContract && isRegistered)
        {
            return;
        }

        mismatches.Add($"providerId={providerId}; claimedCapability={claimedCapability}; expectedInterface={expectedInterface.Name}; implementationType={implementationType.FullName}; implementsExpectedInterface={implementsContract}; registrationSource={registrationSource}; registrationPresent={isRegistered}");
    }

    private static class TestConfigWriter
    {
        public static string WriteTempConfig(AppConfig config)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(config, AppConfigJsonOptions.Write));
            return path;
        }
    }
}
