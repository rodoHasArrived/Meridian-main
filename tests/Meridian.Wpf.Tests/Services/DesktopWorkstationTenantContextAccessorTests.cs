using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
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
        var workflow = new OperationsContinuityWorkflowDto(workflowId, scope.FundAccountId!.Value, scope.PeriodId!,
            null, "official-close", now, now, 7, default, default, default, default, default, default,
            [], [], [], null, [], new(true, "report-1", null, []), [], [], [], [], LedgerBookId: scope.LedgerBookId);
        var authority = Substitute.For<IFinancialOperationsCommandCenterReadService>();
        authority.GetCommandCenterAsync(scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId, scope.PeriodId,
            scope.EntityId, Arg.Any<CancellationToken>(), "company-alpha", "company-alpha")
            .Returns(new FinancialOperationsCommandCenterDto(now, scope.FundProfileId, scope.LedgerBookId,
                scope.FundAccountId, scope.PeriodId, "Ready", true, "Complete retained evidence", 0, 0, 0, [], [],
                ActiveWorkflow: workflow, CloseReadiness: new(scope, now, "Ready", true, true, [], [])));
        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddSingleton(authority);
        new AccountingFeatureModule().Register(services);
        using var provider = services.BuildServiceProvider();
        var guard = provider.GetRequiredService<IClosePublicationReadinessGuard>();
        var accessor = provider.GetRequiredService<IWorkstationTenantContextAccessor>();

        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
        authority.ReceivedCalls().Should().BeEmpty();

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
        (await guard.ValidateAsync(workflowId, 7, scope, "other-company", "other-company")).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_MISMATCH");

        session.SignOut();
        accessor.TryGetCurrent(out var signedOut).Should().BeFalse();
        signedOut.HasTenantScope.Should().BeFalse();
        (await guard.ValidateAsync(workflowId, 7, scope)).Should()
            .ContainSingle(blocker => blocker.Code == "CLOSE_TENANT_SCOPE_REQUIRED");
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
