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

/// <summary>
/// The curve and valuation routes: actors come from the session, rule failures are 400 and a
/// write-once conflict 409, the as-of read returns one match, valuation is a read any viewer may
/// make while curve writes need Modify, and without a curve store the curve routes are absent.
/// </summary>
public sealed class SecurityMasterCurveEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly DateTimeOffset AsOfInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Publish_StampsTheActorFromTheSession_AndMapsARuleFailureToBadRequest()
    {
        var definition = Definition();
        var store = Substitute.For<ISecurityCurveStore>();
        PublishSecurityCurveVersionRequest? captured = null;
        store.PublishVersionAsync(Arg.Do<PublishSecurityCurveVersionRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Version(definition, 1)));
        await using var app = await CreateAppAsync(store);
        var client = app.GetTestClient();

        var request = new PublishSecurityCurveVersionRequest(definition.CurveId, AsOf, [new SecurityCurvePointDto(AsOf, 1m)], "vendor", null, "untrusted.body.actor");
        using var ok = await client.PostAsJsonAsync($"/api/security-master/curves/{definition.CurveId}/versions", request, Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.PublishedBy.Should().Be("casey.doyle", "the actor comes from the session, not the body");
        (await ok.Content.ReadFromJsonAsync<SecurityCurveVersionDto>(Json))!.Version.Should().Be(1);

        using var mismatch = await client.PostAsJsonAsync($"/api/security-master/curves/{Guid.NewGuid()}/versions", request, Json);
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        store.PublishVersionAsync(Arg.Any<PublishSecurityCurveVersionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecurityCurveVersionDto>(new ArgumentException("Rates are stored as fractions; publish 0.04, not 4.")));
        using var refused = await client.PostAsJsonAsync($"/api/security-master/curves/{definition.CurveId}/versions", request, Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("0.04, not 4");
    }

    [Fact]
    public async Task Define_StampsTheActor_AndMapsAWriteOnceConflictToConflict()
    {
        var store = Substitute.For<ISecurityCurveStore>();
        DefineSecurityCurveRequest? captured = null;
        store.DefineAsync(Arg.Do<DefineSecurityCurveRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Definition()));
        await using var app = await CreateAppAsync(store);
        var client = app.GetTestClient();

        var request = new DefineSecurityCurveRequest("USD-OIS", SecurityCurveKind.Discount, "USD", null, null, "ACT/360", SecurityCurveInterpolation.Linear, "vendor", null, "untrusted.body.actor");
        using var ok = await client.PostAsJsonAsync("/api/security-master/curves", request, Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.CreatedBy.Should().Be("casey.doyle");

        store.DefineAsync(Arg.Any<DefineSecurityCurveRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SecurityCurveDefinitionDto>(new InvalidOperationException("Curve key 'USD-OIS' is already defined; a curve key is write-once.")));
        using var conflict = await client.PostAsJsonAsync("/api/security-master/curves", request, Json);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain("write-once");
    }

    [Fact]
    public async Task Reads_ListDefinitions_ReturnTheLatestMatchForAnAsOf_AndNotFoundForUnknownIds()
    {
        var definition = Definition();
        var v1 = Version(definition, 1);
        var v2 = Version(definition, 2);
        var store = Substitute.For<ISecurityCurveStore>();
        store.ListDefinitionsAsync(SecurityCurveKind.Discount, "USD", null, Arg.Any<CancellationToken>()).Returns(new[] { definition });
        store.GetDefinitionAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(definition);
        store.GetDefinitionAsync(Arg.Is<Guid>(id => id != definition.CurveId), Arg.Any<CancellationToken>()).Returns((SecurityCurveDefinitionDto?)null);
        store.ListVersionsAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(new[] { v1, v2 });
        store.GetLatestVersionAsOfAsync(definition.CurveId, AsOf, null, Arg.Any<CancellationToken>()).Returns(v2);
        store.GetLatestVersionAsOfAsync(definition.CurveId, AsOf.AddDays(-1), null, Arg.Any<CancellationToken>()).Returns((SecurityCurveVersionDto?)null);
        store.GetVersionAsync(definition.CurveId, 1, Arg.Any<CancellationToken>()).Returns(v1);
        store.GetVersionAsync(definition.CurveId, 9, Arg.Any<CancellationToken>()).Returns((SecurityCurveVersionDto?)null);
        await using var app = await CreateAppAsync(store);
        var client = app.GetTestClient();

        (await client.GetFromJsonAsync<List<SecurityCurveDefinitionDto>>("/api/security-master/curves?kind=discount&currency=USD", Json)).Should().ContainSingle().Which.CurveKey.Should().Be("USD-OIS");
        (await client.GetAsync("/api/security-master/curves?kind=Spline")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<SecurityCurveDefinitionDto>($"/api/security-master/curves/{definition.CurveId}", Json))!.CurveId.Should().Be(definition.CurveId);
        (await client.GetAsync($"/api/security-master/curves/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetFromJsonAsync<List<SecurityCurveVersionDto>>($"/api/security-master/curves/{definition.CurveId}/versions", Json)).Should().HaveCount(2);
        (await client.GetFromJsonAsync<List<SecurityCurveVersionDto>>($"/api/security-master/curves/{definition.CurveId}/versions?asOf=2026-01-01", Json))
            .Should().ContainSingle().Which.Version.Should().Be(2, "the as-of read returns the single latest match");
        (await client.GetFromJsonAsync<List<SecurityCurveVersionDto>>($"/api/security-master/curves/{definition.CurveId}/versions?asOf=2025-12-31", Json)).Should().BeEmpty();
        (await client.GetAsync($"/api/security-master/curves/{Guid.NewGuid()}/versions")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetFromJsonAsync<SecurityCurveVersionDto>($"/api/security-master/curves/{definition.CurveId}/versions/1", Json))!.Version.Should().Be(1);
        (await client.GetAsync($"/api/security-master/curves/{definition.CurveId}/versions/9")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Valuation_RejectsARouteBodyMismatch_IsNotFoundForAnUnknownSecurity_AndReturnsBlockedResultsAsOk()
    {
        var securityId = Guid.NewGuid();
        var valuation = Substitute.For<ISecurityValuationService>();
        valuation.ValueAsync(Arg.Any<SecurityValuationRequest>(), Arg.Any<CancellationToken>()).Returns((SecurityValuationResult?)null);
        valuation.ValueAsync(Arg.Is<SecurityValuationRequest>(r => r.SecurityId == securityId), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<SecurityValuationResult?>(SecurityValuationResult.Blocked(
                call.Arg<SecurityValuationRequest>(), Detail(securityId), SecurityValuationModelKind.DiscountedCashFlow, "discounted-cash-flow", "v1", AsOfInstant,
                [new SecurityValuationBlockerDto("NoDiscountCurve", "No discount curve is defined for USD.")])));
        await using var app = await CreateAppAsync(Substitute.For<ISecurityCurveStore>(), valuation);
        var client = app.GetTestClient();

        using var mismatch = await client.PostAsJsonAsync($"/api/security-master/{Guid.NewGuid()}/valuation", new SecurityValuationRequest(securityId, AsOfInstant), Json);
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unknown = Guid.NewGuid();
        using var missing = await client.PostAsJsonAsync($"/api/security-master/{unknown}/valuation", new SecurityValuationRequest(unknown, AsOfInstant), Json);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var blocked = await client.PostAsJsonAsync($"/api/security-master/{securityId}/valuation", new SecurityValuationRequest(securityId, AsOfInstant, Quantity: 500_000m), Json);
        blocked.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await blocked.Content.ReadFromJsonAsync<SecurityValuationResult>(Json);
        result!.IsBlocked.Should().BeTrue();
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("NoDiscountCurve");
        result.Price.Should().BeNull();
        result.Quantity.Should().Be(500_000m);
    }

    [Fact]
    public async Task AViewOnlySession_CanValue_ButCannotDefineOrPublish()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityCurveStore>();
        var valuation = Substitute.For<ISecurityValuationService>();
        valuation.ValueAsync(Arg.Any<SecurityValuationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<SecurityValuationResult?>(SecurityValuationResult.Blocked(
                call.Arg<SecurityValuationRequest>(), Detail(securityId), SecurityValuationModelKind.QuotedPrice, "quoted-price", "v1", AsOfInstant,
                [new SecurityValuationBlockerDto("NoPriceSelection", "no hierarchy")])));
        await using var app = await CreateAppAsync(store, valuation, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        (await client.PostAsJsonAsync($"/api/security-master/{securityId}/valuation", new SecurityValuationRequest(securityId, AsOfInstant), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/security-master/curves")).StatusCode.Should().Be(HttpStatusCode.OK);

        var define = new DefineSecurityCurveRequest("USD-OIS", SecurityCurveKind.Discount, "USD", null, null, "ACT/360", SecurityCurveInterpolation.Linear, "vendor", null);
        (await client.PostAsJsonAsync("/api/security-master/curves", define, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var curveId = Guid.NewGuid();
        var publish = new PublishSecurityCurveVersionRequest(curveId, AsOf, [new SecurityCurvePointDto(AsOf, 1m)], "vendor");
        (await client.PostAsJsonAsync($"/api/security-master/curves/{curveId}/versions", publish, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await store.DidNotReceive().DefineAsync(Arg.Any<DefineSecurityCurveRequest>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().PublishVersionAsync(Arg.Any<PublishSecurityCurveVersionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutACurveStoreOrValuationService_TheRoutesAreAbsent()
    {
        await using var app = await CreateAppAsync(store: null, valuation: null);
        var client = app.GetTestClient();

        (await client.GetAsync("/api/security-master/curves")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/security-master/{Guid.NewGuid()}/valuation", new SecurityValuationRequest(Guid.NewGuid(), AsOfInstant), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static SecurityCurveDefinitionDto Definition()
        => new(Guid.NewGuid(), "USD-OIS", SecurityCurveKind.Discount, "USD", null, null, "Actual360", SecurityCurveInterpolation.Linear, "vendor", null, "casey.doyle", AsOfInstant);

    private static SecurityCurveVersionDto Version(SecurityCurveDefinitionDto definition, long version)
    {
        IReadOnlyList<SecurityCurvePointDto> points = [new SecurityCurvePointDto(AsOf, 1m), new SecurityCurvePointDto(AsOf.AddYears(1), 0.96m)];
        return new SecurityCurveVersionDto(Guid.NewGuid(), definition.CurveId, definition.CurveKey, version, AsOf, AsOfInstant.AddHours(version), null, "vendor", "casey.doyle", points, SecurityCurveRules.ContentSha256(AsOf, points));
    }

    private static SecurityDetailDto Detail(Guid securityId)
        => new(securityId, "Bond", SecurityStatusDto.Active, "Test", "USD", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [], 1, AsOfInstant.AddDays(-1), null);

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityCurveStore? store,
        ISecurityValuationService? valuation = null,
        UserPermission permissions = UserPermission.ModifySecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        if (store is not null)
            builder.Services.AddSingleton(store);
        if (valuation is not null)
            builder.Services.AddSingleton(valuation);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "casey.doyle";
            await next();
        });
        app.MapSecurityMasterEndpoints(Json);

        await app.StartAsync();
        return app;
    }
}
