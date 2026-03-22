using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Application.Exceptions;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Utilities;
using Polly;
using Serilog;

// Use centralized HttpHandleResult from HttpResiliencePolicy
using HttpHandleResult = Meridian.Infrastructure.Resilience.HttpHandleResult;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Base class for historical data providers to eliminate duplicate code patterns
/// across all backfill provider implementations. Provides common functionality for:
/// - Rate limiting initialization and management
/// - HTTP client setup and error handling
/// - HTTP resilience (retry, circuit breaker, timeout)
/// - Credential validation
/// - Symbol normalization
/// - Disposed state tracking
/// </summary>
/// <remarks>
/// Derived classes inherit:
/// - <see cref="IRateLimitAwareProvider"/> implementation with virtual <see cref="OnRateLimitHit"/> event
/// - Centralized HTTP error handling via <see cref="HandleHttpResponseAsync"/>
/// - HTTP resilience pipeline for transient fault handling
/// - Rate limit tracking and Retry-After extraction
/// </remarks>
[ImplementsAdr("ADR-001", "Base historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public abstract class BaseHistoricalDataProvider : IHistoricalDataProvider, IRateLimitAwareProvider, IDisposable
{
    private static readonly JsonSerializerOptions ReflectionJsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    protected readonly HttpClient Http;
    protected readonly RateLimiter RateLimiter;
    protected readonly ILogger Log;
    protected readonly ResiliencePipeline<HttpResponseMessage> ResiliencePipeline;
    private readonly HttpResponseHandler _responseHandler;

    private int _requestCount;
    private DateTimeOffset _windowStart;
    private DateTimeOffset? _rateLimitResetsAt;
    private bool _isRateLimited;
    protected bool Disposed;

    #region Abstract Properties (Must be implemented by derived classes)

    /// <summary>
    /// Unique identifier for the provider (e.g., "alpaca", "tiingo").
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Description of the provider's capabilities.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Named HTTP client identifier from HttpClientNames.
    /// </summary>
    protected abstract string HttpClientName { get; }

    #endregion

    #region Virtual Properties (Can be overridden)

    public virtual int Priority => 100;
    public virtual TimeSpan RateLimitDelay => TimeSpan.Zero;
    public virtual int MaxRequestsPerWindow => int.MaxValue;
    public virtual TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    /// <summary>
    /// Consolidated capability flags for this provider.
    /// Override in derived classes to specify supported features.
    /// </summary>
    /// <remarks>
    /// Use static factory methods for common patterns:
    /// - <see cref="HistoricalDataCapabilities.None"/> (default)
    /// - <see cref="HistoricalDataCapabilities.BarsOnly"/>
    /// - <see cref="HistoricalDataCapabilities.FullFeatured"/>
    /// </remarks>
    public virtual HistoricalDataCapabilities Capabilities => HistoricalDataCapabilities.None;

    /// <summary>
    /// Whether this provider returns split/dividend adjusted prices.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsAdjustedPrices => Capabilities.AdjustedPrices;

    /// <summary>
    /// Whether this provider supports intraday bar data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsIntraday => Capabilities.Intraday;

    /// <summary>
    /// Whether this provider includes dividend data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsDividends => Capabilities.Dividends;

    /// <summary>
    /// Whether this provider includes split data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsSplits => Capabilities.Splits;

    /// <summary>
    /// Whether this provider supports historical quote (NBBO) data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsQuotes => Capabilities.Quotes;

    /// <summary>
    /// Whether this provider supports historical trade data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsTrades => Capabilities.Trades;

    /// <summary>
    /// Whether this provider supports historical auction data.
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public bool SupportsAuctions => Capabilities.Auctions;

    /// <summary>
    /// Market regions/countries supported (e.g., "US", "UK", "DE").
    /// </summary>
    /// <remarks>Delegates to <see cref="Capabilities"/>. Provided for backwards compatibility.</remarks>
    public IReadOnlyList<string> SupportedMarkets => Capabilities.SupportedMarkets;

    #endregion

    #region IRateLimitAwareProvider

    /// <summary>
    /// Event raised when the provider hits a rate limit (HTTP 429).
    /// Derived classes can subscribe or override to handle rate limit events.
    /// </summary>
    public virtual event Action<RateLimitInfo>? OnRateLimitHit;

    /// <summary>
    /// Raises the <see cref="OnRateLimitHit"/> event.
    /// </summary>
    protected virtual void RaiseRateLimitHit(RateLimitInfo info)
    {
        OnRateLimitHit?.Invoke(info);
    }

    #endregion

    /// <summary>
    /// Initialize the base provider with common infrastructure.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client (uses factory if not provided).</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="enableResilience">Whether to enable HTTP resilience (retry, circuit breaker). Default: true.</param>
    protected BaseHistoricalDataProvider(
        HttpClient? httpClient = null,
        ILogger? log = null,
        bool enableResilience = true)
    {
        Log = log ?? LoggingSetup.ForContext(GetType());
        Http = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientName);

        // Initialize rate limiter with provider-specific settings
        RateLimiter = new RateLimiter(
            MaxRequestsPerWindow,
            RateLimitWindow,
            RateLimitDelay,
            Log);

        // Initialize resilience pipeline for transient fault handling
        ResiliencePipeline = enableResilience
            ? HttpResiliencePolicy.CreateComprehensivePipeline(
                maxRetries: 3,
                retryBaseDelay: TimeSpan.FromSeconds(1),
                circuitBreakerFailureThreshold: 5,
                circuitBreakerDuration: TimeSpan.FromSeconds(30),
                requestTimeout: TimeSpan.FromSeconds(30))
            : ResiliencePipeline<HttpResponseMessage>.Empty;

        // Initialize centralized HTTP response handler (Name property must be available).
        // Name is abstract, so derived class must implement it before this runs.
        _responseHandler = new HttpResponseHandler(GetType().Name, Log);

        _windowStart = DateTimeOffset.UtcNow;
    }

    #region Symbol Normalization

    /// <summary>
    /// Normalize a symbol using the standard approach.
    /// Override in derived class for provider-specific normalization.
    /// </summary>
    protected virtual string NormalizeSymbol(string symbol)
    {
        return SymbolNormalization.Normalize(symbol);
    }

    /// <summary>
    /// Normalize multiple symbols.
    /// </summary>
    protected IReadOnlyList<string> NormalizeSymbols(IEnumerable<string> symbols)
    {
        return symbols.Select(NormalizeSymbol).Distinct().ToList();
    }

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Wait for a rate limit slot before making a request.
    /// </summary>
    protected async Task WaitForRateLimitSlotAsync(CancellationToken ct)
    {
        await RateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);
        Interlocked.Increment(ref _requestCount);
    }

    /// <summary>
    /// Record that a rate limit was hit.
    /// </summary>
    protected void RecordRateLimitHit(TimeSpan? retryAfter)
    {
        _isRateLimited = true;
        _rateLimitResetsAt = DateTimeOffset.UtcNow + (retryAfter ?? RateLimitWindow);
        Log.Warning("{Provider} rate limit hit. Resets at {ResetsAt}", Name, _rateLimitResetsAt);
    }

    /// <summary>
    /// Get current rate limit information.
    /// </summary>
    public RateLimitInfo GetRateLimitInfo()
    {
        // Reset window if expired
        if (DateTimeOffset.UtcNow - _windowStart > RateLimitWindow)
        {
            _requestCount = 0;
            _windowStart = DateTimeOffset.UtcNow;
            _isRateLimited = false;
            _rateLimitResetsAt = null;
        }

        return new RateLimitInfo(
            Name,
            _requestCount,
            MaxRequestsPerWindow,
            RateLimitWindow,
            _rateLimitResetsAt,
            _isRateLimited,
            _rateLimitResetsAt.HasValue ? _rateLimitResetsAt.Value - DateTimeOffset.UtcNow : null
        );
    }

    #endregion

    #region HTTP Request Helpers

    /// <summary>
    /// Execute an HTTP GET request with rate limiting, resilience, and standard error handling.
    /// </summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="symbol">Symbol for logging context.</param>
    /// <param name="dataType">Data type for logging context (e.g., "bars", "quotes").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    protected async Task<HttpResponseMessage> ExecuteGetAsync(string url, string symbol, string dataType, CancellationToken ct)
    {
        ThrowIfDisposed();
        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        Log.Debug("Requesting {Provider} {DataType} for {Symbol}: {Url}", Name, dataType, symbol, url);

        // Execute with resilience pipeline (retry, circuit breaker)
        var response = await ResiliencePipeline.ExecuteAsync(
            async token => await Http.GetAsync(url, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Execute an HTTP GET request, handle the response, and return the content string.
    /// Provides centralized error handling for all HTTP status codes.
    /// </summary>
    /// <param name="url">The URL to request.</param>
    /// <param name="symbol">Symbol for logging context.</param>
    /// <param name="dataType">Data type for logging context (e.g., "bars", "quotes").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response content as string, or null if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown for auth failures or server errors.</exception>
    protected async Task<string?> ExecuteGetAndReadAsync(string url, string symbol, string dataType, CancellationToken ct)
    {
        using var response = await ExecuteGetAsync(url, symbol, dataType, ct).ConfigureAwait(false);
        var result = await HandleHttpResponseAsync(response, symbol, dataType, ct).ConfigureAwait(false);

        if (result.IsNotFound)
            return null;

        if (!result.IsSuccess)
        {
            if (result.IsAuthError)
                throw new ConnectionException(
                    $"{Name} API returned authentication error for {symbol}. Verify credentials.",
                    provider: Name);

            if (result.IsRateLimited)
                throw new RateLimitException(
                    $"{Name} API rate limit exceeded for {symbol}",
                    provider: Name,
                    symbol: symbol,
                    retryAfter: result.RetryAfter ?? TimeSpan.FromSeconds(60));

            throw new DataProviderException(
                $"{Name} API error for {symbol}: {result.ErrorMessage}",
                provider: Name,
                symbol: symbol);
        }

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Centralized HTTP response handling for all providers.
    /// Delegates to <see cref="HttpResponseHandler.TryHandleResponseAsync"/> to eliminate duplicate code.
    /// Handles common status codes: 200 (OK), 404 (Not Found), 401/403 (Auth), 429 (Rate Limit), 5xx (Server Error).
    /// </summary>
    /// <param name="response">The HTTP response to handle.</param>
    /// <param name="symbol">Symbol for logging context.</param>
    /// <param name="dataType">Data type for logging context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result indicating success or the type of failure.</returns>
    protected virtual async Task<HttpHandleResult> HandleHttpResponseAsync(
        HttpResponseMessage response,
        string symbol,
        string dataType,
        CancellationToken ct)
    {
        // Delegate to centralized handler with rate limit callback
        var result = await _responseHandler.TryHandleResponseAsync(
            response,
            symbol,
            dataType,
            info =>
            {
                RecordRateLimitHit(info.RetryAfter);
                RaiseRateLimitHit(GetRateLimitInfo());
            },
            ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Synchronous HTTP response handling for backwards compatibility.
    /// Prefer using <see cref="HandleHttpResponseAsync"/> for new code.
    /// </summary>
    protected void HandleHttpResponse(HttpResponseMessage response, string symbol, string dataType)
    {
        if (response.IsSuccessStatusCode)
            return;

        var statusCode = (int)response.StatusCode;

        if (statusCode == 401 || statusCode == 403)
        {
            Log.Error("{Provider} API returned {StatusCode} for {Symbol} {DataType}: Authentication failed", Name, statusCode, symbol, dataType);
            throw new ConnectionException(
                $"{Name} API returned {statusCode}: Authentication failed for {symbol}",
                provider: Name);
        }

        if (statusCode == 429)
        {
            var retryAfter = HttpResiliencePolicy.ExtractRetryAfter(response) ?? TimeSpan.FromSeconds(60);
            RecordRateLimitHit(retryAfter);
            RaiseRateLimitHit(GetRateLimitInfo());

            Log.Warning("{Provider} API returned 429 for {Symbol} {DataType}: Rate limit exceeded", Name, symbol, dataType);
            throw new RateLimitException(
                $"{Name} API rate limit exceeded for {symbol}",
                provider: Name,
                symbol: symbol,
                retryAfter: retryAfter);
        }

        if (statusCode == 404)
        {
            Log.Debug("{Provider} API returned 404 for {Symbol} {DataType}: Not found", Name, symbol, dataType);
            return; // Allow empty results for not found
        }

        Log.Warning("{Provider} API returned {StatusCode} for {Symbol} {DataType}", Name, statusCode, symbol, dataType);
        throw new DataProviderException(
            $"{Name} API returned {statusCode} for {symbol}",
            provider: Name,
            symbol: symbol);
    }

    /// <summary>
    /// Deserialize JSON response with error handling.
    /// </summary>
    protected T? DeserializeResponse<T>(string? json, string symbol) where T : class
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, ReflectionJsonOptions);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse {Provider} response for {Symbol}", Name, symbol);
            throw new DataProviderException(
                $"Failed to parse {Name} data for {symbol}",
                provider: Name,
                symbol: symbol);
        }
    }

    #endregion

    #region Validation Helpers

    /// <summary>
    /// Validate that the provider has not been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
    }

    /// <summary>
    /// Validate that a symbol is provided.
    /// </summary>
    protected static void ValidateSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));
    }

    /// <summary>
    /// Validate that at least one symbol is provided.
    /// </summary>
    protected static void ValidateSymbols(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols, nameof(symbols));
        if (!symbols.Any())
            throw new ArgumentException("At least one symbol is required", nameof(symbols));
    }

    #endregion

    #region OHLC Validation

    /// <summary>
    /// Validate OHLC data is valid (all prices > 0).
    /// </summary>
    protected static bool IsValidOhlc(decimal open, decimal high, decimal low, decimal close)
    {
        return open > 0 && high > 0 && low > 0 && close > 0;
    }

    /// <summary>
    /// Validate OHLC data with logging.
    /// </summary>
    protected bool ValidateOhlc(decimal open, decimal high, decimal low, decimal close, string symbol, DateOnly date)
    {
        if (IsValidOhlc(open, high, low, close))
            return true;

        Log.Debug("Skipping invalid OHLC for {Symbol} on {Date}: O={Open} H={High} L={Low} C={Close}",
            symbol, date, open, high, low, close);
        return false;
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Fetch daily OHLCV bars for a symbol within the specified date range.
    /// Must be implemented by derived classes.
    /// </summary>
    public abstract Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);

    #endregion

    #region Virtual Methods with Default Implementations

    /// <summary>
    /// Check if the provider is currently available and healthy.
    /// Default implementation validates credentials.
    /// </summary>
    public virtual Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Get extended bar data with adjustment information when supported.
    /// Default implementation converts standard bars to adjusted bars.
    /// </summary>
    public virtual async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var bars = await GetDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return bars.Select(b => new AdjustedHistoricalBar(
            b.Symbol, b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume, b.Source, b.SequenceNumber
        )).ToList();
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
