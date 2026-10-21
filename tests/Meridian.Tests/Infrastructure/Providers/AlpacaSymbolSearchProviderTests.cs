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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_IncludeBothAuthenticationHeaders_AfterCredentialInitialization(bool details)
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            request.Headers.GetValues("APCA-API-KEY-ID").Should().Equal(KeyId);
            request.Headers.GetValues("APCA-API-SECRET-KEY").Should().Equal(SecretKey);
            request.Headers.Accept.Select(header => header.MediaType).Should().Equal("application/json");
            request.RequestUri!.AbsolutePath.Should().Be(details ? "/v2/assets/AAPL" : "/v2/assets");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(details
                    ? "{\"symbol\":\"AAPL\",\"name\":\"Apple\",\"tradable\":true}"
                    : "[{\"symbol\":\"AAPL\",\"name\":\"Apple\",\"tradable\":true}]")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient);

        if (details)
            (await provider.GetDetailsAsync(new Meridian.Contracts.Domain.SymbolId("AAPL"))).Should().NotBeNull();
        else
            (await provider.SearchAsync("AAPL", 1, CancellationToken.None)).Should().ContainSingle();
        handler.CallCount.Should().Be(1);
    }

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
