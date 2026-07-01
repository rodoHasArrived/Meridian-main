using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Finnhub corporate-action tests for dividend and split endpoint extraction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FinnhubCorporateActionProviderTests
{
    private const string ApiKey = "test-finnhub-key";

    private static readonly string DividendsPayload = """
        [
          {
            "symbol": "AAPL",
            "date": "2024-01-03",
            "amount": 0.24,
            "adjustedAmount": 0.24,
            "currency": "usd",
            "declarationDate": "2023-12-15",
            "recordDate": "2024-01-04",
            "payDate": "2024-01-11",
            "freq": "Quarterly"
          },
          {
            "symbol": "AAPL",
            "date": "bad-date",
            "amount": 1.00,
            "currency": "USD"
          },
          {
            "symbol": "AAPL",
            "date": "2024-01-02",
            "amount": 0.00,
            "currency": "USD"
          }
        ]
        """;

    private static readonly string SplitsPayload = """
        [
          {
            "symbol": "AAPL",
            "date": "2024-01-04",
            "fromFactor": 4,
            "toFactor": 1
          },
          {
            "symbol": "AAPL",
            "date": "2024-01-05",
            "fromFactor": 0,
            "toFactor": 1
          },
          {
            "symbol": "AAPL",
            "date": "bad-date",
            "fromFactor": 2,
            "toFactor": 1
          }
        ]
        """;

    [Fact]
    public async Task Scenario_FinnhubCorporateActionEndpoints_ReturnDividendAndSplitCommands()
    {
        var observedRequests = new List<HttpRequestMessage>();
        var securityId = Guid.NewGuid();
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequests.Add(request);
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/stock/dividend" => JsonResponse(DividendsPayload),
                "/api/v1/stock/split" => JsonResponse(SplitsPayload),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("aapl", securityId, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(result => result.ActionType).Should().Equal("Dividend", "Split");
        results.Select(result => result.SecurityId).Should().OnlyContain(id => id == securityId);
        results.Select(result => result.SourceProvider).Should().OnlyContain(source => source == "finnhub");

        var dividend = results[0];
        dividend.ExDate.Should().Be(new DateOnly(2024, 1, 3));
        dividend.RecordDate.Should().Be(new DateOnly(2024, 1, 4));
        dividend.PayableDate.Should().Be(new DateOnly(2024, 1, 11));
        dividend.Amount.Should().Be(0.24m);
        dividend.Currency.Should().Be("USD");
        dividend.SplitFromFactor.Should().BeNull();
        dividend.SplitToFactor.Should().BeNull();
        dividend.Description.Should().Be("Finnhub dividend amount; frequency Quarterly.");

        var split = results[1];
        split.ExDate.Should().Be(new DateOnly(2024, 1, 4));
        split.Amount.Should().BeNull();
        split.SplitFromFactor.Should().Be(4m);
        split.SplitToFactor.Should().Be(1m);
        split.Description.Should().Be("Finnhub split factor 4:1.");

        observedRequests.Should().HaveCount(2);
        observedRequests.Select(request => request.RequestUri!.AbsolutePath)
            .Should().Equal("/api/v1/stock/dividend", "/api/v1/stock/split");
        observedRequests.Select(request => request.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped))
            .Should()
            .OnlyContain(query =>
                query.Contains("symbol=AAPL", StringComparison.Ordinal) &&
                query.Contains("from=2000-01-01", StringComparison.Ordinal) &&
                query.Contains($"token={ApiKey}", StringComparison.Ordinal));
        observedRequests.Select(request => request.Headers.GetValues("X-Finnhub-Token").Single())
            .Should().OnlyContain(token => token == ApiKey);
    }

    [Fact]
    public async Task FetchAsync_WithoutApiKey_ReturnsEmptyWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FINNHUB_API_KEY"] = string.Empty,
                ["FINNHUB__APIKEY"] = string.Empty,
                ["Backfill:Providers:Finnhub:ApiKey"] = string.Empty,
            })
            .Build();
        var provider = new FinnhubCorporateActionProvider(
            factory,
            configuration,
            NullLogger<FinnhubCorporateActionProvider>.Instance);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task FetchAsync_WhenSplitEndpointFails_ReturnsDividendEvidence()
    {
        using var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/stock/dividend" => JsonResponse(DividendsPayload),
                "/api/v1/stock/split" => new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("upstream unavailable", Encoding.UTF8, "text/plain")
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].ActionType.Should().Be("Dividend");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task FetchAsync_WhenCancelled_PropagatesCancellation()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FINNHUB_API_KEY"] = ApiKey,
            })
            .Build();
        var provider = new FinnhubCorporateActionProvider(
            factory,
            configuration,
            NullLogger<FinnhubCorporateActionProvider>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.FetchAsync("AAPL", Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static FinnhubCorporateActionProvider CreateSut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient(handler);
        factory.CreateClient(HttpClientNames.FinnhubHistorical).Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FINNHUB_API_KEY"] = ApiKey,
            })
            .Build();

        return new FinnhubCorporateActionProvider(
            factory,
            configuration,
            NullLogger<FinnhubCorporateActionProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
