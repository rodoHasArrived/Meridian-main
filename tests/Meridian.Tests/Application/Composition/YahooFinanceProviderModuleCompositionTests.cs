using System.Net;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

// Provider composition installs process-wide catalog callbacks and the HTTP factory is static.
[Collection("Sequential")]
public sealed class YahooFinanceProviderModuleCompositionTests : IDisposable
{
    [Fact]
    public async Task Default_configuration_uses_module_singleton_without_network_until_a_read()
    {
        var services = CreateServices(new AppConfig());
        var requests = CaptureHttp(services);
        var registrationCount = services.Count;
        await using var provider = services.BuildServiceProvider();
        HttpClientFactoryProvider.Initialize(provider);
        using var catalogLease = ProviderCatalogTestLease.Capture(provider);

        var registry = provider.GetRequiredService<ProviderRegistry>();
        var historical = registry.GetCapability<IHistoricalDataProvider>(" YAHOO ");
        var defaults = new YahooFinanceConfig();
        historical.Should().BeOfType<YahooFinanceHistoricalDataProvider>();
        historical.Should().BeSameAs(provider.GetRequiredService<YahooFinanceHistoricalDataProvider>());
        registry.GetProviders<IHistoricalDataProvider>().Should().ContainSingle(item => item.Name == "yahoo");
        historical.Should().BeAssignableTo<IHistoricalAggregateBarProvider>();
        historical!.Priority.Should().Be(defaults.Priority);
        historical.MaxRequestsPerWindow.Should().Be(defaults.RateLimitPerHour);
        ((YahooFinanceHistoricalDataProvider)historical).GetRateLimitInfo().MaxRequests
            .Should().Be(defaults.RateLimitPerHour);
        historical.SupportsIntraday.Should().BeTrue();
        historical.SupportedMarkets.Should().Contain(["US", "UK", "DE", "JP", "CA", "AU", "HK", "SG"]);
        var inventory = provider.GetRequiredService<DataSourceRegistry>().ModuleCapabilityRegistrations
            .Where(item => item.ProviderId == "yahoo").ToArray();
        inventory.Should().ContainSingle(item => item.Contract == typeof(IHistoricalDataProvider)
            && item.ImplementationType == typeof(YahooFinanceHistoricalDataProvider) && item.IsConfigured != null);
        YahooFinanceProviderModule.Descriptor.FactoryOwner.Should().Be(ProviderCapabilityFactoryOwner.Module);
        YahooFinanceProviderModule.Descriptor.Registrations().Should().BeEmpty();

        var first = await provider.InitializeProvidersAsync();
        var second = await provider.InitializeProvidersAsync();
        first.BackfillProviders.Should().Contain("yahoo");
        second.Should().BeEquivalentTo(first);
        services.Count.Should().Be(registrationCount);
        requests.Should().BeEmpty("registration and initialization must not call Yahoo");

        await registry.DisposeAsync();
        var bars = await historical.GetDailyBarsAsync("AAPL", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));
        bars.Should().BeEmpty();
        requests.Should().ContainSingle("the container still owns the adapter after registry disposal");

        await provider.DisposeAsync();
        var readAfterDispose = () => historical.GetDailyBarsAsync("AAPL", null, null);
        await readAfterDispose.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("family")]
    [InlineData("backfill")]
    public async Task Disabled_configuration_keeps_runtime_absent_and_declared_inventory_visible(string disabled)
    {
        var services = CreateServices(CreateConfig(familyEnabled: disabled != "family", backfillEnabled: disabled != "backfill"));
        await using var provider = services.BuildServiceProvider();
        using var catalogLease = ProviderCatalogTestLease.Capture(provider);
        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IHistoricalDataProvider>("yahoo").Should().BeNull();
        registry.GetProviders<IHistoricalDataProvider>().Should().NotContain(item => item.Name == "yahoo");
        var catalog = provider.GetRequiredService<IProviderCatalog>().Get("yahoo");
        catalog.Should().NotBeNull();
        catalog!.Capabilities.SupportsBackfill.Should().BeTrue();
        catalog.Capabilities.SupportsStreaming.Should().BeFalse();
        catalog.Capabilities.SupportsSymbolSearch.Should().BeFalse();
        catalog.RequiresCredentials.Should().BeFalse();

        if (disabled == "family")
            provider.GetService<YahooFinanceHistoricalDataProvider>().Should().BeNull();
        else
        {
            var resolve = () => provider.GetRequiredService<YahooFinanceHistoricalDataProvider>();
            resolve.Should().Throw<InvalidOperationException>().WithMessage("*no configured historical capability*");
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Availability_uses_final_host_backfill_configuration(bool registeredEnabled, bool runtimeEnabled)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderCredentialResolver>(new NoCredentials());
        services.AddProviderServices(CreateConfig(backfillEnabled: registeredEnabled),
            _ => CreateConfig(backfillEnabled: runtimeEnabled));
        await using var provider = services.BuildServiceProvider();
        using var catalogLease = ProviderCatalogTestLease.Capture(provider);

        var historical = provider.GetRequiredService<ProviderRegistry>().GetCapability<IHistoricalDataProvider>("yahoo");

        if (runtimeEnabled)
        {
            historical.Should().BeSameAs(provider.GetRequiredService<YahooFinanceHistoricalDataProvider>());
            historical!.Priority.Should().Be(99);
            historical.MaxRequestsPerWindow.Should().Be(42);
            ((YahooFinanceHistoricalDataProvider)historical).GetRateLimitInfo().MaxRequests.Should().Be(42);
        }
        else
            historical.Should().BeNull();
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("configured", true)]
    [InlineData("family-disabled", false)]
    [InlineData("backfill-disabled", false)]
    public void Standalone_factory_preserves_configuration_and_adapter_defaults(string scenario, bool expected)
    {
        var config = scenario == "default" ? new AppConfig()
            : CreateConfig(familyEnabled: scenario != "family-disabled", backfillEnabled: scenario != "backfill-disabled");
        var providers = new ProviderFactory(config, new NoCredentials()).CreateBackfillProviders();
        try
        {
            var yahoo = providers.Where(item => item.Name == "yahoo").ToArray();
            yahoo.Should().HaveCount(expected ? 1 : 0);
            if (expected)
            {
                yahoo[0].Should().BeOfType<YahooFinanceHistoricalDataProvider>();
                var defaults = new YahooFinanceConfig();
                var expectedPriority = scenario == "configured" ? 99 : defaults.Priority;
                var expectedHourlyLimit = scenario == "configured" ? 42 : defaults.RateLimitPerHour;
                yahoo[0].Priority.Should().Be(expectedPriority);
                yahoo[0].MaxRequestsPerWindow.Should().Be(expectedHourlyLimit);
                ((YahooFinanceHistoricalDataProvider)yahoo[0]).GetRateLimitInfo().MaxRequests
                    .Should().Be(expectedHourlyLimit);
                yahoo[0].Should().BeAssignableTo<IHistoricalAggregateBarProvider>();
            }
        }
        finally
        {
            foreach (var historical in providers)
                historical.Dispose();
        }
    }

    private static AppConfig CreateConfig(bool familyEnabled = true, bool backfillEnabled = true)
        => new(Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
                Yahoo: new YahooFinanceConfig(Enabled: backfillEnabled, Priority: 99, RateLimitPerHour: 42))),
            ProviderModules: new ProviderModulesConfig(new()
            {
                [" YAHOO "] = new(Enabled: familyEnabled)
            }));

    private static ServiceCollection CreateServices(AppConfig config)
    {
        var services = new ServiceCollection();
        services.AddProviderServices(config, new NoCredentials());
        return services;
    }

    private static List<Uri> CaptureHttp(IServiceCollection services)
    {
        var requests = new List<Uri>();
        services.AddHttpClient(HttpClientNames.YahooFinanceHistorical)
            .ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(requests));
        return requests;
    }

    private sealed class CaptureHandler(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"chart\":{\"result\":[],\"error\":null}}")
            });
        }
    }

    private sealed class NoCredentials : IProviderCredentialResolver, ICredentialContext
    {
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null) => this;
        public string? Get(string name) => null;
        public bool IsConfigured(string name) => false;
    }

    public void Dispose() => HttpClientFactoryProvider.Reset();
}
