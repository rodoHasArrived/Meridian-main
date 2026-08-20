using FluentAssertions;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// The read-surface declaration ratchet (W9-GOV-008): every mapped GET route must either declare a
/// permission (<see cref="EndpointAuthorizationMetadata"/>) or declare that it is deliberately open
/// (<see cref="EndpointOpenReadMetadata"/>), or sit in the frozen baseline below.
/// <para>
/// The governed decision this enforces: the read surface is remediated <em>risk-based</em>, not
/// uniformly. Reads exposing account-scoped, position, or PII-bearing data get a permission; broad
/// workstation reads — reference data, catalogs, health — declare openness explicitly with a
/// stated reason rather than by omission. Neither state can be reached silently: a newly mapped
/// GET route carrying no declaration fails immediately, and the frozen inventory below only
/// shrinks as the burn-down classifies each family one way or the other.
/// </para>
/// </summary>
public sealed class EndpointReadDeclarationTests : EndpointIntegrationTestBase
{
    public EndpointReadDeclarationTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// Frozen 2026-08-16 read-surface inventory: GET routes mapped before the declaration
    /// requirement existed, pending risk classification. Tracked under W9-GOV-008 with the
    /// burn-down owned by the remainder-to-zero change. The ratchet only tightens: classify a
    /// route (permission or open-with-reason) and remove its entry.
    /// </summary>
    private static readonly HashSet<string> UndeclaredReadBaseline = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET /api/accounting-system/export-packages",
        "GET /api/accounting-system/export-packages/{exportPackageId}/manifest",
        "GET /api/accounting-system/import/latest",
        "GET /api/accounting-system/mapping-profiles",
        "GET /api/accounting-system/migration-run-artifacts",
        "GET /api/accounting-system/migration-worker-plans",
        "GET /api/accounting-system/production-certification-profile",
        "GET /api/accounting-system/providers",
        "GET /api/accounting-system/reconciliation/latest",
        "GET /api/accounting-system/tenant-administration-profile",
        "GET /api/analytics/anomalies",
        "GET /api/analytics/compare",
        "GET /api/analytics/completeness",
        "GET /api/analytics/gaps",
        "GET /api/analytics/latency",
        "GET /api/analytics/latency/stats",
        "GET /api/analytics/quality-report",
        "GET /api/analytics/rate-limits",
        "GET /api/analytics/throughput",
        "GET /api/backfill/checkpoints",
        "GET /api/backfill/checkpoints/resumable",
        "GET /api/backfill/checkpoints/validation",
        "GET /api/backfill/checkpoints/{jobId}",
        "GET /api/backfill/checkpoints/{jobId}/pending",
        "GET /api/backfill/completeness",
        "GET /api/backfill/gaps",
        "GET /api/backfill/validation",
        "GET /api/backfill/validation/{symbol}",
        "GET /api/backpressure",
        "GET /api/calendar/holidays",
        "GET /api/calendar/status",
        "GET /api/calendar/trading-days",
        "GET /api/canonicalization/config",
        "GET /api/canonicalization/parity",
        "GET /api/canonicalization/parity/{provider}",
        "GET /api/canonicalization/status",
        "GET /api/catalog/coverage",
        "GET /api/catalog/search",
        "GET /api/catalog/symbols",
        "GET /api/catalog/timeline",
        "GET /api/compliance/access-reviews",
        "GET /api/compliance/audit/extract",
        "GET /api/compliance/controls/attestation",
        "GET /api/config/data-sources",
        "GET /api/config/datasources",
        "GET /api/connections",
        "GET /api/demo/historical/{symbol}",
        "GET /api/demo/market-data/{symbol}",
        "GET /api/demo/mode",
        "GET /api/demo/symbols",
        "GET /api/environment-designer/drafts",
        "GET /api/environment-designer/drafts/{draftId:guid}",
        "GET /api/environment-designer/runtime/current",
        "GET /api/environment-designer/runtime/versions/{versionId:guid}",
        "GET /api/environment-designer/versions",
        "GET /api/environment-designer/versions/current",
        "GET /api/environment-designer/versions/{versionId:guid}",
        "GET /api/errors",
        "GET /api/events/stream",
        "GET /api/export/formats",
        "GET /api/export/preview",
        "GET /api/historical/",
        "GET /api/historical/symbols",
        "GET /api/historical/{symbol}/bars",
        "GET /api/historical/{symbol}/daterange",
        "GET /api/indices/{indexName}/constituents",
        "GET /api/lean/algorithms",
        "GET /api/lean/auto-export",
        "GET /api/lean/backtest/history",
        "GET /api/lean/backtest/{backtestId}/results",
        "GET /api/lean/backtest/{backtestId}/status",
        "GET /api/lean/config",
        "GET /api/lean/status",
        "GET /api/lean/symbol-map",
        "GET /api/lean/sync/status",
        "GET /api/ledger/accounting-configuration",
        "GET /api/ledger/accounting-configuration/audit",
        "GET /api/ledger/journal-automation/daily-mark-to-market-schedules",
        "GET /api/ledger/journal-automation/monthly-schedules",
        "GET /api/messaging/activity",
        "GET /api/messaging/config",
        "GET /api/messaging/consumers",
        "GET /api/messaging/endpoints",
        "GET /api/messaging/errors",
        "GET /api/messaging/publishing",
        "GET /api/messaging/stats",
        "GET /api/messaging/status",
        "GET /api/oms/adapters/diagnostics",
        "GET /api/oms/audit",
        "GET /api/oms/messages",
        "GET /api/options/chains/{underlyingSymbol}",
        "GET /api/options/expirations/{underlyingSymbol}",
        "GET /api/options/quotes/{underlyingSymbol}",
        "GET /api/options/strikes/{underlyingSymbol}/{expiration}",
        "GET /api/options/summary",
        "GET /api/options/underlyings",
        "GET /api/packaging/contents",
        "GET /api/packaging/download/{fileName}",
        "GET /api/packaging/list",
        "GET /api/plaid/accounts",
        "GET /api/plaid/institutions/search",
        "GET /api/plaid/items",
        "GET /api/portfolio/cash-ladder/",
        "GET /api/portfolio/cash-ladder/scenarios",
        "GET /api/portfolio/{runId}/cash-flows",
        "GET /api/promotion/evaluate/{runId}",
        "GET /api/promotion/history",
        "GET /api/quant/templates",
        "GET /api/replay/files",
        "GET /api/replay/preview",
        "GET /api/replay/stats",
        "GET /api/replay/{sessionId}/status",
        "GET /api/resilience/circuit-breakers",
        "GET /api/risk/escalations",
        "GET /api/risk/rules",
        "GET /api/risk/rules/{ruleName}/config",
        "GET /api/risk/rules/{ruleName}/status",
        "GET /api/sampling/estimate",
        "GET /api/sampling/saved",
        "GET /api/sampling/{sampleId}",
        "GET /api/sla/health",
        "GET /api/sla/metrics",
        "GET /api/sla/status",
        "GET /api/sla/status/{symbol}",
        "GET /api/sla/violations",
        "GET /api/status",
        "GET /api/strategies/status",
        "GET /api/strategies/{strategyId}/status",
        "GET /api/subscriptions/active",
        "GET /api/v1/risk/escalations",
        "GET /api/v1/risk/rules",
        "GET /api/v1/risk/rules/{ruleName}/config",
        "GET /api/v1/risk/rules/{ruleName}/status",
    };

    [Fact]
    public void EveryMappedReadRoute_DeclaresPermissionOrOpenness_OrIsExplicitlyTracked()
    {
        var readRoutes = Fixture.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Contains("GET", StringComparer.OrdinalIgnoreCase))
            .ToList();

        readRoutes.Should().NotBeEmpty();

        var undeclared = new List<string>();
        foreach (var endpoint in readRoutes)
        {
            var routeKey = $"GET /{(endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')}";
            var declared =
                endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>() is not null ||
                endpoint.Metadata.GetMetadata<EndpointOpenReadMetadata>() is not null;
            if (!declared)
                undeclared.Add(routeKey);
        }

        var undeclaredKeys = undeclared.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newViolations = undeclared
            .Where(key => !UndeclaredReadBaseline.Contains(key))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();
        var remediated = UndeclaredReadBaseline
            .Where(key => !undeclaredKeys.Contains(key))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();

        newViolations.Should().BeEmpty(
            "every newly mapped GET route must declare either a permission (RequirePermission / " +
            "RequireAnyPermission) or deliberate openness (DeclareOpenRead with a stated reason); " +
            "the frozen inventory only shrinks. Undeclared: {0}",
            string.Join("; ", newViolations));
        remediated.Should().BeEmpty(
            "these baseline reads now carry a declaration - remove them from " +
            "UndeclaredReadBaseline so the ratchet tightens: {0}",
            string.Join("; ", remediated));
    }
}
