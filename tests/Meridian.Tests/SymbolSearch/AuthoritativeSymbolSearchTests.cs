using System.Net;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Core.Exceptions;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.SymbolSearch;

public sealed class AuthoritativeSymbolSearchTests
{
    [Fact]
    public async Task AuthoritativeSearch_DoesNotReuseTolerantCachedEmptyResult()
    {
        var lookupCount = 0;
        using var handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.Query.Contains("asset_class=fixed_income", StringComparison.Ordinal)
                ? ++lookupCount == 1 ? "[]" : "{" : """[{"symbol":"AAPL","name":"Apple","tradable":true}]""")
        });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        var request = new SymbolSearchRequest("Treasury", AssetType: "fixed_income", Provider: "alpaca", IncludeFigi: false);
        (await search.SearchAsync(request)).Results.Should().BeEmpty();

        var action = () => search.SearchAuthoritativeAsync(request);

        var exception = await action.Should().ThrowAsync<ProviderSearchException>();
        exception.Which.Failure.Should().Be(ProviderSearchFailure.MalformedPayload);
        lookupCount.Should().Be(2);
    }
}
