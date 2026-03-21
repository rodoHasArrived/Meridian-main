// Replace every occurrence of "Template" with your provider name.
// Replace the namespace segment "Template" with your provider's namespace segment.
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;

namespace Meridian.Infrastructure.Adapters.Template;

/// <summary>
/// Template historical data provider — replace "Template" with your provider name.
///
/// Implementation checklist:
/// - [ ] Replace every "Template" occurrence with your provider name
/// - [ ] Set <see cref="Name"/>, <see cref="DisplayName"/>, <see cref="Description"/>
/// - [ ] Set <see cref="HttpClientName"/> to a value from <c>HttpClientNames</c>
///   (or add a new constant there and register the client in the DI composition root)
/// - [ ] Set <see cref="Priority"/>, <see cref="MaxRequestsPerWindow"/>,
///   <see cref="RateLimitWindow"/>, and <see cref="RateLimitDelay"/>
/// - [ ] Set <see cref="Capabilities"/> with the correct flags
/// - [ ] Implement <see cref="GetDailyBarsAsync"/> (required)
/// - [ ] Implement optional overrides for adjusted bars, intraday, dividends, and splits
///   if the provider supports them (return an empty list by default)
/// - [ ] Validate credentials in the constructor and set _isConfigured
/// - [ ] Remove the TODO comments and this checklist when implementation is complete
/// </summary>
// TODO: Replace "template" with the provider ID, display name, type, and category.
[ImplementsAdr("ADR-001", "Template historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class TemplateHistoricalDataProvider : BaseHistoricalDataProvider
{
    // TODO: Replace with the actual API key environment variable name.
    private const string ApiKeyEnvVar = "TEMPLATE__APIKEY";

    private readonly string? _apiKey;

    // ------------------------------------------------------------------ //
    //  Abstract property implementations (required by base class)         //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    // TODO: Set to your provider's unique ID (lowercase, e.g., "tiingo").
    public override string Name => "template";

    /// <inheritdoc/>
    // TODO: Set a human-readable display name.
    public override string DisplayName => "Template Provider (historical)";

    /// <inheritdoc/>
    // TODO: Describe the provider's capabilities and data coverage.
    public override string Description => "TODO: Add description";

    /// <inheritdoc/>
    // TODO: Set the named HTTP client registered in the DI container.
    // Add a constant to HttpClientNames and register the client in the composition root.
    protected override string HttpClientName => "Template"; // HttpClientNames.TemplateHistorical

    // ------------------------------------------------------------------ //
    //  Virtual property overrides (optional)                              //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    // TODO: Set an appropriate priority (lower = tried first in failover chains).
    public override int Priority => 50;

    /// <inheritdoc/>
    // TODO: Set based on the provider's rate limit (e.g., 60 req/min → 1 s delay).
    public override TimeSpan RateLimitDelay => TemplateRateLimits.MinRequestDelay;

    /// <inheritdoc/>
    public override int MaxRequestsPerWindow => TemplateRateLimits.MaxRequestsPerWindow;

    /// <inheritdoc/>
    public override TimeSpan RateLimitWindow => TemplateRateLimits.Window;

    /// <inheritdoc/>
    // TODO: Adjust to match what the provider actually supports.
    public override HistoricalDataCapabilities Capabilities { get; } = new()
    {
        AdjustedPrices = false,
        Intraday = false,
        Dividends = false,
        Splits = false,
        SupportedMarkets = ["US"]
    };

    // ------------------------------------------------------------------ //
    //  Constructor                                                         //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a new Template historical data provider.
    /// </summary>
    /// <param name="apiKey">API key (falls back to <c>TEMPLATE__APIKEY</c> env var).</param>
    /// <param name="httpClient">Optional HTTP client (uses factory if null).</param>
    public TemplateHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null)
        : base(httpClient)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable(ApiKeyEnvVar);

        // TODO: Add required HTTP headers for this provider.
        // Example:
        //   Http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    // ------------------------------------------------------------------ //
    //  IHistoricalDataProvider implementation                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns historical daily OHLCV bars for the given symbol and date range.
    /// </summary>
    /// <inheritdoc/>
    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol must not be empty.", nameof(symbol));
        if (Disposed) return [];

        // TODO: Build the request URL using TemplateEndpoints constants.
        // TODO: Apply rate limiting before each request:
        //   await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);
        // TODO: Call Http.GetAsync(url, ct) with the resilience pipeline.
        // TODO: Deserialize the response and map to List<HistoricalBar>.
        // TODO: Normalize symbol, convert timestamps to UTC.
        // TODO: Log success: Log.Debug("Fetched {Count} bars for {Symbol}", bars.Count, symbol);

        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotImplementedException("TODO: Implement GetDailyBarsAsync");
    }
}
