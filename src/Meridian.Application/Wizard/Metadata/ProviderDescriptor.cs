namespace Meridian.Application.Wizard.Metadata;

/// <summary>
/// Immutable descriptor for a single data provider.
/// Centralises signup URLs, environment variable names, capabilities and priority
/// so that both <c>ConfigurationWizard</c> and <c>AutoConfigurationService</c>
/// can share the same source of truth.
/// </summary>
/// <param name="Name">Provider identifier (matches <see cref="Config.DataSourceKind"/> names).</param>
/// <param name="DisplayName">Human-readable name shown in the wizard UI.</param>
/// <param name="RequiredEnvVars">
/// Env-vars that must all be set for the provider to be considered "configured".
/// Empty means no credentials required (e.g. Yahoo, Stooq).
/// </param>
/// <param name="AlternativeEnvVars">
/// Alternative env-var names accepted in place of <paramref name="RequiredEnvVars"/>.
/// </param>
/// <param name="Capabilities">
/// Provider capability tags: <c>RealTime</c>, <c>Historical</c>, <c>Trades</c>,
/// <c>Quotes</c>, <c>L2Depth</c>, <c>Aggregates</c>, <c>Daily</c>, etc.
/// </param>
/// <param name="Priority">
/// Lower value = higher preference during auto-configuration ordering.
/// </param>
/// <param name="SignupUrl">Where users sign up for a free or paid tier.</param>
/// <param name="DocsUrl">Provider API documentation.</param>
/// <param name="FreeTierDescription">Short description of the free plan limits.</param>
public sealed record ProviderDescriptor(
    string Name,
    string DisplayName,
    string[] RequiredEnvVars,
    string[] AlternativeEnvVars,
    string[] Capabilities,
    int Priority,
    string SignupUrl = "",
    string DocsUrl = "",
    string FreeTierDescription = "");
