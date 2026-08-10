using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Mechanical authorization coverage sweep (W9-GOV-008 criterion one): enumerates every mapped
/// mutating route from the composed application's <see cref="EndpointDataSource"/> and proves a
/// caller holding zero permissions is rejected with an authorization-family status. A newly mapped
/// write route therefore fails this test until it either enforces a permission check or is added to
/// the explicit allowlist below with a stated reason — no route can ship unguarded by omission.
/// Read routes are deliberately out of scope here: the workstation grants broad read access by
/// role, and read-side tenancy is covered by the per-workspace tenant-scope suites.
/// </summary>
public sealed class EndpointAuthorizationCoverageTests : EndpointIntegrationTestBase
{
    public EndpointAuthorizationCoverageTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>
    /// Routes a permissionless caller may reach by design. Every entry is a deliberate decision:
    /// authentication endpoints must be reachable to authenticate at all, and lifecycle/demo seams
    /// are guarded by their own tokens or environment gates rather than user permissions.
    /// </summary>
    private static readonly HashSet<string> PermissionlessMutationAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Authentication bootstrap: login/logout/refresh must be callable before permissions exist.
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "POST /api/auth/refresh",
        "POST /api/auth/csrf",
        // First-run setup completes before any user or permission store exists.
        "POST /api/setup/account",
    };


    /// <summary>
    /// Frozen 2026-08-10 remediation baseline: mutating routes that predate the permission model
    /// and today process a permissionless request instead of rejecting it. Each entry is known
    /// governance debt tracked under W9-GOV-008. The ratchet only tightens - a route added here is
    /// a deliberate exception, a fixed route MUST be removed, and any newly mapped mutating route
    /// that is neither guarded nor allowlisted fails this test immediately.
    /// </summary>
    private static readonly HashSet<string> UnguardedMutationBaseline = new(StringComparer.OrdinalIgnoreCase)
    {
        "DELETE /api/environment-designer/drafts/{draftId:guid}",
        "DELETE /api/lean/backtest/{backtestId}/delete",
        "DELETE /api/maintenance/schedules/{id}/delete",
        "DELETE /api/maintenance/schedules/{scheduleId}",
        "DELETE /api/packaging/{fileName}",
        "DELETE /api/symbols/{symbol}",
        "DELETE /api/workstation/reconciliation/break-queue/{breakId}/comments/{commentId}",
        "DELETE /api/workstation/workflows/presets/{presetId}",
        "POST /api/alignment/create",
        "POST /api/alignment/preview",
        "POST /api/analytics/gaps/repair",
        "POST /api/auth/access-assignments/{assignmentId}/revoke",
        "POST /api/auth/accounts/{username}/disable",
        "POST /api/auth/accounts/{username}/password-reset",
        "POST /api/backfill/checkpoints/{jobId}/resume",
        "POST /api/backfill/cost-estimate",
        "POST /api/compliance/actions/evaluate",
        "POST /api/diagnostics/dry-run",
        "POST /api/diagnostics/providers/{providerName}/test",
        "POST /api/diagnostics/selftest",
        "POST /api/diagnostics/test-connectivity",
        "POST /api/diagnostics/validate",
        "POST /api/diagnostics/validate-config",
        "POST /api/diagnostics/validate-credentials",
        "POST /api/environment-designer/drafts",
        "POST /api/environment-designer/publish",
        "POST /api/environment-designer/publish/preview",
        "POST /api/environment-designer/validate",
        "POST /api/environment-designer/versions/{versionId:guid}/rollback",
        "POST /api/execution/orders/submit",
        "POST /api/export/analysis",
        "POST /api/export/integrity",
        "POST /api/export/orderflow",
        "POST /api/export/quality-report",
        "POST /api/export/research-package",
        "POST /api/export/strategy-package",
        "POST /api/fund-structure/assignments",
        "POST /api/fund-structure/businesses",
        "POST /api/fund-structure/clients",
        "POST /api/fund-structure/entities",
        "POST /api/fund-structure/funds",
        "POST /api/fund-structure/investment-portfolios",
        "POST /api/fund-structure/ledger-mapping-assignments",
        "POST /api/fund-structure/links",
        "POST /api/fund-structure/links/validate",
        "POST /api/fund-structure/organizations",
        "POST /api/fund-structure/report-pack-preview",
        "POST /api/fund-structure/report-packs",
        "POST /api/fund-structure/reporting/distribution/access-grants",
        "POST /api/fund-structure/reporting/distribution/access-grants/{grantId}/revoke",
        "POST /api/fund-structure/reporting/distribution/deliveries",
        "POST /api/fund-structure/reporting/packs",
        "POST /api/fund-structure/reporting/packs/create",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/approve",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/archive",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/deliveries",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/deliveries/failures",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/publish",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/reject",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/restate",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/restatements",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/submit",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/validate",
        "POST /api/fund-structure/reporting/schedules/run-due",
        "POST /api/fund-structure/reporting/templates/{templateName}/versions/{version:int}/approve",
        "POST /api/fund-structure/reporting/templates/{templateName}/versions/{version:int}/reject",
        "POST /api/fund-structure/reporting/templates/{templateName}/versions/{version:int}/submit",
        "POST /api/fund-structure/setup-drafts/validate",
        "POST /api/fund-structure/sleeves",
        "POST /api/fund-structure/vehicles",
        "POST /api/health/providers/{provider}/test",
        "POST /api/lean/auto-export/configure",
        "POST /api/lean/backtest/start",
        "POST /api/lean/backtest/{backtestId}/stop",
        "POST /api/lean/results/ingest",
        "POST /api/lean/sync",
        "POST /api/lean/verify",
        "POST /api/ledger/periods/{periodId:guid}/close",
        "POST /api/maintenance/execute",
        "POST /api/maintenance/executions/cleanup",
        "POST /api/maintenance/executions/{executionId}/cancel",
        "POST /api/maintenance/schedules",
        "POST /api/maintenance/schedules/{id}/disable",
        "POST /api/maintenance/schedules/{id}/enable",
        "POST /api/maintenance/schedules/{id}/run",
        "POST /api/maintenance/schedules/{scheduleId}/trigger",
        "POST /api/maintenance/validate-cron",
        "POST /api/messaging/errors/{messageId}/retry",
        "POST /api/messaging/queues/{queueName}/purge",
        "POST /api/messaging/test",
        "POST /api/options/refresh",
        "POST /api/packaging/create",
        "POST /api/packaging/import",
        "POST /api/packaging/validate",
        "POST /api/plaid/webhook",
        "POST /api/providers/failover/reset",
        "POST /api/providers/failover/trigger",
        "POST /api/providers/switch",
        "POST /api/providers/{providerName}/test",
        "POST /api/quality/anomalies/{anomalyId}/acknowledge",
        "POST /api/quality/gaps/{symbol}",
        "POST /api/quality/reports/export",
        "POST /api/quant/parameters",
        "POST /api/quant/run",
        "POST /api/reference-data/options/chains/import",
        "POST /api/replay/start",
        "POST /api/replay/{sessionId}/pause",
        "POST /api/replay/{sessionId}/resume",
        "POST /api/replay/{sessionId}/seek",
        "POST /api/replay/{sessionId}/speed",
        "POST /api/replay/{sessionId}/stop",
        "POST /api/sampling/create",
        "POST /api/schedules/cron/next-runs",
        "POST /api/schedules/cron/validate",
        "POST /api/security-master/corporate-actions/inbox/apply",
        "POST /api/security-master/corporate-actions/ingest",
        "POST /api/storage/cleanup",
        "POST /api/storage/convert-parquet",
        "POST /api/storage/maintenance/defrag",
        "POST /api/storage/quality/alerts/{alertId}/acknowledge",
        "POST /api/storage/quality/check",
        "POST /api/storage/tiers/migrate",
        "POST /api/subscriptions/subscribe",
        "POST /api/subscriptions/unsubscribe/{symbol}",
        "POST /api/symbols/add",
        "POST /api/symbols/batch",
        "POST /api/symbols/bulk-add",
        "POST /api/symbols/bulk-remove",
        "POST /api/symbols/create",
        "POST /api/symbols/validate",
        "POST /api/symbols/{symbol}/archive",
        "POST /api/symbols/{symbol}/remove",
        "POST /api/symbols/{symbol}/update",
        "POST /api/workstation/collateral/ingest",
        "POST /api/workstation/data/query",
        "POST /api/workstation/desktop/launch",
        "POST /api/workstation/financial-record-explorers/{explorerId}/saved-views",
        "POST /api/workstation/first-run/outcomes/complete",
        "POST /api/workstation/runs/compare",
        "POST /api/workstation/runs/diff",
        "POST /api/workstation/strategy/designer/preview",
        "POST /api/workstation/strategy/designer/validate",
        "POST /api/workstation/strategy/engine/validate-run",
        "POST /api/workstation/workflows/presets",
        "POST /api/workstation/workflows/presets/{presetId}/pin",
        "POST /api/workstation/workflows/presets/{presetId}/used",
        "POST /hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts",
        "POST /portal/reporting/access-grants/{grantId}/exchange",
        "PUT /api/auth/accounts/{username}",
        "PUT /api/environment-designer/drafts/{draftId:guid}",
        "PUT /api/maintenance/schedules/{scheduleId}",
        "PUT /api/workstation/workflows/presets/{presetId}",
    };

    [Fact]
    public async Task EveryMappedMutatingRoute_RejectsPermissionlessCaller_OrIsExplicitlyAllowlisted()
    {
        var endpointSources = Fixture.Services.GetServices<EndpointDataSource>().ToList();
        var mutatingRoutes = endpointSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => MutatingMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
                .Select(method => (Method: method.ToUpperInvariant(), Endpoint: endpoint)))
            .ToList();

        mutatingRoutes.Should().NotBeEmpty("the composed application maps mutating API routes");

        using var permissionless = Fixture.CreatePermittedClient();
        var violations = new List<string>();
        var swept = 0;

        foreach (var (method, endpoint) in mutatingRoutes
                     .OrderBy(item => item.Endpoint.RoutePattern.RawText, StringComparer.Ordinal)
                     .ThenBy(item => item.Method, StringComparer.Ordinal))
        {
            var rawPattern = endpoint.RoutePattern.RawText ?? string.Empty;
            var routeKey = $"{method} /{rawPattern.TrimStart('/')}";
            if (PermissionlessMutationAllowlist.Contains(routeKey))
                continue;

            var path = MaterializePath(endpoint);
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT" or "PATCH")
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            swept++;
            try
            {
                using var response = await permissionless.SendAsync(request);
                if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
                {
                    violations.Add($"{routeKey} -> {(int)response.StatusCode} {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // TestServer rethrows unhandled handler exceptions. A DI-parameter resolution
                // failure means the test host lacks a service production registers — record it
                // visibly so the fixture gap gets closed instead of silently shrinking the sweep.
                violations.Add($"{routeKey} -> EXCEPTION {ex.GetType().Name}: {FirstLine(ex.Message)}");
            }
        }

        swept.Should().BeGreaterThan(0);

        var violationKeys = violations
            .Select(static entry => entry.Split(" -> ")[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newViolations = violations
            .Where(entry => !UnguardedMutationBaseline.Contains(entry.Split(" -> ")[0]))
            .ToList();
        var remediated = UnguardedMutationBaseline
            .Where(key => !violationKeys.Contains(key))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();

        newViolations.Should().BeEmpty(
            "every newly mapped mutating route must reject a caller holding zero permissions with " +
            "401/403 before doing any other work, or be added to the explicit allowlist with a stated " +
            "reason; the frozen baseline only shrinks. New violations: {0}",
            string.Join("; ", newViolations));
        remediated.Should().BeEmpty(
            "these baseline routes now reject permissionless callers - remove them from " +
            "UnguardedMutationBaseline so the ratchet tightens: {0}",
            string.Join("; ", remediated));
    }


    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n');
        return index < 0 ? message : message[..index].TrimEnd('\r');
    }

    /// <summary>
    /// Substitutes deterministic dummy values for route parameters so the sweep can address
    /// parameterized routes. The values never need to resolve to real objects: the assertion is
    /// that authorization rejects the request before any lookup happens.
    /// </summary>
    private static string MaterializePath(RouteEndpoint endpoint)
    {
        var segments = new List<string>();
        foreach (var segment in endpoint.RoutePattern.PathSegments)
        {
            var rendered = string.Concat(segment.Parts.Select(part => part switch
            {
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter =>
                    parameter.Name.Contains("id", StringComparison.OrdinalIgnoreCase)
                        ? "11111111-1111-1111-1111-111111111111"
                        : "sweep-test",
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternSeparatorPart separator => separator.Content,
                _ => "sweep-test",
            }));
            segments.Add(rendered);
        }

        return "/" + string.Join("/", segments);
    }
}
