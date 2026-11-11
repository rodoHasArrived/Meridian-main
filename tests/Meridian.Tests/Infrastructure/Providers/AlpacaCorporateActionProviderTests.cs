using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Fixtures are the Market Data corporate-actions response as the real API returns it
/// (identifiers shortened): one group per action type, numeric rates, and no currency.
/// </summary>
public sealed class AlpacaCorporateActionProviderTests
{
    private const string AppleActions =
        """
        {"corporate_actions":{
          "cash_dividends":[
            {"cusip":"","ex_date":"2016-02-04","foreign":false,"id":"div-2016","process_date":"2016-02-04","rate":0.52,"special":false,"symbol":"AAPL"},
            {"cusip":"037833100","ex_date":"2024-02-09","foreign":false,"id":"div-2024","payable_date":"2024-02-15","process_date":"2024-02-15","rate":0.24,"record_date":"2024-02-12","special":false,"symbol":"AAPL"}],
          "forward_splits":[
            {"cusip":"037833100","due_bill_redemption_date":"2020-09-01","ex_date":"2020-08-31","id":"split-2020","new_rate":4,"old_rate":1,"payable_date":"2020-08-28","process_date":"2020-08-31","record_date":"2020-08-24","symbol":"AAPL"}]},
         "next_page_token":null}
        """;

    [Fact]
    public async Task FetchAsync_DividendsAndSplit_ReturnsCommandsFromEachGroup()
    {
        var securityId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => Json(AppleActions));
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("AAPL", securityId, CancellationToken.None);

        provider.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
        results.Select(result => result.ActionType).Should().Equal("Dividend", "Dividend", "StockSplit");
        results.Should().OnlyContain(result =>
            result.SecurityId == securityId
            && result.SourceProvider == "alpaca"
            && result.Currency == null
            && result.SourceEventVersion!.StartsWith("payload-sha256:", StringComparison.Ordinal)
            && result.EvidenceHash!.Length == 64
            && result.EvidenceHash.All(character =>
                (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))
            && result.EvidenceReference ==
               $"alpaca://corporate-actions/announcements/{result.SourceEventId}/versions/{result.EvidenceHash}"
            && result.ReleaseStatus == CorporateActionProviderReleaseStatusDto.ReviewOnly);
        results[0].Should().Match<Meridian.Infrastructure.Adapters.Core.CorporateActionCommand>(dividend =>
            dividend.ExDate == new DateOnly(2016, 2, 4) && dividend.Amount == 0.52m
            && dividend.RecordDate == null && dividend.PayableDate == null,
            "older records omit the record and payable dates");
        results[1].Should().Match<Meridian.Infrastructure.Adapters.Core.CorporateActionCommand>(dividend =>
            dividend.Amount == 0.24m && dividend.RecordDate == new DateOnly(2024, 2, 12)
            && dividend.PayableDate == new DateOnly(2024, 2, 15) && dividend.SplitFromFactor == null);
        results[2].Should().Match<Meridian.Infrastructure.Adapters.Core.CorporateActionCommand>(split =>
            split.ExDate == new DateOnly(2020, 8, 31) && split.SplitFromFactor == 1m && split.SplitToFactor == 4m
            && split.Amount == null);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Host.Should().Be("data.alpaca.markets");
        request.AbsolutePath.Should().Be("/v1/corporate-actions");
        request.Query.Should().Contain("symbols=AAPL").And.Contain("types=cash_dividend,forward_split,reverse_split")
            .And.Contain("start=1990-01-01");
    }

    [Fact]
    public async Task FetchAsync_ReverseSplitAndSpecialDividend_AreClassifiedFromTheRecord()
    {
        var provider = CreateSut(new RecordingHandler(_ => Json(
            """
            {"corporate_actions":{
              "cash_dividends":[{"ex_date":"2023-12-01","id":"special","rate":2.5,"special":true,"symbol":"GE"}],
              "reverse_splits":[{"ex_date":"2021-08-02","id":"reverse","new_cusip":"369604301","new_rate":1,"old_cusip":"369604103","old_rate":8,"symbol":"GE"}]},
             "next_page_token":null}
            """)));

        var results = await provider.FetchAsync("GE", Guid.NewGuid(), CancellationToken.None);

        results.Select(result => (result.ActionType, result.Description)).Should().Equal(
            ("Dividend", "Special cash dividend"), ("ReverseStockSplit", "Reverse split"));
        results[1].SplitFromFactor.Should().Be(8m);
        results[1].SplitToFactor.Should().Be(1m);
    }

    [Fact]
    public async Task FetchAsync_PagedHistory_FollowsThePageToken()
    {
        var handler = new RecordingHandler(request => Json(request.RequestUri!.Query.Contains("page_token=next-1", StringComparison.Ordinal)
            ? """{"corporate_actions":{"cash_dividends":[{"ex_date":"2024-05-10","id":"second","rate":0.25,"symbol":"AAPL"}]},"next_page_token":null}"""
            : """{"corporate_actions":{"cash_dividends":[{"ex_date":"2024-02-09","id":"first","rate":0.24,"symbol":"AAPL"}]},"next_page_token":"next-1"}"""));
        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Select(result => result.SourceEventId).Should().Equal("first", "second");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAsync_UnusableRecords_AreLeftOut()
    {
        var provider = CreateSut(new RecordingHandler(_ => Json(
            """
            {"corporate_actions":{
              "cash_dividends":[
                {"ex_date":"2024-02-09","id":"other-symbol","rate":0.24,"symbol":"MSFT"},
                {"ex_date":"2024-02-09","id":"no-rate","symbol":"AAPL"},
                {"ex_date":"not-a-date","id":"bad-date","rate":0.24,"symbol":"AAPL"}],
              "forward_splits":[{"ex_date":"2020-08-31","id":"no-ratio","new_rate":4,"symbol":"AAPL"}]},
             "next_page_token":null}
            """)));

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty("a record for another symbol, or without an amount, date or split ratio, cannot be applied");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FetchAsync_RefusedRequest_DegradesToEmpty(HttpStatusCode status)
    {
        var provider = CreateSut(new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("<html>refused</html>"),
        }));

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_RefusedSecondPage_ReturnsNoPartialHistory()
    {
        var provider = CreateSut(new RecordingHandler(request =>
            request.RequestUri!.Query.Contains("page_token=", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : Json("""{"corporate_actions":{"cash_dividends":[{"ex_date":"2024-02-09","id":"first","rate":0.24,"symbol":"AAPL"}]},"next_page_token":"next-1"}""")));

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty("half a history must not be returned as if it were all of it");
    }

    [Fact]
    public async Task FetchAsync_WithoutCredentials_ReturnsEmptyWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = string.Empty,
                ["ALPACA_SECRET_KEY"] = string.Empty,
            })
            .Build();
        var provider = new AlpacaCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private const string FirstPageWithNextToken =
        """{"corporate_actions":{"cash_dividends":[{"ex_date":"2024-02-09","id":"first","rate":0.24,"symbol":"AAPL"}]},"next_page_token":"next-1"}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchWithOutcomeAsync_SecondPageFails_ReportsIncompleteAndWithholdsPartialHistory(
        bool secondPageThrows)
    {
        var handler = new RecordingHandler(request =>
        {
            if (!request.RequestUri!.Query.Contains("page_token=next-1", StringComparison.Ordinal))
                return Json(FirstPageWithNextToken);
            if (secondPageThrows)
                throw new HttpRequestException("synthetic connection reset");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("synthetic upstream failure body"),
            };
        });
        var provider = CreateSut(handler);

        var outcome = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        outcome.Status.Should().Be(CorporateActionFetchStatus.Incomplete);
        outcome.IsComplete.Should().BeFalse("a failure after page one must not be reported as a complete result");
        outcome.Observations.Should().BeEmpty("half a history must not be returned as if it were all of it");
        outcome.WithheldObservationCount.Should().Be(1);
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
        outcome.Reason.Should().Contain("page 2");
        outcome.Reason.Should().NotContain("test-key").And.NotContain("test-secret")
            .And.NotContain("synthetic upstream failure body");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_RepeatedPageToken_StopsAndReportsIncomplete()
    {
        // Every page names the same next page, so following the token would never end.
        var handler = new RecordingHandler(_ => Json(
            """{"corporate_actions":{"cash_dividends":[{"ex_date":"2024-02-09","id":"first","rate":0.24,"symbol":"AAPL"}]},"next_page_token":"loop"}"""));
        var provider = CreateSut(handler);

        var outcome = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        outcome.Status.Should().Be(CorporateActionFetchStatus.Incomplete);
        outcome.IsComplete.Should().BeFalse();
        outcome.Observations.Should().BeEmpty();
        outcome.WithheldObservationCount.Should().Be(2);
        outcome.Reason.Should().Contain("next_page_token");
        handler.Requests.Should().HaveCount(2, "the fetch stops as soon as a page token repeats");
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_EmptySuccessfulResponse_IsCompleteAndEmpty()
    {
        var handler = new RecordingHandler(_ => Json("""{"corporate_actions":{},"next_page_token":null}"""));
        var provider = CreateSut(handler);

        var outcome = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        outcome.Status.Should().Be(CorporateActionFetchStatus.Complete);
        outcome.IsComplete.Should().BeTrue();
        outcome.Observations.Should().BeEmpty();
        outcome.Reason.Should().BeNull("an empty history is an answer, not an error");
        outcome.WithheldObservationCount.Should().Be(0);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_CompletePagedHistory_ReturnsReviewOnlyObservations()
    {
        var provider = CreateSut(new RecordingHandler(request => Json(
            request.RequestUri!.Query.Contains("page_token=next-1", StringComparison.Ordinal)
                ? """{"corporate_actions":{"cash_dividends":[{"ex_date":"2024-05-10","id":"second","rate":0.25,"symbol":"AAPL"}]},"next_page_token":null}"""
                : FirstPageWithNextToken)));

        var outcome = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        outcome.Status.Should().Be(CorporateActionFetchStatus.Complete);
        outcome.Observations.Select(observation => observation.SourceEventId).Should().Equal("first", "second");
        outcome.Observations.Should().OnlyContain(observation =>
            observation.ReleaseStatus == CorporateActionProviderReleaseStatusDto.ReviewOnly);
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_WithoutCredentials_ReportsUnavailableWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = string.Empty,
                ["ALPACA_SECRET_KEY"] = string.Empty,
            })
            .Build();
        var provider = new AlpacaCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);

        var outcome = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        outcome.Status.Should().Be(CorporateActionFetchStatus.Unavailable);
        outcome.IsComplete.Should().BeFalse("missing credentials are not an empty history");
        outcome.Observations.Should().BeEmpty();
        outcome.Reason.Should().Contain("credentials");
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task FetchWithOutcomeAsync_FirstPageRefused_ReportsUnavailable()
    {
        var provider = CreateSut(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("synthetic refusal body"),
        }));

        var outcome = await provider.FetchWithOutcomeAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        outcome.Status.Should().Be(CorporateActionFetchStatus.Unavailable);
        outcome.Observations.Should().BeEmpty();
        outcome.WithheldObservationCount.Should().Be(0);
        outcome.Reason.Should().Contain("401");
        outcome.Reason.Should().NotContain("synthetic refusal body");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static AlpacaCorporateActionProvider CreateSut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("alpaca-corp-actions").Returns(_ => new HttpClient(handler, disposeHandler: false));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = "test-key",
                ["ALPACA_SECRET_KEY"] = "test-secret",
            })
            .Build();

        return new AlpacaCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
