using FluentAssertions;
using Meridian.Application.Services;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class ProviderFactoryCredentialIsolationTests
{
    [Theory]
    [InlineData("unspecified", "paper")]
    [InlineData("paper", "paper")]
    [InlineData("live", "live")]
    public async Task BrokerageFactory_EnvironmentResolverCapturesTradingEnvironment(string? configuredEnvironment, string expectedEnvironment)
    {
        using var ambient = AmbientCredentials.Alpaca();
        Environment.SetEnvironmentVariable("ALPACA_TRADING_ENVIRONMENT", configuredEnvironment);
        using var handler = new RecordingHandler();
        using var services = CreateServices(handler);
        var config = Config() with { Alpaca = new AlpacaOptions(UseSandbox: expectedEnvironment == "live") };
        var factory = new ProviderFactory(config, new EnvironmentCredentialResolver(), services: services);
        await using var gateway = (AlpacaBrokerageGateway)factory.CreateAlpacaBrokerageGateway();
        Environment.SetEnvironmentVariable("ALPACA_TRADING_ENVIRONMENT", expectedEnvironment == "paper" ? "live" : "paper");

        await gateway.GetAccountInfoAsync();

        handler.Requests.Should().ContainSingle().Which.Uri.Host.Should().Be(
            expectedEnvironment == "paper" ? "paper-api.alpaca.markets" : "api.alpaca.markets");
        handler.Requests.Single().Key.Should().Be("ambient-key");
    }

    [Theory]
    [InlineData(false, "paper")]
    [InlineData(true, "paper")]
    [InlineData(false, "live")]
    [InlineData(true, "live")]
    public async Task BrokerageFactory_SelectedRecordOwnsTradingEnvironment(bool scoped, string tradingEnvironment)
    {
        using var ambient = AmbientCredentials.Alpaca();
        Environment.SetEnvironmentVariable("ALPACA_TRADING_ENVIRONMENT", tradingEnvironment == "paper" ? "live" : "paper");
        var store = new FileProviderCredentialStore(_root, _ => null);
        var scope = new ProviderCredentialScope(Owner.TenantId, Owner.ConnectionId, Owner.ExternalAccountId, tradingEnvironment);
        var request = Request("owned-key", "owned-secret") with { Environment = tradingEnvironment };
        if (scoped)
            await store.SaveScopedAsync(request, scope);
        else
            await store.SaveAsync(request);
        IProviderCredentialResolver resolver = scoped
            ? new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), scope)
            : new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        using var handler = new RecordingHandler();
        using var services = CreateServices(handler);
        var config = Config() with { Alpaca = new AlpacaOptions(UseSandbox: tradingEnvironment == "live") };
        var factory = new ProviderFactory(config, resolver, services: services);
        await using var gateway = (AlpacaBrokerageGateway)factory.CreateAlpacaBrokerageGateway();

        await gateway.GetAccountInfoAsync();

        handler.Requests.Should().ContainSingle().Which.Uri.Host.Should().Be(
            tradingEnvironment == "paper" ? "paper-api.alpaca.markets" : "api.alpaca.markets");
        handler.Requests.Single().Key.Should().Be("owned-key");
        handler.Requests.Single().Secret.Should().Be("owned-secret");
        gateway.TradeUpdatesClient!.ConfigureDurableStateScope("owned-account", tradingEnvironment);
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", "changed-ambient-key");
        var rescope = () => gateway.TradeUpdatesClient.ConfigureDurableStateScope("owned-account", tradingEnvironment);
        rescope.Should().NotThrow("the execution stream retains the same selected credentials as its gateway");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ComposedFactory_SettingsOnlyModulePreservesResolvedCredentials(bool stored, bool emptyMapping)
    {
        using var environment = AmbientCredentials.Alpaca();
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveScopedAsync(Request("owned-key", "owned-secret"), Owner);
        IProviderCredentialResolver resolver = stored
            ? new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Owner)
            : new EnvironmentCredentialResolver();
        using var handler = new RecordingHandler();
        var config = Config() with
        {
            ProviderModules = new ProviderModulesConfig(new()
            {
                ["alpaca"] = new(Credentials: emptyMapping ? new() : null, Settings: new() { ["feed"] = "iex" })
            })
        };
        await using var services = CreateServiceCollection(handler).AddProviderServices(config, resolver).BuildServiceProvider();
        var factory = services.GetRequiredService<ProviderFactory>();
        using var historical = (IDisposable)factory.CreateAlpacaBackfillProvider(null)!;
        historical.Should().NotBeNull();
        await using var gateway = (AlpacaBrokerageGateway)factory.CreateAlpacaBrokerageGateway();

        await gateway.GetAccountInfoAsync();

        handler.Requests.Should().ContainSingle().Which.Key.Should().Be(stored ? "owned-key" : "ambient-key");
        handler.Requests.Single().Secret.Should().Be(stored ? "owned-secret" : "ambient-secret");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("partial")]
    [InlineData("cleared")]
    public async Task ComposedFactory_SelectedSidecarDoesNotBorrowEnvironmentMappings(string state)
    {
        using var environment = AmbientCredentials.Alpaca();
        var sidecar = new Dictionary<string, string>();
        if (state != "empty")
            sidecar["keyId"] = "sidecar-key";
        if (state == "cleared")
            sidecar["secretKey"] = "";
        using var handler = new RecordingHandler();
        await using var services = CreateServiceCollection(handler)
            .AddProviderServices(ConfigWithModuleCredentials(), new EnvironmentCredentialResolver(), sidecars: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["alpaca"] = sidecar
            }).BuildServiceProvider();
        var factory = services.GetRequiredService<ProviderFactory>();

        factory.CreateAlpacaBackfillProvider(null).Should().BeNull();
        await using var gateway = (AlpacaBrokerageGateway)factory.CreateAlpacaBrokerageGateway();
        var read = () => gateway.GetAccountInfoAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("partial")]
    [InlineData("deleted")]
    public async Task ComposedFactory_ScopedVaultCannotBeReplacedByModuleRecord(string state)
    {
        using var environment = AmbientCredentials.Alpaca();
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveScopedAsync(Request("owned-key", state == "partial" ? "" : "owned-secret"), Owner);
        if (state == "deleted")
            await store.DeleteScopedAsync("alpaca", Owner);
        using var handler = new RecordingHandler();
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Owner);
        await using var services = CreateServiceCollection(handler)
            .AddProviderServices(ConfigWithModuleCredentials(), resolver, sidecars: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["alpaca"] = new Dictionary<string, string> { ["keyId"] = "module-key", ["secretKey"] = "module-secret" }
            }).BuildServiceProvider();
        var factory = services.GetRequiredService<ProviderFactory>();
        await using var gateway = (AlpacaBrokerageGateway)factory.CreateAlpacaBrokerageGateway();

        if (state == "complete")
        {
            await gateway.GetAccountInfoAsync();
            handler.Requests.Should().ContainSingle().Which.Key.Should().Be("owned-key");
            handler.Requests.Single().Secret.Should().Be("owned-secret");
        }
        else
        {
            factory.CreateAlpacaBackfillProvider(null).Should().BeNull();
            var read = () => gateway.GetAccountInfoAsync();
            await read.Should().ThrowAsync<InvalidOperationException>();
            handler.Requests.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cleared")]
    [InlineData("deleted")]
    public async Task RobinhoodFactory_SelectedRecordCannotReviveAmbientToken(string state)
    {
        using var environment = new AmbientCredentials(new() { ["ROBINHOOD_ACCESS_TOKEN"] = "ambient-token" });
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveScopedAsync(new("robinhood", new Dictionary<string, string?> { ["AccessToken"] = "owned-token" }, "paper"), Owner);
        if (state == "deleted")
            await store.DeleteScopedAsync("robinhood", Owner);
        else if (state == "cleared")
            await store.SaveScopedAsync(new("robinhood", new Dictionary<string, string?> { ["AccessToken"] = "" }, "paper"), Owner);
        using var handler = new RecordingHandler();
        using var services = CreateServices(handler);
        var factory = new ProviderFactory(Config(), new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Owner), services: services);

        await using var streaming = factory.CreateRobinhoodStreamingClient();
        streaming.IsEnabled.Should().Be(state == "complete");
        var options = factory.CreateRobinhoodOptionsProvider();
        await options.GetExpirationsAsync("AAPL");
        await using var brokerage = factory.CreateRobinhoodBrokerageGateway();
        await brokerage.GetAccountInfoAsync();

        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().OnlyContain(request => state == "complete"
            ? request.BearerToken == "owned-token" : string.IsNullOrEmpty(request.BearerToken));
    }

    private static AppConfig ConfigWithModuleCredentials() => Config() with
    {
        ProviderModules = new ProviderModulesConfig(new()
        {
            ["alpaca"] = new(Credentials: new()
            {
                ["keyId"] = "ALPACA_KEY_ID",
                ["secretKey"] = "ALPACA_SECRET_KEY"
            })
        })
    };
}
