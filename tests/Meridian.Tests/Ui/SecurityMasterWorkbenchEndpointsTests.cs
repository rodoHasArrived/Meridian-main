using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Phase 4 governed-write surface: the workbench endpoints route through
/// <see cref="ISecurityMasterWorkbenchCommandService"/> behind <see cref="UserPermission.ModifySecurityMaster"/>
/// and a tenant scope, and map domain failures to stable status codes (409 stale version / lifecycle
/// conflict, 422 rejected invariant, 500 publish side-effect failure).
/// </summary>
public sealed class SecurityMasterWorkbenchEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string SessionActor = "session.actor";

    [Fact]
    public async Task Field_WithoutModifyPermission_Returns403()
    {
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        await using var app = await CreateAppAsync(service, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{Guid.NewGuid():D}/workbench/field", FieldRequest(Guid.NewGuid()), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await service.DidNotReceive().UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Field_WithPermission_Returns200_AndServerOverridesPathIdAndActor()
    {
        var pathId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        UpdateSecurityFieldRequest? captured = null;
        service.UpdateSecurityFieldAsync(Arg.Do<UpdateSecurityFieldRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(ci => EditResult(((UpdateSecurityFieldRequest)ci[0]).SecurityId));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        // Body carries a DIFFERENT security id than the path AND a spoofed actor; both must be overridden.
        var spoofed = FieldRequest(Guid.NewGuid()) with { Actor = "spoofed.actor" };
        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{pathId:D}/workbench/field", spoofed, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.SecurityId.Should().Be(pathId, "the route id is authoritative over the request body");
        captured.Actor.Should().Be(SessionActor, "the acting principal is derived from the session, not the body");
    }

    [Fact]
    public async Task Field_WithForeignFundProfile_Returns403_WithoutPersisting()
    {
        // The tenant guard reports the body-supplied fund profile as belonging to another company; the
        // endpoint must reject before the draft (which would later scope restatement impact) is persisted.
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("foreign"));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster, fundGuard: guard);
        var client = app.GetTestClient();

        var body = FieldRequest(Guid.NewGuid()) with { FundProfileId = "fund-other-company" };
        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{Guid.NewGuid():D}/workbench/field", body, Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await service.DidNotReceive().UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Field_WithAllowedFundProfile_Returns200()
    {
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        service.UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => EditResult(((UpdateSecurityFieldRequest)ci[0]).SecurityId));
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Allow("own fund"));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster, fundGuard: guard);
        var client = app.GetTestClient();

        var body = FieldRequest(Guid.NewGuid()) with { FundProfileId = "fund-caller" };
        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{Guid.NewGuid():D}/workbench/field", body, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await service.Received(1).UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Field_WithBlankFundProfile_SkipsGuard_Returns200()
    {
        // A blank fund scope is the existing unscoped semantics and never persists a foreign fund, so the
        // guard must not even be consulted (and a deny-everything guard must not block the edit).
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        service.UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => EditResult(((UpdateSecurityFieldRequest)ci[0]).SecurityId));
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("should not be called"));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster, fundGuard: guard);
        var client = app.GetTestClient();

        // FieldRequest carries no FundProfileId (null) → guard is skipped.
        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{Guid.NewGuid():D}/workbench/field", FieldRequest(Guid.NewGuid()), Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await guard.DidNotReceive().EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await service.Received(1).UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_DerivesActorAndReviewerFromSession_IgnoringBody()
    {
        // The impersonation vector: a caller posts the assigned reviewer's name to approve as that
        // person. The endpoint must override both Actor and Reviewer with the authenticated session id.
        var securityId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        ApproveSecurityMasterRevisionRequest? captured = null;
        service.ApproveRevisionAsync(Arg.Do<ApproveSecurityMasterRevisionRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(ci => EditResult(((ApproveSecurityMasterRevisionRequest)ci[0]).SecurityId));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        var body = new ApproveSecurityMasterRevisionRequest(
            SecurityId: securityId,
            RevisionId: Guid.NewGuid(),
            WorkflowId: Guid.NewGuid(),
            ExpectedWorkflowVersion: 1,
            Actor: "attacker",
            Reviewer: "victim.assigned.reviewer",
            Rationale: "looks good",
            ReportPackId: "rp-1");

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/approve", body, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Actor.Should().Be(SessionActor);
        captured.Reviewer.Should().Be(SessionActor, "the caller is the reviewer; a body-supplied reviewer name must not impersonate");
    }

    [Fact]
    public async Task Submit_WithoutWorkflowId_Returns422_WithoutAdvancingRevision()
    {
        // A workflow-less submit would advance the revision to Submitted with no bound workflow, which the
        // approve route can never satisfy — so the governed endpoint must reject it up front.
        var securityId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        var body = new SubmitSecurityMasterRevisionRequest(
            SecurityId: securityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            Note: "ready",
            WorkflowId: null);

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/submit", body, Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetString().Should().Be("workflow-required");
        await service.DidNotReceive().SubmitForApprovalAsync(Arg.Any<SubmitSecurityMasterRevisionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnauthenticatedActor_Returns401_WithoutTouchingService()
    {
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        // Permission present but no resolvable session actor.
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster, sessionActor: null);
        var client = app.GetTestClient();

        var securityId = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/field", FieldRequest(securityId), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await service.DidNotReceive().UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Field_StaleExpectedVersion_Returns409_WithCurrentVersion()
    {
        var securityId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        service.UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>())
            .Throws(new SecurityMasterConcurrencyException(securityId, expectedVersion: 3, currentVersion: 7));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/field", FieldRequest(securityId), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("version-conflict");
        body.GetProperty("currentVersion").GetInt64().Should().Be(7);
    }

    [Fact]
    public async Task Field_RejectedInvariant_Returns422()
    {
        var securityId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        service.UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>())
            .Throws(new ArgumentException("An operator-origin edit requires a non-empty justification."));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/field", FieldRequest(securityId), Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Publish_NotApproved_Returns409_RevisionStateConflict()
    {
        var securityId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        service.PublishRevisionAsync(Arg.Any<PublishSecurityMasterRevisionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new SecurityMasterRevisionStateException(revisionId, "expected state Approved but the revision is Submitted."));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/publish",
            new PublishSecurityMasterRevisionRequest(securityId, revisionId, "ops.analyst", "ops.approver"), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("revision-state-conflict");
    }

    [Fact]
    public async Task Publish_SideEffectFailure_Returns500()
    {
        var securityId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        service.PublishRevisionAsync(Arg.Any<PublishSecurityMasterRevisionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new SecurityMasterPublishFailedException(securityId, revisionId, ["SecurityProjectionRebuildHandler"]));
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/security-master/{securityId:D}/workbench/publish",
            new PublishSecurityMasterRevisionRequest(securityId, revisionId, "ops.analyst", "ops.approver"), Json);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("publish-side-effect-failed");
    }

    [Fact]
    public async Task MissingBody_RejectsWithClientError_WithoutTouchingService()
    {
        var service = Substitute.For<ISecurityMasterWorkbenchCommandService>();
        await using var app = await CreateAppAsync(service, UserPermission.ModifySecurityMaster);
        var client = app.GetTestClient();

        // No JSON body → the endpoint rejects (4xx) before touching the command service.
        using var response = await client.PostAsync(
            $"/api/security-master/{Guid.NewGuid():D}/workbench/field", content: null);

        ((int)response.StatusCode).Should().BeInRange(400, 499);
        await service.DidNotReceive().UpdateSecurityFieldAsync(Arg.Any<UpdateSecurityFieldRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static UpdateSecurityFieldRequest FieldRequest(Guid securityId)
        => new(
            SecurityId: securityId,
            ExpectedVersion: 3,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "5.125",
            EffectiveFrom: new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
            Actor: "ops.analyst",
            Justification: "Vendor confirmation #4821 corrects the coupon.");

    private static SecurityMasterEditResultDto EditResult(Guid securityId)
        => new(
            SecurityId: securityId,
            RevisionId: Guid.NewGuid(),
            NewVersion: 4,
            State: SecurityMasterRevisionStateDto.Draft,
            ChangeEntry: new SecurityMasterChangeHistoryItemDto(
                ChangeId: Guid.NewGuid().ToString("N"),
                StreamVersion: 4,
                EventType: "OperatorFieldAnnotation",
                ChangedAtUtc: new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveAtUtc: new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
                Actor: "ops.analyst",
                Origin: "Operator",
                SourceSystem: "operator",
                SourceRecordId: null,
                Reason: "Vendor confirmation #4821 corrects the coupon.",
                Summary: "Coupon corrected to 5.125.",
                ChangedFields: ["EconomicDefinition.Coupon"],
                ChangedFieldsSummary: "EconomicDefinition.Coupon"));

    private static async Task<WebApplication> CreateAppAsync(
        ISecurityMasterWorkbenchCommandService service,
        UserPermission permissions,
        string? sessionActor = SessionActor,
        IFundProfileTenantGuard? fundGuard = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        if (fundGuard is not null)
        {
            builder.Services.AddSingleton(fundGuard);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            if (sessionActor is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = sessionActor;
            }
            await next();
        });
        app.MapSecurityMasterWorkbenchEndpoints(Json);

        await app.StartAsync();
        return app;
    }
}
