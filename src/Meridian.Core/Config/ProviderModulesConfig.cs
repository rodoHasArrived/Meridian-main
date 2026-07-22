namespace Meridian.Core.Config;

/// <summary>
/// Root configuration section for declaring an arbitrary set of provider modules.
/// Loaded from the <c>"ProviderModules"</c> key in <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Example configuration enabling Alpaca and Polygon:
/// <code>
/// {
///   "ProviderModules": {
///     "Modules": {
///       "alpaca": {
///         "Enabled": true,
///         "Priority": 10,
///         "Credentials": {
///           "keyId":     "ALPACA_KEY_ID",
///           "secretKey": "ALPACA_SECRET_KEY"
///         }
///       },
///       "polygon": {
///         "Enabled": true,
///         "Priority": 20,
///         "Credentials": {
///           "apiKey": "POLYGON_API_KEY"
///         }
///       }
///     }
///   }
/// }
/// </code>
/// Credential values are the names of environment variables to read at startup.
/// This keeps actual secrets out of configuration files.
/// </remarks>
public sealed record ProviderModulesConfig(
    Dictionary<string, ProviderModuleSettings>? Modules = null
);

/// <summary>
/// Configuration for a single provider module, keyed by provider family ID
/// (e.g. "alpaca", "polygon", "ib"). The family ID must match the module's
/// <c>IProviderModule.ModuleId</c> (lower-case, no spaces).
/// </summary>
/// <param name="Enabled">Whether this provider is active. Default true.</param>
/// <param name="Priority">
/// Routing priority — lower values are preferred when multiple providers
/// cover the same capability. Default 100.
/// </param>
/// <param name="Credentials">
/// Map of credential key → environment variable name. Conventional keys:
/// "apiKey", "keyId", "secretKey", "username", "password".
/// The Application layer reads each environment variable and passes the
/// resolved value to the module.
/// </param>
/// <param name="Settings">
/// Map of provider-specific setting key → value string. Used for
/// non-credential configuration such as base URLs, feature flags,
/// or connection parameters.
/// </param>
public sealed record ProviderModuleSettings(
    bool Enabled = true,
    int Priority = 100,
    Dictionary<string, string?>? Credentials = null,
    Dictionary<string, string?>? Settings = null
);
