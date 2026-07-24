using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Alpha Vantage corporate-action tests for adjusted daily dividend/split extraction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlphaVantageCorporateActionProviderTests
{
    private const string ApiKey = "test-alpha-vantage-key";

    private static readonly string CorporateActionPayload = """
        {
          "Meta Data": {
            "1. Information": "Daily Time Series with Splits and Dividend Events",
            "2. Symbol": "AAPL"
          },
          "Time Series (Daily)": {
            "2024-01-03": {
              "7. dividend amount": "0.2400",
              "8. split coefficient": "2.0000"
            },
            "2024-01-02": {
              "7. dividend amount": "0.0000",
              "8. split coefficient": "1.0000"
            },
            "bad-date": {
              "7. dividend amount": "1.0000",
              "8. split coefficient": "4.0000"
            }
          }
        }
        """;

    private static readonly string RateLimitResponse = """
        {
          "Information": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 100 calls per day."
        }
        """;

    [Fact]
    public async Task FetchAsync_AdjustedDailyPayload_ReturnsDividendAndSplitCommands()
    {
        HttpRequestMessage? observedRequest = null;
        var securityId = Guid.NewGuid();
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(CorporateActionPayload);
        });
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("aapl", securityId, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(result => result.ActionType).Should().Equal("Dividend", "StockSplit");
        results.Select(result => result.SecurityId).Should().OnlyContain(id => id == securityId);
        results.Select(result => result.SourceProvider).Should().OnlyContain(source => source == "alphavantage");
        results.Select(result => result.ExDate).Should().OnlyContain(date => date == new DateOnly(2024, 1, 3));

        var dividend = results[0];
        dividend.Amount.Should().Be(0.2400m);
        dividend.Currency.Should().BeNull("Alpha Vantage adjusted daily payloads do not include dividend currency");
        dividend.SplitFromFactor.Should().BeNull();
        dividend.SplitToFactor.Should().BeNull();

        var split = results[1];
        split.Amount.Should().BeNull();
        split.SplitFromFactor.Should().Be(1m);
        split.SplitToFactor.Should().Be(2.0000m);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/query");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("function=TIME_SERIES_DAILY_ADJUSTED")
            .And.Contain("symbol=AAPL")
            .And.Contain("outputsize=full")
            .And.Contain($"apikey={ApiKey}");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WithoutApiKey_ReturnsEmptyWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPHA_VANTAGE_API_KEY"] = string.Empty,
                ["ALPHAVANTAGE__APIKEY"] = string.Empty,
                ["Backfill:Providers:AlphaVantage:ApiKey"] = string.Empty,
            })
            .Build();
        var provider = new AlphaVantageCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlphaVantageCorporateActionProvider>.Instance);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task FetchAsync_WhenBodyIsRateLimited_ThrowsTypedRateLimitMetadata()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RateLimitResponse));
        var provider = CreateSut(handler);

        var act = () => provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("alphavantage");
        exception.Which.Symbol.Should().Be("AAPL");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WhenHttp429_ThrowsTypedRateLimitAndPreservesRetryAfter()
    {
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(19));
            return response;
        });
        var provider = CreateSut(handler);

        var act = () => provider.FetchAsync("aapl", Guid.NewGuid(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("alphavantage");
        exception.Which.Symbol.Should().Be("AAPL");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(19));
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WhenCancelled_PropagatesCancellation()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPHA_VANTAGE_API_KEY"] = ApiKey,
            })
            .Build();
        var provider = new AlphaVantageCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlphaVantageCorporateActionProvider>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.FetchAsync("AAPL", Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static AlphaVantageCorporateActionProvider CreateSut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient(handler);
        factory.CreateClient(HttpClientNames.AlphaVantageHistorical).Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPHA_VANTAGE_API_KEY"] = ApiKey,
            })
            .Build();

        return new AlphaVantageCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlphaVantageCorporateActionProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
