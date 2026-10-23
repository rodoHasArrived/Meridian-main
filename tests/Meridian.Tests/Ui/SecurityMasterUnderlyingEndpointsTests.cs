using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterUnderlyingEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetUnderlyings_ReturnsTheGraph_WithGapsForUnresolvedAndUnknownTargets()
    {
        var securityId = Guid.NewGuid();
        var knownUnderlying = Guid.NewGuid();
        var unknownUnderlying = Guid.NewGuid();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(Detail(securityId));
        queryService.GetByIdAsync(knownUnderlying, Arg.Any<CancellationToken>()).Returns(Detail(knownUnderlying));
        queryService.GetByIdAsync(unknownUnderlying, Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null);
        var graph = Substitute.For<ISecurityUnderlyingGraphStore>();
        graph.GetUnderlyingsAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            Link(securityId, SecurityUnderlyingRole.Underlying, knownUnderlying, null, from, null),
            Link(securityId, SecurityUnderlyingRole.BasketConstituent, unknownUnderlying, null, from, null),
            Link(securityId, SecurityUnderlyingRole.Index, null, "SOFR", from, null),
            Link(securityId, SecurityUnderlyingRole.Collateral, knownUnderlying, null, from, from.AddDays(1))
        });
        graph.GetDependentsAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            Link(Guid.NewGuid(), SecurityUnderlyingRole.Underlying, securityId, null, from, null)
        });

        await using var app = await CreateAppAsync(queryService, graph);
        using var response = await app.GetTestClient().GetAsync($"/api/security-master/{securityId}/underlyings?asOfUtc=2026-06-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SecurityUnderlyingGraphDto>(Json);
        dto.Should().NotBeNull();
        dto!.Underlyings.Should().HaveCount(3, "the expired collateral link is filtered by the as-of instant");
        dto.Dependents.Should().ContainSingle();
        dto.Gaps.Should().HaveCount(2);
        dto.Gaps.Should().Contain(g => g.Contains("SOFR"));
        dto.Gaps.Should().Contain(g => g.Contains(unknownUnderlying.ToString("D")));
    }

    [Fact]
    public async Task GetUnderlyings_ReturnsNotFound_ForAnUnknownSecurity()
    {
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null);
        await using var app = await CreateAppAsync(queryService, Substitute.For<ISecurityUnderlyingGraphStore>());

        using var response = await app.GetTestClient().GetAsync($"/api/security-master/{Guid.NewGuid()}/underlyings");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Declare_StampsTheActor_AndRejectsARouteBodyMismatch()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(Detail(securityId));
        var graph = Substitute.For<ISecurityUnderlyingGraphStore>();
        DeclareSecurityUnderlyingLinkRequest? captured = null;
        graph.DeclareAsync(Arg.Do<DeclareSecurityUnderlyingLinkRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Link(securityId, SecurityUnderlyingRole.Collateral, Guid.NewGuid(), null, DateTimeOffset.UtcNow, null)));
        await using var app = await CreateAppAsync(queryService, graph);
        var client = app.GetTestClient();

        var request = new DeclareSecurityUnderlyingLinkRequest(securityId, SecurityUnderlyingRole.Collateral, Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, null, null, "untrusted.body.actor");
        using var ok = await client.PostAsJsonAsync($"/api/security-master/{securityId}/underlyings", request, Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.CreatedBy.Should().Be("casey.doyle", "the actor comes from the session, not the body");

        using var mismatch = await client.PostAsJsonAsync($"/api/security-master/{Guid.NewGuid()}/underlyings", request, Json);
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Declare_MapsAStoreArgumentFailure_ToBadRequest()
    {
        var securityId = Guid.NewGuid();
        var queryService = Substitute.For<ISecurityMasterQueryService>();
        queryService.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(Detail(securityId));
        var graph = Substitute.For<ISecurityUnderlyingGraphStore>();
        graph.DeclareAsync(Arg.Any<DeclareSecurityUnderlyingLinkRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecurityUnderlyingLinkDto>(new ArgumentException("A security cannot be its own underlying.")));
        await using var app = await CreateAppAsync(queryService, graph);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/security-master/{securityId}/underlyings",
            new DeclareSecurityUnderlyingLinkRequest(securityId, SecurityUnderlyingRole.Underlying, securityId, null, null, DateTimeOffset.UtcNow, null, null),
            Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("its own underlying");
    }

    [Fact]
    public async Task Remove_ReturnsNoContent_OrNotFound()
    {
        var securityId = Guid.NewGuid();
        var present = Guid.NewGuid();
        var absent = Guid.NewGuid();
        var graph = Substitute.For<ISecurityUnderlyingGraphStore>();
        graph.RemoveDeclaredAsync(securityId, present, Arg.Any<CancellationToken>()).Returns(true);
        graph.RemoveDeclaredAsync(securityId, absent, Arg.Any<CancellationToken>()).Returns(false);
        await using var app = await CreateAppAsync(Substitute.For<ISecurityMasterQueryService>(), graph);
        var client = app.GetTestClient();

        (await client.DeleteAsync($"/api/security-master/{securityId}/underlyings/{present}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/security-master/{securityId}/underlyings/{absent}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static SecurityUnderlyingLinkDto Link(Guid securityId, SecurityUnderlyingRole role, Guid? target, string? reference, DateTimeOffset from, DateTimeOffset? to)
        => new(Guid.NewGuid(), securityId, role, target, reference, null, from, to, SecurityUnderlyingLinkSource.Derived, null, "test", from);

    private static SecurityDetailDto Detail(Guid securityId)
        => new(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            "Test",
            "USD",
            JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }),
            [],
            [],
            1,
            DateTimeOffset.UtcNow.AddDays(-1),
            null);

    private static async Task<WebApplication> CreateAppAsync(ISecurityMasterQueryService queryService, ISecurityUnderlyingGraphStore graph)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(queryService);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(graph);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "casey.doyle";
            await next();
        });
        app.MapSecurityMasterEndpoints(Json);

        await app.StartAsync();
        return app;
    }
}
