using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UserRole.Accounting, null, "Controller", UserPermission.ManageDirectLending, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Admin, null, "Controller", UserPermission.AdminMaintenance, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Controller, null, "Controller", UserPermission.ManageLedgerReports, HttpStatusCode.OK)]
    [InlineData(UserRole.Controller, null, " controller ", UserPermission.ManageLedgerReports, HttpStatusCode.OK)]
    [InlineData(UserRole.Accounting, "Assistant Controller", "Assistant Controller", UserPermission.ManageLedgerReports, HttpStatusCode.OK)]
    [InlineData(UserRole.Controller, null, "Assistant Controller", UserPermission.ManageLedgerReports, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Controller, null, "Controller", UserPermission.ViewLedgerReports, HttpStatusCode.Forbidden)]
    public async Task LedgerLateAdjustmentReview_RequiresRetainedReviewRoleAndMutationPermission(
        UserRole role, string? profile, string reviewRole, UserPermission permissions, HttpStatusCode expectedStatus)
    {
        var workflowId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();
        var plan = BuildScopedClosePlan(workflowId, ledgerBookId, fundAccountId);
        var adjustment = new LateAdjustmentRequestDto("adjustment-a", Guid.NewGuid(), "independent-requester",
            DateTimeOffset.UtcNow, 15_000m, "USD", "Close adjustment", ManualJournalEntryStatusDto.Submitted,
            plan.MaterialityPolicy with { ReviewRole = reviewRole });
        plan = plan with { LateAdjustments = [adjustment] };
        var service = Substitute.For<IAccountingCloseManagementService>();
        service.GetPeriodPlanScopedAsync(workflowId, "tenant-test", "tenant-test", Arg.Any<CancellationToken>())
            .Returns(plan);
        service.ReviewLateAdjustmentScopedAsync(Arg.Any<ReviewLateAdjustmentRequestDto>(), "ops-user",
            "tenant-test", "tenant-test", Arg.Any<CancellationToken>()).Returns(plan);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(service);
            RegisterScopedCloseAccessServices(services, ledgerBookId, fundAccountId, plan.PeriodId);
        }, mapLedgerApi: true, currentUserPermissions: permissions, currentUserRole: role,
            currentUserRoleProfileName: profile);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            UiApiRoutes.LedgerCloseManagementLateAdjustmentReview,
            new ReviewLateAdjustmentRequestDto(workflowId, $" {adjustment.RequestId} ",
                ManualJournalEntryStatusDto.Approved, "Controller", "Independent review",
                EvidenceLinks: [$"evidence:late-adjustment:{adjustment.RequestId}:book:{ledgerBookId:D}:approval"]));

        response.StatusCode.Should().Be(expectedStatus);
        if (expectedStatus == HttpStatusCode.OK)
        {
            await service.Received(1).ReviewLateAdjustmentScopedAsync(
                Arg.Is<ReviewLateAdjustmentRequestDto>(request => request.Actor == "ops-user"), "ops-user",
                "tenant-test", "tenant-test", Arg.Any<CancellationToken>());
        }
        else
        {
            await service.DidNotReceiveWithAnyArgs().ReviewLateAdjustmentScopedAsync(default!, default!, default, default, default);
        }
    }
}
