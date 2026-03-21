using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Base class for symbol search providers that eliminates common boilerplate code.
/// Provides standardized handling of:
/// - HTTP client initialization via factory pattern
/// - Rate limiting with configurable limits
/// - Credential validation and header setup
/// - Disposal pattern
/// - Error handling and logging
/// - Match score calculation
/// </summary>
/// <remarks>
/// Derived classes should override abstract properties and implement the protected
/// abstract methods for provider-specific API integration.
/// </remarks>
[ImplementsAdr("ADR-001", "Base symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public abstract class BaseSymbolSearchProvider : IFilterableSymbolSearchProvider, IDisposable
{
    protected readonly HttpClient Http;
    protected readonly RateLimiter RateLimiter;
    protected readonly ILogger Log;
    protected bool Disposed;

    #region Abstract Properties (Must be implemented by derived classes)

    /// <summary>
    /// Unique identifier for the provider (e.g., "alpaca", "finnhub").
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Human-readable display name for the provider.
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Named HTTP client identifier from HttpClientNames.
    /// </summary>
    protected abstract string HttpClientName { get; }

    /// <summary>
    /// Base URL for the provider's API.
    /// </summary>
    protected abstract string BaseUrl { get; }

    /// <summary>
    /// Environment variable name for the API key.
    /// </summary>
    protected abstract string ApiKeyEnvVar { get; }

    /// <summary>
    /// Alternate environment variable names for the API key (using __ notation).
    /// </summary>
    protected virtual IReadOnlyList<string> AlternateApiKeyEnvVars => Array.Empty<string>();

    #endregion

    #region Virtual Properties (Can be overridden)

    /// <summary>
    /// Priority for this provider (lower = higher priority).
    /// Default is 50.
    /// </summary>
    public virtual int Priority => 50;

    /// <summary>
    /// Maximum requests allowed per rate limit window.
    /// </summary>
    protected virtual int MaxRequestsPerWindow => 60;

    /// <summary>
    /// Duration of the rate limit window.
    /// </summary>
    protected virtual TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Minimum delay between requests.
    /// </summary>
    protected virtual TimeSpan MinRequestDelay => TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Supported asset types for filtering.
    /// </summary>
    public virtual IReadOnlyList<string> SupportedAssetTypes => Array.Empty<string>();

    /// <summary>
    /// Supported exchanges for filtering.
    /// </summary>
    public virtual IReadOnlyList<string> SupportedExchanges => Array.Empty<string>();

    #endregion

    /// <summary>
    /// API key loaded from environment variables.
    /// </summary>
    protected string? ApiKey { get; private set; }

    /// <summary>
    /// Initialize the base provider with common infrastructure.
    /// </summary>
    /// <param name="apiKey">Optional API key (loads from environment if not provided).</param>
    /// <param name="httpClient">Optional HTTP client (uses factory if not provided).</param>
    /// <param name="log">Optional logger.</param>
    protected BaseSymbolSearchProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
    {
        Log = log ?? LoggingSetup.ForContext(GetType());

        // Load API key from parameter or environment
        ApiKey = apiKey ?? LoadApiKeyFromEnvironment();

        // Initialize HTTP client using factory pattern (TD-10 compliance)
        Http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientName);
        ConfigureHttpClientHeaders();

        // Initialize rate limiter with provider-specific settings
        RateLimiter = new RateLimiter(MaxRequestsPerWindow, RateLimitWindow, MinRequestDelay, Log);
    }

    /// <summary>
    /// Load API key from environment variables.
    /// Checks primary env var first, then alternates.
    /// </summary>
    private string? LoadApiKeyFromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (!string.IsNullOrEmpty(key))
            return key;

        foreach (var altEnvVar in AlternateApiKeyEnvVars)
        {
            key = Environment.GetEnvironmentVariable(altEnvVar);
            if (!string.IsNullOrEmpty(key))
                return key;
        }

        return null;
    }

    /// <summary>
    /// Configure HTTP client headers. Override to add provider-specific headers.
    /// </summary>
    protected virtual void ConfigureHttpClientHeaders()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
    }

    /// <summary>
    /// Check if the provider has valid credentials configured.
    /// </summary>
    protected virtual bool HasValidCredentials()
    {
        return !string.IsNullOrEmpty(ApiKey);
    }

    /// <summary>
    /// Check if the provider is available (credentials configured and API reachable).
    /// </summary>
    public virtual async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!HasValidCredentials())
        {
            Log.Debug("{Provider} API credentials not configured", Name);
            return false;
        }

        try
        {
            // Default health check: try a simple search
            var results = await SearchAsync("AAPL", 1, ct).ConfigureAwait(false);
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Search for symbols matching the query (simple overload).
    /// </summary>
    public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default)
    {
        return SearchAsync(query, limit, null, null, ct);
    }

    /// <summary>
    /// Search for symbols with filtering options.
    /// </summary>
    public async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        string? assetType = null,
        string? exchange = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SymbolSearchResult>();

        if (!HasValidCredentials())
            return Array.Empty<SymbolSearchResult>();

        await RateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        try
        {
            var url = BuildSearchUrl(query, assetType, exchange);
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("{Provider} search returned {Status} for query {Query}",
                    Name, response.StatusCode, query);
                return Array.Empty<SymbolSearchResult>();
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var results = DeserializeSearchResults(json, query);

            // Apply additional filtering if provider doesn't support it natively
            results = ApplyFilters(results, assetType, exchange);

            return results
                .OrderByDescending(r => r.MatchScore)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Provider} search failed for query {Query}", Name, query);
            return Array.Empty<SymbolSearchResult>();
        }
    }

    /// <summary>
    /// Get detailed information about a specific symbol.
    /// </summary>
    public async Task<SymbolDetails?> GetDetailsAsync(string symbol, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        if (!HasValidCredentials())
            return null;

        await RateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = symbol.ToUpperInvariant();

        try
        {
            var url = BuildDetailsUrl(normalizedSymbol);
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("{Provider} details returned {Status} for {Symbol}",
                    Name, response.StatusCode, symbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return await DeserializeDetailsAsync(json, normalizedSymbol, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Provider} details lookup failed for {Symbol}", Name, symbol);
            return null;
        }
    }

    #region Abstract Methods (Must be implemented by derived classes)

    /// <summary>
    /// Build the URL for a search request.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="assetType">Optional asset type filter.</param>
    /// <param name="exchange">Optional exchange filter.</param>
    /// <returns>Complete URL for the search request.</returns>
    protected abstract string BuildSearchUrl(string query, string? assetType, string? exchange);

    /// <summary>
    /// Build the URL for a symbol details request.
    /// </summary>
    /// <param name="symbol">Normalized symbol.</param>
    /// <returns>Complete URL for the details request.</returns>
    protected abstract string BuildDetailsUrl(string symbol);

    /// <summary>
    /// Deserialize the search response JSON into results.
    /// </summary>
    /// <param name="json">Response JSON.</param>
    /// <param name="query">Original search query (for match scoring).</param>
    /// <returns>List of search results.</returns>
    protected abstract IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query);

    /// <summary>
    /// Deserialize the details response JSON into a SymbolDetails object.
    /// </summary>
    /// <param name="json">Response JSON.</param>
    /// <param name="symbol">Normalized symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Symbol details or null if not found.</returns>
    protected abstract Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct);

    #endregion

    #region Virtual Methods (Can be overridden)

    /// <summary>
    /// Apply filters to results. Override if provider supports filtering natively.
    /// </summary>
    protected virtual IEnumerable<SymbolSearchResult> ApplyFilters(
        IEnumerable<SymbolSearchResult> results,
        string? assetType,
        string? exchange)
    {
        if (!string.IsNullOrEmpty(assetType))
        {
            results = results.Where(r =>
                r.AssetType?.Equals(assetType, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrEmpty(exchange))
        {
            results = results.Where(r =>
                r.Exchange?.Contains(exchange, StringComparison.OrdinalIgnoreCase) == true);
        }

        return results;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Calculate match score using the shared utility.
    /// </summary>
    protected static int CalculateMatchScore(string query, string symbol, string? name, int position)
        => SymbolSearchUtility.CalculateMatchScore(query, symbol, name, position);

    /// <summary>
    /// Deserialize JSON with error handling.
    /// </summary>
    protected T? DeserializeJson<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse {Provider} response", Name);
            return null;
        }
    }

    /// <summary>
    /// Throw if the provider has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed)
            return;
        Disposed = true;

        if (disposing)
        {
            RateLimiter.Dispose();
            Http.Dispose();
        }
    }

    #endregion
}
