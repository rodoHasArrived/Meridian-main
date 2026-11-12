using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

// A new amortization batch refused by the Security Master terms capability floor reaches the shared
// handler's 503 through the ledger posting route, whose own InvalidOperationException catch (409) no
// longer swallows it.
public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task PostingCandidate_RefusedByTheTermsCapabilityFloor_Returns503NotTheRoute409()
    {
        var postService = Substitute.For<IAccountingPostingCandidatePostService>();
        postService.PostCandidateAsync(Arg.Any<PostPostingRuleJournalCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<PostedPostingRuleJournalCandidateResultDto>>(_ =>
                throw SecurityMasterTermsCapabilityRefusalEndpointsTests.MissingFloorRefusal());
        await using var app = await CreateAppAsync(
            services => services.AddSingleton(postService),
            currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserCompanyId: "company-alpha",
            mapLedgerApi: true,
            useApiExceptionHandler: true);
        var ledgerBookId = Guid.NewGuid();

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerAccountingConfigurationPostingRuleCandidatePosts,
            new PostPostingRuleJournalCandidateRequestDto(
                new PostingRuleJournalCandidateRequestDto(
                    "fund-alpha", "BondPremiumAmortization", 12.5m, "USD", new DateOnly(2026, 5, 31), "browser-user",
                    ledgerBookId, Guid.NewGuid(), DateTimeOffset.Parse("2026-05-31T21:00:00Z"), "Amortize the bond premium",
                    LedgerBookId: ledgerBookId),
                Actor: "browser-user",
                ApprovalId: "approval-amortization-202605"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await SecurityMasterTermsCapabilityRefusalEndpointsTests.AssertServiceUnavailableAsync(response);
    }
}
