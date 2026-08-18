using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// The declarative half of the authorization ratchet: every mapped mutating route must carry
/// <see cref="EndpointAuthorizationMetadata"/> — stamped by <c>RequirePermission</c> /
/// <c>RequireAnyPermission</c> — or be a documented permissionless decision, or sit in the frozen
/// declaration baseline below.
/// <para>
/// This exists beside the behavioural sweep, not instead of it, because the two prove different
/// things and each is blind where the other sees. The behavioural sweep proves a permissionless
/// request is actually rejected, but it is confounded wherever routing or composition answers
/// first — a missing test-host service turns a guarded route into a 503, and for years a
/// <c>{version:int}</c> constraint turned three guarded routes into 404s — while a raw
/// <c>AddEndpointFilter</c> guard passes it without leaving any declared trace. The metadata
/// assertion proves the requirement is <em>declared</em> where tooling, reviewers, and this test
/// can see it, but says nothing about enforcement. A route is done only when both hold.
/// </para>
/// </summary>
public sealed class EndpointAuthorizationDeclarationTests : EndpointIntegrationTestBase
{
    public EndpointAuthorizationDeclarationTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// Frozen 2026-08-16 declaration baseline: mutating routes that reject permissionless callers
    /// through handler-internal checks or raw endpoint filters, without declaring the requirement
    /// as metadata. Tracked under W9-GOV-008; the tranche that touches a family converts its
    /// checks to declarative form and removes the entries. The ratchet only tightens.
    /// </summary>
    private static readonly HashSet<string> UndeclaredMutationBaseline = new(StringComparer.OrdinalIgnoreCase)
    {
        "DELETE /api/maintenance/schedules/{id}/delete",
        "DELETE /api/maintenance/schedules/{scheduleId}",
        "DELETE /api/packaging/{fileName}",
        "DELETE /api/symbols/{symbol}",
        "POST /api/alignment/create",
        "POST /api/alignment/preview",
        "POST /api/backfill/checkpoints/{jobId}/resume",
        "POST /api/backfill/cost-estimate",
        "POST /api/compliance/actions/evaluate",
        "POST /api/execution/orders/submit",
        "POST /api/health/providers/{provider}/test",
        "POST /api/lean/results/ingest",
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
        "POST /api/options/refresh",
        "POST /api/packaging/create",
        "POST /api/packaging/import",
        "POST /api/packaging/validate",
        "POST /api/plaid/webhook",
        "POST /api/providers/{providerName}/test",
        "POST /api/quality/anomalies/{anomalyId}/acknowledge",
        "POST /api/quality/gaps/{symbol}",
        "POST /api/quality/reports/export",
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
        "POST /hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts",
        "POST /portal/reporting/access-grants/{grantId}/exchange",
        "PUT /api/maintenance/schedules/{scheduleId}",
    };

    [Fact]
    public void EveryMappedMutatingRoute_DeclaresItsAuthorization_OrIsExplicitlyTracked()
    {
        var mutatingRoutes = Fixture.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => new[] { "POST", "PUT", "PATCH", "DELETE" }
                    .Contains(method, StringComparer.OrdinalIgnoreCase))
                .Select(method => (Method: method.ToUpperInvariant(), Endpoint: endpoint)))
            .ToList();

        mutatingRoutes.Should().NotBeEmpty();

        var undeclared = new List<string>();
        foreach (var (method, endpoint) in mutatingRoutes)
        {
            var routeKey = $"{method} /{(endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')}";
            if (EndpointAuthorizationCoverageTests.PermissionlessMutationAllowlist.Contains(routeKey))
                continue;

            if (endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>() is null)
                undeclared.Add(routeKey);
        }

        var undeclaredKeys = undeclared.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newViolations = undeclared
            .Where(key => !UndeclaredMutationBaseline.Contains(key))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();
        var remediated = UndeclaredMutationBaseline
            .Where(key => !undeclaredKeys.Contains(key))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();

        newViolations.Should().BeEmpty(
            "every newly mapped mutating route must declare its authorization requirement with " +
            "RequirePermission/RequireAnyPermission (or join the permissionless allowlist with a " +
            "stated reason); the frozen baseline only shrinks. Undeclared: {0}",
            string.Join("; ", newViolations));
        remediated.Should().BeEmpty(
            "these baseline routes now declare authorization metadata - remove them from " +
            "UndeclaredMutationBaseline so the ratchet tightens: {0}",
            string.Join("; ", remediated));
    }
}
