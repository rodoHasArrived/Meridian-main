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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
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
}
