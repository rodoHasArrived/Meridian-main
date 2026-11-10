using System.Globalization;
using System.Net;
using System.Text;
using System.Web;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Fixtures mirror the Trading API announcements contract as the paper host actually serves it:
/// underscore path, mandatory <c>since</c>/<c>until</c> no more than 90 days apart, string-encoded
/// amounts, and <c>target_symbol</c> rather than a plain symbol field.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlpacaCorporateActionProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);

    private const string CashDividend = """
        {"id":"7808fce5-4d71-4c5f-9636-80a34a0ed87c","corporate_action_id":"000000100_AC26","ca_type":"dividend",
         "ca_sub_type":"cash","initiating_symbol":"ACME","initiating_original_cusip":"000000100","target_symbol":"ACME",
         "target_original_cusip":"000000100","declaration_date":"2026-09-13","effective_date":"2026-09-15",
         "ex_date":"2026-09-15","record_date":"2026-09-15","payable_date":"2026-10-01","cash":"0.53","old_rate":"1","new_rate":"1"}
        """;

    private const string ForwardSplit = """
        {"id":"2db36951-3b75-4839-a249-26aa901e1bc7","corporate_action_id":"3173333","ca_type":"split",
         "ca_sub_type":"stock_split","initiating_symbol":"","initiating_original_cusip":"","target_symbol":"ACME",
         "target_original_cusip":"000000100","declaration_date":null,"effective_date":null,
         "ex_date":"2026-06-10","record_date":"2026-06-06","payable_date":"2026-06-07","cash":"0","old_rate":"1","new_rate":"10"}
        """;

    private const string ReverseSplit = """
        {"id":"1505f209-4dd0-49b0-ab67-dc85e1dc1181","corporate_action_id":"3173284","ca_type":"split",
         "ca_sub_type":"reverse_split","initiating_symbol":"","initiating_original_cusip":"","target_symbol":"ACME",
         "target_original_cusip":"000000100","effective_date":"2025-10-29","ex_date":"2025-10-29",
         "record_date":"2025-10-29","payable_date":"2025-10-29","cash":"0","old_rate":"40","new_rate":"1"}
        """;

    [Fact]
    public async Task FetchAsync_ReadsStringEncodedAnnouncementsFromTheTradingApiContract()
    {
        var securityId = Guid.NewGuid();
        var requests = new List<Uri>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            request.Headers.GetValues("APCA-API-KEY-ID").Should().Equal("test-key");
            request.Headers.GetValues("APCA-API-SECRET-KEY").Should().Equal("test-secret");
            var (since, until) = Window(request.RequestUri!);
            return Json(Contained(since, until, CashDividend, ForwardSplit, ReverseSplit));
        });
        var provider = CreateSut(handler, useSandbox: true);

        var results = await provider.FetchAsync("ACME", securityId, CancellationToken.None);

        provider.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.AcceptanceEligible);
        results.Select(result => (result.ActionType, result.ExDate)).Should().Equal(
            ("ReverseStockSplit", new DateOnly(2025, 10, 29)),
            ("StockSplit", new DateOnly(2026, 6, 10)),
            ("Dividend", new DateOnly(2026, 9, 15)));
        results.Should().OnlyContain(result => result.SecurityId == securityId && result.SourceProvider == "alpaca");

        var reverse = results[0];
        reverse.SplitFromFactor.Should().Be(40m);
        reverse.SplitToFactor.Should().Be(1m);
        reverse.Amount.Should().BeNull("a split announcement's zero cash is not a payment");

        var split = results[1];
        split.SplitFromFactor.Should().Be(1m);
        split.SplitToFactor.Should().Be(10m);
        split.RecordDate.Should().Be(new DateOnly(2026, 6, 6));

        var dividend = results[2];
        dividend.Amount.Should().Be(0.53m);
        dividend.RecordDate.Should().Be(new DateOnly(2026, 9, 15));
        dividend.PayableDate.Should().Be(new DateOnly(2026, 10, 1));
        dividend.Currency.Should().BeNull("the announcement carries no currency; the ingest binds it from the security");
        dividend.SourceEventId.Should().Be("7808fce5-4d71-4c5f-9636-80a34a0ed87c");

        results.Should().OnlyContain(result =>
            result.SourceEventVersion!.StartsWith("payload-sha256:", StringComparison.Ordinal)
            && result.EvidenceHash!.Length == 64
            && result.EvidenceHash.All(character =>
                (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))
            && result.EvidenceReference ==
               $"alpaca://corporate-actions/announcements/{result.SourceEventId}/versions/{result.EvidenceHash}"
            && result.ReleaseStatus == CorporateActionProviderReleaseStatusDto.AcceptanceEligible);

        requests.Should().OnlyContain(uri =>
            uri.Host == "paper-api.alpaca.markets" && uri.AbsolutePath == "/v2/corporate_actions/announcements");
        requests.Select(uri => HttpUtility.ParseQueryString(uri.Query)).Should().OnlyContain(query =>
            query["ca_types"] == "dividend,split" && query["symbol"] == "ACME" && query["date_type"] == "ex_date");
    }

    [Fact]
    public async Task FetchAsync_CoversTheLookbackWithContiguousWindowsTheEndpointAccepts()
    {
        var windows = new List<(DateOnly Since, DateOnly Until)>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            windows.Add(Window(request.RequestUri!));
            return Json("[]");
        });
        var provider = CreateSut(handler, useSandbox: true);

        (await provider.FetchAsync("ACME", Guid.NewGuid(), CancellationToken.None)).Should().BeEmpty();

        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        windows.Should().HaveCount(9, "the default lookback plus the forward horizon is nine full windows");
        windows.Should().OnlyContain(window =>
            window.Until.DayNumber - window.Since.DayNumber <= AlpacaCorporateActionProvider.MaxWindowDays);
        windows[0].Until.Should().Be(today.AddDays(AlpacaCorporateActionProvider.ForwardDays));
        windows[^1].Since.Should().Be(today.AddDays(-AlpacaCorporateActionProvider.DefaultLookbackDays));
        for (var index = 1; index < windows.Count; index++)
            windows[index].Until.Should().Be(windows[index - 1].Since.AddDays(-1), "windows neither overlap nor leave a gap");
    }

    [Theory]
    [InlineData(true, "paper-api.alpaca.markets")]
    [InlineData(false, "api.alpaca.markets")]
    public async Task FetchAsync_UsesTheTradingHostOfTheSelectedEnvironment(bool useSandbox, string expectedHost)
    {
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        using var handler = new StubHttpMessageHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return Json("[]");
        });

        await CreateSut(handler, useSandbox).FetchAsync("ACME", Guid.NewGuid(), CancellationToken.None);

        hosts.Should().Equal(expectedHost);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.UnprocessableEntity, null)]
    [InlineData(HttpStatusCode.OK, "{\"code\":40010001,\"message\":\"invalid param\"}")]
    [InlineData(HttpStatusCode.OK, "[{\"id\":\"x\",\"ca_type\":\"dividend\",\"target_symbol\":\"ACME\",\"ex_date\":\"2026-09-15\",\"cash\":\"not-a-number\"}]")]
    public async Task FetchAsync_WhenAnyWindowCannotBeRead_ReturnsNothingRatherThanAPartialHistory(
        HttpStatusCode failingStatus, string? failingBody)
    {
        var calls = 0;
        using var handler = new StubHttpMessageHandler(request =>
        {
            calls++;
            var (since, until) = Window(request.RequestUri!);
            // The newest windows succeed and hold a real dividend; a later window fails.
            return calls < 3
                ? Json(Contained(since, until, CashDividend))
                : new HttpResponseMessage(failingStatus)
                {
                    Content = new StringContent(failingBody ?? "{\"message\":\"refused\"}", Encoding.UTF8, "application/json"),
                };
        });

        var results = await CreateSut(handler, useSandbox: true).FetchAsync("ACME", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty("a history with an unread window must not look complete");
        calls.Should().Be(3, "the walk stops at the first unreadable window");
    }

    [Fact]
    public async Task FetchAsync_DoesNotBindAnAnnouncementThatNamesAnotherSecurity()
    {
        var foreign = CashDividend.Replace("\"ACME\"", "\"OTHER\"", StringComparison.Ordinal);
        using var handler = new StubHttpMessageHandler(request =>
        {
            var (since, until) = Window(request.RequestUri!);
            return Json(Contained(since, until, foreign));
        });

        var results = await CreateSut(handler, useSandbox: true).FetchAsync("ACME", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_BindsByTargetSymbolAndFallsBackToInitiatingOnlyWhenThereIsNoTarget()
    {
        // The initiating symbol matches but the action applies to another security.
        var appliesElsewhere = CashDividend
            .Replace("\"target_symbol\":\"ACME\"", "\"target_symbol\":\"OTHER\"", StringComparison.Ordinal)
            .Replace("7808fce5-4d71-4c5f-9636-80a34a0ed87c", "11111111-1111-1111-1111-111111111111", StringComparison.Ordinal);
        // No target at all: the initiating symbol is the only name the announcement carries.
        var initiatingOnly = CashDividend
            .Replace("\"target_symbol\":\"ACME\",", string.Empty, StringComparison.Ordinal)
            .Replace("7808fce5-4d71-4c5f-9636-80a34a0ed87c", "22222222-2222-2222-2222-222222222222", StringComparison.Ordinal);
        using var handler = new StubHttpMessageHandler(request =>
        {
            var (since, until) = Window(request.RequestUri!);
            return Json(Contained(since, until, appliesElsewhere, initiatingOnly));
        });

        var results = await CreateSut(handler, useSandbox: true).FetchAsync(" ACME ", Guid.NewGuid(), CancellationToken.None);

        results.Should().ContainSingle().Which.SourceEventId.Should().Be("22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public async Task FetchAsync_WhenARequestTimesOut_ReturnsNothingRatherThanReportingCancellation()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("The request timed out."));

        // The caller's token is not cancelled; an HttpClient timeout is a failed read.
        var results = await CreateSut(handler, useSandbox: true).FetchAsync("ACME", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_PropagatesCancellation()
    {
        using var handler = new StubHttpMessageHandler(_ => Json("[]"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var fetch = () => CreateSut(handler, useSandbox: true).FetchAsync("ACME", Guid.NewGuid(), cancelled.Token);

        await fetch.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void BuildExDateWindows_SplitsARangeIntoNonOverlappingWindowsNewestFirst()
    {
        var earliest = new DateOnly(2026, 1, 1);
        var latest = new DateOnly(2026, 7, 15);

        var windows = AlpacaCorporateActionProvider.BuildExDateWindows(earliest, latest);

        windows.Should().Equal(
            (new DateOnly(2026, 4, 16), new DateOnly(2026, 7, 15)),
            (new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 15)),
            (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 14)));
        AlpacaCorporateActionProvider.BuildExDateWindows(latest, earliest).Should().BeEmpty();
        AlpacaCorporateActionProvider.BuildExDateWindows(earliest, earliest).Should().Equal((earliest, earliest));
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

    private static AlpacaCorporateActionProvider CreateSut(HttpMessageHandler handler, bool useSandbox)
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
            NullLogger<AlpacaCorporateActionProvider>.Instance,
            credentials: null,
            pacing: AlpacaReferenceRequestPacing.Unpaced,
            useSandbox: useSandbox,
            timeProvider: new FixedTimeProvider(Now),
            lookbackDays: AlpacaCorporateActionProvider.DefaultLookbackDays);
    }

    private static (DateOnly Since, DateOnly Until) Window(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        return (Parse(query["since"]), Parse(query["until"]));

        static DateOnly Parse(string? value) =>
            DateOnly.ParseExact(value!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Serves only the announcements whose ex-date falls in the requested window, as the endpoint does.</summary>
    private static string Contained(DateOnly since, DateOnly until, params string[] announcements)
    {
        var served = announcements.Where(announcement =>
        {
            using var document = System.Text.Json.JsonDocument.Parse(announcement);
            var exDate = DateOnly.ParseExact(
                document.RootElement.GetProperty("ex_date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return exDate >= since && exDate <= until;
        });
        return $"[{string.Join(',', served)}]";
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
