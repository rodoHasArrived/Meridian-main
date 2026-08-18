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
        "DELETE /api/admin/retention/{policyId}/delete",
        "DELETE /api/brokerage-connections/alpaca",
        "DELETE /api/brokerage-connections/robinhood",
        "DELETE /api/config/datasources/{id}",
        "DELETE /api/credentials/{provider}",
        "DELETE /api/fund-accounts/{accountId:guid}",
        "DELETE /api/maintenance/schedules/{id}/delete",
        "DELETE /api/maintenance/schedules/{scheduleId}",
        "DELETE /api/packaging/{fileName}",
        "DELETE /api/providers/modules/{moduleId}",
        "DELETE /api/providers/{providerId}/credentials",
        "DELETE /api/symbols/{symbol}",
        "PATCH /api/fund-accounts/{accountId:guid}/bank-details",
        "PATCH /api/fund-accounts/{accountId:guid}/custodian-details",
        "PATCH /api/security-master/equities/{securityId:guid}/preferred-terms",
        "PATCH /api/security-master/{securityId:guid}/convertible-equity-terms",
        "PATCH /api/security-master/{securityId:guid}/operator-overrides",
        "PATCH /api/security-master/{securityId:guid}/preferred-equity-terms",
        "POST /api/admin/cleanup/execute",
        "POST /api/admin/maintenance/run",
        "POST /api/admin/retention/apply",
        "POST /api/admin/selftest",
        "POST /api/admin/storage/migrate/{targetTier}",
        "POST /api/alignment/create",
        "POST /api/alignment/preview",
        "POST /api/analytics/gaps/repair",
        "POST /api/backfill/checkpoints/{jobId}/resume",
        "POST /api/backfill/cost-estimate",
        "POST /api/brokerage-connections/alpaca/connect",
        "POST /api/brokerage-connections/robinhood/connect",
        "POST /api/compliance/access-reviews/assess",
        "POST /api/compliance/access-reviews/run",
        "POST /api/compliance/actions/evaluate",
        "POST /api/compliance/approval-requests",
        "POST /api/compliance/approval-requests/{approvalRequestId}/decisions",
        "POST /api/config/data-sources",
        "POST /api/config/datasources",
        "POST /api/config/datasources/defaults",
        "POST /api/config/datasources/failover",
        "POST /api/config/datasources/{id}/toggle",
        "POST /api/credentials/{provider}",
        "POST /api/credentials/{provider}/test",
        "POST /api/execution/controls/circuit-breaker",
        "POST /api/execution/controls/manual-overrides",
        "POST /api/execution/controls/manual-overrides/{overrideId}/clear",
        "POST /api/execution/controls/position-limits/default",
        "POST /api/execution/controls/position-limits/{symbol}",
        "POST /api/execution/orders/cancel-all",
        "POST /api/execution/orders/submit",
        "POST /api/execution/orders/{orderId}/cancel",
        "POST /api/execution/positions/actions/close",
        "POST /api/execution/positions/actions/upsize",
        "POST /api/execution/positions/{symbol}/close",
        "POST /api/execution/sessions/create",
        "POST /api/execution/sessions/{sessionId}/close",
        "POST /api/fund-accounts/",
        "POST /api/fund-accounts/{accountId:guid}/balance-snapshots",
        "POST /api/fund-accounts/{accountId:guid}/bank-statements",
        "POST /api/fund-accounts/{accountId:guid}/brokerage-sync/link",
        "POST /api/fund-accounts/{accountId:guid}/brokerage-sync/reconcile-ledger",
        "POST /api/fund-accounts/{accountId:guid}/brokerage-sync/run",
        "POST /api/fund-accounts/{accountId:guid}/custodian-statements",
        "POST /api/fund-accounts/{accountId:guid}/reconcile",
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
        "POST /api/messaging/errors/{messageId}/retry",
        "POST /api/messaging/queues/{queueName}/purge",
        "POST /api/messaging/test",
        "POST /api/oms/auth/signing-keys/rotate",
        "POST /api/oms/excel/sync",
        "POST /api/oms/ingest",
        "POST /api/options/refresh",
        "POST /api/packaging/create",
        "POST /api/packaging/import",
        "POST /api/packaging/validate",
        "POST /api/plaid/items/{itemId}/sync",
        "POST /api/plaid/link-token",
        "POST /api/plaid/public-token/exchange",
        "POST /api/plaid/transfers/sandbox",
        "POST /api/plaid/webhook",
        "POST /api/promotion/approve",
        "POST /api/promotion/reject",
        "POST /api/promotion/runs/{runId}/walk-forward-evidence",
        "POST /api/provider-routing/preview",
        "POST /api/providers/configure",
        "POST /api/providers/failover/reset",
        "POST /api/providers/failover/trigger",
        "POST /api/providers/modules",
        "POST /api/providers/modules/{moduleId}/test",
        "POST /api/providers/restart",
        "POST /api/providers/switch",
        "POST /api/providers/{providerId}/verify",
        "POST /api/providers/{providerName}/test",
        "POST /api/providers/{provider}/test-connection",
        "POST /api/providers/{provider}/validate-credentials",
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
        "POST /api/risk/escalations/{escalationId}/approve",
        "POST /api/risk/escalations/{escalationId}/deny",
        "POST /api/sampling/create",
        "POST /api/schedules/cron/next-runs",
        "POST /api/schedules/cron/validate",
        "POST /api/security-master",
        "POST /api/security-master/aliases/upsert",
        "POST /api/security-master/amend",
        "POST /api/security-master/asset-profiles/approve",
        "POST /api/security-master/asset-profiles/drafts",
        "POST /api/security-master/asset-profiles/rollback",
        "POST /api/security-master/conflicts/{conflictId:guid}/resolve",
        "POST /api/security-master/corporate-actions/inbox/apply",
        "POST /api/security-master/corporate-actions/ingest",
        "POST /api/security-master/deactivate",
        "POST /api/security-master/import",
        "POST /api/security-master/ingest/edgar",
        "POST /api/security-master/resolve",
        "POST /api/security-master/search",
        "POST /api/security-master/{securityId:guid}/corporate-actions",
        "POST /api/security-master/{securityId:guid}/operator-overrides/decision",
        "POST /api/security-master/{securityId:guid}/workbench/approve",
        "POST /api/security-master/{securityId:guid}/workbench/discard",
        "POST /api/security-master/{securityId:guid}/workbench/field",
        "POST /api/security-master/{securityId:guid}/workbench/publish",
        "POST /api/security-master/{securityId:guid}/workbench/resolve-conflict",
        "POST /api/security-master/{securityId:guid}/workbench/submit",
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
        "POST /api/v1/risk/escalations/{escalationId}/approve",
        "POST /api/v1/risk/escalations/{escalationId}/deny",
        "POST /hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts",
        "POST /portal/reporting/access-grants/{grantId}/exchange",
        "PUT /api/maintenance/schedules/{scheduleId}",
        "PUT /api/providers/modules/{moduleId}",
        "PUT /api/providers/modules/{moduleId}/enabled",
        "PUT /api/providers/{providerId}/credentials",
        "PUT /api/risk/rules/{ruleName}/config",
        "PUT /api/v1/risk/rules/{ruleName}/config",
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
