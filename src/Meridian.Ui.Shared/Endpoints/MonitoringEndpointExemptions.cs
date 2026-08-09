namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Canonical set of monitoring endpoints served without authentication: the health, readiness,
/// liveness, and startup probes plus the Prometheus scrape endpoint. Both authentication gates
/// (<see cref="LoginSessionMiddleware"/> and <see cref="ApiKeyMiddleware"/>) consult this one
/// set so the exemption surface cannot drift between them again — PRD-019 traced a broken
/// compose healthcheck (<c>curl -f /health</c>) and failed Prometheus scrapes to the two
/// middlewares carrying different private copies, neither of which listed <c>/health</c> or
/// <c>/metrics</c>.
/// </summary>
/// <remarks>
/// This is a deliberate probe/scrape posture decision, not a convenience: external monitors
/// (container healthchecks, load balancers, Prometheus) authenticate at the network boundary,
/// not with operator sessions, and every payload here is sanitized status. Matching is exact
/// (ignoring trailing slashes), so sub-paths stay authenticated — <c>/health/detailed</c> and
/// the <c>/api/health</c> aliases remain governed by the session and API-key gates like any
/// other API surface.
/// </remarks>
public static class MonitoringEndpointExemptions
{
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/healthz",
        "/ready",
        "/readyz",
        "/live",
        "/livez",
        "/startup",
        "/startupz",
        "/metrics"
    };

    /// <summary>
    /// True when <paramref name="path"/> is one of the unauthenticated monitoring endpoints.
    /// Trailing slashes are ignored and matching is case-insensitive; sub-paths do not match.
    /// </summary>
    public static bool IsExempt(string path) => ExemptPaths.Contains(path.TrimEnd('/'));
}
