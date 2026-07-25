using System.Net.Http;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Execution;

/// <summary>
/// Scenario coverage for hosted brokerage gateway registration, guarding against mapper-only broker assets being promoted as runtime gateways.
/// </summary>
public sealed class HostedBrokerageGatewayRegistrationTests
{
    [Fact]
    public async Task AddHostedBrokerageGateways_RegistersProductionRuntimeGatewaySurfaces()
    {
        var services = CreateServices();

        services.AddHostedBrokerageGateways();

        await using var provider = services.BuildServiceProvider();
        var alpaca = provider.GetRequiredKeyedService<IBrokerageGateway>("alpaca");
        var ib = provider.GetRequiredKeyedService<IBrokerageGateway>("ib");
        var ibkr = provider.GetRequiredKeyedService<IBrokerageGateway>("ibkr");
        var robinhood = provider.GetRequiredKeyedService<IBrokerageGateway>("robinhood");

        alpaca.Should().BeOfType<AlpacaBrokerageGateway>();
        ib.Should().BeOfType<IBBrokerageGateway>();
        ibkr.Should().BeSameAs(ib);
        robinhood.Should().BeOfType<RobinhoodBrokerageGateway>();

        provider.GetServices<IBrokerageAccountCatalog>()
            .Select(catalog => catalog.ProviderId)
            .Should()
            .Contain(["alpaca", "ibkr", "robinhood"]);
        provider.GetServices<IBrokeragePortfolioSync>()
            .Select(sync => sync.ProviderId)
            .Should()
            .Contain(["alpaca", "ibkr", "robinhood"]);
        provider.GetServices<IBrokerageActivitySync>()
            .Select(sync => sync.ProviderId)
            .Should()
            .Contain(["alpaca", "ibkr", "robinhood"]);

        var surfaces = HostedBrokerageGatewayRuntimeSurfaceCatalog.Build(provider);
        surfaces.Should().Contain(surface =>
            surface.GatewayId == "alpaca" &&
            surface.IsRegistered &&
            surface.DeclaredGatewayId == "alpaca" &&
            surface.GatewayType == typeof(AlpacaBrokerageGateway).FullName &&
            surface.GatewayIdMatchesRuntimeKey &&
            surface.SupportsAccountCatalog &&
            surface.SupportsPortfolioSync &&
            surface.SupportsActivitySync &&
            surface.SupportsPartialFills &&
            surface.ValidationIssues.Count == 0);
        surfaces.Should().Contain(surface =>
            surface.GatewayId == "ibkr" &&
            surface.IsRegistered &&
            surface.DeclaredGatewayId == "ibkr" &&
            surface.GatewayType == typeof(IBBrokerageGateway).FullName &&
            surface.GatewayIdMatchesRuntimeKey &&
            surface.SupportsAccountCatalog &&
            surface.SupportsPortfolioSync &&
            surface.SupportsActivitySync &&
            surface.Notes.Any(note => note.Contains("canonical runtime key", StringComparison.OrdinalIgnoreCase)) &&
            surface.ValidationIssues.Count == 0);
        surfaces.Should().Contain(surface =>
            surface.GatewayId == "ib" &&
            surface.IsRegistered &&
            surface.DeclaredGatewayId == "ibkr" &&
            surface.GatewayType == typeof(IBBrokerageGateway).FullName &&
            surface.GatewayIdMatchesRuntimeKey &&
            surface.SupportsAccountCatalog &&
            surface.SupportsPortfolioSync &&
            surface.SupportsActivitySync &&
            surface.Notes.Any(note => note.Contains("compatibility alias", StringComparison.OrdinalIgnoreCase)) &&
            surface.ValidationIssues.Count == 0);
        surfaces.Should().Contain(surface =>
            surface.GatewayId == "robinhood" &&
            surface.IsRegistered &&
            surface.DeclaredGatewayId == "robinhood" &&
            surface.GatewayType == typeof(RobinhoodBrokerageGateway).FullName &&
            surface.GatewayIdMatchesRuntimeKey &&
            surface.SupportsAccountCatalog &&
            surface.SupportsPortfolioSync &&
            surface.SupportsActivitySync &&
            !surface.SupportsOrderModification &&
            surface.SupportsPartialFills &&
            surface.ValidationIssues.Count == 0);
        surfaces.Should().Contain(surface =>
            surface.GatewayId == "stocksharp" &&
            !surface.IsRegistered &&
            !surface.GatewayIdMatchesRuntimeKey &&
            surface.ValidationIssues.Contains("stocksharp-runtime-type-missing") &&
            surface.Notes.Any(note => note.Contains("runtime type is not present", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AddHostedBrokerageGateways_WhenCalledTwice_DoesNotDuplicateGatewaySurfaces()
    {
        var services = CreateServices();

        services.AddHostedBrokerageGateways();
        services.AddHostedBrokerageGateways();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IBrokerageGateway>("ib")
            .Should()
            .BeSameAs(provider.GetRequiredKeyedService<IBrokerageGateway>("ibkr"));
        provider.GetServices<IBrokerageAccountCatalog>()
            .Count(catalog => catalog.ProviderId == "ibkr")
            .Should()
            .Be(1);
        provider.GetServices<IBrokeragePortfolioSync>()
            .Count(sync => sync.ProviderId == "ibkr")
            .Should()
            .Be(1);
        provider.GetServices<IBrokerageActivitySync>()
            .Count(sync => sync.ProviderId == "ibkr")
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task RegisterOptionalStockSharpGateway_WhenRuntimeTypeExists_RegistersKeyedGatewayAndSyncAdapters()
    {
        var services = CreateServices();

        HostedBrokerageGatewayServiceCollectionExtensions.RegisterOptionalStockSharpGateway(
            services,
            typeof(FakeStockSharpBrokerageGateway));

        await using var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredKeyedService<IBrokerageGateway>("stocksharp");

        gateway.Should().BeOfType<FakeStockSharpBrokerageGateway>();
        provider.GetServices<IBrokerageAccountCatalog>()
            .Select(catalog => catalog.ProviderId)
            .Should()
            .Contain("stocksharp");
        provider.GetServices<IBrokeragePortfolioSync>()
            .Select(sync => sync.ProviderId)
            .Should()
            .Contain("stocksharp");
        provider.GetServices<IBrokerageActivitySync>()
            .Select(sync => sync.ProviderId)
            .Should()
            .Contain("stocksharp");

        var surfaces = HostedBrokerageGatewayRuntimeSurfaceCatalog.Build(provider);
        surfaces.Should().Contain(surface =>
            surface.GatewayId == "stocksharp" &&
            surface.IsRegistered &&
            surface.DeclaredGatewayId == "stocksharp" &&
            surface.GatewayType == typeof(FakeStockSharpBrokerageGateway).FullName &&
            surface.GatewayIdMatchesRuntimeKey &&
            surface.SupportsAccountCatalog &&
            surface.SupportsPortfolioSync &&
            surface.SupportsActivitySync &&
            surface.ValidationIssues.Count == 0);
    }

    [Fact]
    public async Task RegisterOptionalStockSharpGateway_WhenRuntimeTypeMissing_DoesNotRegisterPlaceholder()
    {
        var services = CreateServices();

        HostedBrokerageGatewayServiceCollectionExtensions.RegisterOptionalStockSharpGateway(services, gatewayType: null);

        await using var provider = services.BuildServiceProvider();

        provider.GetKeyedService<IBrokerageGateway>("stocksharp").Should().BeNull();
        provider.GetServices<IBrokerageAccountCatalog>()
            .Should()
            .NotContain(catalog => catalog.ProviderId.Equals("stocksharp", StringComparison.OrdinalIgnoreCase));

        HostedBrokerageGatewayRuntimeSurfaceCatalog.Build(provider)
            .Should()
            .Contain(surface =>
                surface.GatewayId == "stocksharp" &&
                !surface.IsRegistered &&
                surface.ValidationIssues.Contains("stocksharp-runtime-type-missing"));
    }

    [Fact]
    public async Task Scenario_BrokerageExperimentGate_TradierAndTradeStationRemainUnregisteredUntilConcreteGatewaysExist()
    {
        var services = CreateServices();

        services.AddHostedBrokerageGateways();

        await using var provider = services.BuildServiceProvider();

        provider.GetKeyedService<IBrokerageGateway>("tradier").Should().BeNull();
        provider.GetKeyedService<IBrokerageGateway>("tradestation").Should().BeNull();
        provider.GetServices<IBrokerageAccountCatalog>()
            .Select(catalog => catalog.ProviderId)
            .Should()
            .NotContain(["tradier", "tradestation"]);
        provider.GetServices<IBrokeragePortfolioSync>()
            .Select(sync => sync.ProviderId)
            .Should()
            .NotContain(["tradier", "tradestation"]);

        HostedBrokerageGatewayRuntimeSurfaceCatalog.Build(provider)
            .Select(surface => surface.GatewayId)
            .Should()
            .Contain("robinhood")
            .And
            .NotContain(["tradier", "tradestation"]);
    }

    [Fact]
    public async Task RuntimeSurfaceCatalog_FlagsGatewayIdAndCapabilityDrift()
    {
        var services = CreateServices();
        services.AddBrokerageGateway("alpaca", _ => new DriftedBrokerageGateway());

        await using var provider = services.BuildServiceProvider();

        var surface = HostedBrokerageGatewayRuntimeSurfaceCatalog.Build(provider)
            .Single(surface => surface.GatewayId == "alpaca");

        surface.IsRegistered.Should().BeTrue();
        surface.DeclaredGatewayId.Should().Be("wrong-alpaca");
        surface.GatewayIdMatchesRuntimeKey.Should().BeFalse();
        surface.ValidationIssues.Should().Contain([
            "gateway-id-mismatch:wrong-alpaca",
            "account-catalog-missing",
            "portfolio-sync-missing",
            "activity-sync-missing",
            "order-types-empty",
            "time-in-force-empty",
            "asset-classes-empty"]);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory());
        return services;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeStockSharpBrokerageGateway : IBrokerageGateway
    {
        public string GatewayId => "stocksharp";

        public bool IsConnected => false;

        public string BrokerDisplayName => "StockSharp";

        public BrokerageCapabilities BrokerageCapabilities { get; } = BrokerageCapabilities.UsEquity();

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AccountInfo { AccountId = "stocksharp-test" });

        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerPosition>>([]);

        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerOrder>>([]);

        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new BrokerHealthStatus
            {
                IsHealthy = false,
                IsConnected = false,
                Message = "Test gateway is not connected."
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DriftedBrokerageGateway : IBrokerageGateway
    {
        public string GatewayId => "wrong-alpaca";

        public bool IsConnected => false;

        public string BrokerDisplayName => "Drifted Alpaca";

        public BrokerageCapabilities BrokerageCapabilities { get; } = new()
        {
            SupportedOrderTypes = new HashSet<OrderType>(),
            SupportedTimeInForce = new HashSet<TimeInForce>(),
            SupportedAssetClasses = []
        };

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AccountInfo { AccountId = "drifted" });

        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerPosition>>([]);

        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerOrder>>([]);

        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new BrokerHealthStatus { IsHealthy = false, Message = "Drifted test gateway." });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
