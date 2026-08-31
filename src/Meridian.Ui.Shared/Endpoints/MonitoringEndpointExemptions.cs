namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Canonical unauthenticated lifecycle and scrape surface. Matching is exact (apart from a
/// trailing slash) so comprehensive or nested health routes remain governed.
/// </summary>
public static class MonitoringEndpointExemptions
{
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/healthz", "/ready", "/readyz", "/live", "/livez",
        "/startup", "/startupz", "/metrics"
    };

    public static bool IsExempt(string path) => ExemptPaths.Contains(path.TrimEnd('/'));
}
