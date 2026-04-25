using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Enums;
using Meridian.ProviderSdk;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class BestOfBreedProviderSelectorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _configPath;

    public BestOfBreedProviderSelectorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"meridian-selector-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _configPath = Path.Combine(_tempDirectory, "appsettings.json");
    }

    [Fact]
    public async Task SelectAsync_PrefersHealthyProvider_WhenCompetingRouteHasHigherQualityButPoorHealth()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("healthy-low-quality", "alpha", "Healthy"),
                    new ProviderConnectionConfig("degraded-high-quality", "beta", "Degraded")
                ],
                Bindings:
                [
                    new ProviderBindingConfig("rt-a", ProviderCapabilityKind.RealtimeMarketData, "healthy-low-quality", Priority: 100),
                    new ProviderBindingConfig("rt-b", ProviderCapabilityKind.RealtimeMarketData, "degraded-high-quality", Priority: 100)
                ])));

        var selector = CreateSelector(
            health: new FakeHealthSource(("healthy-low-quality", true, 95), ("degraded-high-quality", false, 15)),
            quality: new FakeDataQualityService(
                new SourceRanking("alpha", 0.35, 100, 0, 15, false),
                new SourceRanking("beta", 0.98, 100, 0, 15, true)));

        var result = await selector.SelectAsync(new ProviderRouteContext(ProviderCapabilityKind.RealtimeMarketData, Symbol: "SPY"));

        result.SelectedDecision.Should().NotBeNull();
        result.SelectedDecision!.ConnectionId.Should().Be("healthy-low-quality");
        result.Candidates.Should().BeInDescendingOrder(c => c.CompositeScore);
    }

    [Fact]
    public async Task SelectAsync_ExcludesPolicyGatedRoutes_FromSelection()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("manual-route", "alpha", "Manual"),
                    new ProviderConnectionConfig("auto-route", "beta", "Auto")
                ],
                Bindings:
                [
                    new ProviderBindingConfig("rt-a", ProviderCapabilityKind.RealtimeMarketData, "manual-route", SafetyModeOverride: ProviderSafetyMode.ManualApprovalRequired),
                    new ProviderBindingConfig("rt-b", ProviderCapabilityKind.RealtimeMarketData, "auto-route")
                ])));

        var selector = CreateSelector();
        var result = await selector.SelectAsync(new ProviderRouteContext(ProviderCapabilityKind.RealtimeMarketData, Symbol: "SPY"));

        result.SelectedDecision.Should().NotBeNull();
        result.SelectedDecision!.ConnectionId.Should().Be("auto-route");
        result.Candidates.Should().Contain(c => c.ConnectionId == "manual-route" && c.PolicyGateScore == 0);
    }

    [Fact]
    public async Task SelectAsync_UsesConnectionIdAsDeterministicTieBreaker()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig("aaa-route", "alpha", "Aaa"),
                    new ProviderConnectionConfig("zzz-route", "beta", "Zzz")
                ],
                Bindings:
                [
                    new ProviderBindingConfig("rt-a", ProviderCapabilityKind.RealtimeMarketData, "zzz-route", Priority: 100),
                    new ProviderBindingConfig("rt-b", ProviderCapabilityKind.RealtimeMarketData, "aaa-route", Priority: 100)
                ])));

        var selector = CreateSelector(
            health: new FakeHealthSource(("aaa-route", true, 80), ("zzz-route", true, 80)),
            quality: new FakeDataQualityService(
                new SourceRanking("alpha", 0.70, 100, 0, 15, true),
                new SourceRanking("beta", 0.70, 100, 0, 15, true)));

        var first = await selector.SelectAsync(new ProviderRouteContext(ProviderCapabilityKind.RealtimeMarketData, Symbol: "SPY"));
        var second = await selector.SelectAsync(new ProviderRouteContext(ProviderCapabilityKind.RealtimeMarketData, Symbol: "SPY"));

        first.SelectedDecision!.ConnectionId.Should().Be("aaa-route");
        second.SelectedDecision!.ConnectionId.Should().Be("aaa-route");
        first.Candidates.Select(c => c.ConnectionId).Should().Equal(second.Candidates.Select(c => c.ConnectionId));
    }

    private BestOfBreedProviderSelector CreateSelector(
        IProviderConnectionHealthSource? health = null,
        IDataQualityService? quality = null)
    {
        var store = new ConfigStore(_configPath);
        var routing = new ProviderRoutingService(store, health ?? new FakeHealthSource(), new FakeCatalogService());
        return new BestOfBreedProviderSelector(routing, health ?? new FakeHealthSource(), store, quality ?? new FakeDataQualityService());
    }

    private async Task SaveConfigAsync(AppConfig config)
    {
        var store = new ConfigStore(_configPath);
        await store.SaveAsync(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }

    private sealed class FakeCatalogService : IProviderFamilyCatalogService
    {
        public IReadOnlyList<IProviderFamilyAdapter> GetFamilies()
            => [new FakeAdapter("alpha"), new FakeAdapter("beta")];

        public IProviderFamilyAdapter? GetFamily(string providerFamilyId) => new FakeAdapter(providerFamilyId);
    }

    private sealed class FakeAdapter(string familyId) : IProviderFamilyAdapter
    {
        public string ProviderFamilyId => familyId;
        public string DisplayName => familyId;
        public string Description => "test";

        public IReadOnlyList<ProviderCapabilityDescriptor> CapabilityDescriptors =>
        [
            new(ProviderCapabilityKind.RealtimeMarketData, "rt")
        ];

        public bool SupportsCapability(ProviderCapabilityKind capability) => capability == ProviderCapabilityKind.RealtimeMarketData;

        public Task InitializeConnectionAsync(string connectionId, ProviderConnectionScope scope, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ProviderConnectionTestResult> TestConnectionAsync(string connectionId, CancellationToken ct = default)
            => Task.FromResult(new ProviderConnectionTestResult(true, ["ok"], DateTimeOffset.UtcNow, "healthy"));

        public ValueTask<object?> ResolveCapabilityAsync(ProviderCapabilityKind capability, CancellationToken ct = default)
            => ValueTask.FromResult<object?>(new object());
    }

    private sealed class FakeHealthSource(params (string ConnectionId, bool IsHealthy, double Score)[] entries) : IProviderConnectionHealthSource
    {
        private readonly Dictionary<string, (bool IsHealthy, double Score)> _entries = entries
            .ToDictionary(e => e.ConnectionId, e => (e.IsHealthy, e.Score), StringComparer.OrdinalIgnoreCase);

        public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(string connectionId, string providerFamilyId, CancellationToken ct = default)
        {
            var (isHealthy, score) = _entries.TryGetValue(connectionId, out var value) ? value : (true, 85d);
            return ValueTask.FromResult(new ProviderConnectionHealthSnapshot(connectionId, providerFamilyId, isHealthy, isHealthy ? "healthy" : "degraded", score, DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeDataQualityService(params SourceRanking[] rankings) : IDataQualityService
    {
        private readonly SourceRanking[] _rankings = rankings;

        public Task<DataQualityScore> ScoreAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DataQualityReport> GenerateReportAsync(QualityReportOptions options, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DataQualityScore[]> GetHistoricalScoresAsync(string path, TimeSpan window, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SourceRanking[]> RankSourcesAsync(string symbol, DateTimeOffset date, MarketEventType type, CancellationToken ct = default) => Task.FromResult(_rankings);
        public Task<ConsolidatedDataset> CreateGoldenRecordAsync(string symbol, DateTimeOffset date, ConsolidationOptions options, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QualityTrend> GetTrendAsync(string symbol, TimeSpan window, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QualityAlert[]> GetQualityAlertsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
