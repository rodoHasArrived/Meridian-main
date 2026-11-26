using System.Globalization;
using System.Net;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Integrity;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaAccountingClosePriceTests
{
    private static readonly DateOnly Session = new(2026, 7, 2);

    [Theory]
    [InlineData("split", 50)]
    [InlineData("dividend", 98)]
    [InlineData("all", 49)]
    public async Task AccountingRequestsRaw_PreservesConfiguredAdjustedResearchPrices(string researchAdjustment, int adjustedClose)
    {
        var requests = new List<string>();
        using var handler = new ResponseHandler(request =>
        {
            var uri = request.RequestUri!.OriginalString;
            requests.Add(uri);
            return Json(Response(Bar(uri.Contains("adjustment=raw", StringComparison.Ordinal) ? 100m : adjustedClose)));
        });
        using var client = new HttpClient(handler);
        using var provider = new AlpacaHistoricalDataProvider("synthetic-key", "synthetic-secret", "sip", researchAdjustment,
            rateLimitPerMinute: 60000, httpClient: client);

        var quote = await new HistoricalCloseMarkPriceSource(provider).GetMarkPriceAsync("AAPL", Session);
        var research = await provider.GetDailyBarsAsync("AAPL", Session, Session);

        quote.Should().NotBeNull();
        quote!.Price.Should().Be(100m, "split and dividend adjustment must never change the historical accounting mark");
        quote.EvidenceReference.Should().Contain("basis=raw").And.Contain("feed=sip")
            .And.Contain(Sha256Digest.ComputeUtf8(Bar(100m)));
        research.Should().ContainSingle().Which.Close.Should().Be(adjustedClose);
        requests.Should().HaveCount(2);
        requests[0].Should().Contain("adjustment=raw").And.Contain("feed=sip");
        requests[1].Should().Contain($"adjustment={researchAdjustment}");
        provider.CoverageFeed.Should().Be($"sip:{researchAdjustment}", "research configuration must not be mutated by accounting");
    }

    [Fact]
    public async Task DefaultAllAdjustment_StillRequestsRawForAccounting()
    {
        string? requested = null;
        using var handler = new ResponseHandler(request =>
        {
            requested = request.RequestUri!.OriginalString;
            return Json(Response(Bar(100m)));
        });
        using var client = new HttpClient(handler);
        using var provider = new AlpacaHistoricalDataProvider("synthetic-key", "synthetic-secret", httpClient: client);

        var quote = await provider.GetRawAccountingCloseAsync("AAPL", Session, Session);

        quote!.PriceBasis.Should().Be(AccountingClosePriceBasis.Raw);
        requested.Should().Contain("adjustment=raw");
        provider.CoverageFeed.Should().Be("iex:all");
    }

    [Fact]
    public async Task PagedRawHistory_SelectsLatestSessionOnlyAfterAllPagesSucceed()
    {
        var requests = 0;
        using var handler = new ResponseHandler(_ => Json(++requests == 1
            ? Response(Bar(100m, "2026-07-01"), token: "next")
            : Response(Bar(102m))));
        using var client = new HttpClient(handler);
        using var provider = new AlpacaHistoricalDataProvider("synthetic-key", "synthetic-secret",
            rateLimitPerMinute: 60000, httpClient: client);

        var quote = await provider.GetRawAccountingCloseAsync("AAPL", Session.AddDays(-1), Session);

        quote!.SessionDate.Should().Be(Session);
        quote.Close.Should().Be(102m);
        requests.Should().Be(2);
    }

    [Fact]
    public async Task FailedContinuation_DoesNotPublishAnEarlierRawClose()
    {
        var requests = 0;
        using var handler = new ResponseHandler(_ => ++requests == 1
            ? Json(Response(Bar(100m), token: "next"))
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new HttpClient(handler);
        using var provider = new AlpacaHistoricalDataProvider("synthetic-key", "synthetic-secret",
            rateLimitPerMinute: 60000, httpClient: client);

        (await new HistoricalCloseMarkPriceSource(provider).GetMarkPriceAsync("AAPL", Session)).Should().BeNull();
        requests.Should().Be(2);
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("missing-date")]
    [InlineData("invalid-close")]
    [InlineData("missing-continuation")]
    [InlineData("conflicting-session")]
    [InlineData("out-of-window")]
    [InlineData("partial-response")]
    public async Task InvalidRawResponse_IsUnavailable(string defect)
    {
        var body = defect switch
        {
            "symbol" => Response(Bar(100m)).Replace("AAPL", "MSFT", StringComparison.Ordinal),
            "missing-date" => Response("""{"o":100,"h":100,"l":100,"c":100,"v":10}"""),
            "invalid-close" => Response(Bar(0m)),
            "missing-continuation" => $$"""{"symbol":"AAPL","bars":[{{Bar(100m)}}]}""",
            "conflicting-session" => Response($"{Bar(100m)},{Bar(101m)}"),
            "out-of-window" => Response(Bar(100m, "2026-07-03")),
            _ => Response(Bar(100m))
        };
        using var handler = new ResponseHandler(_ => Json(body,
            defect == "partial-response" ? HttpStatusCode.PartialContent : HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        using var provider = new AlpacaHistoricalDataProvider("synthetic-key", "synthetic-secret", httpClient: client);

        (await new HistoricalCloseMarkPriceSource(provider).GetMarkPriceAsync("AAPL", Session)).Should().BeNull();
    }

    [Fact]
    public async Task RawRequest_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new CancellationHandler();
        using var client = new HttpClient(handler);
        using var provider = new AlpacaHistoricalDataProvider("synthetic-key", "synthetic-secret", httpClient: client);
        var request = new HistoricalCloseMarkPriceSource(provider).GetMarkPriceAsync("AAPL", Session, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var act = () => request;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string Bar(decimal price, string session = "2026-07-02")
    {
        var value = price.ToString(CultureInfo.InvariantCulture);
        return $$"""{"t":"{{session}}T04:00:00Z","o":{{value}},"h":{{value}},"l":{{value}},"c":{{value}},"v":10}""";
    }

    private static string Response(string bars, string? token = null)
        => $$"""{"symbol":"AAPL","bars":[{{bars}}],"next_page_token":{{(token is null ? "null" : $"\"{token}\"")}}}""";

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body) };

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(response(request));
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("A cancelled request cannot return a close.");
        }
    }
}
