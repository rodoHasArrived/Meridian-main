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
[Collection("Endpoint")]
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
