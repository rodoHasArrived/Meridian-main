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
    /// Adds standard resilience policies (retry with exponential backoff, circuit breaker)
    /// from the shared Infrastructure definition so desktop and service HTTP clients behave
    /// identically.
    /// </summary>
    private static IHttpClientBuilder AddStandardResiliencePolicy(this IHttpClientBuilder builder)
        => builder.AddSharedResiliencePolicy();
}

/// <summary>
/// Static factory for creating HttpClient instances from IHttpClientFactory in desktop apps.
/// Provides backward-compatible static access for services not yet fully converted to DI.
/// </summary>
/// <remarks>
/// This is a transitional pattern. New code should inject IHttpClientFactory directly.
/// </remarks>
public static class HttpClientFactoryProvider
{
    private static readonly Lazy<IHttpClientFactory?> _factory = new(BuildFactory);

    private static IHttpClientFactory? BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddDesktopHttpClients();
        var provider = services.BuildServiceProvider();
        return provider.GetService<IHttpClientFactory>();
    }

    /// <summary>
    /// Gets an HttpClient for the specified named client.
    /// Auto-initializes on first use via thread-safe lazy initialization.
    /// </summary>
    public static HttpClient CreateClient(string name)
    {
        if (_factory.Value is { } factory)
        {
            return factory.CreateClient(name);
        }

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
    public static bool IsInitialized => _factory.IsValueCreated;
}
