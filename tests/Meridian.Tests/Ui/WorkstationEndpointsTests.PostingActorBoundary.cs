using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
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
