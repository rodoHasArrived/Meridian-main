using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderRoutingServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _configPath;

    public ProviderRoutingServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"meridian-provider-routing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _configPath = Path.Combine(_tempDirectory, "appsettings.json");
    }

    [Fact]
    public async Task RouteAsync_PrefersAccountBindingOverWorkspaceAndGlobal()
    {
        var accountId = Guid.NewGuid();
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("global-conn", "global-family", "Global feed"),
                    new ProviderConnectionConfig("workspace-conn", "workspace-family", "Workspace feed", Scope: new ProviderConnectionScope(Workspace: "research")),
                    new ProviderConnectionConfig("account-conn", "account-family", "Account feed", ConnectionType: ProviderConnectionType.Brokerage, Scope: new ProviderConnectionScope(AccountId: accountId))
                ],
                Bindings:
                [
                    new ProviderBindingConfig("global-binding", ProviderCapabilityKind.HistoricalBars, "global-conn", Target: new ProviderBindingTarget(), Priority: 300),
                    new ProviderBindingConfig("workspace-binding", ProviderCapabilityKind.HistoricalBars, "workspace-conn", Target: new ProviderBindingTarget(Workspace: "research"), Priority: 200),
                    new ProviderBindingConfig("account-binding", ProviderCapabilityKind.HistoricalBars, "account-conn", Target: new ProviderBindingTarget(AccountId: accountId), Priority: 100)
                ])));

        var service = CreateService();
        var result = await service.RouteAsync(new ProviderRouteContext(
            Capability: ProviderCapabilityKind.HistoricalBars,
            Workspace: "research",
            AccountId: accountId));

        result.IsSuccess.Should().BeTrue();
        result.SelectedDecision.Should().NotBeNull();
        result.SelectedDecision!.ConnectionId.Should().Be("account-conn");
    }

    [Fact]
    public async Task RouteAsync_UsesHealthyFallbackWhenPrimaryIsUnhealthy()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("primary", "alpha", "Primary"),
                    new ProviderConnectionConfig("backup", "beta", "Backup")
                ],
                Bindings:
                [
                    new ProviderBindingConfig(
                        "historical-binding",
                        ProviderCapabilityKind.HistoricalBars,
                        "primary",
                        FailoverConnectionIds: ["backup"])
                ])));

        var service = CreateService(new FakeHealthSource(("primary", false), ("backup", true)));
        var result = await service.RouteAsync(new ProviderRouteContext(ProviderCapabilityKind.HistoricalBars));

        result.IsSuccess.Should().BeTrue();
        result.SelectedDecision.Should().NotBeNull();
        result.SelectedDecision!.ConnectionId.Should().Be("backup");
        result.SelectedDecision.ReasonCodes.Should().Contain(code => code.Contains("failover", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RouteAsync_BlocksStrictCapabilityWithoutAccountScopedBinding()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("broker-1", "broker", "Broker", ConnectionType: ProviderConnectionType.Brokerage)
                ],
                Bindings:
                [
                    new ProviderBindingConfig("execution-binding", ProviderCapabilityKind.OrderExecution, "broker-1", Target: new ProviderBindingTarget())
                ])));

        var service = CreateService();
        var result = await service.RouteAsync(new ProviderRouteContext(
            Capability: ProviderCapabilityKind.OrderExecution,
            AccountId: Guid.NewGuid(),
            RequireProductionReady: true));

        result.IsSuccess.Should().BeFalse();
        result.PolicyGate.Should().Contain("account-scoped");
    }

    [Fact]
    public async Task RouteAsync_RebuildsCachedSnapshotWhenConfigChanges()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("primary-a", "alpha", "Primary A"),
                    new ProviderConnectionConfig("primary-b", "beta", "Primary B")
                ],
                Bindings:
                [
                    new ProviderBindingConfig("historical-binding", ProviderCapabilityKind.HistoricalBars, "primary-a")
                ])));

        var service = CreateService();

        var first = await service.RouteAsync(new ProviderRouteContext(ProviderCapabilityKind.HistoricalBars));
        first.SelectedDecision!.ConnectionId.Should().Be("primary-a");

        await Task.Delay(25);
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("primary-a", "alpha", "Primary A"),
                    new ProviderConnectionConfig("primary-b", "beta", "Primary B")
                ],
                Bindings:
                [
                    new ProviderBindingConfig("historical-binding", ProviderCapabilityKind.HistoricalBars, "primary-b")
                ])));

        var second = await service.RouteAsync(new ProviderRouteContext(ProviderCapabilityKind.HistoricalBars));
        second.SelectedDecision!.ConnectionId.Should().Be("primary-b");
    }

    private ProviderRoutingService CreateService(IProviderConnectionHealthSource? healthSource = null)
    {
        var store = new ConfigStore(_configPath);
        return new ProviderRoutingService(
            store,
            healthSource ?? new FakeHealthSource(),
            new FakeCatalogService());
    }

    private async Task SaveConfigAsync(AppConfig config)
    {
        var store = new ConfigStore(_configPath);
        await store.SaveAsync(config);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class FakeCatalogService : IProviderFamilyCatalogService
    {
        public IReadOnlyList<IProviderFamilyAdapter> GetFamilies()
            => new[] { "global-family", "workspace-family", "account-family", "alpha", "beta", "broker" }
                .Select(id => (IProviderFamilyAdapter)new FakeAdapter(id))
                .ToArray();

        public IProviderFamilyAdapter? GetFamily(string providerFamilyId)
            => new FakeAdapter(providerFamilyId);
    }

    private sealed class FakeAdapter : IProviderFamilyAdapter
    {
        public FakeAdapter(string familyId)
        {
            ProviderFamilyId = familyId;
        }

        public string ProviderFamilyId { get; }

        public string DisplayName => ProviderFamilyId;

        public string Description => "test";

        public IReadOnlyList<ProviderCapabilityDescriptor> CapabilityDescriptors =>
        [
            new(ProviderCapabilityKind.HistoricalBars, "bars"),
            new(ProviderCapabilityKind.OrderExecution, "execution", RequiresAccountBinding: true, SupportsFailover: false)
        ];

        public bool SupportsCapability(ProviderCapabilityKind capability)
            => capability is ProviderCapabilityKind.HistoricalBars or ProviderCapabilityKind.OrderExecution;

        public Task InitializeConnectionAsync(string connectionId, ProviderConnectionScope scope, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ProviderConnectionTestResult> TestConnectionAsync(string connectionId, CancellationToken ct = default)
            => Task.FromResult(new ProviderConnectionTestResult(true, ["ok"], DateTimeOffset.UtcNow, "healthy"));

        public ValueTask<object?> ResolveCapabilityAsync(ProviderCapabilityKind capability, CancellationToken ct = default)
            => ValueTask.FromResult<object?>(new object());
    }

    private sealed class FakeHealthSource : IProviderConnectionHealthSource
    {
        private readonly Dictionary<string, bool> _health;

        public FakeHealthSource(params (string ConnectionId, bool IsHealthy)[] entries)
        {
            _health = entries.ToDictionary(e => e.ConnectionId, e => e.IsHealthy, StringComparer.OrdinalIgnoreCase);
        }

        public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(string connectionId, string providerFamilyId, CancellationToken ct = default)
        {
            var isHealthy = !_health.TryGetValue(connectionId, out var explicitHealth) || explicitHealth;
            return ValueTask.FromResult(new ProviderConnectionHealthSnapshot(
                ConnectionId: connectionId,
                ProviderFamilyId: providerFamilyId,
                IsHealthy: isHealthy,
                Status: isHealthy ? "healthy" : "degraded",
                Score: isHealthy ? 100 : 25,
                CheckedAt: DateTimeOffset.UtcNow));
        }
    }
}
