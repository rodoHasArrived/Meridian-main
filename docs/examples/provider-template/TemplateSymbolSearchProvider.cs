// Replace every occurrence of "Template" with your provider name.
// Replace the namespace segment "Template" with your provider's namespace segment.
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Adapters.Template;

/// <summary>
/// Template symbol search provider — replace "Template" with your provider name.
///
/// Implementation checklist:
/// - [ ] Replace every "Template" occurrence with your provider name
/// - [ ] Set <see cref="Name"/>, <see cref="DisplayName"/>
/// - [ ] Set <see cref="HttpClientName"/> (add constant to HttpClientNames if needed)
/// - [ ] Set <see cref="BaseUrl"/> and <see cref="ApiKeyEnvVar"/>
/// - [ ] Override <see cref="MaxRequestsPerWindow"/>, <see cref="RateLimitWindow"/>,
///   <see cref="MinRequestDelay"/> with provider-specific values
/// - [ ] Override <see cref="SupportedAssetTypes"/> and <see cref="SupportedExchanges"/>
///   if the provider supports filtering
/// - [ ] Override <see cref="ConfigureHttpClientHeaders"/> to set auth headers
/// - [ ] Implement <see cref="SearchAsync(string, int, CancellationToken)"/>
/// - [ ] Implement <see cref="SearchAsync(string, string?, string?, int, CancellationToken)"/>
///   if the provider supports asset-type / exchange filtering
/// - [ ] Remove the TODO comments and this checklist when implementation is complete
/// </summary>
// TODO: Replace "template" with the provider ID, display name, type, and category.
[ImplementsAdr("ADR-001", "Template symbol search provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class TemplateSymbolSearchProvider : BaseSymbolSearchProvider
{
    // ------------------------------------------------------------------ //
    //  Abstract property implementations (required by base class)         //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    // TODO: Set to your provider's unique ID (lowercase, e.g., "finnhub").
    public override string Name => "template";

    /// <inheritdoc/>
    // TODO: Set a human-readable display name.
    public override string DisplayName => "Template Provider Symbol Search";

    /// <inheritdoc/>
    // TODO: Set the named HTTP client registered in the DI container.
    protected override string HttpClientName => "Template"; // HttpClientNames.TemplateSearch

    /// <inheritdoc/>
    // TODO: Set the base URL for this provider's REST API.
    protected override string BaseUrl => TemplateEndpoints.BaseUrl; // or a hard-coded URL

    /// <inheritdoc/>
    // TODO: Set the environment variable name used to load the API key.
    protected override string ApiKeyEnvVar => "TEMPLATE__APIKEY";

    // ------------------------------------------------------------------ //
    //  Virtual property overrides (optional)                              //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    protected override int MaxRequestsPerWindow => TemplateRateLimits.MaxRequestsPerWindow;

    /// <inheritdoc/>
    protected override TimeSpan RateLimitWindow => TemplateRateLimits.Window;

    /// <inheritdoc/>
    protected override TimeSpan MinRequestDelay => TemplateRateLimits.MinRequestDelay;

    // TODO: If the provider supports asset-type filtering, override SupportedAssetTypes.
    // public override IReadOnlyList<string> SupportedAssetTypes => ["stock", "etf", "crypto"];

    // TODO: If the provider supports exchange filtering, override SupportedExchanges.
    // public override IReadOnlyList<string> SupportedExchanges => ["NYSE", "NASDAQ"];

    // ------------------------------------------------------------------ //
    //  Constructor                                                         //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Creates a new Template symbol search provider.
    /// </summary>
    /// <param name="apiKey">API key (falls back to <c>TEMPLATE__APIKEY</c> env var).</param>
    /// <param name="httpClient">Optional HTTP client (uses factory if null).</param>
    public TemplateSymbolSearchProvider(string? apiKey = null, HttpClient? httpClient = null)
        : base(apiKey, httpClient)
    {
    }

    // ------------------------------------------------------------------ //
    //  HTTP header configuration                                          //
    // ------------------------------------------------------------------ //

    /// <inheritdoc/>
    protected override void ConfigureHttpClientHeaders()
    {
        base.ConfigureHttpClientHeaders();
        // TODO: Add provider-specific authentication headers.
        // Example:
        //   if (!string.IsNullOrEmpty(ApiKey))
        //       Http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }

    // ------------------------------------------------------------------ //
    //  ISymbolSearchProvider implementation                               //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Searches for symbols matching the given query.
    /// </summary>
    /// <inheritdoc/>
    public override async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        if (Disposed) return [];

        await RateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

        // TODO: Build the request URL from BaseUrl and query parameters.
        // TODO: Send the HTTP request and handle errors.
        // TODO: Deserialize the JSON response.
        // TODO: Map provider-specific DTOs to SymbolSearchResult.
        // TODO: Return the mapped results (up to maxResults).

        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotImplementedException("TODO: Implement SearchAsync");
    }

    /// <summary>
    /// Searches for symbols with optional asset-type and exchange filters.
    /// Override this only if the provider supports server-side filtering.
    /// The base implementation falls back to <see cref="SearchAsync(string, int, CancellationToken)"/>
    /// and applies filtering client-side.
    /// </summary>
    /// <inheritdoc/>
    public override async Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query,
        string? assetType,
        string? exchange,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        // TODO: If the provider supports server-side filtering, implement it here.
        // Otherwise, keep this call to the base class which filters client-side.
        return await base.SearchAsync(query, assetType, exchange, maxResults, ct).ConfigureAwait(false);
    }
}
