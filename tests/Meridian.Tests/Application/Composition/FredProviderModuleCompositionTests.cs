using System.Net;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Tests.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class FredProviderModuleCompositionTests : IDisposable
{
    private const string MappedKeyVariable = "MERIDIAN_TEST_FRED_MODULE_KEY";
    private readonly Dictionary<string, string?> _originalEnvironment = new();
    private readonly string _credentialRoot = Path.Combine(Path.GetTempPath(), $"fred-module-{Guid.NewGuid():N}");

    public FredProviderModuleCompositionTests()
    {
        foreach (var name in new[] { "FRED_API_KEY", "FRED__APIKEY", "MDC_FRED_API_KEY", MappedKeyVariable })
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task Module_owns_both_factories_and_reuses_container_singletons_without_network_at_startup()
    {
        var services = CreateServices(CreateConfig(new FredConfig(ApiKey: "configured-key")));
        var requests = CaptureHttp(services);
        var registrationCount = services.Count;
        await using var provider = services.BuildServiceProvider();
        HttpClientFactoryProvider.Initialize(provider);

        var registry = provider.GetRequiredService<ProviderRegistry>();
        var historical = registry.GetCapability<IHistoricalDataProvider>(" FRED-SYMBOLS ");
        var search = registry.GetCapability<ISymbolSearchProvider>(" FRED ");
        historical.Should().BeSameAs(provider.GetRequiredService<FredHistoricalDataProvider>());
        search.Should().BeSameAs(provider.GetRequiredService<FredSymbolSearchProvider>());
        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeSameAs(historical);
        registry.GetCapability<ISymbolSearchProvider>("fred").Should().BeSameAs(search);
        registry.GetProviders<IProviderMetadata>().Should().Contain(historical!).And.Contain(search!);
        registry.GetCapability<IProviderMetadata>("fred").Should().NotBeNull();
        var registrations = provider.GetRequiredService<DataSourceRegistry>().ModuleCapabilityRegistrations;
        registrations.Where(registration => registration.ProviderId == "fred")
            .Select(registration => registration.Contract).Should().BeEquivalentTo(
                [typeof(IHistoricalDataProvider), typeof(ISymbolSearchProvider)]);
        registrations.Should().OnlyContain(registration => registration.IsConfigured != null);
        FredProviderModule.Descriptor.FactoryOwner.Should().Be(ProviderCapabilityFactoryOwner.Module);
        FredProviderModule.Descriptor.Registrations().Should().BeEmpty(
            "hosted composition must not recreate a skipped or failed module through descriptor factories");
        requests.Should().BeEmpty("registration and runtime resolution must not contact FRED");
        services.Count.Should().Be(registrationCount);

        var first = await provider.InitializeProvidersAsync();
        var second = await provider.InitializeProvidersAsync();
        first.BackfillProviders.Should().Contain("fred");
        first.SymbolSearchProviders.Should().Contain("fred");
        second.Should().BeEquivalentTo(first);
        provider.GetRequiredService<FredHistoricalDataProvider>().Should().BeSameAs(historical);

        await registry.DisposeAsync();
        await historical!.GetDailyBarsAsync("GDP", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));
        requests.Should().ContainSingle("the container retains lifetime ownership when the registry is disposed");
        await provider.DisposeAsync();
        var readAfterDispose = () => historical.GetDailyBarsAsync("GDP", null, null);
        await readAfterDispose.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("family-disabled")]
    [InlineData("backfill-disabled")]
    [InlineData("missing-key")]
    public async Task Unconfigured_capabilities_remain_absent_while_catalog_retains_declared_inventory(string scenario)
    {
        var config = CreateConfig(new FredConfig(Enabled: scenario != "backfill-disabled",
            ApiKey: scenario == "missing-key" ? null : "configured-key"), familyEnabled: scenario != "family-disabled");
        var services = CreateServices(config);
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeNull();
        registry.GetCapability<ISymbolSearchProvider>("fred-symbols").Should().BeNull();
        registry.GetProviders<IHistoricalDataProvider>().Should().NotContain(item => item.Name == "fred");
        registry.GetProviders<ISymbolSearchProvider>().Should().NotContain(item => item.Name == "fred");
        var catalog = provider.GetRequiredService<Meridian.Contracts.Api.IProviderCatalog>().Get("fred");
        catalog.Should().NotBeNull();
        catalog!.Capabilities.SupportsBackfill.Should().BeTrue();
        catalog.Capabilities.SupportsSymbolSearch.Should().BeTrue();
        catalog.RequiresCredentials.Should().BeTrue();
        catalog.CredentialFields.Should().Contain(field => field.Name == "FRED_API_KEY" && field.Required);
        catalog.Capabilities.MarketDataCapabilities.Should().BeEmpty();
        if (scenario == "family-disabled")
        {
            provider.GetService<FredHistoricalDataProvider>().Should().BeNull();
            provider.GetService<FredSymbolSearchProvider>().Should().BeNull();
        }
        else
        {
            var resolve = () => provider.GetRequiredService<FredHistoricalDataProvider>();
            resolve.Should().Throw<InvalidOperationException>().WithMessage("*no configured historical capability*");
        }
    }

    [Fact]
    public async Task Absent_backfill_configuration_preserves_search_default_and_historical_opt_in()
    {
        Environment.SetEnvironmentVariable("FRED_API_KEY", "environment-key");
        var services = CreateServices(CreateConfig(fred: null));
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeNull();
        registry.GetCapability<ISymbolSearchProvider>("fred").Should().BeOfType<FredSymbolSearchProvider>();
    }

    [Fact]
    public async Task Final_credential_resolver_is_evaluated_after_registration_without_building_a_second_container()
    {
        var config = CreateConfig(new FredConfig());
        var services = CreateServices(config);
        var resolver = new DeferredCredentialResolver();
        services.AddSingleton<IProviderCredentialResolver>(resolver);
        resolver.CallCount.Should().Be(0);
        resolver.Enabled = true;

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        resolver.CallCount.Should().BeGreaterThan(0);
        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeOfType<FredHistoricalDataProvider>();
        registry.GetCapability<ISymbolSearchProvider>("fred").Should().BeOfType<FredSymbolSearchProvider>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Fred_credentials_do_not_fall_back_or_prevent_other_providers_from_starting(bool sidecarPresent)
    {
        var services = new ServiceCollection();
        services.AddProviderServices(CreateConfig(new FredConfig(ApiKey: "must-not-fall-back")),
            new FailingFredCredentialResolver(), sidecars: sidecarPresent ? FredSidecar() : null);
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeNull();
        registry.GetCapability<ISymbolSearchProvider>("fred").Should().BeNull();
        registry.GetCapability<IHistoricalDataProvider>("yahoo").Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Credentials_lost_between_gate_and_construction_leave_no_partial_factory(bool throwOnSecondRead)
    {
        var services = CreateServices(CreateConfig(new FredConfig(ApiKey: "must-not-fall-back")));
        services.AddSingleton<IProviderCredentialResolver>(new DisappearingFredCredentialResolver(throwOnSecondRead));
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeNull();
        registry.GetCapability<ISymbolSearchProvider>("fred").Should().BeNull();
        registry.GetProviders<IHistoricalDataProvider>().Should().NotContain(item => item.Name == "fred");
        registry.GetCapability<IHistoricalDataProvider>("yahoo").Should().NotBeNull();
    }

    [Theory]
    [InlineData("sidecar", "sidecar-key")]
    [InlineData("module-environment", "mapped-key")]
    [InlineData("stored", "stored-key")]
    [InlineData("stored-over-sidecar", "stored-key")]
    [InlineData("stored-over-module-environment", "stored-key")]
    [InlineData("environment", "environment-key")]
    [InlineData("config", "configured-key")]
    public async Task Both_capabilities_retain_credential_precedence_on_the_wire(string source, string expectedKey)
    {
        if (source != "config")
            Environment.SetEnvironmentVariable("FRED_API_KEY", "environment-key");
        if (source is "sidecar" or "module-environment" or "stored-over-sidecar" or "stored-over-module-environment")
            Environment.SetEnvironmentVariable(MappedKeyVariable, "mapped-key");
        var config = CreateConfig(new FredConfig(ApiKey: "configured-key"),
            credentialMapping: source is "module-environment" or "stored-over-module-environment" ? MappedKeyVariable : null);
        var services = new ServiceCollection();
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? sidecars = source is "sidecar" or "stored-over-sidecar"
            ? FredSidecar()
            : null;
        IProviderCredentialResolver resolver = new EnvironmentCredentialResolver();
        if (source.StartsWith("stored", StringComparison.Ordinal))
        {
            var store = new FileProviderCredentialStore(_credentialRoot);
            await store.SaveAsync(new ProviderCredentialSaveRequest("fred",
                new Dictionary<string, string?> { ["ApiKey"] = "stored-key" }, Actor: "test"));
            resolver = new StoredProviderCredentialResolver(store, resolver);
        }
        services.AddProviderServices(config, resolver, sidecars: sidecars);
        var requests = CaptureHttp(services);
        await using var provider = services.BuildServiceProvider();
        HttpClientFactoryProvider.Initialize(provider);
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var historical = registry.GetCapability<IHistoricalDataProvider>("fred")!;
        var search = registry.GetCapability<ISymbolSearchProvider>("fred")!;

        await historical.GetDailyBarsAsync("GDP", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));
        await search.SearchAsync("GDP");

        requests.Should().HaveCount(2);
        requests.Should().OnlyContain(uri => uri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Contains($"api_key={expectedKey}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_empty_credentials_cannot_borrow_module_or_environment_keys(bool scoped)
    {
        Environment.SetEnvironmentVariable("FRED_API_KEY", "environment-key");
        Environment.SetEnvironmentVariable(MappedKeyVariable, "mapped-key");
        var store = new FileProviderCredentialStore(_credentialRoot);
        var empty = new ProviderCredentialSaveRequest("fred", new Dictionary<string, string?>(), Actor: "test");
        IProviderCredentialResolver resolver;
        if (scoped)
        {
            await store.SaveAsync(empty with { Credentials = new Dictionary<string, string?> { ["ApiKey"] = "other-owner-key" } });
            var scope = new ProviderCredentialScope("tenant-a", "fred-connection", "fred-account", "production");
            await store.SaveScopedAsync(empty, scope);
            resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), scope);
        }
        else
        {
            await store.SaveAsync(empty);
            resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        }
        var services = new ServiceCollection();
        services.AddProviderServices(CreateConfig(new FredConfig(ApiKey: "configured-key"),
            credentialMapping: MappedKeyVariable), resolver, sidecars: FredSidecar());
        var requests = CaptureHttp(services);
        await using var provider = services.BuildServiceProvider();
        HttpClientFactoryProvider.Initialize(provider);

        var registry = provider.GetRequiredService<ProviderRegistry>();

        registry.GetCapability<IHistoricalDataProvider>("fred").Should().BeNull();
        registry.GetCapability<ISymbolSearchProvider>("fred").Should().BeNull();
        requests.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FredSidecar() =>
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["fred-symbols"] = new Dictionary<string, string> { ["apiKey"] = "sidecar-key" }
        };

    private static AppConfig CreateConfig(FredConfig? fred, bool familyEnabled = true, string? credentialMapping = null)
        => new(Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(Fred: fred)),
            ProviderModules: new ProviderModulesConfig(new()
            {
                [" FRED-SYMBOLS "] = new(Enabled: familyEnabled,
                    Credentials: credentialMapping is null ? null : new() { ["apiKey"] = credentialMapping })
            }));

    private static ServiceCollection CreateServices(AppConfig config)
    {
        var services = new ServiceCollection();
        services.AddProviderServices(config, new EnvironmentCredentialResolver());
        return services;
    }

    private static List<Uri> CaptureHttp(IServiceCollection services)
    {
        var requests = new List<Uri>();
        foreach (var name in new[] { HttpClientNames.FredHistorical, HttpClientNames.FredSymbolSearch })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(requests));
        return requests;
    }

    private sealed class CaptureHandler(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"observations\":[],\"seriess\":[]}")
            });
        }
    }

    private sealed class DeferredCredentialResolver : IProviderCredentialResolver, ICredentialContext
    {
        public bool Enabled { get; set; }
        public int CallCount { get; private set; }
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
        {
            CallCount++;
            return this;
        }
        public string? Get(string name) => Enabled && name == "FRED_API_KEY" ? "deferred-key" : null;
        public bool IsConfigured(string name) => Get(name) is not null;
    }

    private sealed class FailingFredCredentialResolver : IProviderCredentialResolver
    {
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
            => providerType == typeof(FredHistoricalDataProvider)
                ? throw new IOException("Credential record cannot be read.")
                : new EnvironmentCredentialResolver().CreateContext(providerType, configuredValues);
    }

    private sealed class DisappearingFredCredentialResolver(bool throwOnSecondRead) : IProviderCredentialResolver
    {
        private int _fredReads;
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
        {
            if (providerType != typeof(FredHistoricalDataProvider))
                return new EnvironmentCredentialResolver().CreateContext(providerType, configuredValues);
            if (++_fredReads > 1 && throwOnSecondRead)
                throw new IOException("Credential record disappeared after configuration.");
            return new FredCredentialContext(_fredReads == 1 ? "initial-key" : null);
        }
    }

    private sealed class FredCredentialContext(string? value) : ICredentialContext
    {
        public string? Get(string name) => name == "FRED_API_KEY" ? value : null;
        public bool IsConfigured(string name) => Get(name) is not null;
    }

    public void Dispose()
    {
        foreach (var (name, value) in _originalEnvironment)
            Environment.SetEnvironmentVariable(name, value);
        HttpClientFactoryProvider.Reset();
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;
        if (Directory.Exists(_credentialRoot))
            Directory.Delete(_credentialRoot, recursive: true);
    }
}
