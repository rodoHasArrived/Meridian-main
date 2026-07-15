using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Composition.Features;
using Meridian.Core.Config;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

public sealed class BackfillFeatureRegistrationTests
{
    [Fact]
    public void BackfillOnlyComposition_EnablesCanonicalSymbolManagement()
    {
        CompositionOptions.BackfillOnly.EnableSymbolManagement.Should().BeTrue();
    }

    [Fact]
    public async Task Register_BackfillWorkerFactoryWithSymbolResolutionEnabled_UsesSharedProviderScopedResolver()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"backfill-symbol-resolution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        try
        {
            var resolver = new RecordingSymbolResolver("stooq", "aapl.us");
            var provider = new RecordingHistoricalProvider("stooq");
            var registrations = new ServiceCollection();
            registrations.AddSingleton<ISymbolResolver>(resolver);
            new BackfillFeatureRegistration().Register(registrations, CompositionOptions.Default);
            using var serviceProvider = registrations.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<BackfillServiceFactory>();
            using var backfill = factory.CreateServices(
                new AppConfig(DataRoot: dataRoot),
                new BackfillConfig(
                    EnableSymbolResolution: true,
                    Jobs: new BackfillJobsConfig(PersistJobs: false)),
                dataRoot,
                [provider]);

            var bars = await backfill.Provider.GetDailyBarsAsync(
                "AAPL",
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1));

            bars.Should().ContainSingle();
            provider.LastRequestedSymbol.Should().Be("aapl.us");
            resolver.Requests.Should().ContainSingle(request =>
                request.Symbol == "AAPL" &&
                request.FromProvider == "input" &&
                request.ToProvider == "stooq");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateAutoGapRemediationPolicy_MapsConfiguredProviderAndLimits()
    {
        var config = new AutoGapRemediationConfig(
            MinimumGapDurationSeconds: 30,
            MinimumGapSize: 4,
            SymbolCooldownSeconds: 90,
            ProviderCooldownSeconds: 15,
            MaxConcurrentRemediations: 3,
            DefaultProvider: " polygon ");

        var policy = BackfillFeatureRegistration.CreateAutoGapRemediationPolicy(config);

        policy.MinimumGapDuration.Should().Be(TimeSpan.FromSeconds(30));
        policy.MinimumGapSize.Should().Be(4);
        policy.SymbolCooldown.Should().Be(TimeSpan.FromSeconds(90));
        policy.ProviderCooldown.Should().Be(TimeSpan.FromSeconds(15));
        policy.MaxConcurrentRemediations.Should().Be(3);
        policy.DefaultProvider.Should().Be("polygon");
    }

    [Fact]
    public void CreateAutoGapRemediationPolicy_InvalidBounds_FailsSafeToMinimumsAndDefaultProvider()
    {
        var config = new AutoGapRemediationConfig(
            MinimumGapDurationSeconds: -1,
            MinimumGapSize: 0,
            SymbolCooldownSeconds: -1,
            ProviderCooldownSeconds: -1,
            MaxConcurrentRemediations: 0,
            DefaultProvider: " ");

        var policy = BackfillFeatureRegistration.CreateAutoGapRemediationPolicy(config);

        policy.MinimumGapDuration.Should().Be(TimeSpan.Zero);
        policy.MinimumGapSize.Should().Be(1);
        policy.SymbolCooldown.Should().Be(TimeSpan.Zero);
        policy.ProviderCooldown.Should().Be(TimeSpan.Zero);
        policy.MaxConcurrentRemediations.Should().Be(1);
        policy.DefaultProvider.Should().Be(AutoGapRemediationPolicy.Default.DefaultProvider);
    }

    private sealed class RecordingSymbolResolver(string provider, string providerSymbol) : ISymbolResolver
    {
        public string Name => "recording";
        public List<(string Symbol, string FromProvider, string ToProvider)> Requests { get; } = [];

        public Task<SymbolResolution?> ResolveAsync(
            string symbol,
            string? exchange = null,
            CancellationToken ct = default)
            => Task.FromResult<SymbolResolution?>(null);

        public Task<string?> MapSymbolAsync(
            string symbol,
            string fromProvider,
            string toProvider,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add((symbol, fromProvider, toProvider));
            return Task.FromResult<string?>(
                string.Equals(toProvider, provider, StringComparison.OrdinalIgnoreCase)
                    ? providerSymbol
                    : null);
        }

        public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
            string query,
            int maxResults = 10,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);
    }

    private sealed class RecordingHistoricalProvider(string name) : IHistoricalDataProvider
    {
        public string Name { get; } = name;
        public string DisplayName => Name;
        public string Description => "Records the provider-scoped symbol received by the worker composite.";
        public string? LastRequestedSymbol { get; private set; }

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol,
            DateOnly? from,
            DateOnly? to,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastRequestedSymbol = symbol;
            IReadOnlyList<HistoricalBar> bars =
            [
                new HistoricalBar(
                    symbol,
                    from ?? new DateOnly(2026, 7, 1),
                    100m,
                    101m,
                    99m,
                    100.5m,
                    1_000,
                    Name)
            ];
            return Task.FromResult(bars);
        }
    }
}
