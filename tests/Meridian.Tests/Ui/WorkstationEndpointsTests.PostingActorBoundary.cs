using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Validate)]
    [InlineData(JournalEntryLifecycleActionDto.Submit)]
    [InlineData(JournalEntryLifecycleActionDto.Approve)]
    [InlineData(JournalEntryLifecycleActionDto.Reject)]
    [InlineData(JournalEntryLifecycleActionDto.Post)]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    [InlineData(JournalEntryLifecycleActionDto.LockAfterClose)]
    public async Task ManualJournalLifecycle_AuthorizedActionPreservesCompleteCommand(JournalEntryLifecycleActionDto action)
    {
        var service = new LifecycleBoundaryCaptureService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IManualJournalEntryLifecycleService>(service),
            currentUserPermissions: UserPermission.AdminMaintenance, currentUserRole: UserRole.Controller,
            currentUserRoleProfileName: "review-profile", currentUserTenantId: "trusted-tenant",
            currentUserCompanyId: "trusted-company", mapLedgerApi: true);
        var request = new JournalEntryLifecycleActionRequestDto(
            Guid.NewGuid(), "fund-alpha", action, "body-actor", 17,
            Notes: "Retained independent review", CorrelationId: "synthetic-lifecycle-command",
            EvidenceLinks: ["evidence://synthetic-review"], PeriodIsLocked: true,
            RebookLines: [new("reviewed-line", AccountingTemplateLineSideDto.Debit, 125.44m, "USD", "Assets:Cash",
                EvidenceLink: "evidence://synthetic-line", Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")
                { PositionId = Guid.NewGuid(), IntercompanyAgreementId = Guid.NewGuid(), IntercompanyAgreementVersion = 7 })],
            LedgerBookId: Guid.NewGuid(), TenantId: "forged-tenant", CompanyId: "forged-company",
            ReportGroupPrincipalIds: ["forged-group"])
        { AuthenticatedActorAssertion = "forged-assertion" };

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction, request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var forwarded = service.Requests.Should().ContainSingle().Which;
        forwarded.Should().BeEquivalentTo(request, options => options.Excluding(value => value.Actor)
            .Excluding(value => value.TenantId).Excluding(value => value.CompanyId)
            .Excluding(value => value.ReportGroupPrincipalIds).Excluding(value => value.AuthenticatedActorAssertion));
        forwarded.Actor.Should().Be("ops-user");
        forwarded.TenantId.Should().Be("trusted-tenant");
        forwarded.CompanyId.Should().Be("trusted-company");
        forwarded.ReportGroupPrincipalIds.Should().BeEquivalentTo(new[] { "Controller", "review-profile" });
        forwarded.AuthenticatedActorAssertion.Should().Be("body-actor");
    }

    [Theory]
    [InlineData(false, OperationsActionOriginDto.AutomationSuggestion, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(false, OperationsActionOriginDto.AssistantDraft, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(false, OperationsActionOriginDto.AutomationAssistant, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(false, (OperationsActionOriginDto)254, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(false, (OperationsActionOriginDto)255, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(true, OperationsActionOriginDto.HumanOperator, JournalEntryLifecycleActionDto.Approve)]
    [InlineData(true, OperationsActionOriginDto.HumanOperator, JournalEntryLifecycleActionDto.Post)]
    public async Task ManualJournalLifecycle_NonHumanRequestCannotApproveOrPostRetainedAsset(
        bool apiKey, OperationsActionOriginDto origin, JournalEntryLifecycleActionDto action)
    {
        using var fixture = await AssetApprovalFixture.CreateAsync();
        var before = fixture.Submitted;
        if (action == JournalEntryLifecycleActionDto.Post)
            before = (await fixture.Workbench.ApplyLifecycleActionAsync(fixture.Action(JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IManualJournalEntryWorkbenchService>(fixture.Workbench);
            services.AddSingleton<IManualJournalEntryLifecycleService>(fixture.Workbench);
            if (apiKey)
                services.AddSingleton<IStartupFilter>(new LifecycleApiKeyPrincipalFilter());
        }, currentUserPermissions: UserPermission.AdminMaintenance, currentUserCompanyId: AssetApprovalFixture.Company,
            currentUserName: AssetApprovalFixture.Reviewer, mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            fixture.Action(action, before) with { ActionOrigin = origin }, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("human operator");
        var after = (await fixture.ReadAsync())!;
        after.Status.Should().Be(before.Status);
        after.Version.Should().Be(before.Version);
        after.AssetAccountingApproval.Should().BeEquivalentTo(before.AssetAccountingApproval);
        fixture.PostRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Validate)]
    [InlineData(JournalEntryLifecycleActionDto.Submit)]
    [InlineData(JournalEntryLifecycleActionDto.Approve)]
    [InlineData(JournalEntryLifecycleActionDto.Reject)]
    [InlineData(JournalEntryLifecycleActionDto.Post)]
    [InlineData(JournalEntryLifecycleActionDto.Reverse)]
    [InlineData(JournalEntryLifecycleActionDto.Rebook)]
    [InlineData(JournalEntryLifecycleActionDto.LockAfterClose)]
    public async Task ManualJournalLifecycle_ReadAuthorityOnlyAdmitsValidation(JournalEntryLifecycleActionDto action)
    {
        var service = new LifecycleBoundaryCaptureService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IManualJournalEntryLifecycleService>(service),
            currentUserPermissions: UserPermission.ManageLedgerReports, mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(Guid.NewGuid(), "fund-alpha", action, "body-actor", 1), ServerJsonOptions);

        if (action == JournalEntryLifecycleActionDto.Validate)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            service.Requests.Should().ContainSingle().Which.Action.Should().Be(JournalEntryLifecycleActionDto.Validate);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            service.Requests.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task ManualJournalLifecycle_UnknownActionFailsClosedBeforeDispatch(int action)
    {
        var service = new LifecycleBoundaryCaptureService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IManualJournalEntryLifecycleService>(service),
            currentUserPermissions: UserPermission.AdminMaintenance, mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.LedgerManualJournalEntryLifecycleAction,
            new JournalEntryLifecycleActionRequestDto(Guid.NewGuid(), "fund-alpha", (JournalEntryLifecycleActionDto)action,
                "body-actor", 1), ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.HumanOperator)]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public async Task PostingCandidate_DerivesOriginAndPreservesReviewedEvidence(OperationsActionOriginDto declaredOrigin)
    {
        var service = new CapturingAccountingPostingCandidatePostService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IAccountingPostingCandidatePostService>(service),
            currentUserPermissions: UserPermission.AdminMaintenance, mapLedgerApi: true);
        var at = new DateTimeOffset(2026, 5, 31, 21, 0, 0, TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("synthetic-evidence", "evidence://synthetic-review", new string('a', 64),
            "synthetic-source", "synthetic-reference", "Approved", "reviewer", at, new DateOnly(2026, 5, 31),
            1, at, "retainer", "AssetAcquisitionTermsCompletion", "synthetic-subject");
        var completion = new AssetAcquisitionTermsCompletionDto(Guid.NewGuid(), new string('b', 64),
            BondAmortizationMethod.ConstantYield, 0.05m, evidence);
        var agreementId = Guid.NewGuid();
        var request = new PostPostingRuleJournalCandidateRequestDto(
            new PostingRuleJournalCandidateRequestDto(
                "fund-alpha", "CustodianInterestAccrual", 125.44m, "USD", new DateOnly(2026, 5, 31), "client-preparer",
                Guid.NewGuid(), Guid.NewGuid(), at, "Reviewed interest posting",
                Dimensions: new LedgerDimensionSetDto(FundId: "fund-alpha")
                {
                    IntercompanyAgreementId = agreementId,
                    IntercompanyAgreementVersion = 7
                }),
            "forged-client-poster", "retained-approval", "independent review", ["evidence://synthetic-review"],
            "synthetic-correlation", declaredOrigin, "forged-tenant", "forged-company")
        { ApprovalEvidence = [evidence], AcquisitionTermsCompletion = completion };

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts, request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var forwarded = service.Requests.Should().ContainSingle().Which;
        forwarded.Should().BeEquivalentTo(request, options => options
            .Excluding(value => value.Actor).Excluding(value => value.TenantId).Excluding(value => value.CompanyId));
        forwarded.Actor.Should().Be("ops-user");
        forwarded.TenantId.Should().Be("tenant-test");
        forwarded.CompanyId.Should().Be("tenant-test");
        forwarded.ActionOrigin.Should().Be(declaredOrigin);
        forwarded.Candidate.Dimensions!.IntercompanyAgreementId.Should().Be(agreementId);
        forwarded.Candidate.Dimensions.IntercompanyAgreementVersion.Should().Be(7);
    }

    [Fact]
    public async Task PostingCandidate_PermissionWithoutResolvedActorCannotUseClientActor()
    {
        var service = new CapturingAccountingPostingCandidatePostService();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IAccountingPostingCandidatePostService>(service),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserName: "", mapLedgerApi: true);
        var request = new PostPostingRuleJournalCandidateRequestDto(
            new PostingRuleJournalCandidateRequestDto(
                "fund-alpha", "CustodianInterestAccrual", 125.44m, "USD", new DateOnly(2026, 5, 31), "client-preparer",
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-05-31T21:00:00Z"), "Reviewed interest posting"),
            "forged-client-poster", "retained-approval");

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts, request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Approve)]
    [InlineData(JournalEntryLifecycleActionDto.Post)]
    public async Task ManualJournalLifecycle_PermissionWithoutResolvedActorCannotUseClientActor(JournalEntryLifecycleActionDto action)
    {
        var service = new Mock<IManualJournalEntryLifecycleService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(
            services => services.AddSingleton(service.Object),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserName: "", mapLedgerApi: true);
        var request = new JournalEntryLifecycleActionRequestDto(Guid.NewGuid(), "fund-alpha", action,
            "forged-client-poster", 1, LedgerBookId: Guid.NewGuid());

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerManualJournalEntryLifecycleAction, request, ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.VerifyNoOtherCalls();
    }

    private sealed class LifecycleBoundaryCaptureService : IManualJournalEntryLifecycleService
    {
        public List<JournalEntryLifecycleActionRequestDto> Requests { get; } = [];

        public Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(
            JournalEntryLifecycleActionRequestDto request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            throw new InvalidOperationException("Synthetic lifecycle boundary capture; no financial mutation was performed.");
        }
    }

    private sealed class LifecycleApiKeyPrincipalFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continueRequest) =>
            {
                context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = true;
                await continueRequest();
            });
            next(app);
        };
    }
}
