using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class CorporateActionOperationsEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid SecurityId = Guid.Parse("40fac25c-9613-45df-9450-5a2eed01c5ce");
    private static readonly Guid ProposalId = Guid.Parse("bb390ac9-90e8-474b-9d90-bc5e673a6f75");
    private const string FanOutBlocker =
        "Corporate-action source decisions are read-only until an authoritative service can enumerate every affected tenant/company scope and apply the decision atomically.";

    [Fact]
    public async Task Inbox_RemainsReadableButLocksSourceDecisionsUntilAuthoritativeFanOutExists()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        service.GetInboxAsync(Arg.Any<CorporateActionCaseScopeDto>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CreateInbox());
        await using var app = await CreateAppAsync(
            service,
            UserPermission.ViewCorporateActions |
            UserPermission.ModifySecurityMaster |
            UserPermission.ResolveCorporateActionTerms);

        var response = await app.GetTestClient().GetAsync("/api/security-master/corporate-actions/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<CorporateActionDurableInboxDto>(JsonOptions);
        payload.Should().NotBeNull();
        var entry = payload!.Staged.Should().ContainSingle().Subject;
        entry.ProposalId.Should().Be(ProposalId);
        entry.Version.Should().Be(7);
        entry.AcceptanceScope.TenantId.Should().Be("tenant-a");
        entry.AcceptanceScope.CompanyId.Should().Be("company-a");
        entry.ActionAvailability.CanAccept.Should().BeFalse();
        entry.ActionAvailability.CanReject.Should().BeFalse();
        entry.ActionAvailability.CanCompareEvidence.Should().BeFalse();
        entry.ActionAvailability.Blockers.Should().ContainSingle().Which.Should().Be(FanOutBlocker);
        var processingCase = payload.Cases.Should().ContainSingle().Subject;
        processingCase.SourceSnapshot.Should().NotBeNull();
        processingCase.SourceSnapshot!.ProposedAction.EventType.Should().Be(CorporateActionEventTypes.Dividend);
        processingCase.SourceSnapshot.ProviderIdentity.SourceEventId.Should().Be("event-1");
        processingCase.SourceSnapshot.DisplayMetadata!.Ticker.Should().Be("ACME");
        await service.Received(1).GetInboxAsync(
            Arg.Is<CorporateActionCaseScopeDto>(scope =>
                scope.TenantId == "tenant-a" && scope.CompanyId == "company-a"),
            250,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inbox_ViewOnlyCallerReceivesLockedMutationPosture()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        service.GetInboxAsync(Arg.Any<CorporateActionCaseScopeDto>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CreateInbox());
        await using var app = await CreateAppAsync(service, UserPermission.ViewCorporateActions);

        var response = await app.GetTestClient().GetAsync("/api/security-master/corporate-actions/inbox");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<CorporateActionDurableInboxDto>(JsonOptions);
        var availability = payload!.Staged.Should().ContainSingle().Subject.ActionAvailability;
        availability.CanAccept.Should().BeFalse();
        availability.CanReject.Should().BeFalse();
        availability.Blockers.Should().Contain(blocker =>
            blocker.Contains(nameof(UserPermission.ModifySecurityMaster), StringComparison.Ordinal) &&
            blocker.Contains(nameof(UserPermission.ResolveCorporateActionTerms), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(UserPermission.ResolveCorporateActionTerms)]
    [InlineData(UserPermission.ModifySecurityMaster)]
    [InlineData(UserPermission.ViewCorporateActions)]
    public async Task AcceptCanonicalFact_RequiresBothSecurityMasterAndTermResolutionAuthority(
        UserPermission incompleteGrant)
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(service, incompleteGrant);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/security-master/corporate-actions/source-proposals/{ProposalId:D}/accept",
            AcceptRequest(new CorporateActionCaseScopeDto("tenant-a", "company-a")),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await service.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptCanonicalFact_RejectsCallerSuppliedCrossCompanyScopeBeforeServiceCall()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(
            service,
            UserPermission.ModifySecurityMaster | UserPermission.ResolveCorporateActionTerms);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/security-master/corporate-actions/source-proposals/{ProposalId:D}/accept",
            AcceptRequest(new CorporateActionCaseScopeDto("tenant-a", "company-b")),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CorporateActionProblemCodes.ScopeMismatch);
        await service.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptCanonicalFact_WhenFanOutIsUnavailable_ReturnsTypedServiceUnavailableWithoutServiceCall()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(
            service,
            UserPermission.ModifySecurityMaster | UserPermission.ResolveCorporateActionTerms);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/security-master/corporate-actions/source-proposals/{ProposalId:D}/accept",
            AcceptRequest(new CorporateActionCaseScopeDto("tenant-a", "company-a")),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CorporateActionProblemCodes.PersistenceUnavailable);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(FanOutBlocker);
        await service.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LegacyInboxApplyRoute_IsAnAuthorizedGoneTombstone()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(
            service,
            UserPermission.ModifySecurityMaster | UserPermission.ResolveCorporateActionTerms);

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/corporate-actions/inbox/apply",
            AcceptRequest(new CorporateActionCaseScopeDto("tenant-a", "company-a")),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        await service.DidNotReceive().AcceptSourceProposalAsync(
            Arg.Any<AcceptCorporateActionSourceProposalRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSourceProposal_PublicCallerCannotGrantAcceptanceEligibleRelease()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        service.RecordSourceProposalAsync(
                Arg.Any<RecordCorporateActionSourceProposalRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<RecordCorporateActionSourceProposalRequestDto>(0);
                var now = DateTimeOffset.UtcNow;
                return new CorporateActionSourceProposalDto(
                    request.ProposalId ?? Guid.NewGuid(),
                    SecurityId,
                    request.ProviderIdentity,
                    request.ProposedAction,
                    request.ProposedAction.PayloadSchemaVersion,
                    CorporateActionEconomicFingerprint.Compute(request.ProposedAction),
                    CorporateActionSourceProposalStates.ReviewRequired,
                    1,
                    request.SupersedesProposalId,
                    null,
                    null,
                    request.Actor,
                    now,
                    now);
            });
        await using var app = await CreateAppAsync(service, UserPermission.IngestCorporateActions);
        var action = Dividend();
        var request = new RecordCorporateActionSourceProposalRequestDto(
            action,
            new CorporateActionProviderEventIdentityDto(
                "alpaca",
                "announcement-100",
                "payload-sha256:v1",
                DateTimeOffset.UtcNow,
                new string('a', 64),
                "alpaca://corporate-actions/announcements/announcement-100/versions/v1",
                CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
            Actor: "forged-browser-actor");

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/security-master/corporate-actions/source-proposals",
            request,
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await service.Received(1).RecordSourceProposalAsync(
            Arg.Is<RecordCorporateActionSourceProposalRequestDto>(recorded =>
                recorded.Actor == "operations-user"
                && recorded.ProviderIdentity.ReleaseStatus
                == CorporateActionProviderReleaseStatusDto.ReviewOnly),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectSourceProposal_WhenFanOutIsUnavailable_ReturnsTypedServiceUnavailableWithoutServiceCall()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(
            service,
            UserPermission.ModifySecurityMaster | UserPermission.ResolveCorporateActionTerms);

        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/security-master/corporate-actions/source-proposals/{ProposalId:D}/reject",
            new RejectCorporateActionSourceProposalRequestDto(
                ProposalId,
                ExpectedVersion: 7,
                IdempotencyKey: "reject:proposal:v7",
                Actor: "browser",
                Reason: "Provider observation does not match retained evidence."),
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CorporateActionProblemCodes.PersistenceUnavailable);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(FanOutBlocker);
        await service.DidNotReceive().RejectSourceProposalAsync(
            Arg.Any<RejectCorporateActionSourceProposalRequestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CaseConflictReads_ReturnDurableIdentifierWithinTrustedScope()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        var processingCase = TermsConfirmedCase();
        var conflict = Conflict(processingCase.CaseId);
        service.GetCaseAsync(
                processingCase.CaseId,
                "tenant-a",
                "company-a",
                Arg.Any<CancellationToken>())
            .Returns(processingCase);
        service.ListConflictsAsync(
                processingCase.CaseId,
                "tenant-a",
                "company-a",
                CorporateActionConflictStates.Open,
                25,
                Arg.Any<CancellationToken>())
            .Returns(new[] { conflict });
        service.GetConflictAsync(
                processingCase.CaseId,
                conflict.ConflictId,
                "tenant-a",
                "company-a",
                Arg.Any<CancellationToken>())
            .Returns(conflict);
        await using var app = await CreateAppAsync(service, UserPermission.ViewCorporateActions);

        var listResponse = await app.GetTestClient().GetAsync(
            $"/api/security-master/corporate-actions/cases/{processingCase.CaseId:D}/conflicts?state=Open&take=25");
        var detailResponse = await app.GetTestClient().GetAsync(
            $"/api/security-master/corporate-actions/cases/{processingCase.CaseId:D}/conflicts/{conflict.ConflictId:D}");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<CorporateActionConflictDto[]>(JsonOptions);
        listed.Should().NotBeNull();
        listed!.Should().ContainSingle().Which.ConflictId.Should().Be(conflict.ConflictId);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await detailResponse.Content.ReadFromJsonAsync<CorporateActionConflictDto>(JsonOptions);
        loaded.Should().NotBeNull();
        loaded!.ConflictId.Should().Be(conflict.ConflictId);
        loaded.Candidates.Should().HaveCount(2);
        await service.Received(1).ListConflictsAsync(
            processingCase.CaseId,
            "tenant-a",
            "company-a",
            CorporateActionConflictStates.Open,
            25,
            Arg.Any<CancellationToken>());
        await service.Received(1).GetConflictAsync(
            processingCase.CaseId,
            conflict.ConflictId,
            "tenant-a",
            "company-a",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorporateActionRoutes_DeclareSeparatedEndpointPermissions()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        await using var app = await CreateAppAsync(service, UserPermission.ViewCorporateActions);

        AssertPermissions(
            app,
            "GetSecurityMasterCorporateActionInbox",
            (true, new[] { UserPermission.ViewCorporateActions }));
        AssertPermissions(
            app,
            "AcceptCorporateActionSourceProposal",
            (true, new[]
            {
                UserPermission.ModifySecurityMaster,
                UserPermission.ResolveCorporateActionTerms,
            }));
        AssertPermissions(
            app,
            "RetiredSecurityMasterCorporateActionInboxApply",
            (true, new[]
            {
                UserPermission.ModifySecurityMaster,
                UserPermission.ResolveCorporateActionTerms,
            }));
        AssertPermissions(
            app,
            "AddCorporateActionCaseEvidence",
            (false, new[]
            {
                UserPermission.ResolveCorporateActionTerms,
                UserPermission.RecordCorporateActionElection,
                UserPermission.PrepareCorporateActionAccounting,
            }));
        AssertPermissions(
            app,
            "UpsertCorporateActionCaseOption",
            (true, new[] { UserPermission.PrepareCorporateActionAccounting }));
        AssertPermissions(
            app,
            "ResolveCorporateActionCaseConflict",
            (true, new[] { UserPermission.ResolveCorporateActionTerms }));
        AssertPermissions(
            app,
            "ListCorporateActionCaseConflicts",
            (true, new[] { UserPermission.ViewCorporateActions }));
        AssertPermissions(
            app,
            "GetCorporateActionCaseConflict",
            (true, new[] { UserPermission.ViewCorporateActions }));
    }

    [Fact]
    public async Task CaseProjection_AdvertisesOnlyTransitionTargetsAuthorizedForCaller()
    {
        var service = Substitute.For<ICorporateActionOperationsService>();
        service.ListCasesAsync(
                "tenant-a", "company-a", null, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { TermsConfirmedCase() });
        await using var app = await CreateAppAsync(
            service,
            UserPermission.ViewCorporateActions | UserPermission.RecordCorporateActionElection);

        var response = await app.GetTestClient().GetAsync("/api/security-master/corporate-actions/cases");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cases = await response.Content.ReadFromJsonAsync<CorporateActionProcessingCaseDto[]>(JsonOptions);
        cases.Should().NotBeNull();
        var availability = cases!.Should().ContainSingle().Subject.ActionAvailability;
        availability.Should().NotBeNull();
        availability!.CanTransition.Should().BeTrue();
        availability.AllowedTransitionTargets.Should().Equal(
            CorporateActionCaseStates.ElectionPending,
            CorporateActionCaseStates.Blocked);
        availability.AllowedTransitionTargets.Should().NotContain(CorporateActionCaseStates.AccountingReview);
        availability.AllowedTransitionTargets.Should().NotContain(CorporateActionCaseStates.Cancelled);
    }

    private static async Task<WebApplication> CreateAppAsync(
        ICorporateActionOperationsService service,
        UserPermission permissions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddMutationRateLimiter();
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton(Substitute.For<ISecurityMasterQueryService>());

        var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operations-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next(context);
        });
        app.MapSecurityMasterEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static AcceptCorporateActionSourceProposalRequestDto AcceptRequest(CorporateActionCaseScopeDto scope) =>
        new(
            ProposalId,
            ExpectedVersion: 7,
            IdempotencyKey: "accept:proposal:v7",
            Scope: scope,
            Actor: "browser");

    private static CorporateActionDurableInboxDto CreateInbox()
    {
        var action = Dividend();
        var scope = new CorporateActionCaseScopeDto("tenant-a", "company-a");
        var entry = new CorporateActionDurableInboxEntryDto(
            SecurityId,
            "ACME",
            action.EventType,
            action.ExDate,
            action.RecordDate,
            action.PayDate,
            action.DividendPerShare,
            action.Currency,
            null,
            null,
            "provider-a",
            ["provider-a", "provider-b"],
            [],
            AutoApplied: false,
            ProposalId,
            Version: 7,
            CorporateActionSourceProposalStates.Observed,
            scope,
            new CorporateActionSourceProposalActionAvailabilityDto(true, true, true, []));
        return new CorporateActionDurableInboxDto(
            new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
            StagedCount: 1,
            AppliedLastRun: 0,
            DuplicatesSkippedLastRun: 0,
            Staged: [entry],
            Errors: [],
            Cases: [TermsConfirmedCase()]);
    }

    private static CorporateActionProcessingCaseDto TermsConfirmedCase()
    {
        var caseId = Guid.Parse("57d852c5-7a92-41c4-8ff9-386cf87cc1c6");
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        return new CorporateActionProcessingCaseDto(
            caseId,
            ProposalId,
            Guid.Parse("d55769d5-454e-4dad-be61-8239ba131c3c"),
            SecurityId,
            new CorporateActionCaseScopeDto("tenant-a", "company-a"),
            CorporateActionCaseStates.TermsConfirmed,
            Version: 3,
            "clearwater-corporate-actions/v1",
            AssignedTo: null,
            BlockedReason: null,
            CreatedBy: "operations-user",
            CreatedAtUtc: now.AddHours(-1),
            UpdatedBy: "operations-user",
            UpdatedAtUtc: now,
            new CorporateActionCaseActionAvailabilityDto(
                CanAddEvidence: true,
                CanRecordConflict: false,
                CanManageOptions: true,
                CanTransition: true,
                CanApproveAccounting: false,
                CorporateActionCaseTransitionPolicy.GetAllowedTargets(CorporateActionCaseStates.TermsConfirmed),
                Blockers: []),
            SourceSnapshot: new CorporateActionCaseSourceSnapshotDto(
                Dividend(),
                new CorporateActionProviderEventIdentityDto(
                    "provider-a",
                    "event-1",
                    "v1",
                    now,
                    EvidenceHash: new string('a', 64),
                    EvidenceReference: "provider-event://provider-a/event-1/v1",
                    ReleaseStatus: CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
                new CorporateActionSourceDisplayMetadataDto(
                    "ACME",
                    "provider-a",
                    ["provider-a", "provider-b"],
                    [])));
    }

    private static CorporateActionConflictDto Conflict(Guid caseId) =>
        new(
            Guid.Parse("de53c04b-041f-461c-8aa0-1710d330e941"),
            caseId,
            CorporateActionPayloads.CashAmount,
            "Provider values differ.",
            [
                new CorporateActionConflictCandidateDto(
                    "provider-a",
                    JsonSerializer.SerializeToElement(0.24m),
                    "provider-event://provider-a/event-100/v1"),
                new CorporateActionConflictCandidateDto(
                    "provider-b",
                    JsonSerializer.SerializeToElement(0.26m),
                    "provider-event://provider-b/event-200/v1"),
            ],
            CorporateActionConflictStates.Open,
            Resolution: null,
            CaseVersion: 3,
            RecordedBy: "ingest",
            RecordedAtUtc: new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    private static CorporateActionSourceProposalAcceptanceResultDto CreateAcceptance(
        AcceptCorporateActionSourceProposalRequestDto request)
    {
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var action = Dividend();
        var proposal = new CorporateActionSourceProposalDto(
            ProposalId,
            SecurityId,
            new CorporateActionProviderEventIdentityDto("provider-a", "event-1", "v1", now),
            action,
            action.PayloadSchemaVersion,
            CorporateActionEconomicFingerprint.Compute(action),
            CorporateActionSourceProposalStates.Accepted,
            8,
            null,
            action.CorpActId,
            request.CaseId ?? Guid.Parse("8d563f6c-dafe-451e-ac05-c724717fcaef"),
            "ingest",
            now.AddMinutes(-10),
            now,
            DecisionBy: request.Actor,
            DecisionAtUtc: now,
            ActionAvailability: new CorporateActionSourceProposalActionAvailabilityDto(false, false, true, []));
        var processingCase = new CorporateActionProcessingCaseDto(
            proposal.InitialCaseId!.Value,
            proposal.ProposalId,
            action.CorpActId,
            SecurityId,
            request.Scope,
            CorporateActionCaseStates.Detected,
            1,
            request.MethodologyProfileId,
            null,
            null,
            request.Actor,
            now,
            request.Actor,
            now);
        var transition = new CorporateActionCaseTransitionDto(
            Guid.NewGuid(), processingCase.CaseId, null, CorporateActionCaseStates.Detected,
            0, 1, request.Actor, "Accepted canonical source fact.", request.IdempotencyKey, now, request.CorrelationId);
        var audit = new SecurityMasterCorporateActionAuditDto(
            $"audit:{action.CorpActId:D}", SecurityId, action.CorpActId, action.EventType,
            "provider-a", request.Actor, now, "event-1:v1", request.Reason, request.CorrelationId);
        return new CorporateActionSourceProposalAcceptanceResultDto(
            proposal, action, processingCase, transition, audit, Restatement: null, Replayed: false);
    }

    private static CorporateActionDto Dividend() =>
        new(
            Guid.Parse("d55769d5-454e-4dad-be61-8239ba131c3c"),
            SecurityId,
            CorporateActionEventTypes.Dividend,
            new DateOnly(2026, 8, 14),
            new DateOnly(2026, 8, 28),
            0.24m,
            "USD",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            RecordDate: new DateOnly(2026, 8, 15));

    private static void AssertPermissions(
        WebApplication app,
        string endpointName,
        params (bool RequireAll, UserPermission[] Permissions)[] expected)
    {
        var endpoint = app.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Single(candidate => string.Equals(
                candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                endpointName,
                StringComparison.Ordinal));
        var declarations = endpoint.Metadata.GetOrderedMetadata<EndpointAuthorizationMetadata>();

        declarations.Should().HaveCount(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            declarations[index].RequireAll.Should().Be(expected[index].RequireAll);
            declarations[index].Permissions.Should().Equal(expected[index].Permissions);
        }
    }
}
