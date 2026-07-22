using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Core.Exceptions;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Guards free-tier symbol-search adapters from consuming quota for invalid operator request limits.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BaseSymbolSearchProviderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Scenario_FreeTierSearchBudget_NonPositiveLimitDoesNotUseRateLimiterOrHttp(int limit)
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Invalid limits should not call secondary symbol-search providers."));
        using var httpClient = new HttpClient(handler);
        using var provider = new TestSymbolSearchProvider("test-key", httpClient);

        var results = await provider.SearchAsync("MSFT", limit, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_InheritedHttp429Mapping_ThrowsProviderAttributedRateLimit()
    {
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TestSymbolSearchProvider("test-key", httpClient);

        var act = () => provider.SearchAsync("MSFT", 10, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("test-search");
        exception.Which.Symbol.Should().Be("MSFT");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public async Task SearchAsync_StructuredQuotaCode_ThrowsProviderAttributedRateLimit()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":429,"data":[]}""", Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TestSymbolSearchProvider("test-key", httpClient);

        var act = () => provider.SearchAsync("MSFT", 10, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("test-search");
        exception.Which.Symbol.Should().Be("MSFT");
    }

    [Fact]
    public async Task GetDetailsAsync_InheritedHttp429Mapping_ThrowsProviderAttributedRateLimit()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler);
        using var provider = new TestSymbolSearchProvider("test-key", httpClient);

        var act = () => provider.GetDetailsAsync(new SymbolId("MSFT"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("test-search");
        exception.Which.Symbol.Should().Be("MSFT");
    }

    [Fact]
    public async Task IsAvailableAsync_CallerCancellation_IsNotReducedToUnavailable()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        using var httpClient = new HttpClient(handler);
        using var provider = new TestSymbolSearchProvider("test-key", httpClient);

        var act = () => provider.IsAvailableAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(0);
    }

    private sealed class TestSymbolSearchProvider(string apiKey, HttpClient httpClient)
        : BaseSymbolSearchProvider(apiKey, httpClient)
    {
        public override string Name => "test-search";
        public override string DisplayName => "Test Search";
        protected override string HttpClientName => "test-search";
        protected override string BaseUrl => "https://example.test";
        protected override string ApiKeyEnvVar => "TEST_SEARCH_API_KEY";

        protected override string BuildSearchUrl(string query, string? assetType, string? exchange)
            => $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}";

        protected override string BuildDetailsUrl(string symbol)
            => $"{BaseUrl}/details/{Uri.EscapeDataString(symbol)}";

        protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query)
            => [new SymbolSearchResult("MSFT", "Microsoft Corporation", Source: Name, MatchScore: 100)];

        protected override Task<SymbolDetails?> DeserializeDetailsAsync(
            string json,
            string symbol,
            CancellationToken ct)
            => Task.FromResult<SymbolDetails?>(new SymbolDetails(symbol, "Microsoft Corporation", Source: Name));
    }
}
