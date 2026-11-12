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

public sealed class SecurityMasterRateEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly SecurityRateObservationDto Observation = new("fred", "SOFR", 4.12345678901234567890123456m,
        "4.12345678901234567890123456", SecurityRateUnit.Percent, "Percent", AsOf, null, AsOf,
        new DateOnly(9999, 12, 31), new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        "lin (untransformed provider level)", SecurityRateObservationKind.ObservedFixing, "USD");

    [Fact]
    public async Task Read_ReturnsExactDecimalStringsAndTypedProvenance()
    {
        var provider = Provider();
        await using var app = await CreateAppAsync(Substitute.For<ISecurityCurveStore>(), provider);
        using var response = await app.GetTestClient().GetAsync("/api/security-master/rates/observations?provider=fred&seriesId=SOFR&from=2026-01-01&to=2026-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        document.RootElement[0].GetProperty("value").GetString().Should().Be(Observation.OriginalValue);
        document.RootElement[0].GetProperty("publishedAt").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement[0].GetProperty("kind").GetString().Should().Be("ObservedFixing");
    }

    [Fact]
    public async Task Publish_ReverifiesProviderFactsAndStampsAuthenticatedReviewer()
    {
        var definition = Definition();
        var store = Substitute.For<ISecurityCurveStore>();
        store.GetDefinitionAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(definition);
        PublishSecurityCurveVersionRequest? captured = null;
        store.PublishVersionAsync(Arg.Do<PublishSecurityCurveVersionRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(call => new SecurityCurveVersionDto(Guid.NewGuid(), definition.CurveId, definition.CurveKey, 1, AsOf,
                Observation.RetrievedAt, null, "fred", "session.operator", ((PublishSecurityCurveVersionRequest)call[0]).Points, "hash"));
        var provider = Provider();
        await using var app = await CreateAppAsync(store, provider);
        // The client supplies a false publication time. Only the freshly verified provider fact is retained.
        var request = Request(definition.CurveId, Observation with { PublishedAt = Observation.RetrievedAt.AddDays(50) });
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/security-master/rates/ingest", request, Json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Points[0].RateEvidence!.Observation.Should().Be(Observation);
        captured.Points[0].RateEvidence!.ReviewedBy.Should().Be("session.operator");
        captured.Points[0].Value.Should().Be(0.0412345678901234567890123456m);
        await provider.Received(1).GetRateObservationsAsync("SOFR", AsOf, AsOf, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_RefusesEditedProviderValueAndViewerCannotWrite()
    {
        var definition = Definition();
        var store = Substitute.For<ISecurityCurveStore>();
        store.GetDefinitionAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(definition);
        await using var app = await CreateAppAsync(store, Provider());
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/security-master/rates/ingest",
            Request(definition.CurveId, Observation with { Value = 5m, OriginalValue = "5" }), Json);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await store.DidNotReceive().PublishVersionAsync(Arg.Any<PublishSecurityCurveVersionRequest>(), Arg.Any<CancellationToken>());

        await using var viewer = await CreateAppAsync(store, Provider(), UserPermission.ViewSecurityMaster);
        using var forbidden = await viewer.GetTestClient().PostAsJsonAsync("/api/security-master/rates/ingest", Request(definition.CurveId, Observation), Json);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await store.DidNotReceive().PublishVersionAsync(Arg.Any<PublishSecurityCurveVersionRequest>(), Arg.Any<CancellationToken>());
    }

    private static IngestSecurityRatesRequest Request(Guid curveId, SecurityRateObservationDto observation) => new(curveId, AsOf,
        SecurityRateConstructionMethod.ObservedFixingFlatForecast, "Reviewed assumption, not a future fixing.", [new(AsOf.AddYears(1), observation)]);

    [Fact]
    public async Task Publish_WhenProviderVerificationFails_ReturnsActionableConflictAndWritesNothing()
    {
        var definition = Definition();
        var store = Substitute.For<ISecurityCurveStore>();
        store.GetDefinitionAsync(definition.CurveId, Arg.Any<CancellationToken>()).Returns(definition);
        var provider = Provider();
        provider.GetRateObservationsAsync("SOFR", Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SecurityRateObservationDto>>(new HttpRequestException("provider offline")));
        await using var app = await CreateAppAsync(store, provider);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/security-master/rates/ingest", Request(definition.CurveId, Observation), Json);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("reload observations");
        await store.DidNotReceive().PublishVersionAsync(Arg.Any<PublishSecurityCurveVersionRequest>(), Arg.Any<CancellationToken>());
    }

    private static SecurityCurveDefinitionDto Definition() => new(Guid.NewGuid(), "USD-SOFR", SecurityCurveKind.Forward,
        "USD", "SOFR", null, "Actual360", SecurityCurveInterpolation.Linear, "fred", null, "owner", Observation.RetrievedAt);

    private static ISecurityRateObservationProvider Provider()
    {
        var provider = Substitute.For<ISecurityRateObservationProvider>();
        provider.Name.Returns("fred");
        provider.GetRateObservationsAsync("SOFR", Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Observation });
        return provider;
    }

    private static async Task<WebApplication> CreateAppAsync(ISecurityCurveStore store, ISecurityRateObservationProvider provider,
        UserPermission permissions = UserPermission.ModifySecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(provider);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "session.operator";
            await next();
        });
        app.MapSecurityMasterEndpoints(Json);
        await app.StartAsync();
        return app;
    }
}
