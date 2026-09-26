using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Accounting;
using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Wpf.Tests.Services.DesktopAuthenticationSessionTests;

namespace Meridian.Wpf.Tests.Services;

[Collection("DesktopAuthenticationEnvironment")]
public sealed class DesktopWorkstationTenantContextAccessorTests
{
    [Fact]
    public async Task RegisteredCloseGuard_RequiresLiveCompanySession_AndRecoversAfterSignIn()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopAdminUsersJson()).Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null).Set("MDC_AUTH_MODE", null);
        var session = CreateSession("Production");
        var scope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), Guid.NewGuid(), "entity-alpha", "2026-07");
        var workflowId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var reviewedRevision = new OperationsEvidenceLinkDto(
            $"accounting-report-package-revision:{new string('a', 64)}",
            "Retained package and journal revision", null, "accounting-report-package-revision", now);
        var reviewedReport = new OperationsReportPackReadinessDto(true, "report-1", null, [reviewedRevision]);
        var workflow = new OperationsContinuityWorkflowDto(workflowId, scope.FundAccountId!.Value, scope.PeriodId!,
            null, "official-close", now, now, 7, default, default, default, default, default, default,
            [], [], [], null, [], reviewedReport, [], [], [], [], LedgerBookId: scope.LedgerBookId);
        var authority = Substitute.For<IFinancialOperationsCommandCenterReadService>();
        authority.GetCommandCenterAsync(scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId, scope.PeriodId,
            scope.EntityId, Arg.Any<CancellationToken>(), "company-alpha", "company-alpha")
            .Returns(new FinancialOperationsCommandCenterDto(now, scope.FundProfileId, scope.LedgerBookId,
                scope.FundAccountId, scope.PeriodId, "Ready", true, "Complete retained evidence", 0, 0, 0, [], [],
                ActiveWorkflow: workflow, CloseReadiness: new(scope, now, "Ready", true, true, [], [])));
        // This session-boundary fixture supplies both evidence ports consumed by the registered
        // guard. Package retention and canonical journal validation are covered by shared tests.
        var reportAuthority = Substitute.For<IOperationsReportPackAuthority>();
        var reportAvailable = true;
        var currentReport = reviewedReport;
        reportAuthority.ResolveAsync(workflow, "report-1", "company-alpha", "company-alpha", Arg.Any<CancellationToken>())
            .Returns(_ => reportAvailable
                ? Task.FromResult(currentReport)
                : Task.FromException<OperationsReportPackReadinessDto>(new InvalidOperationException("Report source unavailable.")));
        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddSingleton(authority);
        services.AddSingleton(reportAuthority);
        new AccountingFeatureModule().Register(services);
        using var provider = services.BuildServiceProvider();
        var guard = provider.GetRequiredService<IClosePublicationReadinessGuard>();
        var accessor = provider.GetRequiredService<IWorkstationTenantContextAccessor>();
        guard.Should().BeOfType<ClosePublicationReadinessGuard>();
        provider.GetRequiredService<IOperationsReportPackAuthority>().Should().BeSameAs(reportAuthority);

        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
        authority.ReceivedCalls().Should().BeEmpty();
        reportAuthority.ReceivedCalls().Should().BeEmpty();

        session.SignIn("desktop-admin", "pw").Succeeded.Should().BeTrue();
        accessor.GetRequired().Should().BeEquivalentTo(new
        {
            TenantId = "company-alpha",
            CompanyId = "company-alpha",
            Actor = "desktop-admin"
        });
        (await guard.ValidateAsync(workflowId, 7, scope)).Should().BeEmpty();
        await authority.Received(1).GetCommandCenterAsync(scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId,
            scope.PeriodId, scope.EntityId, Arg.Any<CancellationToken>(), "company-alpha", "company-alpha");
        await reportAuthority.Received(1).ResolveAsync(workflow, "report-1", "company-alpha", "company-alpha", Arg.Any<CancellationToken>());

        reportAvailable = false;
        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_READINESS_UNAVAILABLE");
        reportAvailable = true;
        currentReport = new(false, null, "Canonical report support requires repair.", []);
        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_REPORT_SUPPORT_NOT_READY");
        currentReport = reviewedReport with
        {
            EvidenceLinks = [reviewedRevision with { EvidenceId = $"accounting-report-package-revision:{new string('b', 64)}" }]
        };
        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_REPORT_SUPPORT_CHANGED");
        currentReport = reviewedReport;
        (await guard.ValidateAsync(workflowId, 7, scope)).Should().BeEmpty();

        (await guard.ValidateAsync(workflowId, 7, scope, "other-company", "other-company")).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_MISMATCH");

        session.SignOut();
        accessor.TryGetCurrent(out var signedOut).Should().BeFalse();
        signedOut.HasTenantScope.Should().BeFalse();
        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
        await authority.Received(5).GetCommandCenterAsync(scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId,
            scope.PeriodId, scope.EntityId, Arg.Any<CancellationToken>(), "company-alpha", "company-alpha");
        await reportAuthority.Received(5).ResolveAsync(workflow, "report-1", "company-alpha", "company-alpha", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AuthenticatedUserWithoutCompany_CannotBorrowCloseSubjectAsTenant()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("MDC_USERS", HashedDesktopReadOnlyUsersJson()).Set("MDC_USERNAME", null)
            .Set("MDC_PASSWORD_HASH", null).Set("MDC_AUTH_MODE", null);
        var session = CreateSession("Production");
        session.SignIn("desktop-viewer", "pw").Succeeded.Should().BeTrue();
        var accessor = new DesktopWorkstationTenantContextAccessor(session);

        accessor.TryGetCurrent(out var context).Should().BeFalse();
        context.HasTenantScope.Should().BeFalse();
        ((Action)(() => accessor.GetRequired())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MissingDesktopSession_FailsClosed()
    {
        var accessor = new DesktopWorkstationTenantContextAccessor();
        accessor.TryGetCurrent(out var context).Should().BeFalse();
        context.HasTenantScope.Should().BeFalse();
    }
}
