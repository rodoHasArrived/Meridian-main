using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Twelve Data corporate-action tests for dividends and split extraction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TwelveDataCorporateActionProviderTests
{
    private const string ApiKey = "test-twelve-data-key";

    private static readonly string DividendsPayload = """
        {
          "meta": {
            "symbol": "AAPL",
            "name": "Apple Inc",
            "currency": "USD",
            "exchange": "NASDAQ",
            "mic_code": "XNAS"
          },
          "dividends": [
            {
              "ex_date": "2024-01-03",
              "amount": 0.24
            },
            {
              "ex_date": "bad-date",
              "amount": 1.00
            },
            {
              "ex_date": "2024-01-02",
              "amount": 0.00
            }
          ]
        }
        """;

    private static readonly string SplitsPayload = """
        {
          "meta": {
            "symbol": "AAPL",
            "name": "Apple Inc",
            "currency": "USD",
            "exchange": "NASDAQ",
            "mic_code": "XNAS"
          },
          "splits": [
            {
              "date": "2024-01-04",
              "description": "4-for-1 split",
              "ratio": 0.25,
              "from_factor": 4,
              "to_factor": 1
            },
            {
              "date": "bad-date",
              "description": "malformed split",
              "ratio": 0.50,
              "from_factor": 2,
              "to_factor": 1
            }
          ]
        }
        """;

    [Fact]
    public async Task FetchAsync_DividendsAndSplitsPayloads_ReturnsCorporateActionCommands()
    {
        var observedRequests = new List<HttpRequestMessage>();
        var securityId = Guid.NewGuid();
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequests.Add(request);
            return request.RequestUri!.AbsolutePath switch
            {
                "/dividends" => JsonResponse(DividendsPayload),
                "/splits" => JsonResponse(SplitsPayload),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = CreateSut(handler);
        provider.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);

        var results = await provider.FetchAsync("aapl", securityId, CancellationToken.None);

        results.Should().HaveCount(2);
        // The ratio-0.25 / 4→1 factor payload is economically a reverse split; the provider
        // maps it to the canonical ReverseStockSplit event type.
        results.Select(result => result.ActionType).Should().Equal("Dividend", "ReverseStockSplit");
        results.Select(result => result.SecurityId).Should().OnlyContain(id => id == securityId);
        results.Select(result => result.SourceProvider).Should().OnlyContain(source => source == "twelvedata");

        var dividend = results[0];
        dividend.ExDate.Should().Be(new DateOnly(2024, 1, 3));
        dividend.Amount.Should().Be(0.24m);
        dividend.Currency.Should().Be("USD");
        dividend.SplitFromFactor.Should().BeNull();
        dividend.SplitToFactor.Should().BeNull();

        var split = results[1];
        split.ExDate.Should().Be(new DateOnly(2024, 1, 4));
        split.Amount.Should().BeNull();
        split.SplitFromFactor.Should().Be(4m);
        split.SplitToFactor.Should().Be(1m);
        split.Description.Should().Be("4-for-1 split");

        observedRequests.Should().HaveCount(2);
        observedRequests.Select(request => request.RequestUri!.AbsolutePath)
            .Should().Equal("/dividends", "/splits");
        observedRequests.Select(request => request.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped))
            .Should()
            .OnlyContain(query =>
                query.Contains("symbol=AAPL", StringComparison.Ordinal) &&
                query.Contains("range=full", StringComparison.Ordinal) &&
                query.Contains($"apikey={ApiKey}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_WithoutApiKey_ReturnsEmptyWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TWELVEDATA_API_KEY"] = string.Empty,
                ["TWELVEDATA__APIKEY"] = string.Empty,
                ["Backfill:Providers:TwelveData:ApiKey"] = string.Empty,
            })
            .Build();
        var provider = new TwelveDataCorporateActionProvider(
            factory,
            configuration,
            NullLogger<TwelveDataCorporateActionProvider>.Instance);

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
                "/dividends" => JsonResponse(DividendsPayload),
                "/splits" => new HttpResponseMessage(HttpStatusCode.InternalServerError)
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
                ["TWELVEDATA_API_KEY"] = ApiKey,
            })
            .Build();
        var provider = new TwelveDataCorporateActionProvider(
            factory,
            configuration,
            NullLogger<TwelveDataCorporateActionProvider>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.FetchAsync("AAPL", Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static TwelveDataCorporateActionProvider CreateSut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient(handler);
        factory.CreateClient(HttpClientNames.TwelveDataHistorical).Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TWELVEDATA_API_KEY"] = ApiKey,
            })
            .Build();

        return new TwelveDataCorporateActionProvider(
            factory,
            configuration,
            NullLogger<TwelveDataCorporateActionProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
