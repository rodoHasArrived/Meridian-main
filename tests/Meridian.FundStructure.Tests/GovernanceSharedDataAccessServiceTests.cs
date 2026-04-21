using Meridian.Application.Backfill;
using Meridian.Application.Config;
using Meridian.Application.FundStructure;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class GovernanceSharedDataAccessServiceTests
{
    [Fact]
    public async Task GetSharedDataAccessAsync_SummarizesAvailableSecurityPriceAndBackfillData()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "data");
        var configPath = Path.Combine(tempDirectory, "appsettings.json");

        try
        {
            Directory.CreateDirectory(Path.Combine(dataRoot, "AAPL"));
            Directory.CreateDirectory(Path.Combine(dataRoot, "MSFT"));
            await File.WriteAllTextAsync(Path.Combine(dataRoot, "AAPL", "AAPL_bars_2026-04-07.jsonl"), "{}\n");
            await File.WriteAllTextAsync(Path.Combine(dataRoot, "MSFT", "MSFT_bars_2026-04-07.jsonl"), "{}\n");

            var configStore = new ConfigStore(configPath);
            await configStore.SaveAsync(new AppConfig(DataRoot: dataRoot));

            var statusStore = new BackfillStatusStore(dataRoot);
            await statusStore.WriteAsync(new BackfillResult(
                Success: true,
                Provider: "stooq",
                Symbols: ["AAPL", "MSFT"],
                From: new DateOnly(2026, 01, 01),
                To: new DateOnly(2026, 04, 07),
                BarsWritten: 240,
                StartedUtc: new DateTimeOffset(2026, 04, 07, 17, 0, 0, TimeSpan.Zero),
                CompletedUtc: new DateTimeOffset(2026, 04, 07, 18, 0, 0, TimeSpan.Zero)));
            await statusStore.WriteSymbolCheckpointAsync("AAPL", new DateOnly(2026, 04, 07), barsWritten: 120);
            await statusStore.WriteSymbolCheckpointAsync("MSFT", new DateOnly(2026, 04, 07), barsWritten: 120);

            using var backfillCoordinator = new BackfillCoordinator(configStore);
            var historicalQueryService = new HistoricalDataQueryService(dataRoot);
            var service = new GovernanceSharedDataAccessService(
                new FakeSecurityMasterQueryService(isAvailable: true),
                historicalQueryService,
                backfillCoordinator);

            var result = await service.GetSharedDataAccessAsync();

            Assert.True(result.SecurityMaster.IsAvailable);
            Assert.True(result.SecurityMaster.InstrumentDefinitionsAccessible);
            Assert.True(result.HistoricalPrices.IsAvailable);
            Assert.True(result.HistoricalPrices.HasStoredData);
            Assert.True(result.HistoricalPrices.AvailableSymbolCount >= 2);
            Assert.Contains("AAPL", result.HistoricalPrices.SampleSymbols);
            Assert.True(result.Backfill.IsAvailable);
            Assert.True(result.Backfill.ProviderCount > 0);
            Assert.Equal("stooq", result.Backfill.LastProvider);
            Assert.Equal(2, result.Backfill.SymbolCheckpointCount);
            Assert.Equal(2, result.Backfill.SymbolBarCountCount);
            Assert.True(result.Backfill.LastRunSucceeded);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task GetSharedDataAccessAsync_ReportsUnavailableDependencies_WhenServicesAreMissing()
    {
        var service = new GovernanceSharedDataAccessService(
            new NullSecurityMasterQueryService(),
            historicalDataQueryService: null,
            backfillCoordinator: null);

        var result = await service.GetSharedDataAccessAsync();

        Assert.False(result.SecurityMaster.IsAvailable);
        Assert.False(result.HistoricalPrices.IsAvailable);
        Assert.False(result.Backfill.IsAvailable);
        Assert.Contains("not registered", result.HistoricalPrices.AvailabilityDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not registered", result.Backfill.AvailabilityDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSharedDataAccessAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new GovernanceSharedDataAccessService(
            new FakeSecurityMasterQueryService(isAvailable: true),
            historicalDataQueryService: null,
            backfillCoordinator: null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetSharedDataAccessAsync(cts.Token));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MeridianGovernanceSharedDataAccessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeSecurityMasterQueryService : Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService, ISecurityMasterRuntimeStatus
    {
        public FakeSecurityMasterQueryService(bool isAvailable)
        {
            IsAvailable = isAvailable;
            AvailabilityDescription = isAvailable
                ? "Security Master is available."
                : "Security Master is unavailable in tests.";
        }

        public bool IsAvailable { get; }

        public string AvailabilityDescription { get; }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
}
