using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Meridian.Tests.Ui;

public sealed class WorkstationTenantContextTests
{
    [Fact]
    public void TryGetCurrent_WhenSessionHasCompany_UsesCompanyAsTenantScope()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[LoginSessionMiddleware.CurrentUserKey] = "ops.user";
        httpContext.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = " company-alpha ";
        httpContext.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewAnalytics;
        var accessor = new HttpContextWorkstationTenantContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var found = accessor.TryGetCurrent(out var context);

        found.Should().BeTrue();
        context.Actor.Should().Be("ops.user");
        context.CompanyId.Should().Be("company-alpha");
        context.TenantId.Should().Be("company-alpha");
        context.Permissions.Should().Be(UserPermission.ViewAnalytics);
    }

    [Fact]
    public void GetRequired_WhenTenantScopeIsMissing_Throws()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[LoginSessionMiddleware.CurrentUserKey] = "ops.user";
        var accessor = new HttpContextWorkstationTenantContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        Action act = () => accessor.GetRequired();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*tenant-scoped workstation request context is required*");
    }
}
