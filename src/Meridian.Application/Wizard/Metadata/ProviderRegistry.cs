namespace Meridian.Application.Wizard.Metadata;

/// <summary>
/// Central registry of all supported data providers with their metadata.
/// Replaces the duplicated dictionaries in <c>ConfigurationWizard</c> and
/// <c>AutoConfigurationService</c>.
/// </summary>
public static class ProviderRegistry
{
    /// <summary>All provider descriptors, ordered by ascending priority.</summary>
    public static IReadOnlyList<ProviderDescriptor> All { get; } = BuildRegistry();

    /// <summary>Returns the descriptor for the given provider name, or <c>null</c>.</summary>
    public static ProviderDescriptor? Get(string name) =>
        _byName.TryGetValue(name, out var d) ? d : null;

    // ── Private lookup cache ─────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, ProviderDescriptor> _byName =
        BuildRegistry().ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    // ── Build ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ProviderDescriptor> BuildRegistry() =>
        new ProviderDescriptor[]
        {
            new(
                Name: "IB",
                DisplayName: "Interactive Brokers",
                RequiredEnvVars: Array.Empty<string>(),   // Uses TWS, not API keys
                AlternativeEnvVars: Array.Empty<string>(),
                Capabilities: new[] { "RealTime", "Historical", "Trades", "Quotes", "L2Depth" },
                Priority: 1,
                SignupUrl: "https://www.interactivebrokers.com/en/trading/tws.php",
                DocsUrl: "https://interactivebrokers.github.io/tws-api/",
                FreeTierDescription: "Requires brokerage account; TWS or IB Gateway must be running"
            ),
            new(
                Name: "NYSE",
                DisplayName: "NYSE Direct",
                RequiredEnvVars: new[] { "NYSE_API_KEY" },
                AlternativeEnvVars: new[] { "MDC_NYSE_API_KEY", "NYSE_API_SECRET" },
                Capabilities: new[] { "RealTime", "Trades", "Quotes", "L2Depth" },
                Priority: 3,
                SignupUrl: "https://www.nyse.com/market-data",
                DocsUrl: "https://www.nyse.com/market-data",
                FreeTierDescription: "Professional subscription required"
            ),
            new(
                Name: "Alpaca",
                DisplayName: "Alpaca Markets",
                RequiredEnvVars: new[] { "ALPACA_KEY_ID", "ALPACA_SECRET_KEY" },
                AlternativeEnvVars: new[] { "MDC_ALPACA_KEY_ID", "MDC_ALPACA_SECRET_KEY" },
                Capabilities: new[] { "RealTime", "Historical", "Trades", "Quotes" },
                Priority: 5,
                SignupUrl: "https://app.alpaca.markets/signup",
                DocsUrl: "https://docs.alpaca.markets/docs/getting-started",
                FreeTierDescription: "Free: IEX feed (~10% of trades), unlimited paper trading"
            ),
            new(
                Name: "Polygon",
                DisplayName: "Polygon.io",
                RequiredEnvVars: new[] { "POLYGON_API_KEY" },
                AlternativeEnvVars: new[] { "MDC_POLYGON_API_KEY" },
                Capabilities: new[] { "RealTime", "Historical", "Trades", "Quotes", "Aggregates" },
                Priority: 10,
                SignupUrl: "https://polygon.io/dashboard/signup",
                DocsUrl: "https://polygon.io/docs/stocks/getting-started",
                FreeTierDescription: "Free: 5 API calls/min, end-of-day data"
            ),
            new(
                Name: "Yahoo",
                DisplayName: "Yahoo Finance",
                RequiredEnvVars: Array.Empty<string>(),
                AlternativeEnvVars: Array.Empty<string>(),
                Capabilities: new[] { "Historical", "Daily" },
                Priority: 10,
                SignupUrl: "https://finance.yahoo.com",
                DocsUrl: "https://finance.yahoo.com",
                FreeTierDescription: "Free: daily historical data, no API key required"
            ),
            new(
                Name: "Tiingo",
                DisplayName: "Tiingo",
                RequiredEnvVars: new[] { "TIINGO_API_TOKEN" },
                AlternativeEnvVars: new[] { "TIINGO_TOKEN", "MDC_TIINGO_TOKEN" },
                Capabilities: new[] { "Historical", "Daily" },
                Priority: 15,
                SignupUrl: "https://www.tiingo.com/account/api/token",
                DocsUrl: "https://www.tiingo.com/documentation/general/overview",
                FreeTierDescription: "Free: 500 requests/hour, daily historical data"
            ),
            new(
                Name: "Finnhub",
                DisplayName: "Finnhub",
                RequiredEnvVars: new[] { "FINNHUB_API_KEY" },
                AlternativeEnvVars: new[] { "MDC_FINNHUB_API_KEY" },
                Capabilities: new[] { "Historical", "Daily", "Fundamentals" },
                Priority: 18,
                SignupUrl: "https://finnhub.io/register",
                DocsUrl: "https://finnhub.io/docs/api",
                FreeTierDescription: "Free: 60 API calls/min, US stock data"
            ),
            new(
                Name: "Stooq",
                DisplayName: "Stooq",
                RequiredEnvVars: Array.Empty<string>(),
                AlternativeEnvVars: Array.Empty<string>(),
                Capabilities: new[] { "Historical", "Daily" },
                Priority: 20,
                SignupUrl: "https://stooq.com",
                DocsUrl: "https://stooq.com",
                FreeTierDescription: "Free: daily historical data, no API key required"
            ),
            new(
                Name: "AlphaVantage",
                DisplayName: "Alpha Vantage",
                RequiredEnvVars: new[] { "ALPHA_VANTAGE_API_KEY" },
                AlternativeEnvVars: new[] { "ALPHAVANTAGE_API_KEY", "MDC_ALPHA_VANTAGE_API_KEY" },
                Capabilities: new[] { "Historical", "Daily", "Intraday" },
                Priority: 25,
                SignupUrl: "https://www.alphavantage.co/support/#api-key",
                DocsUrl: "https://www.alphavantage.co/documentation/",
                FreeTierDescription: "Free: 25 requests/day, daily historical data"
            ),
            new(
                Name: "FRED",
                DisplayName: "FRED Economic Data",
                RequiredEnvVars: new[] { "FRED_API_KEY" },
                AlternativeEnvVars: new[] { "FRED__APIKEY", "MDC_FRED_API_KEY" },
                Capabilities: new[] { "Historical", "Daily", "EconomicSeries" },
                Priority: 28,
                SignupUrl: "https://fredaccount.stlouisfed.org/apikeys",
                DocsUrl: "https://fred.stlouisfed.org/docs/api/fred/",
                FreeTierDescription: "Free: economic time series via FRED API"
            ),
            new(
                Name: "NasdaqDataLink",
                DisplayName: "Nasdaq Data Link (Quandl)",
                RequiredEnvVars: new[] { "NASDAQ_API_KEY" },
                AlternativeEnvVars: new[] { "MDC_NASDAQ_API_KEY", "QUANDL_API_KEY" },
                Capabilities: new[] { "Historical", "Daily", "AdjustedPrices", "Dividends", "Splits" },
                Priority: 30,
                SignupUrl: "https://data.nasdaq.com/sign-up",
                DocsUrl: "https://docs.data.nasdaq.com/",
                FreeTierDescription: "Free tier available with limited daily calls"
            ),
        }.OrderBy(d => d.Priority).ToArray();
}
