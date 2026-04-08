using System.Net.Http.Headers;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Http;

/// <summary>
/// Named HttpClient identifiers for IHttpClientFactory.
/// Using constants ensures consistency across the codebase.
/// </summary>
/// <remarks>
/// Implements TD-10: Replace instance HttpClient with IHttpClientFactory.
/// </remarks>
public static class HttpClientNames
{
    // Streaming/Trading providers
    public const string Alpaca = "alpaca";
    public const string AlpacaData = "alpaca-data";
    public const string Polygon = "polygon";
    public const string NYSE = "nyse";

    // Backfill providers
    public const string AlpacaHistorical = "alpaca-historical";
    public const string PolygonHistorical = "polygon-historical";
    public const string TiingoHistorical = "tiingo-historical";
    public const string YahooFinanceHistorical = "yahoo-finance-historical";
    public const string StooqHistorical = "stooq-historical";
    public const string FinnhubHistorical = "finnhub-historical";
    public const string AlphaVantageHistorical = "alpha-vantage-historical";
    public const string NasdaqDataLinkHistorical = "nasdaq-data-link-historical";
    public const string FredHistorical = "fred-historical";
    public const string TwelveDataHistorical = "twelvedata-historical";
    public const string RobinhoodHistorical = "robinhood-historical";
    public const string RobinhoodMarketData = "robinhood-market-data";
    public const string RobinhoodBrokerage = "robinhood-brokerage";
    public const string RobinhoodSymbolSearch = "robinhood-symbol-search";

    // Symbol search providers
    public const string AlpacaSymbolSearch = "alpaca-symbol-search";
    public const string AlpacaOptions = "alpaca-options";
    public const string PolygonSymbolSearch = "polygon-symbol-search";
    public const string PolygonOptions = "polygon-options";
    public const string FinnhubSymbolSearch = "finnhub-symbol-search";
    public const string OpenFigi = "openfigi";
    public const string EdgarSymbolSearch = "edgar-symbol-search";
    public const string EdgarSecurityMaster = "edgar-security-master";

    // Application services
    public const string CredentialValidation = "credential-validation";
    public const string ConnectivityTest = "connectivity-test";
    public const string DailySummaryWebhook = "daily-summary-webhook";
    public const string OAuthTokenRefresh = "oauth-token-refresh";
    public const string CredentialTesting = "credential-testing";
    public const string PortfolioImport = "portfolio-import";
    public const string IBClientPortal = "ib-client-portal";
    public const string DryRun = "dry-run";
    public const string PreflightChecker = "preflight-checker";

    // Default client for general purpose
    public const string Default = "default";
}

/// <summary>
/// Extension methods for configuring HttpClient instances via IHttpClientFactory.
/// </summary>
/// <remarks>
/// Implements TD-10: Replace instance HttpClient with IHttpClientFactory.
/// Benefits:
/// - Proper connection pooling and DNS refresh
/// - Prevents socket exhaustion
/// - Centralized configuration for timeouts, headers, retry policies
/// - Better testability through DI
///
/// Resilience policies (retry, circuit breaker) are provided by SharedResiliencePolicies
/// to eliminate duplication and ensure consistent behavior across all HttpClients.
/// </remarks>
[ImplementsAdr("ADR-010", "HttpClientFactory for proper HTTP client lifecycle management")]
public static class HttpClientConfiguration
{

    /// <summary>
    /// Registers all named HttpClient configurations with the DI container.
    /// </summary>
    public static IServiceCollection AddMarketDataHttpClients(this IServiceCollection services)
    {
        // Default client
        services.AddHttpClient(HttpClientNames.Default)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Alpaca Trading API client
        services.AddHttpClient(HttpClientNames.Alpaca)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Alpaca Data API client
        services.AddHttpClient(HttpClientNames.AlpacaData)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.alpaca.markets/v2/stocks/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Alpaca Historical Data client
        services.AddHttpClient(HttpClientNames.AlpacaHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.alpaca.markets/v2/stocks/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Alpaca Symbol Search client
        services.AddHttpClient(HttpClientNames.AlpacaSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.alpaca.markets/v2/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Alpaca Options chain client
        services.AddHttpClient(HttpClientNames.AlpacaOptions)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.alpaca.markets/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Polygon clients
        services.AddHttpClient(HttpClientNames.Polygon)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        services.AddHttpClient(HttpClientNames.PolygonHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        services.AddHttpClient(HttpClientNames.PolygonSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Tiingo Historical client
        services.AddHttpClient(HttpClientNames.TiingoHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.tiingo.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Yahoo Finance Historical client
        services.AddHttpClient(HttpClientNames.YahooFinanceHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
                client.Timeout = SharedResiliencePolicies.LongTimeout;
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
            })
            .AddSharedResiliencePolicy();

        // Stooq Historical client
        services.AddHttpClient(HttpClientNames.StooqHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://stooq.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
            })
            .AddSharedResiliencePolicy();

        // Finnhub clients
        services.AddHttpClient(HttpClientNames.FinnhubHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://finnhub.io/api/v1/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        services.AddHttpClient(HttpClientNames.FinnhubSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://finnhub.io/api/v1/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Alpha Vantage Historical client
        services.AddHttpClient(HttpClientNames.AlphaVantageHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.alphavantage.co/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Nasdaq Data Link Historical client
        services.AddHttpClient(HttpClientNames.NasdaqDataLinkHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.nasdaq.com/api/v3/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // FRED Historical client
        services.AddHttpClient(HttpClientNames.FredHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.stlouisfed.org/fred/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
            })
            .AddSharedResiliencePolicy();

        // Twelve Data Historical client
        services.AddHttpClient(HttpClientNames.TwelveDataHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.twelvedata.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Robinhood Historical client (unofficial API; auth header set per-provider via env var)
        services.AddHttpClient(HttpClientNames.RobinhoodHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.robinhood.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Robinhood live quotes polling client
        services.AddHttpClient(HttpClientNames.RobinhoodMarketData)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.robinhood.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Robinhood brokerage client
        services.AddHttpClient(HttpClientNames.RobinhoodBrokerage)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.robinhood.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // OpenFIGI client
        services.AddHttpClient(HttpClientNames.OpenFigi)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.openfigi.com/v3/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddSharedResiliencePolicy();

        // EDGAR symbol search client (SEC public API — no auth required, 10 req/s courtesy limit)
        services.AddHttpClient(HttpClientNames.EdgarSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.sec.gov/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0 contact@meridian.io");
            })
            .AddSharedResiliencePolicy();

        // EDGAR security master ingest client (data.sec.gov endpoint for submissions)
        services.AddHttpClient(HttpClientNames.EdgarSecurityMaster)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.sec.gov/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0 contact@meridian.io");
            })
            .AddSharedResiliencePolicy();

        // NYSE client
        services.AddHttpClient(HttpClientNames.NYSE)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicy();

        // Credential validation client (short timeout)
        services.AddHttpClient(HttpClientNames.CredentialValidation)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicy();

        // Connectivity test client (short timeout)
        services.AddHttpClient(HttpClientNames.ConnectivityTest)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddSharedResiliencePolicy();

        // Daily summary webhook client
        services.AddHttpClient(HttpClientNames.DailySummaryWebhook)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicy();

        // OAuth token refresh client
        services.AddHttpClient(HttpClientNames.OAuthTokenRefresh)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.6.1");
            })
            .AddSharedResiliencePolicy();

        // Credential testing client
        services.AddHttpClient(HttpClientNames.CredentialTesting)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicy();

        // Portfolio import client
        services.AddHttpClient(HttpClientNames.PortfolioImport)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
            })
            .AddSharedResiliencePolicy();

        // IB Client Portal client (uses custom SSL handler for self-signed certificates)
        services.AddHttpClient(HttpClientNames.IBClientPortal)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
            .AddSharedResiliencePolicy();

        // Dry run client
        services.AddHttpClient(HttpClientNames.DryRun)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicy();

        // Preflight checker client
        services.AddHttpClient(HttpClientNames.PreflightChecker)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicy();

        return services;
    }

    /// <summary>
    /// Registers all named HttpClient configurations with circuit breaker state-change reporting.
    /// Each HTTP circuit breaker calls <paramref name="onStateChanged"/> when it transitions state,
    /// enabling the <c>CircuitBreakerStatusService</c> to surface breaker health in the dashboard.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="onStateChanged">
    /// Callback invoked on every circuit breaker state transition.
    /// Parameters: (breakerName, newState "Open"|"Closed"|"HalfOpen", lastError or null).
    /// </param>
    public static IServiceCollection AddMarketDataHttpClientsTracked(
        this IServiceCollection services,
        Action<string, string, string?> onStateChanged)
    {
        // Default client
        services.AddHttpClient(HttpClientNames.Default)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.Default, onStateChanged);

        // Alpaca Trading API client
        services.AddHttpClient(HttpClientNames.Alpaca)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.Alpaca, onStateChanged);

        // Alpaca Data API client
        services.AddHttpClient(HttpClientNames.AlpacaData)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.alpaca.markets/v2/stocks/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.AlpacaData, onStateChanged);

        // Alpaca Historical Data client
        services.AddHttpClient(HttpClientNames.AlpacaHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.alpaca.markets/v2/stocks/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.AlpacaHistorical, onStateChanged);

        // Alpaca Symbol Search client
        services.AddHttpClient(HttpClientNames.AlpacaSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.alpaca.markets/v2/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.AlpacaSymbolSearch, onStateChanged);

        // Polygon clients
        services.AddHttpClient(HttpClientNames.Polygon)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.Polygon, onStateChanged);

        services.AddHttpClient(HttpClientNames.PolygonHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.PolygonHistorical, onStateChanged);

        services.AddHttpClient(HttpClientNames.PolygonSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.PolygonSymbolSearch, onStateChanged);

        // Polygon Options chain client
        services.AddHttpClient(HttpClientNames.PolygonOptions)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.PolygonOptions, onStateChanged);

        // Tiingo Historical client
        services.AddHttpClient(HttpClientNames.TiingoHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.tiingo.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.TiingoHistorical, onStateChanged);

        // Yahoo Finance Historical client
        services.AddHttpClient(HttpClientNames.YahooFinanceHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
                client.Timeout = SharedResiliencePolicies.LongTimeout;
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.YahooFinanceHistorical, onStateChanged);

        // Stooq Historical client
        services.AddHttpClient(HttpClientNames.StooqHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://stooq.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.StooqHistorical, onStateChanged);

        // Finnhub clients
        services.AddHttpClient(HttpClientNames.FinnhubHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://finnhub.io/api/v1/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.FinnhubHistorical, onStateChanged);

        services.AddHttpClient(HttpClientNames.FinnhubSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://finnhub.io/api/v1/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.FinnhubSymbolSearch, onStateChanged);

        // Alpha Vantage Historical client
        services.AddHttpClient(HttpClientNames.AlphaVantageHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.alphavantage.co/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.AlphaVantageHistorical, onStateChanged);

        // Nasdaq Data Link Historical client
        services.AddHttpClient(HttpClientNames.NasdaqDataLinkHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.nasdaq.com/api/v3/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.NasdaqDataLinkHistorical, onStateChanged);

        // FRED Historical client
        services.AddHttpClient(HttpClientNames.FredHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.stlouisfed.org/fred/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.FredHistorical, onStateChanged);

        // Twelve Data Historical client
        services.AddHttpClient(HttpClientNames.TwelveDataHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.twelvedata.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.TwelveDataHistorical, onStateChanged);

        // Robinhood Historical client (unofficial API; auth header set per-provider via env var)
        services.AddHttpClient(HttpClientNames.RobinhoodHistorical)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.robinhood.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.RobinhoodHistorical, onStateChanged);

        // Robinhood live quotes polling client
        services.AddHttpClient(HttpClientNames.RobinhoodMarketData)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.robinhood.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.RobinhoodMarketData, onStateChanged);

        // Robinhood brokerage client
        services.AddHttpClient(HttpClientNames.RobinhoodBrokerage)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.robinhood.com/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.RobinhoodBrokerage, onStateChanged);

        // OpenFIGI client
        services.AddHttpClient(HttpClientNames.OpenFigi)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.openfigi.com/v3/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.OpenFigi, onStateChanged);

        // EDGAR symbol search client (SEC public API — no auth required, 10 req/s courtesy limit)
        services.AddHttpClient(HttpClientNames.EdgarSymbolSearch)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.sec.gov/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0 contact@meridian.io");
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.EdgarSymbolSearch, onStateChanged);

        // EDGAR security master ingest client (data.sec.gov endpoint for submissions)
        services.AddHttpClient(HttpClientNames.EdgarSecurityMaster)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.sec.gov/");
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0 contact@meridian.io");
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.EdgarSecurityMaster, onStateChanged);
        services.AddHttpClient(HttpClientNames.NYSE)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.NYSE, onStateChanged);

        // Credential validation client (short timeout)
        services.AddHttpClient(HttpClientNames.CredentialValidation)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.CredentialValidation, onStateChanged);

        // Connectivity test client (short timeout)
        services.AddHttpClient(HttpClientNames.ConnectivityTest)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.ConnectivityTest, onStateChanged);

        // Daily summary webhook client
        services.AddHttpClient(HttpClientNames.DailySummaryWebhook)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.DailySummaryWebhook, onStateChanged);

        // OAuth token refresh client
        services.AddHttpClient(HttpClientNames.OAuthTokenRefresh)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
                client.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.6.1");
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.OAuthTokenRefresh, onStateChanged);

        // Credential testing client
        services.AddHttpClient(HttpClientNames.CredentialTesting)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.CredentialTesting, onStateChanged);

        // Portfolio import client
        services.AddHttpClient(HttpClientNames.PortfolioImport)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.PortfolioImport, onStateChanged);

        // IB Client Portal client (uses custom SSL handler for self-signed certificates)
        services.AddHttpClient(HttpClientNames.IBClientPortal)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.DefaultTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.IBClientPortal, onStateChanged);

        // Dry run client
        services.AddHttpClient(HttpClientNames.DryRun)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.DryRun, onStateChanged);

        // Preflight checker client
        services.AddHttpClient(HttpClientNames.PreflightChecker)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = SharedResiliencePolicies.ShortTimeout;
            })
            .AddSharedResiliencePolicyTracked(HttpClientNames.PreflightChecker, onStateChanged);

        return services;
    }
}

/// <summary>
/// Static factory for creating HttpClient instances from IHttpClientFactory.
/// Provides backward-compatible static access for services not yet fully converted to DI.
/// </summary>
/// <remarks>
/// This is a transitional pattern. New code should inject IHttpClientFactory directly.
/// 
/// Thread Safety: This class uses static fields and is designed to be thread-safe for the
/// common initialization pattern where Initialize() is called once during startup.
/// 
/// Disposal Handling: If the underlying IServiceProvider is disposed (e.g., during test cleanup),
/// CreateClient() will catch ObjectDisposedException and fall back to creating a new HttpClient.
/// This ensures graceful degradation and test isolation.
/// </remarks>
public static class HttpClientFactoryProvider
{
    private static IHttpClientFactory? _factory;
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initializes the provider with the service provider.
    /// Call this during application startup after ConfigureServices.
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _factory = serviceProvider.GetService<IHttpClientFactory>();
    }

    /// <summary>
    /// Gets an HttpClient for the specified named client.
    /// Falls back to creating a new HttpClient if factory is not initialized or disposed.
    /// </summary>
    public static HttpClient CreateClient(string name)
    {
        if (_factory != null)
        {
            try
            {
                return _factory.CreateClient(name);
            }
            catch (ObjectDisposedException)
            {
                // Service provider was disposed (e.g., during test cleanup)
                // Reset and fall back to creating a new HttpClient
                _factory = null;
                _serviceProvider = null;
            }
        }

        // Fallback for non-DI scenarios (e.g., CLI tools, tests)
        // This maintains backward compatibility during transition
        return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Gets an HttpClient for the specified named client with header configuration.
    /// </summary>
    public static HttpClient CreateClient(string name, Action<HttpClient> configure)
    {
        var client = CreateClient(name);
        configure(client);
        return client;
    }

    /// <summary>
    /// Checks if the factory has been initialized.
    /// </summary>
    public static bool IsInitialized => _factory != null;

    /// <summary>
    /// Resets the provider, clearing any cached factory or service provider references.
    /// Primarily for test scenarios where the service provider gets disposed between tests.
    /// </summary>
    public static void Reset()
    {
        _factory = null;
        _serviceProvider = null;
    }
}
