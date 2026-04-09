using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class PolygonCorporateActionFetcherTests : IDisposable
{
    private readonly string _tempDir;

    public PolygonCorporateActionFetcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_polygon_fetcher_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task StartAsync_WithoutPolygonApiKey_LoadsConfigFromConfigStoreWithoutThrowing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataRoot"] = "data",
                ["DataSource"] = "IB"
            })
            .Build();

        var fetcher = new PolygonCorporateActionFetcher(
            new StubHttpClientFactory(),
            new StubSecurityMasterQueryService(),
            new StubSecurityMasterEventStore(),
            new RateLimiter(1, TimeSpan.FromSeconds(1)),
            configuration,
            NullLogger<PolygonCorporateActionFetcher>.Instance);

        var start = async () => await fetcher.StartAsync(CancellationToken.None);

        await start.Should().NotThrowAsync();
        await fetcher.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubSecurityMasterQueryService : ISecurityMasterQueryService
    {
        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<SecurityDetailDto?>(null);
        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default) => Task.FromResult<SecurityDetailDto?>(null);
        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(Array.Empty<SecuritySummaryDto>());
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());
        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);
        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default) => Task.FromResult<TradingParametersDto?>(null);
        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());
        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<PreferredEquityTermsDto?>(null);
    }

    private sealed class StubSecurityMasterEventStore : ISecurityMasterEventStore
    {
        public Task AppendAsync(Guid securityId, long expectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> events, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());
        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(long sequenceExclusive, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(Array.Empty<SecurityMasterEventEnvelope>());
        public Task<long> GetLatestSequenceAsync(CancellationToken ct = default) => Task.FromResult(0L);
        public Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(Guid securityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CorporateActionDto>>(Array.Empty<CorporateActionDto>());
    }
}
