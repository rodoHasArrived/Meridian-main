using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderRoutingEligibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-route-eligibility-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisabledPrimary_IsExcludedBeforeHealthChecks(bool tenantScoped, bool scored)
    {
        var health = new RecordingHealthSource();
        var store = await SaveAsync(
            [Connection("disabled") with { Enabled = false }, Connection("enabled")],
            [Binding("disabled", priority: 1), Binding("enabled", priority: 2)]);

        var result = await RouteAsync(store, health, tenantScoped, scored);

        result.SelectedDecision!.ConnectionId.Should().Be("enabled");
        result.Candidates.Should().NotContain(candidate => candidate.ConnectionId == "disabled");
        result.SkippedCandidates.Should().Contain("Connection 'disabled' is disabled.");
        health.Requested.Should().NotContain("disabled");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisabledFallback_IsExcludedBeforeHealthChecks(bool tenantScoped, bool scored)
    {
        var health = new RecordingHealthSource("primary");
        var store = await SaveAsync(
            [Connection("primary") with { ProductionReady = false }, Connection("disabled") with { Enabled = false }],
            [Binding("primary", fallbacks: ["disabled"])]);

        var result = await RouteAsync(store, health, tenantScoped, scored, requireProductionReady: true);

        result.IsSuccess.Should().BeFalse();
        result.SelectedDecision.Should().BeNull();
        result.Candidates.Should().NotContain(candidate => candidate.ConnectionId == "disabled");
        result.SkippedCandidates.Should().Contain("Fallback connection 'disabled' is disabled.");
        health.Requested.Should().NotContain("disabled");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HealthyFallback_CannotBypassItsProductionReadinessGate(bool tenantScoped, bool scored)
    {
        var store = await SaveAsync(
            [Connection("primary") with { ProductionReady = false }, Connection("draft") with { ProductionReady = false }],
            [Binding("primary", fallbacks: ["draft"])]);

        var result = await RouteAsync(store, new RecordingHealthSource("primary"), tenantScoped, scored, requireProductionReady: true);

        result.IsSuccess.Should().BeFalse();
        result.SelectedDecision.Should().BeNull();
        result.Candidates.Should().Contain(candidate => candidate.ConnectionId == "draft" && candidate.PolicyGate != null);
        result.PolicyGate.Should().Contain("not production ready");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HealthyEligibleFallback_IsSelectedAfterAGatedFallback(bool tenantScoped, bool scored)
    {
        var store = await SaveAsync(
            [Connection("primary"), Connection("draft") with { ProductionReady = false }, Connection("ready")],
            [Binding("primary", fallbacks: ["draft", "ready"])]);

        var result = await RouteAsync(store, new RecordingHealthSource("primary"), tenantScoped, scored, requireProductionReady: true);

        result.IsSuccess.Should().BeTrue();
        result.SelectedDecision!.ConnectionId.Should().Be("ready");
    }

    [Theory]
    [InlineData(ProviderSafetyMode.NoAutomaticFailover, false)]
    [InlineData(ProviderSafetyMode.NoAutomaticFailover, true)]
    [InlineData(ProviderSafetyMode.SameInstitutionOnly, false)]
    [InlineData(ProviderSafetyMode.SameInstitutionOnly, true)]
    [InlineData(ProviderSafetyMode.ManualApprovalRequired, false)]
    [InlineData(ProviderSafetyMode.ManualApprovalRequired, true)]
    public async Task RestrictedSafetyMode_DoesNotActivateAHealthyBackup(ProviderSafetyMode mode, bool scored)
    {
        var store = await SaveAsync([Connection("primary"), Connection("backup")],
            [Binding("primary", fallbacks: ["backup"]) with { SafetyModeOverride = mode }]);

        var result = await RouteAsync(store, new RecordingHealthSource("primary"), tenantScoped: true, scored);

        result.IsSuccess.Should().BeFalse();
        result.SelectedDecision?.ConnectionId.Should().NotBe("backup");
        result.Candidates.Should().NotContain(candidate => candidate.ConnectionId == "backup");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EligibleLaterBinding_DoesNotInheritAnEarlierCandidatesGate(bool tenantScoped, bool scored)
    {
        var store = await SaveAsync(
            [Connection("draft") with { ProductionReady = false }, Connection("ready")],
            [Binding("draft", priority: 1), Binding("ready", priority: 2)]);

        var result = await RouteAsync(store, new RecordingHealthSource(), tenantScoped, scored, requireProductionReady: true);

        result.SelectedDecision!.ConnectionId.Should().Be("ready");
        result.PolicyGate.Should().BeNull();
        result.RequiresManualApproval.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EligibleLaterFailover_DoesNotInheritAnEarlierCandidatesGate(bool tenantScoped)
    {
        var store = await SaveAsync(
            [Connection("draft") with { ProductionReady = false }, Connection("primary"), Connection("backup")],
            [Binding("draft", priority: 1), Binding("primary", priority: 2, fallbacks: ["backup"])]);

        var result = await RouteAsync(store, new RecordingHealthSource("primary"), tenantScoped, scored: false, requireProductionReady: true);

        result.SelectedDecision!.ConnectionId.Should().Be("backup");
        result.PolicyGate.Should().BeNull();
        result.RequiresManualApproval.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("alpaca", ProviderCapabilityKind.AccountBalances)]
    [InlineData("alpaca", ProviderCapabilityKind.AccountPositions)]
    [InlineData("alpaca", ProviderCapabilityKind.OrderExecution)]
    [InlineData("ibkr", ProviderCapabilityKind.AccountBalances)]
    [InlineData("ibkr", ProviderCapabilityKind.AccountPositions)]
    [InlineData("ibkr", ProviderCapabilityKind.OrderExecution)]
    [InlineData("robinhood", ProviderCapabilityKind.AccountBalances)]
    [InlineData("robinhood", ProviderCapabilityKind.AccountPositions)]
    [InlineData("robinhood", ProviderCapabilityKind.OrderExecution)]
    public async Task CatalogedBrokerage_RetainsLegacyAccountCapabilityCompatibility(string providerId, ProviderCapabilityKind capability)
    {
        var accountId = Guid.NewGuid();
        var store = await SaveAsync(
            [Connection("broker") with { ProviderFamilyId = providerId, ConnectionType = ProviderConnectionType.Brokerage }],
            [Binding("broker") with { Capability = capability, Target = new ProviderBindingTarget(AccountId: accountId) }]);
        using var registry = new ProviderRegistry();
        // IBKR is supplied by the runtime registry rather than the built-in static catalog.
        if (providerId == "ibkr")
            registry.RegisterStreamingFactory(providerId, () => Mock.Of<IMarketDataClient>());
        var catalog = new ProviderFamilyCatalogService(registry, [], []);
        var family = catalog.GetFamily(providerId);
        family.Should().NotBeNull();
        family!.SupportsCapability(capability).Should().BeFalse("market-data metadata does not describe broker account relationships");
        var router = new ProviderRoutingService(store, new RecordingHealthSource(), catalog);

        var result = await router.RouteForTenantAsync(new ProviderRouteContext(capability, AccountId: accountId), "tenant-a");

        result.IsSuccess.Should().BeTrue();
        result.SelectedDecision!.ConnectionId.Should().Be("broker");
    }

    [Theory]
    [InlineData(ProviderCapabilityKind.RealtimeMarketData)]
    [InlineData(ProviderCapabilityKind.HistoricalTrades)]
    [InlineData(ProviderCapabilityKind.OptionsChain)]
    public async Task KnownBarsOnlyAdapter_DoesNotAcquireUnsupportedDataVendorCapabilities(ProviderCapabilityKind capability)
    {
        var store = await SaveAsync([Connection("bars")], [Binding("bars") with { Capability = capability }]);
        var health = new RecordingHealthSource();

        var result = await RouteAsync(store, health, tenantScoped: true, scored: false, capability: capability);

        result.IsSuccess.Should().BeFalse();
        result.Candidates.Should().BeEmpty();
        result.SkippedCandidates.Should().Contain($"Provider family 'bars-only' does not support capability '{capability}'.");
        health.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyConnectionWithoutAnAdapter_RetainsTypeBasedCompatibility()
    {
        var store = await SaveAsync([Connection("legacy") with { ProviderFamilyId = "legacy-unregistered" }], [Binding("legacy")]);
        var catalog = new Mock<IProviderFamilyCatalogService>();
        var service = new ProviderRoutingService(store, new RecordingHealthSource(), catalog.Object);

        var result = await service.RouteAsync(new ProviderRouteContext(ProviderCapabilityKind.HistoricalBars));

        result.IsSuccess.Should().BeTrue();
        result.SelectedDecision!.ConnectionId.Should().Be("legacy");
    }

    private async Task<ConfigStore> SaveAsync(ProviderConnectionConfig[] connections, ProviderBindingConfig[] bindings)
    {
        Directory.CreateDirectory(_root);
        var store = new ConfigStore(Path.Combine(_root, "appsettings.json"));
        await store.SaveAsync(new AppConfig(ProviderConnections: new ProviderConnectionsConfig(Connections: connections, Bindings: bindings)));
        return store;
    }

    private static ProviderConnectionConfig Connection(string id)
        => new(id, "bars-only", id, ProductionReady: true, TenantId: "tenant-a");

    private static ProviderBindingConfig Binding(string connectionId, int priority = 100, string[]? fallbacks = null)
        => new($"binding-{connectionId}", ProviderCapabilityKind.HistoricalBars, connectionId, Priority: priority, FailoverConnectionIds: fallbacks);

    private static async Task<ProviderRouteResult> RouteAsync(ConfigStore store, RecordingHealthSource health,
        bool tenantScoped, bool scored, bool requireProductionReady = false,
        ProviderCapabilityKind capability = ProviderCapabilityKind.HistoricalBars)
    {
        var adapter = new Mock<IProviderFamilyAdapter>();
        adapter.Setup(candidate => candidate.SupportsCapability(It.IsAny<ProviderCapabilityKind>()))
            .Returns((ProviderCapabilityKind candidate) => candidate == ProviderCapabilityKind.HistoricalBars);
        var catalog = new Mock<IProviderFamilyCatalogService>();
        catalog.Setup(candidate => candidate.GetFamily(It.IsAny<string>())).Returns(adapter.Object);
        var router = new ProviderRoutingService(store, health, catalog.Object);
        var context = new ProviderRouteContext(capability, RequireProductionReady: requireProductionReady);
        if (scored)
        {
            var selector = new BestOfBreedProviderSelector(router, health, store);
            return tenantScoped ? await selector.SelectForTenantAsync(context, "tenant-a") : await selector.SelectAsync(context);
        }

        return tenantScoped ? await router.RouteForTenantAsync(context, "tenant-a") : await router.RouteAsync(context);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingHealthSource(params string[] unhealthy) : IProviderConnectionHealthSource
    {
        public List<string> Requested { get; } = [];

        public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(string connectionId, string providerFamilyId, CancellationToken ct = default)
        {
            Requested.Add(connectionId);
            var healthy = !unhealthy.Contains(connectionId, StringComparer.OrdinalIgnoreCase);
            return ValueTask.FromResult(new ProviderConnectionHealthSnapshot(connectionId, providerFamilyId, healthy,
                healthy ? "healthy" : "degraded", healthy ? 100 : 0));
        }
    }
}
