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
[Collection("Endpoint")]
public sealed class EndpointReadDeclarationTests : EndpointIntegrationTestBase
{
    public EndpointReadDeclarationTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>
    /// The frozen 2026-08-16 read-surface inventory, now empty: every GET route mapped before the
    /// declaration requirement existed has been classified, so the ratchet no longer tolerates any
    /// undeclared read. A newly mapped GET route fails immediately unless it declares a permission or
    /// declares openness with a stated reason.
    /// <para>
    /// Left in place rather than deleted with the assertion: the pair of checks below is what keeps
    /// the surface at zero, and an empty tolerance list is the strongest form of the ratchet rather
    /// than a leftover. Re-adding an entry here is a deliberate, reviewable act.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> UndeclaredReadBaseline = new(StringComparer.OrdinalIgnoreCase)
    {
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
