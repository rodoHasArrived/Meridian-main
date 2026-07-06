namespace Meridian.Contracts.Api;

/// <summary>
/// Canonical default endpoint URLs shared across all layers. Runtime fallbacks, config
/// templates, and operator-facing guidance must reference these constants instead of
/// hardcoding <c>localhost:&lt;port&gt;</c> literals, so a default port change is a
/// single-file edit.
/// </summary>
public static class ApiEndpointDefaults
{
    /// <summary>Default base URL for the local Meridian UI/API service.</summary>
    public const string LocalApiBaseUrl = "http://localhost:8080";

    /// <summary>Default base URL for the Interactive Brokers Client Portal gateway.</summary>
    public const string IbClientPortalBaseUrl = "https://localhost:5000";
}
