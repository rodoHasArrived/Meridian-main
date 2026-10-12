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
    internal static readonly HashSet<string> PermissionlessMutationAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Authentication bootstrap: login/logout/refresh must be callable before permissions exist.
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "POST /api/auth/refresh",
        "POST /api/auth/csrf",
        // First-run setup completes before any user or permission store exists.
        "POST /api/setup/account",
        // Bootstrap is the same seam behind a one-use setup token: it can only create the
        // first administrator, and refuses whenever any account already exists.
        "POST /api/auth/bootstrap",
        // 410 Gone tombstones for the retired legacy reporting lifecycle. They perform no action
        // and answer every caller with the canonical replacement route; guarding them would swap
        // that pointer for a 403 while protecting nothing. Remove the entry when the tombstone is
        // unmapped.
        "POST /api/fund-structure/report-pack-preview",
        "POST /api/fund-structure/report-packs",
        "POST /api/fund-structure/reporting/packs",
        "POST /api/fund-structure/reporting/packs/create",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/validate",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/submit",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/approve",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/reject",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/publish",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/restatements",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/restate",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/archive",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/deliveries",
        "POST /api/fund-structure/reporting/packs/{reportId:guid}/deliveries/failures",
        "POST /api/fund-structure/reporting/schedules/run-due",

        // Authenticated by the caller's own credential rather than by a session permission, so a
        // permission declaration would be the wrong instrument for both.
        //
        // The reporting delivery-receipt hook is an inbound provider callback: it verifies an
        // X-Meridian-Reporting-Signature over the body before recording anything, and no session
        // exists on a provider callback to carry permissions.
        "POST /hooks/reporting/distribution/{transportId}/deliveries/{jobId}/receipts",
        // The Plaid webhook is an inbound provider callback and carries no session, so it cannot
        // hold a permission. It authenticates the caller instead by verifying the ES256
        // Plaid-Verification header against a configured key and confirming the signed body hash
        // matches the bytes actually received; an unverifiable callback is refused, not recorded.
        "POST /api/plaid/webhook",
        // The portal grant exchange is the seam that mints a session for an external report
        // recipient: it authenticates the grant token it is given, so requiring a permission the
        // caller cannot yet hold would make the route unusable for its only purpose.
        "POST /portal/reporting/access-grants/{grantId}/exchange",
    };


    /// <summary>
    /// Remediation baseline for mutating routes that process a permissionless request instead of
    /// rejecting it — burned down to empty (W9-GOV-008 criterion one). The final nine entries were
    /// guarded routes whose binding 400s or handler service resolution answered before their
    /// authorization filters could; the pre-binding <c>MutationAuthorizationGuardMiddleware</c>
    /// now decides the declared requirement first, so the sweep observes 401/403 on every guarded
    /// route. The ratchet stays: any newly mapped mutating route that is neither guarded nor
    /// allowlisted fails this test immediately, and a route may be re-added here only as a
    /// deliberate, documented exception.
    /// </summary>
    internal static readonly HashSet<string> UnguardedMutationBaseline = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    /// <summary>
    /// Locks the allowlist above to the runtime's own permissionless declarations, so the tested
    /// exemption and the enforced exemption cannot drift: every mapped allowlisted mutation must
    /// carry <see cref="Meridian.Ui.Shared.Endpoints.EndpointPermissionlessMutationMetadata"/> or
    /// <see cref="Meridian.Ui.Shared.Endpoints.EndpointIndependentAuthenticationMetadata"/> (the
    /// two markers the pre-binding mutation guard stands aside for), and no mutating route outside
    /// the allowlist may carry either — a new webhook or bootstrap seam joins the allowlist with a
    /// stated reason, or it does not ship permissionless.
    /// </summary>
    [Fact]
    public void PermissionlessMutationAllowlist_AndDeclaredMarkers_CannotDrift()
    {
        var mutatingRoutes = Fixture.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => MutatingMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
                .Select(method => (Method: method.ToUpperInvariant(), Endpoint: endpoint)))
            .ToList();

        mutatingRoutes.Should().NotBeEmpty();

        var allowlistedWithoutMarker = new List<string>();
        var markedOutsideAllowlist = new List<string>();
        foreach (var (method, endpoint) in mutatingRoutes)
        {
            var routeKey = $"{method} /{(endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')}";
            var declaresPermissionless =
                endpoint.Metadata.GetMetadata<Meridian.Ui.Shared.Endpoints.EndpointPermissionlessMutationMetadata>() is not null ||
                endpoint.Metadata.GetMetadata<Meridian.Ui.Shared.Endpoints.EndpointIndependentAuthenticationMetadata>() is not null;

            if (PermissionlessMutationAllowlist.Contains(routeKey))
            {
                if (!declaresPermissionless)
                    allowlistedWithoutMarker.Add(routeKey);
            }
            else if (declaresPermissionless)
            {
                markedOutsideAllowlist.Add(routeKey);
            }
        }

        allowlistedWithoutMarker.Should().BeEmpty(
            "every allowlisted mutation must declare its permissionless posture as endpoint " +
            "metadata so the pre-binding mutation guard exempts exactly what this suite exempts. " +
            "Undeclared: {0}",
            string.Join("; ", allowlistedWithoutMarker));
        markedOutsideAllowlist.Should().BeEmpty(
            "a mutating route declaring a permissionless or independently-authenticated posture " +
            "bypasses the pre-binding mutation guard, so it must also sit in the allowlist with a " +
            "stated reason where this suite can sweep its own rejection behaviour. Unlisted: {0}",
            string.Join("; ", markedOutsideAllowlist));
    }

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
                    // Constraint-aware: rendering "sweep-test" into an {x:int} segment 404s at
                    // routing, so the route is never swept and a guarded route reads as unguarded.
                    parameter.ParameterPolicies.Any(policy =>
                        policy.Content is "int" or "long" or "min(1)")
                        ? "1"
                        : parameter.Name.Contains("id", StringComparison.OrdinalIgnoreCase)
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
