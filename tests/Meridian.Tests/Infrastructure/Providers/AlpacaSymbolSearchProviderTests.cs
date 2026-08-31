using System.Net;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Guards Alpaca's non-paginated asset search against unfiltered responses.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlpacaSymbolSearchProviderTests
{
    private const string KeyId = "test-alpaca-key";
    private const string SecretKey = "test-alpaca-secret";

    [Fact]
    public async Task SearchAsync_WithoutAssetType_RequestsOnlyEquities()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient);

        var results = await provider.SearchAsync("AAPL", limit: 1, CancellationToken.None);

        results.Should().BeEmpty();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("status=active")
            .And.Contain("asset_class=us_equity");
    }

    [Theory]
    [InlineData("crypto", "crypto")]
    [InlineData("option", "us_option")]
    [InlineData("bond", "fixed_income")]
    public async Task SearchAsync_WithAssetType_RequestsTheSelectedAssetClass(string assetType, string expectedAssetClass)
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient);

        var results = await provider.SearchAsync("BTC", limit: 1, assetType: assetType, ct: CancellationToken.None);

        results.Should().BeEmpty();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain($"asset_class={expectedAssetClass}");
    }
}
