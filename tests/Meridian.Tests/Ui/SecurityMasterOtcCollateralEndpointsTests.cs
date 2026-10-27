using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
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
/// The exposure, collateral balance and margin call routes: every write stamps the session actor
/// and discards the body's and needs Modify permission, a rule failure is a 400, computing for an
/// unknown netting set and reading an unknown snapshot or call are 404s, issuing from an unknown
/// snapshot or moving an unknown call is a 400 (the store refuses it as a rule failure), reads pass
/// the store's answer through, and a host without the collateral services maps none of the routes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterOtcCollateralEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    [Fact]
    public async Task ComputeExposure_StampsTheActor_AndMapsNullToNotFound_AndARuleFailureToBadRequest()
    {
        var nettingSetId = Guid.NewGuid();
        var exposure = Substitute.For<IOtcExposureService>();
        exposure.ComputeAsync(nettingSetId, AsOf, Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(Snapshot(nettingSetId));
        exposure.ComputeAsync(Arg.Is<Guid>(id => id != nettingSetId), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns((OtcExposureSnapshotDto?)null);
        await using var app = await CreateAppAsync(Substitute.For<IOtcCollateralStore>(), exposure);
        var client = app.GetTestClient();

        using var ok = await client.PostAsJsonAsync($"/api/security-master/otc/netting-sets/{nettingSetId}/exposure-snapshots", new ComputeOtcExposureSnapshotRequest(AsOf), Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<OtcExposureSnapshotDto>(Json))!.NettingSetId.Should().Be(nettingSetId);
        await exposure.Received(1).ComputeAsync(nettingSetId, AsOf, "casey.doyle", null, Arg.Any<CancellationToken>());

        using var missing = await client.PostAsJsonAsync($"/api/security-master/otc/netting-sets/{Guid.NewGuid()}/exposure-snapshots", new ComputeOtcExposureSnapshotRequest(AsOf), Json);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        exposure.ComputeAsync(nettingSetId, AsOf, Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtcExposureSnapshotDto?>(new ArgumentException("The netting set has no collateral agreement and no contracts, so there is no currency to compute exposure in.")));
        using var refused = await client.PostAsJsonAsync($"/api/security-master/otc/netting-sets/{nettingSetId}/exposure-snapshots", new ComputeOtcExposureSnapshotRequest(AsOf), Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("no currency");
    }

    [Fact]
    public async Task Snapshots_ReadBack_OrNotFound()
    {
        var nettingSetId = Guid.NewGuid();
        var snapshot = Snapshot(nettingSetId);
        var store = Substitute.For<IOtcCollateralStore>();
        store.GetExposureSnapshotAsync(snapshot.SnapshotId, Arg.Any<CancellationToken>()).Returns(snapshot);
        store.ListExposureSnapshotsAsync(nettingSetId, AsOf, false, Arg.Any<CancellationToken>()).Returns(new[] { snapshot, snapshot with { Version = 1 } });
        store.ListExposureSnapshotsAsync(nettingSetId, null, true, Arg.Any<CancellationToken>()).Returns(new[] { snapshot });
        await using var app = await CreateAppAsync(store, Substitute.For<IOtcExposureService>());
        var client = app.GetTestClient();

        (await client.GetFromJsonAsync<OtcExposureSnapshotDto>($"/api/security-master/otc/exposure-snapshots/{snapshot.SnapshotId}", Json))!.Movements.Should().ContainSingle();
        (await client.GetFromJsonAsync<List<OtcExposureSnapshotDto>>($"/api/security-master/otc/netting-sets/{nettingSetId}/exposure-snapshots", Json)).Should().ContainSingle("latest version only by default");
        (await client.GetFromJsonAsync<List<OtcExposureSnapshotDto>>($"/api/security-master/otc/netting-sets/{nettingSetId}/exposure-snapshots?asOfDate=2026-03-31&latestVersionOnly=false", Json)).Should().HaveCount(2);
        (await client.GetAsync($"/api/security-master/otc/exposure-snapshots/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BalanceWrites_StampTheActor_AndMapARuleFailureToBadRequest()
    {
        var csaId = Guid.NewGuid();
        var store = Substitute.For<IOtcCollateralStore>();
        UpsertOtcCollateralBalanceRequest? captured = null;
        store.UpsertCollateralBalanceAsync(Arg.Do<UpsertOtcCollateralBalanceRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Balance(csaId)));
        store.ListCollateralBalancesAsync(csaId, Arg.Any<CancellationToken>()).Returns(new[] { Balance(csaId), Balance(csaId) });
        await using var app = await CreateAppAsync(store, Substitute.For<IOtcExposureService>());
        var client = app.GetTestClient();

        var request = new UpsertOtcCollateralBalanceRequest(null, csaId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 400_000m, 1m, AsOf, null, null, "untrusted.body.actor");
        using var ok = await client.PostAsJsonAsync("/api/security-master/otc/collateral-balances", request, Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.UpdatedBy.Should().Be("casey.doyle", "the actor comes from the session, not the body");
        captured.Quantity.Should().Be(400_000m);

        (await client.GetFromJsonAsync<List<OtcCollateralBalanceDto>>($"/api/security-master/otc/collateral-agreements/{csaId}/collateral-balances", Json)).Should().HaveCount(2);

        store.UpsertCollateralBalanceAsync(Arg.Any<UpsertOtcCollateralBalanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtcCollateralBalanceDto>(new ArgumentException("'Equities' is not eligible under this collateral agreement.")));
        using var refused = await client.PostAsJsonAsync("/api/security-master/otc/collateral-balances", request, Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("not eligible");
    }

    [Fact]
    public async Task MarginCallWrites_StampTheActor_TheRouteIdWins_AndReadsReturnNotFound()
    {
        var nettingSetId = Guid.NewGuid();
        var call = Call(nettingSetId);
        var store = Substitute.For<IOtcCollateralStore>();
        IssueOtcMarginCallRequest? issued = null;
        TransitionOtcMarginCallRequest? transitioned = null;
        store.IssueMarginCallAsync(Arg.Do<IssueOtcMarginCallRequest>(r => issued = r), Arg.Any<CancellationToken>()).Returns(call);
        store.TransitionMarginCallAsync(Arg.Do<TransitionOtcMarginCallRequest>(r => transitioned = r), Arg.Any<CancellationToken>())
            .Returns(call with { Status = OtcMarginCallStatus.Agreed, AmountAgreed = 330_000m, Version = 2 });
        store.GetMarginCallAsync(call.CallId, Arg.Any<CancellationToken>()).Returns(call);
        store.ListMarginCallsAsync(nettingSetId, Arg.Any<CancellationToken>()).Returns(new[] { call });
        await using var app = await CreateAppAsync(store, Substitute.For<IOtcExposureService>());
        var client = app.GetTestClient();

        using var issue = await client.PostAsJsonAsync("/api/security-master/otc/margin-calls",
            new IssueOtcMarginCallRequest(call.SnapshotId, OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, "body"), Json);
        issue.StatusCode.Should().Be(HttpStatusCode.OK);
        issued!.UpdatedBy.Should().Be("casey.doyle");
        issued.SnapshotId.Should().Be(call.SnapshotId);

        using var agree = await client.PostAsJsonAsync($"/api/security-master/otc/margin-calls/{call.CallId}/transition",
            new TransitionOtcMarginCallRequest(Guid.NewGuid(), OtcMarginCallAction.Agree, null, null, null, null, "body"), Json);
        agree.StatusCode.Should().Be(HttpStatusCode.OK);
        transitioned!.CallId.Should().Be(call.CallId, "the route's call id wins over the body's");
        transitioned.Action.Should().Be(OtcMarginCallAction.Agree);
        transitioned.UpdatedBy.Should().Be("casey.doyle");
        (await agree.Content.ReadFromJsonAsync<OtcMarginCallDto>(Json))!.Status.Should().Be(OtcMarginCallStatus.Agreed);

        (await client.GetFromJsonAsync<OtcMarginCallDto>($"/api/security-master/otc/margin-calls/{call.CallId}", Json))!.AmountCalled.Should().Be(330_000m);
        (await client.GetFromJsonAsync<List<OtcMarginCallDto>>($"/api/security-master/otc/netting-sets/{nettingSetId}/margin-calls", Json)).Should().ContainSingle();
        (await client.GetAsync($"/api/security-master/otc/margin-calls/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        store.IssueMarginCallAsync(Arg.Any<IssueOtcMarginCallRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtcMarginCallDto>(new ArgumentException("The exposure snapshot does not exist.")));
        using var issueRefused = await client.PostAsJsonAsync("/api/security-master/otc/margin-calls",
            new IssueOtcMarginCallRequest(Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf, null, null, null), Json);
        issueRefused.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unknown snapshot is refused by the store as a rule failure");
        (await issueRefused.Content.ReadAsStringAsync()).Should().Contain("does not exist");

        store.TransitionMarginCallAsync(Arg.Any<TransitionOtcMarginCallRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OtcMarginCallDto>(new ArgumentException("Call is Settled and can no longer change.")));
        using var refused = await client.PostAsJsonAsync($"/api/security-master/otc/margin-calls/{call.CallId}/transition",
            new TransitionOtcMarginCallRequest(call.CallId, OtcMarginCallAction.Cancel, null, "late", null, null, null), Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Writes_WithoutModifyPermission_AreForbidden()
    {
        var store = Substitute.For<IOtcCollateralStore>();
        var exposure = Substitute.For<IOtcExposureService>();
        await using var app = await CreateAppAsync(store, exposure, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        (await client.PostAsJsonAsync("/api/security-master/otc/collateral-balances",
            new UpsertOtcCollateralBalanceRequest(null, Guid.NewGuid(), OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 1m, 1m, AsOf), Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/security-master/otc/netting-sets/{Guid.NewGuid()}/exposure-snapshots", new ComputeOtcExposureSnapshotRequest(AsOf), Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/security-master/otc/margin-calls",
            new IssueOtcMarginCallRequest(Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf), Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync($"/api/security-master/otc/margin-calls/{Guid.NewGuid()}/transition",
            new TransitionOtcMarginCallRequest(Guid.NewGuid(), OtcMarginCallAction.Agree), Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/security-master/otc/netting-sets/{Guid.NewGuid()}/margin-calls")).StatusCode.Should().Be(HttpStatusCode.OK, "reads need only view permission");

        await store.DidNotReceiveWithAnyArgs().UpsertCollateralBalanceAsync(default!, default);
        await store.DidNotReceiveWithAnyArgs().IssueMarginCallAsync(default!, default);
        await store.DidNotReceiveWithAnyArgs().TransitionMarginCallAsync(default!, default);
        await exposure.DidNotReceiveWithAnyArgs().ComputeAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task AHostWithoutTheCollateralServices_MapsNoneOfTheRoutes()
    {
        await using var app = await CreateAppAsync(null, null);
        var client = app.GetTestClient();

        (await client.PostAsJsonAsync($"/api/security-master/otc/netting-sets/{Guid.NewGuid()}/exposure-snapshots", new ComputeOtcExposureSnapshotRequest(AsOf), Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the route is unavailable rather than a 500 from an unresolvable service");
        (await client.GetAsync($"/api/security-master/otc/exposure-snapshots/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/security-master/otc/margin-calls",
            new IssueOtcMarginCallRequest(Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, AsOf), Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/security-master/otc/netting-sets/{Guid.NewGuid()}/margin-calls")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static OtcExposureSnapshotDto Snapshot(Guid nettingSetId)
    {
        var movement = new OtcCollateralMovementDto(OtcMovementKind.Delivery, OtcMovementParty.Fund, 330_000m, "USD");
        var fund = new OtcCreditSupportSideDto(OtcMovementParty.Fund, 75_000m, 0m, 1_000_000m, 250_000m, 0m, 0m, 0m, 0m, null);
        var counterparty = new OtcCreditSupportSideDto(OtcMovementParty.Counterparty, -75_000m, 1_000_000m, 0m, 0m, 925_000m, 596_245m, 328_755m, -328_755m, movement);
        return new OtcExposureSnapshotDto(
            Guid.NewGuid(), nettingSetId, Guid.NewGuid(), Guid.NewGuid(), AsOf, 2, OtcExposureRules.RuleVersion, "USD",
            OtcNettingBasis.CloseOutNet, OtcExposureSnapshotStatus.Complete, DataProvenance.Real, 1, 1, [], 125_000m, 50_000m, 75_000m,
            fund, counterparty, [], [movement], [], new string('a', 64), "casey.doyle", DateTimeOffset.UtcNow);
    }

    private static OtcCollateralBalanceDto Balance(Guid csaId)
        => new(Guid.NewGuid(), csaId, OtcCollateralDirection.Posted, OtcCollateralKind.Cash, "USD cash", null, "USD", 400_000m, 1m, 400_000m, 0m, AsOf, null, null, 1, "casey.doyle", DateTimeOffset.UtcNow);

    private static OtcMarginCallDto Call(Guid nettingSetId)
        => new(Guid.NewGuid(), nettingSetId, Guid.NewGuid(), Guid.NewGuid(), OtcMovementKind.Delivery, OtcMovementParty.Fund, "USD", 330_000m, null,
            OtcMarginCallStatus.Issued, AsOf, AsOf.AddDays(1), null, null, null, null, 1, "casey.doyle", DateTimeOffset.UtcNow);

    private static async Task<WebApplication> CreateAppAsync(IOtcCollateralStore? store, IOtcExposureService? exposure, UserPermission permissions = UserPermission.ModifySecurityMaster)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterService>());
        builder.Services.AddSingleton(Substitute.For<IOtcContractStore>());
        if (store is not null)
            builder.Services.AddSingleton(store);
        if (exposure is not null)
            builder.Services.AddSingleton(exposure);

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
