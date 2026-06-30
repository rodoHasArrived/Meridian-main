using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Tiingo corporate-action tests for adjusted EOD dividend/split extraction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TiingoCorporateActionProviderTests
{
    private const string ApiToken = "test-tiingo-token";

    private static readonly string CorporateActionPayload = """
        [
          {
            "date": "2024-01-02T00:00:00.000Z",
            "divCash": 0.0000,
            "splitFactor": 1.0000
          },
          {
            "date": "2024-01-03T00:00:00.000Z",
            "divCash": 0.2400,
            "splitFactor": 2.0000
          },
          {
            "date": "bad-date",
            "divCash": 1.0000,
            "splitFactor": 4.0000
          }
        ]
        """;

    [Fact]
    public async Task FetchAsync_AdjustedEodPayload_ReturnsDividendAndSplitCommands()
    {
        HttpRequestMessage? observedRequest = null;
        var securityId = Guid.NewGuid();
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(CorporateActionPayload);
        });
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("brk.b", securityId, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(result => result.ActionType).Should().Equal("Dividend", "Split");
        results.Select(result => result.SecurityId).Should().OnlyContain(id => id == securityId);
        results.Select(result => result.SourceProvider).Should().OnlyContain(source => source == "tiingo");
        results.Select(result => result.ExDate).Should().OnlyContain(date => date == new DateOnly(2024, 1, 3));

        var dividend = results[0];
        dividend.Amount.Should().Be(0.2400m);
        dividend.Currency.Should().BeNull("Tiingo adjusted EOD payloads do not include dividend currency");
        dividend.SplitFromFactor.Should().BeNull();
        dividend.SplitToFactor.Should().BeNull();

        var split = results[1];
        split.Amount.Should().BeNull();
        split.SplitFromFactor.Should().Be(1m);
        split.SplitToFactor.Should().Be(2.0000m);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/tiingo/daily/BRK-B/prices");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("startDate=2000-01-01")
            .And.Contain($"token={ApiToken}");
        observedRequest.Headers.Authorization.Should().NotBeNull();
        observedRequest.Headers.Authorization!.Scheme.Should().Be("Token");
        observedRequest.Headers.Authorization.Parameter.Should().Be(ApiToken);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WithoutToken_ReturnsEmptyWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TIINGO_API_TOKEN"] = string.Empty,
                ["TIINGO__TOKEN"] = string.Empty,
                ["Backfill:Providers:Tiingo:ApiToken"] = string.Empty,
            })
            .Build();
        var provider = new TiingoCorporateActionProvider(
            factory,
            configuration,
            NullLogger<TiingoCorporateActionProvider>.Instance);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task FetchAsync_WhenUpstreamFails_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream unavailable", Encoding.UTF8, "text/plain")
            });
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WhenCancelled_PropagatesCancellation()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TIINGO_API_TOKEN"] = ApiToken,
            })
            .Build();
        var provider = new TiingoCorporateActionProvider(
            factory,
            configuration,
            NullLogger<TiingoCorporateActionProvider>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.FetchAsync("AAPL", Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static TiingoCorporateActionProvider CreateSut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient(handler);
        factory.CreateClient(HttpClientNames.TiingoHistorical).Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TIINGO_API_TOKEN"] = ApiToken,
            })
            .Build();

        return new TiingoCorporateActionProvider(
            factory,
            configuration,
            NullLogger<TiingoCorporateActionProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
