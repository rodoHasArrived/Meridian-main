using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in, read-only certification of the production Alpaca historical and corporate-action
/// providers against the real Market Data API. A transport guard permits only GET requests to
/// <c>data.alpaca.markets</c>. Every expectation is a settled historical fact, so the result does
/// not depend on when the test runs.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaMarketDataSandboxTests(ITestOutputHelper output)
{
    // The first full trading week of November 2025: five regular sessions, long settled.
    private static readonly DateOnly WeekStart = new(2025, 11, 3);
    private static readonly DateOnly WeekEnd = new(2025, 11, 7);
    private static readonly DateTimeOffset MiddayMinute = new(2025, 11, 5, 17, 0, 0, TimeSpan.Zero);

    [AlpacaMarketDataFact]
    public async Task CorporateActions_ReturnTheKnownSplitsAndDividendsWithEvidence()
    {
        using var market = new MarketDataSession(output);
        var provider = new AlpacaCorporateActionProvider(market, market.Configuration, NullLogger<AlpacaCorporateActionProvider>.Instance);
        var securityId = Guid.NewGuid();

        var apple = await provider.FetchAsync("AAPL", securityId);
        var generalElectric = await provider.FetchAsync("GE", securityId);
        var unknown = await provider.FetchAsync("ZZZZNOTREAL", securityId);

        apple.Should().ContainSingle(action => action.ActionType == "StockSplit")
            .Which.Should().Match<Meridian.Infrastructure.Adapters.Core.CorporateActionCommand>(split =>
                split.ExDate == new DateOnly(2020, 8, 31) && split.SplitFromFactor == 1m && split.SplitToFactor == 4m && split.Amount == null);
        generalElectric.Should().ContainSingle(action => action.ActionType == "ReverseStockSplit")
            .Which.Should().Match<Meridian.Infrastructure.Adapters.Core.CorporateActionCommand>(split =>
                split.ExDate == new DateOnly(2021, 8, 2) && split.SplitFromFactor == 8m && split.SplitToFactor == 1m);
        var dividends = apple.Where(action => action.ActionType == "Dividend").ToArray();
        (dividends.Length >= 40).Should().BeTrue("Apple has paid a quarterly dividend throughout the provider's history");
        dividends.Should().OnlyContain(dividend => dividend.Amount > 0m && dividend.SplitFromFactor == null);
        dividends.Should().Contain(dividend => dividend.ExDate == new DateOnly(2024, 2, 9) && dividend.Amount == 0.24m
            && dividend.PayableDate == new DateOnly(2024, 2, 15));
        apple.Concat(generalElectric).Should().OnlyContain(action =>
            action.SecurityId == securityId && action.SourceProvider == "alpaca"
            && !string.IsNullOrWhiteSpace(action.SourceEventId) && action.EvidenceHash!.Length == 64);
        apple.Select(action => action.SourceEventId).Should().OnlyHaveUniqueItems();
        unknown.Should().BeEmpty("an unlisted ticker has no corporate actions");
    }

    [AlpacaMarketDataFact]
    public async Task DailyBars_ReturnEverySessionAndApplyTheConfiguredAdjustment()
    {
        using var market = new MarketDataSession(output);
        using var adjusted = market.CreateHistoricalProvider(adjustment: "all");
        using var raw = market.CreateHistoricalProvider(adjustment: "raw");

        (await adjusted.IsAvailableAsync()).Should().BeTrue();
        var week = await adjusted.GetDailyBarsAsync("SPY", WeekStart, WeekEnd);
        week.Select(bar => bar.SessionDate).Should().Equal(Enumerable.Range(0, 5).Select(WeekStart.AddDays));
        week.Should().OnlyContain(bar => bar.Low > 0m && bar.Low <= bar.Open && bar.Low <= bar.Close
            && bar.High >= bar.Open && bar.High >= bar.Close && bar.Volume > 0);

        // Apple split four for one on 2020-08-31: the last close before it is about 499 unadjusted.
        var beforeSplit = new DateOnly(2020, 8, 28);
        var rawClose = (await raw.GetAdjustedDailyBarsAsync("AAPL", beforeSplit, beforeSplit)).Single();
        var adjustedClose = (await adjusted.GetAdjustedDailyBarsAsync("AAPL", beforeSplit, beforeSplit)).Single();
        (rawClose.Close is > 480m and < 520m).Should().BeTrue("the raw close predates the split");
        (rawClose.AdjustedClose is null).Should().BeTrue("raw bars carry no adjusted prices");
        (adjustedClose.Close < rawClose.Close / 3.9m).Should().BeTrue("split adjustment divides the pre-split close by at least four");
        adjustedClose.AdjustedClose.Should().Be(adjustedClose.Close);

        var years = await adjusted.GetDailyBarsAsync("SPY", new DateOnly(2021, 1, 4), new DateOnly(2025, 12, 31));
        (years.Count > 1200).Should().BeTrue("five years of sessions must all be returned");
        years.Select(bar => bar.SessionDate).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        (await adjusted.GetDailyBarsAsync("ZZZZNOTREAL", WeekStart, WeekEnd)).Should().BeEmpty();
    }

    [AlpacaMarketDataFact]
    public async Task QuotesTradesAndAuctions_AreReadForOneSymbolWithinTheRequestedLimit()
    {
        using var market = new MarketDataSession(output);
        using var provider = market.CreateHistoricalProvider(adjustment: "all");

        var quotes = await provider.GetHistoricalQuotesAsync("SPY", MiddayMinute, MiddayMinute.AddMinutes(1), limit: 5);
        quotes.Quotes.Should().HaveCount(5);
        quotes.HasMore.Should().BeTrue("a liquid minute holds more than five quotes");
        quotes.Quotes.Should().OnlyContain(quote => quote.Symbol == "SPY" && quote.BidPrice > 0m && quote.AskPrice >= quote.BidPrice
            && quote.Timestamp >= MiddayMinute && quote.Timestamp < MiddayMinute.AddMinutes(1));

        var trades = await provider.GetHistoricalTradesAsync("SPY", MiddayMinute, MiddayMinute.AddMinutes(1), limit: 5);
        trades.Trades.Should().HaveCount(5);
        trades.HasMore.Should().BeTrue();
        trades.Trades.Should().OnlyContain(trade => trade.Symbol == "SPY" && trade.Price > 0m && trade.Size > 0
            && trade.TradeId.All(char.IsAsciiDigit));
        trades.Trades.Select(trade => trade.TradeId).Should().OnlyHaveUniqueItems("each trade keeps its exchange trade id");
        market.RequestCount.Should().Be(2, "a limit bounds the result instead of paging through the whole window");

        var auctions = await provider.GetHistoricalAuctionsAsync("SPY", WeekStart, WeekEnd);
        auctions.Auctions.Select(auction => auction.SessionDate).Should().Equal(Enumerable.Range(0, 5).Select(WeekStart.AddDays));
        auctions.Auctions.Should().OnlyContain(auction => auction.Symbol == "SPY"
            && auction.OpeningAuctions.Count > 0 && auction.ClosingAuctions.Count > 0);
    }

    /// <summary>The guarded transport and credentials shared by one test.</summary>
    private sealed class MarketDataSession : IHttpClientFactory, IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ReadOnlyMarketDataHandler _handler = new();
        private readonly string _key;
        private readonly string _secret;

        public MarketDataSession(ITestOutputHelper output)
        {
            _output = output;
            var key = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName);
            var secret = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName);
            // Assert presence only: test failures must never print a credential value.
            string.IsNullOrWhiteSpace(key).Should().BeFalse("ALPACA_KEY_ID must be configured for the opted-in market-data run");
            string.IsNullOrWhiteSpace(secret).Should().BeFalse("ALPACA_SECRET_KEY must be configured for the opted-in market-data run");
            _key = key!;
            _secret = secret!;
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AlpacaCredentialEnvironment.KeyIdName] = _key,
                [AlpacaCredentialEnvironment.SecretKeyName] = _secret,
            }).Build();
        }

        public IConfiguration Configuration { get; }
        public int RequestCount => _handler.RequestCount;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };

        // Each provider adds its credential headers to the client it is given, so each gets its own.
        public AlpacaHistoricalDataProvider CreateHistoricalProvider(string adjustment) =>
            new(_key, _secret, adjustment: adjustment, httpClient: CreateClient(nameof(AlpacaHistoricalDataProvider)));

        public void Dispose()
        {
            _output.WriteLine("Market Data API requests: {0} (GET only).", RequestCount);
            _handler.Dispose();
        }
    }

    private sealed class ReadOnlyMarketDataHandler : DelegatingHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        public ReadOnlyMarketDataHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (request.Method != HttpMethod.Get || uri is null || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
                || !string.Equals(uri.Host, "data.alpaca.markets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Market-data certification permits only GET requests to data.alpaca.markets.");
            Interlocked.Increment(ref _requestCount);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public sealed class AlpacaMarketDataFactAttribute : FactAttribute
{
    public AlpacaMarketDataFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MERIDIAN_RUN_ALPACA_MARKET_DATA"), "1", StringComparison.Ordinal))
            Skip = "Real Alpaca market-data validation is opt-in: set MERIDIAN_RUN_ALPACA_MARKET_DATA=1 and the documented credentials.";
    }
}
