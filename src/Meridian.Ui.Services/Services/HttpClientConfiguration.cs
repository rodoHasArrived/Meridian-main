using System.Net.Http.Headers;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Services;

/// <summary>
/// Named HttpClient identifiers for IHttpClientFactory in desktop apps.
/// Using constants ensures consistency across the codebase.
/// </summary>
/// <remarks>
/// Implements TD-10: Replace instance HttpClient with IHttpClientFactory.
/// This is a standalone implementation for desktop apps since they cannot reference the main project.
/// </remarks>
public static class HttpClientNames
{
    // API client for communicating with collector service
    public const string ApiClient = "api-client";
    public const string BackfillClient = "backfill-client";

    // Credential testing
    public const string CredentialTest = "credential-test";

    // Setup wizard connectivity checks
    public const string SetupWizard = "setup-wizard";

    // Provider-specific clients
    public const string Alpaca = "alpaca";
    public const string Polygon = "polygon";
    public const string Tiingo = "tiingo";
    public const string Finnhub = "finnhub";
    public const string AlphaVantage = "alpha-vantage";
    public const string OpenFigi = "openfigi";
    public const string NasdaqDataLink = "nasdaq-data-link";

    // Default client for general purpose
    public const string Default = "default";
}

/// <summary>
/// Extension methods for configuring HttpClient instances via IHttpClientFactory in desktop apps.
/// </summary>
/// <remarks>
/// Implements TD-10: Replace instance HttpClient with IHttpClientFactory.
/// Benefits:
/// - Proper connection pooling and DNS refresh
/// - Prevents socket exhaustion
/// - Centralized configuration for timeouts, headers, retry policies
/// - Better testability through DI
///
/// Retry and circuit-breaker behaviour is sourced from the single
/// <see cref="SharedResiliencePolicies"/> definition in Meridian.Infrastructure (which
/// both this project and the WPF desktop app already reference), so there is no longer a
/// separate copy to keep in sync.
/// </remarks>
public static class HttpClientConfiguration
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Registers all named HttpClient configurations with the DI container for desktop apps.
    /// </summary>
    public static IServiceCollection AddDesktopHttpClients(this IServiceCollection services)
    {
        // Default client
        services.AddHttpClient(HttpClientNames.Default)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        // API client for communicating with collector service. Shares the process-wide
        // ApiClientSession cookie container so one login establishes the server session
        // (mdc-session + mdc-csrf) for every consumer of this named client.
        services.AddHttpClient(HttpClientNames.ApiClient)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                CookieContainer = ApiClientSession.Cookies,
                UseCookies = true
            })
            .AddStandardResiliencePolicy();

        // Backfill client with long timeout; same shared session as the API client.
        services.AddHttpClient(HttpClientNames.BackfillClient)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = LongTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                CookieContainer = ApiClientSession.Cookies,
                UseCookies = true
            })
            .AddStandardResiliencePolicy();

        // Credential test client (short timeout)
        services.AddHttpClient(HttpClientNames.CredentialTest)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = ShortTimeout;
            })
            .AddStandardResiliencePolicy();

        // Setup wizard client
        services.AddHttpClient(HttpClientNames.SetupWizard)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = ShortTimeout;
            })
            .AddStandardResiliencePolicy();

        // Alpaca clients
        services.AddHttpClient(HttpClientNames.Alpaca)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        // Polygon client
        services.AddHttpClient(HttpClientNames.Polygon)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.polygon.io/");
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        // Tiingo client
        services.AddHttpClient(HttpClientNames.Tiingo)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.tiingo.com/");
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        // Finnhub client
        services.AddHttpClient(HttpClientNames.Finnhub)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://finnhub.io/api/v1/");
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        // Alpha Vantage client
        services.AddHttpClient(HttpClientNames.AlphaVantage)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.alphavantage.co/");
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        // OpenFIGI client
        services.AddHttpClient(HttpClientNames.OpenFigi)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.openfigi.com/v3/");
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddStandardResiliencePolicy();

        // Nasdaq Data Link client
        services.AddHttpClient(HttpClientNames.NasdaqDataLink)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://data.nasdaq.com/api/v3/");
                client.Timeout = DefaultTimeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddStandardResiliencePolicy();

        return services;
    }

    /// <summary>
    /// Registers the process-wide compatibility API singleton without transferring its lifetime
    /// to the service provider. The desktop application initializes its host client factory and
    /// performs terminal disposal explicitly at process shutdown.
    /// </summary>
    public static IServiceCollection AddDesktopApiClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(ApiClientService.Instance);
        return services;
    }

    /// <summary>
    /// Attaches the desktop host's client factory to the process-wide compatibility API singleton.
    /// Registration is intentionally separate so short-lived test providers cannot dispose or
    /// otherwise assume ownership of <see cref="ApiClientService.Instance"/>.
    /// </summary>
    public static ApiClientService InitializeDesktopApiClient(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var apiClient = services.GetRequiredService<ApiClientService>();
        apiClient.AttachHttpClientFactory(services.GetRequiredService<IHttpClientFactory>());
        return apiClient;
    }

    /// <summary>
    /// Adds standard resilience policies (retry with exponential backoff, circuit breaker)
    /// from the shared Infrastructure definition so desktop and service HTTP clients behave
    /// identically.
    /// </summary>
    private static IHttpClientBuilder AddStandardResiliencePolicy(this IHttpClientBuilder builder)
        => builder.AddSharedResiliencePolicy();
}

/// <summary>
/// Backward-compatible client creation for legacy services that are not constructed by DI.
/// </summary>
/// <remarks>
/// No service provider is built or retained here. Host-composed services must inject
/// <see cref="IHttpClientFactory"/>; this process-local fallback exists only for legacy static
/// entry points and gives each caller an independently disposable client/handler pair.
/// </remarks>
public static class HttpClientFactoryProvider
{
    internal static IHttpClientFactory CompatibilityFactory { get; } = new CompatibilityHttpClientFactory();

    /// <summary>
    /// Gets an HttpClient for the specified named client.
    /// The returned fallback client is owned by the caller.
    /// </summary>
    public static HttpClient CreateClient(string name)
        => CompatibilityFactory.CreateClient(name);

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
    /// Indicates that the compatibility factory is available.
    /// </summary>
    public static bool IsInitialized => true;

    private sealed class CompatibilityHttpClientFactory : IHttpClientFactory
    {
        private static readonly TimeSpan CompatibilityDefaultTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan CompatibilityShortTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CompatibilityLongTimeout = TimeSpan.FromMinutes(60);

        public HttpClient CreateClient(string name)
        {
            var sharesApiSession = string.Equals(name, HttpClientNames.ApiClient, StringComparison.Ordinal)
                || string.Equals(name, HttpClientNames.BackfillClient, StringComparison.Ordinal);
            var handler = new HttpClientHandler
            {
                UseCookies = sharesApiSession,
                CookieContainer = sharesApiSession ? ApiClientSession.Cookies : new System.Net.CookieContainer()
            };
            var client = new HttpClient(handler, disposeHandler: true);
            ConfigureClient(client, name);
            return client;
        }

        private static void ConfigureClient(HttpClient client, string name)
        {
            client.Timeout = name switch
            {
                HttpClientNames.BackfillClient => CompatibilityLongTimeout,
                HttpClientNames.CredentialTest or HttpClientNames.SetupWizard => CompatibilityShortTimeout,
                _ => CompatibilityDefaultTimeout
            };

            if (name is HttpClientNames.Default
                or HttpClientNames.ApiClient
                or HttpClientNames.BackfillClient
                or HttpClientNames.Alpaca
                or HttpClientNames.Polygon
                or HttpClientNames.Tiingo
                or HttpClientNames.Finnhub
                or HttpClientNames.AlphaVantage)
            {
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            }

            client.BaseAddress = name switch
            {
                HttpClientNames.Polygon => new Uri("https://api.polygon.io/"),
                HttpClientNames.Tiingo => new Uri("https://api.tiingo.com/"),
                HttpClientNames.Finnhub => new Uri("https://finnhub.io/api/v1/"),
                HttpClientNames.AlphaVantage => new Uri("https://www.alphavantage.co/"),
                HttpClientNames.OpenFigi => new Uri("https://api.openfigi.com/v3/"),
                HttpClientNames.NasdaqDataLink => new Uri("https://data.nasdaq.com/api/v3/"),
                _ => null
            };

            if (string.Equals(name, HttpClientNames.OpenFigi, StringComparison.Ordinal))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }
    }
}
