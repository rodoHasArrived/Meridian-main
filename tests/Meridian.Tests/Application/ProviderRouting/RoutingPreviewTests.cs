using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class RoutingPreviewTests
{
    [Fact]
    public void ProposedBindings_ChangesOnlyOwnedBindingWithoutMutatingOriginal()
    {
        var config = OwnedConfig();
        var changed = ProviderRoutingPreviewConfig.ApplyBindings(config, "tenant-a",
            new ProviderRoutingProposedConfig(Bindings: [new("a", Priority: 3, Enabled: false)]));
        changed.ProviderConnections!.Bindings![0].Priority.Should().Be(3);
        changed.ProviderConnections.Bindings[0].Enabled.Should().BeFalse();
        changed.ProviderConnections.Connections.Should().BeSameAs(config.ProviderConnections!.Connections);
        config.ProviderConnections.Bindings![0].Priority.Should().Be(100);
        config.ProviderConnections.Bindings[0].Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("tenant-b", "a")]
    [InlineData("tenant-a", "b")]
    [InlineData("tenant-a", "missing")]
    public void ProposedBindings_RefusesForeignOrMissingBinding(string tenant, string binding)
    {
        var act = () => ProviderRoutingPreviewConfig.ApplyBindings(OwnedConfig(), tenant,
            new ProviderRoutingProposedConfig(Bindings: [new(binding, Priority: 1)]));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProposedBackfill_PreservesCredentialAdjustmentAndOriginal()
    {
        var config = new AppConfig(Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
            Alpaca: new AlpacaBackfillConfig(KeyId: "fixture-key", SecretKey: "fixture-secret", Adjustment: "raw"))));
        var result = ProviderRoutingPreviewConfig.ApplyBackfill(config,
            new ProviderRoutingProposedConfig(BackfillProviders: [new("alpaca", Priority: 2, Enabled: false)]));
        result.Backfill!.Providers!.Alpaca.Should().BeEquivalentTo(config.Backfill!.Providers!.Alpaca! with { Priority = 2, Enabled = false });
        config.Backfill.Providers.Alpaca!.Priority.Should().Be(5);
    }

    [Fact]
    public void ProposedDiff_RejectsCredentialAndTenantInjection()
    {
        var act = () => JsonSerializer.Deserialize<ProviderRoutingProposedConfig>(
            """{"Bindings":[{"BindingId":"a","CredentialReference":"foreign"}]}""");
        act.Should().Throw<JsonException>();
        var hostInjection = () => JsonSerializer.Deserialize<ProviderRoutingProposedConfig>(
            """{"TenantScopeEnforcement":false}""");
        hostInjection.Should().Throw<JsonException>();
    }

    [Fact]
    public void BackfillPreview_ExplainsFabricatedWinnerAndUnavailableVendorWithoutFetching()
    {
        var config = new AppConfig(Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(Tiingo: new TiingoConfig())));
        var provider = new PreviewProvider("synthetic", 99, simulated: true);
        var result = BackfillRoutingPreview.Preview(config, Request() with
        {
            ProposedConfig = new ProviderRoutingProposedConfig(BackfillProviders: [new("tiingo", Priority: 1)])
        }, proposed =>
        {
            proposed.Backfill!.Providers!.Tiingo!.Priority.Should().Be(1);
            return [provider];
        });
        result.SelectedProviderFamilyId.Should().Be("synthetic");
        result.Warnings.Should().Contain(w => w.Contains("FABRICATED DATA WOULD WIN"));
        result.SkippedCandidates.Should().Contain(w => w.StartsWith("Tiingo:"));
        result.IsProposed.Should().BeTrue();
        provider.Fetches.Should().Be(0);
        provider.Disposed.Should().BeTrue();
        config.Backfill!.Providers!.Tiingo!.Priority.Should().Be(15);
    }

    [Fact]
    public void BackfillPreview_RanksRealAdaptersByProposedPriorityAndDoesNotClaimHealth()
    {
        var real = new PreviewProvider("tiingo", 3);
        var synthetic = new PreviewProvider("synthetic", 99, simulated: true);
        var result = BackfillRoutingPreview.Preview(new AppConfig(), Request(), _ => [synthetic, real]);
        result.SelectedProviderFamilyId.Should().Be("tiingo");
        result.Candidates.Should().OnlyContain(c => !c.IsHealthy);
        result.Warnings.Should().Contain(w => w.Contains("date availability") && w.Contains("unverified"));
        real.Disposed.Should().BeTrue();
        synthetic.Disposed.Should().BeTrue();
    }

    [Fact]
    public void BackfillPreview_CancellationAndInvalidRangeNeverConstructAdapters()
    {
        var calls = 0;
        IReadOnlyList<IHistoricalDataProvider> Create(AppConfig _)
        { calls++; return []; }
        var cancelled = () => BackfillRoutingPreview.Preview(new AppConfig(), Request(), Create, new CancellationToken(true));
        cancelled.Should().Throw<OperationCanceledException>();
        var invalid = () => BackfillRoutingPreview.Preview(new AppConfig(), Request() with { From = new DateOnly(2025, 1, 1) }, Create);
        invalid.Should().Throw<ArgumentException>();
        calls.Should().Be(0);
    }

    [Fact]
    public void ConfigurationLint_WarnsWhenSyntheticCanOutrankRealAndTokenUnresolved()
    {
        var warnings = BackfillRoutingLint.Evaluate(new AppConfig(Backfill: new BackfillConfig(Providers:
            new BackfillProvidersConfig(Synthetic: new SyntheticMarketDataConfig(Enabled: true, Priority: 1), Tiingo: new TiingoConfig()))));
        warnings.Should().Contain(w => w.Contains("outrank"));
        warnings.Should().Contain(w => w.Contains("Tiingo") && w.Contains("token"));
        BackfillRoutingLint.Evaluate(new AppConfig()).Should().BeEmpty();
    }

    private static AppConfig OwnedConfig() => new(ProviderConnections: new ProviderConnectionsConfig(
        Connections: [new("ca", "alpaca", "A", TenantId: "tenant-a"), new("cb", "tiingo", "B", TenantId: "tenant-b")],
        Bindings: [new("a", ProviderCapabilityKind.HistoricalBars, "ca"), new("b", ProviderCapabilityKind.HistoricalBars, "cb")]));

    private static RoutePreviewRequest Request() => new("HistoricalBars", Symbol: "AAPL", PreviewBackfill: true,
        From: new DateOnly(2019, 1, 1), To: new DateOnly(2024, 12, 31));

    private sealed class PreviewProvider(string name, int priority, bool simulated = false) : IHistoricalDataProvider
    {
        public string Name => name;
        public string DisplayName => name;
        public string Description => "Deterministic preview fixture";
        public int Priority => priority;
        public bool IsSimulated => simulated;
        public int Fetches { get; private set; }
        public bool Disposed { get; private set; }
        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            Fetches++;
            throw new InvalidOperationException("Preview must never fetch.");
        }
        public void Dispose() => Disposed = true;
    }
}
