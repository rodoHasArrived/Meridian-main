using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Http;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Nasdaq Data Link corporate-action tests for adjusted dataset dividend/split extraction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NasdaqDataLinkCorporateActionProviderTests
{
    private const string ApiKey = "test-nasdaq-key";

    private static readonly string CorporateActionPayload = """
        {
          "dataset": {
            "dataset_code": "BRK_B",
            "database_code": "EOD",
            "column_names": [
              "Date",
              "Open",
              "High",
              "Low",
              "Close",
              "Volume",
              "Ex-Dividend",
              "Split Ratio"
            ],
            "data": [
              ["2024-01-04", 378.40, 380.00, 376.90, 379.75, 2400000, 0.24, 2.0],
              ["2024-01-03", 372.80, 376.25, 371.10, 374.50, 2180000, 0.0, 1.0],
              ["bad-date", 372.80, 376.25, 371.10, 374.50, 2180000, 1.0, 4.0]
            ]
          }
        }
        """;

    [Fact]
    public async Task FetchAsync_DatasetPayload_ReturnsDividendAndSplitCommands()
    {
        HttpRequestMessage? observedRequest = null;
        var securityId = Guid.NewGuid();
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(CorporateActionPayload);
        });
        var provider = CreateSut(handler, apiKey: ApiKey, database: "EOD");
        provider.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);

        var results = await provider.FetchAsync("brk.b", securityId, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(result => result.ActionType).Should().Equal("Dividend", "StockSplit");
        results.Select(result => result.SecurityId).Should().OnlyContain(id => id == securityId);
        results.Select(result => result.SourceProvider).Should().OnlyContain(source => source == "nasdaq");
        results.Select(result => result.ExDate).Should().OnlyContain(date => date == new DateOnly(2024, 1, 4));

        var dividend = results[0];
        dividend.Amount.Should().Be(0.24m);
        dividend.Currency.Should().BeNull("Nasdaq Data Link WIKI/EOD rows do not include dividend currency");
        dividend.SplitFromFactor.Should().BeNull();
        dividend.SplitToFactor.Should().BeNull();

        var split = results[1];
        split.Amount.Should().BeNull();
        split.SplitFromFactor.Should().Be(1m);
        split.SplitToFactor.Should().Be(2.0m);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v3/datasets/EOD/BRK_B.json");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain($"api_key={ApiKey}");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WithoutApiKey_UsesConfiguredDatasetWithoutCredential()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(CorporateActionPayload);
        });
        var provider = CreateSut(handler, apiKey: null, database: "WIKI");

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().HaveCount(2);
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v3/datasets/WIKI/AAPL.json");
        observedRequest.RequestUri.Query.Should().BeEmpty(
            "Nasdaq Data Link credentials are optional for configured free datasets");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_WhenUpstreamFails_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream unavailable", Encoding.UTF8, "text/plain")
            });
        var provider = CreateSut(handler, apiKey: ApiKey, database: "EOD");

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
                ["Backfill:Providers:Nasdaq:Database"] = "EOD",
                ["NASDAQ_DATA_LINK_API_KEY"] = ApiKey,
            })
            .Build();
        var provider = new NasdaqDataLinkCorporateActionProvider(
            factory,
            configuration,
            NullLogger<NasdaqDataLinkCorporateActionProvider>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.FetchAsync("AAPL", Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static NasdaqDataLinkCorporateActionProvider CreateSut(
        HttpMessageHandler handler,
        string? apiKey,
        string database)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient(handler);
        factory.CreateClient(HttpClientNames.NasdaqDataLinkHistorical).Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backfill:Providers:Nasdaq:Database"] = database,
                ["NASDAQ_DATA_LINK_API_KEY"] = apiKey,
            })
            .Build();

        return new NasdaqDataLinkCorporateActionProvider(
            factory,
            configuration,
            NullLogger<NasdaqDataLinkCorporateActionProvider>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
