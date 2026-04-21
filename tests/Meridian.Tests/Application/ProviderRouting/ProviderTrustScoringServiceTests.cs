using FluentAssertions;
using Meridian.Application.Config;
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
        snapshot.Decision.Should().NotBeNull();
        snapshot.Decision.Trace.SchemaVersion.Should().Be(ProviderTrustScoringService.DecisionSchemaVersion);
        snapshot.Decision.Trace.KernelVersion.Should().Be(ProviderTrustScoringService.KernelVersion);
        snapshot.Decision.Reasons.Should().Contain(r => r.RuleId == "provider-trust.connection-enabled");
        snapshot.Decision.Reasons.Should().Contain(r => r.ReasonCode == "HEALTH_NOT_HEALTHY");
        snapshot.Decision.Reasons.Should().OnlyContain(r => r.EvidenceRefs is { Count: > 0 });
        snapshot.Decision.Reasons.Select(r => r.HumanExplanation).Should().BeEquivalentTo(snapshot.Signals);
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
        snapshots[0].Decision.Reasons.Should().BeEmpty();
        snapshots[0].Decision.Score.Should().Be(100);
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
