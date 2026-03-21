// SCAFFOLD FILE — copy to src/Meridian.Core/Config/ when implementing a new provider.
// Rename every "Template" occurrence to your provider name.
//
// Integration steps:
//   1. Copy TemplateBackfillConfig into src/Meridian.Core/Config/BackfillConfig.cs,
//      change the namespace to Meridian.Application.Config, and rename the record.
//   2. Add `{YourProvider}Config? YourProvider = null` to BackfillProvidersConfig in BackfillConfig.cs.
//   3. Copy TemplateStreamingOptions to a new dedicated file:
//      src/Meridian.Core/Config/TemplateOptions.cs
//      (only if the provider supports real-time streaming).
//   4. Add `{YourProvider}Options? YourProvider = null` to AppConfig in AppConfig.cs
//      (only if the provider supports real-time streaming).
//
// See docs/development/provider-implementation.md for the full guide.

namespace Meridian.Infrastructure.Adapters.Template;

// ─────────────────────────────────────────────────────────────────────────────
//  Backfill / historical provider configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration for the Template historical (backfill) provider.
///
/// TODO: Rename to <c>{YourProvider}Config</c>.
/// TODO: Add provider-specific properties (e.g., feed, adjustment mode, database name).
/// TODO: Move this record into <c>src/Meridian.Core/Config/BackfillConfig.cs</c>
///       and change the namespace to <c>Meridian.Application.Config</c>.
/// TODO: Add <c>{YourProvider}Config? YourProvider = null</c> to
///       <c>BackfillProvidersConfig</c> in <c>BackfillConfig.cs</c>.
/// </summary>
/// <param name="Enabled">Enable this provider in the backfill chain.</param>
/// <param name="ApiKey">
/// API key (falls back to the <c>TEMPLATE__APIKEY</c> environment variable).
/// Set to <see langword="null"/> to rely on the environment variable only.
/// </param>
/// <param name="Priority">
/// Priority in the backfill fallback chain — lower numbers are tried first.
/// Reference values: Alpaca=5, Stooq=20, Tiingo=15, Polygon=12, Finnhub=18.
/// </param>
/// <param name="RateLimitPerMinute">Maximum API requests per minute allowed by the provider.</param>
public sealed record TemplateBackfillConfig(
    bool Enabled = true,
    string? ApiKey = null,
    int Priority = 50,           // TODO: Set appropriate priority relative to other providers.
    int RateLimitPerMinute = 60  // TODO: Set based on the provider's documented rate limit.
);

// ─────────────────────────────────────────────────────────────────────────────
//  Real-time streaming provider configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration for the Template real-time streaming client.
///
/// TODO: Rename to <c>{YourProvider}Options</c>.
/// TODO: Add provider-specific properties (e.g., feed variant, subscription scopes, SSL options).
/// TODO: Move this record to a dedicated file:
///       <c>src/Meridian.Core/Config/TemplateOptions.cs</c>.
/// TODO: Add <c>{YourProvider}Options? YourProvider = null</c> to <c>AppConfig</c>
///       in <c>AppConfig.cs</c> (only required for streaming providers).
/// TODO: Remove this entire record if the provider supports historical data only.
/// </summary>
/// <param name="ApiKey">API key (falls back to the <c>TEMPLATE__APIKEY</c> environment variable).</param>
/// <param name="Feed">
/// Data feed variant (provider-specific).
/// Common examples: "live", "delayed", "sandbox".
/// </param>
/// <param name="UseSandbox">When true, connects to the sandbox / paper-trading endpoint.</param>
public sealed record TemplateStreamingOptions(
    string ApiKey = "",
    string Feed = "live",   // TODO: Set the correct default feed identifier for this provider.
    bool UseSandbox = false
);
