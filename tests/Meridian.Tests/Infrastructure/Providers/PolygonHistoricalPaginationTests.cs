using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Polygon;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class PolygonHistoricalPaginationTests
{
    private static readonly DateOnly From = new(2026, 1, 2);
    private static readonly DateOnly To = new(2026, 1, 5);
    private const string NextPage = "https://api.polygon.io/v3/page?cursor=second";
    private const string ApiKey = "test+key&value";

    [Theory]
    [InlineData("daily")]
    [InlineData("intraday")]
    [InlineData("splits")]
    [InlineData("dividends")]
    public async Task OverlappingPages_ReturnEveryDistinctResultInOrder(string kind)
    {
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock,
            Page([Row(kind, 2), Row(kind, 1)], NextPage),
            Page([Row(kind, 2), Row(kind, 3)]));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        var identities = await FetchIdentitiesAsync(provider, kind);

        identities.Should().Equal(1L, 2L, 3L);
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().AllSatisfy(uri =>
            uri.Query.Should().Contain("apiKey=test%2Bkey%26value"));
        handler.Requests[1].Query.Should().Contain("cursor=second");
        provider.GetRateLimitInfo().RequestsMade.Should().Be(2, "each page consumes a request slot");
    }

    [Fact]
    public async Task IntradayPages_PreserveDifferentBarsWithinOneSessionAndFilterRequestedDates()
    {
        var clock = new AdvancingClock();
        var first = Timestamp(1);
        using var handler = new SequenceHandler(clock,
            Page([Aggregate(first - 86_400_000), Aggregate(first + 60_000)], NextPage),
            Page([Aggregate(first + 60_000), Aggregate(first), Aggregate(first + 120_000), Aggregate(Timestamp(5))]));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        var bars = await provider.GetIntradayBarsAsync("AAPL", "1min", From, From);

        bars.Select(bar => bar.SequenceNumber).Should().Equal(first, first + 60_000, first + 120_000);
        bars.Should().OnlyContain(bar => bar.SessionDate == From);
    }

    [Theory]
    [InlineData("splits")]
    [InlineData("dividends")]
    public async Task CorporateActions_DeduplicateByProviderIdAndPreserveDistinctEqualValuedActions(string kind)
    {
        object Action(string id, int value) => kind == "splits"
            ? new { id, execution_date = "2026-01-02", split_from = 1, split_to = value }
            : new { id, ex_dividend_date = "2026-01-02", cash_amount = value, currency = "USD" };
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock,
            Page([Action("first-action", 2)], NextPage),
            Page([Action("first-action", 3), Action("second-action", 2)]));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        if (kind == "splits")
        {
            var actions = await provider.GetSplitsAsync("AAPL");
            actions.Select(action => action.SplitTo).Should().Equal(2m, 2m);
        }
        else
        {
            var actions = await provider.GetDividendsAsync("AAPL");
            actions.Select(action => action.Amount).Should().Equal(2m, 2m);
        }
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("intraday")]
    [InlineData("splits")]
    [InlineData("dividends")]
    public async Task EmptyIntermediatePage_StillFollowsContinuation(string kind)
    {
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock, Page([], NextPage), Page([Row(kind, 1)]));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        (await FetchIdentitiesAsync(provider, kind)).Should().Equal(1L);
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("intraday")]
    [InlineData("splits")]
    [InlineData("dividends")]
    public async Task RepeatedCursor_FailsWithoutFetchingItAgainOrReturningPartialData(string kind)
    {
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock,
            Page([Row(kind, 1)], NextPage + "&limit=2"),
            Page([Row(kind, 2)], "https://api.polygon.io/v3/page?apiKey=stale&limit=2&cursor=second"));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        Func<Task> act = () => FetchIdentitiesAsync(provider, kind);

        await act.Should().ThrowAsync<DataProviderException>().WithMessage("*repeated*cursor*incomplete*");
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("intraday")]
    [InlineData("splits")]
    [InlineData("dividends")]
    public async Task CancellationWhileWaitingForNextPage_PropagatesWithoutSendingIt(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new SequenceHandler(null, Page([Row(kind, 1)], NextPage))
        {
            OnRequest = () => cancellation.CancelAfter(TimeSpan.FromMilliseconds(50))
        };
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http);

        Func<Task> act = () => FetchIdentitiesAsync(provider, kind, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("daily", HttpStatusCode.InternalServerError)]
    [InlineData("intraday", HttpStatusCode.NotFound)]
    [InlineData("splits", HttpStatusCode.Unauthorized)]
    [InlineData("dividends", HttpStatusCode.TooManyRequests)]
    public async Task SecondPageHttpFailure_PropagatesTypedFailure(string kind, HttpStatusCode status)
    {
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock,
            Page([Row(kind, 1)], NextPage),
            Response("{}", status));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        Func<Task> act = () => FetchIdentitiesAsync(provider, kind);

        if (status == HttpStatusCode.Unauthorized)
            await act.Should().ThrowAsync<ConnectionException>();
        else if (status == HttpStatusCode.TooManyRequests)
            await act.Should().ThrowAsync<RateLimitException>();
        else
            await act.Should().ThrowAsync<DataProviderException>();
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("daily", "{\"status\":\"ERROR\",\"error\":\"request failed\"}")]
    [InlineData("intraday", "{\"status\":\"OK\",\"resultsCount\":4}")]
    [InlineData("splits", "{\"status\":\"OK\"}")]
    [InlineData("dividends", "{}")]
    public async Task SecondPageWithoutSuccessfulCoverageEvidence_Throws(string kind, string json)
    {
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock, Page([Row(kind, 1)], NextPage), Response(json));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        Func<Task> act = () => FetchIdentitiesAsync(provider, kind);

        await act.Should().ThrowAsync<DataProviderException>();
    }

    [Theory]
    [InlineData("https://untrusted.example/page?cursor=second")]
    [InlineData("http://api.polygon.io/page?cursor=second")]
    public async Task UntrustedContinuation_FailsBeforeSendingCredentials(string continuation)
    {
        var clock = new AdvancingClock();
        using var handler = new SequenceHandler(clock, Page([Row("daily", 1)], continuation));
        using var http = new HttpClient(handler);
        using var provider = new PolygonHistoricalDataProvider(ApiKey, http, timeProvider: clock);

        Func<Task> act = () => provider.GetAdjustedDailyBarsAsync("AAPL", From, To);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().ContainSingle();
    }

    private static async Task<IReadOnlyList<long>> FetchIdentitiesAsync(
        PolygonHistoricalDataProvider provider, string kind, CancellationToken ct = default)
        => kind switch
        {
            "daily" => (await provider.GetAdjustedDailyBarsAsync("AAPL", From, To, ct))
                .Select(bar => (long)(bar.SessionDate.DayNumber - From.DayNumber + 1)).ToList(),
            "intraday" => (await provider.GetIntradayBarsAsync("AAPL", "1min", From, To, ct))
                .Select(bar => (bar.SequenceNumber - Timestamp(1)) / 86_400_000 + 1).ToList(),
            "splits" => (await provider.GetSplitsAsync("AAPL", ct))
                .Select(split => (long)(split.ExDate.DayNumber - From.DayNumber + 1)).ToList(),
            "dividends" => (await provider.GetDividendsAsync("AAPL", ct))
                .Select(dividend => (long)(dividend.ExDate.DayNumber - From.DayNumber + 1)).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static object Row(string kind, int day)
        => kind switch
        {
            "daily" or "intraday" => Aggregate(Timestamp(day)),
            "splits" => new { execution_date = From.AddDays(day - 1).ToString("yyyy-MM-dd"), split_from = 1, split_to = day + 1 },
            "dividends" => new { ex_dividend_date = From.AddDays(day - 1).ToString("yyyy-MM-dd"), cash_amount = day, currency = "USD" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static object Aggregate(long timestamp) => new { o = 100, h = 105, l = 95, c = 102, v = 1000, t = timestamp };

    private static long Timestamp(int day)
        => new DateTimeOffset(From.AddDays(day - 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static HttpResponseMessage Page(object[] results, string? next = null)
        => Response(JsonSerializer.Serialize(new { status = "OK", results, next_url = next }));

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class AdvancingClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 6, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance() => _utcNow += TimeSpan.FromSeconds(13);
    }

    private sealed class SequenceHandler(AdvancingClock? clock, params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public Action? OnRequest { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            OnRequest?.Invoke();
            clock?.Advance();
            return Task.FromResult(responses[Requests.Count - 1]);
        }
    }
}
