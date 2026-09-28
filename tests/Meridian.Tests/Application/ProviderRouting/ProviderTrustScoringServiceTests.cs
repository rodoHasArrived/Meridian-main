using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderTrustScoringServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _configPath;

    public ProviderTrustScoringServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"meridian-provider-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _configPath = Path.Combine(_tempDirectory, "appsettings.json");
    }

    [Fact]
    public async Task GetTrustSnapshotsAsync_ReturnsDecisionEnvelopeWithRuleReasons()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig(
                        ConnectionId: "alpaca-paper",
                        ProviderFamilyId: "alpaca",
                        DisplayName: "Alpaca Paper",
                        Enabled: false,
                        ProductionReady: false)
                ],
                Certifications: [])));

        var service = new ProviderTrustScoringService(
            new ConfigStore(_configPath),
            new FakeHealthSource(("alpaca-paper", false)));

        var snapshots = await service.GetTrustSnapshotsAsync();

        snapshots.Should().ContainSingle();
        var snapshot = snapshots[0];
        var decision = snapshot.Decision;
        decision.Should().NotBeNull();
        decision!.Trace.SchemaVersion.Should().Be(ProviderTrustScoringService.DecisionSchemaVersion);
        decision.Trace.KernelVersion.Should().Be(ProviderTrustScoringService.KernelVersion);
        decision.Reasons.Should().Contain(r => r.RuleId == "provider-trust.connection-enabled");
        decision.Reasons.Should().Contain(r => r.ReasonCode == "HEALTH_NOT_HEALTHY");
        decision.Reasons.Should().OnlyContain(r => r.EvidenceRefs != null && r.EvidenceRefs.Count > 0);
        decision.Reasons.Select(r => r.HumanExplanation).Should().BeEquivalentTo(snapshot.Signals);
    }

    [Fact]
    public async Task GetTrustSnapshotsAsync_HealthyConnection_ProducesEmptyReasonsEnvelope()
    {
        await SaveConfigAsync(new AppConfig(
            ProviderConnections: new ProviderConnectionsConfig(
                Connections:
                [
                    new ProviderConnectionConfig(
                        ConnectionId: "polygon-live",
                        ProviderFamilyId: "polygon",
                        DisplayName: "Polygon Live",
                        Enabled: true,
                        ProductionReady: true)
                ],
                Certifications:
                [
                    new ProviderCertificationConfig(
                        ConnectionId: "polygon-live",
                        Status: "passed",
                        LastRunAt: DateTimeOffset.UtcNow.AddDays(-1),
                        ExpiresAt: DateTimeOffset.UtcNow.AddDays(10),
                        ProductionReady: true,
                        Checks: ["streaming", "historical"],
                        Notes: ["passed"])
                ])));

        var service = new ProviderTrustScoringService(
            new ConfigStore(_configPath),
            new FakeHealthSource(("polygon-live", true)));

        var snapshots = await service.GetTrustSnapshotsAsync();

        snapshots.Should().ContainSingle();
        var decision = snapshots[0].Decision;
        decision.Should().NotBeNull();
        decision!.Reasons.Should().BeEmpty();
        decision.Score.Should().Be(100);
    }

    [Theory]
    [InlineData("tenant-alpaca", "alpaca")]
    [InlineData("alpaca", "alpaca")]
    [InlineData("ALPACA", "alpaca")]
    [InlineData("shared-runtime", "shared-runtime")]
    public async Task GetTrustSnapshotsForTenantAsync_IgnoresRuntimeHealthWithoutOwnershipProvenance(
        string connectionId,
        string metricProviderId)
    {
        var dataRoot = Path.Combine(_tempDirectory, "data");
        Directory.CreateDirectory(Path.Combine(dataRoot, "_status"));
        await SaveConfigAsync(new AppConfig() with
        {
            DataRoot = dataRoot,
            ProviderConnections = new ProviderConnectionsConfig(
                Connections: [new ProviderConnectionConfig(connectionId, "alpaca", "Tenant Alpaca", TenantId: "tenant-a")],
                Certifications: [])
        });
        // Provider IDs have no ownership provenance, even when an owner chooses a colliding connection ID.
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "_status", "providers.json"),
            $$"""
            {"timestamp":"2026-09-28T12:00:00Z","providers":[{"providerId":"{{metricProviderId}}","providerType":"Streaming","isConnected":false,"dataQualityScore":10,"timestamp":"2026-09-28T12:00:00Z"}],"totalProviders":1,"healthyProviders":0}
            """);
        var store = new ConfigStore(_configPath);
        var healthSource = new DefaultProviderConnectionHealthSource(store);
        var service = new ProviderTrustScoringService(store, healthSource);

        var unscoped = await service.GetTrustSnapshotsAsync();
        var tenant = await service.GetTrustSnapshotsForTenantAsync("tenant-a");

        unscoped.Single().Decision!.Reasons.Should().Contain(r => r.ReasonCode == "HEALTH_NOT_HEALTHY");
        tenant.Single().Decision!.Reasons.Should().NotContain(r => r.ReasonCode == "HEALTH_NOT_HEALTHY",
            "a provider ID match cannot attribute shared runtime telemetry to this tenant's connection");
        var unscopedHealth = await healthSource.GetHealthAsync(connectionId, "alpaca");
        unscopedHealth.Status.Should().Be("degraded");
        unscopedHealth.Score.Should().Be(10);
        var scopedHealth = await ((IProviderConnectionHealthSource)healthSource).GetConnectionHealthAsync(connectionId, "alpaca");
        scopedHealth.ConnectionId.Should().Be(connectionId);
        scopedHealth.ProviderFamilyId.Should().Be("alpaca");
        scopedHealth.Status.Should().Be("unknown");
        scopedHealth.IsHealthy.Should().BeTrue();
        scopedHealth.Score.Should().Be(100);
    }

    private async Task SaveConfigAsync(AppConfig config)
    {
        var store = new ConfigStore(_configPath);
        await store.SaveAsync(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
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
