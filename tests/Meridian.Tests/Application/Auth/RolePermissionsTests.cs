using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Application.Auth;

public sealed class RolePermissionsTests
{
    [Fact]
    public void ReportingRoles_GrantExplicitReportingPermissions()
    {
        RolePermissions.For(UserRole.FundAccountant).Should().HaveFlag(UserPermission.ViewReporting);
        RolePermissions.For(UserRole.FundAccountant).Should().HaveFlag(UserPermission.ManageReporting);
        RolePermissions.For(UserRole.FundAccountant).Should().HaveFlag(UserPermission.DeliverReporting);
        RolePermissions.For(UserRole.FundAccountant).Should().NotHaveFlag(UserPermission.ApproveReporting);

        RolePermissions.For(UserRole.ReportingAnalyst).Should().HaveFlag(UserPermission.ViewReporting);
        RolePermissions.For(UserRole.ReportingAnalyst).Should().HaveFlag(UserPermission.ManageReporting);
        RolePermissions.For(UserRole.ReportingAnalyst).Should().NotHaveFlag(UserPermission.ApproveReporting);

        RolePermissions.For(UserRole.Controller).Should().HaveFlag(UserPermission.ViewReporting);
        RolePermissions.For(UserRole.Controller).Should().HaveFlag(UserPermission.ManageReporting);
        RolePermissions.For(UserRole.Controller).Should().HaveFlag(UserPermission.ApproveReporting);
        RolePermissions.For(UserRole.Controller).Should().HaveFlag(UserPermission.DeliverReporting);

        RolePermissions.For(UserRole.Compliance).Should().HaveFlag(UserPermission.ViewReporting);
        RolePermissions.For(UserRole.Compliance).Should().HaveFlag(UserPermission.ApproveReporting);
        RolePermissions.For(UserRole.Compliance).Should().HaveFlag(UserPermission.DeliverReporting);
        RolePermissions.For(UserRole.Compliance).Should().NotHaveFlag(UserPermission.ManageReporting);
    }

    [Fact]
    public void Catalog_IncludesReportingPermissionsAndRoles()
    {
        var catalog = RolePermissions.GetCatalog();

        catalog.Permissions.Should().Contain(permission =>
            permission.Name == nameof(UserPermission.ViewReporting) &&
            permission.Group == "Reporting");
        catalog.Permissions.Should().Contain(permission =>
            permission.Name == nameof(UserPermission.ManageReporting) &&
            permission.Group == "Reporting");
        catalog.Permissions.Should().Contain(permission =>
            permission.Name == nameof(UserPermission.ApproveReporting) &&
            permission.Group == "Reporting");
        catalog.Permissions.Should().Contain(permission =>
            permission.Name == nameof(UserPermission.DeliverReporting) &&
            permission.Group == "Reporting");
        catalog.Roles.Should().Contain(role =>
            role.Role == nameof(UserRole.ReportingAnalyst) &&
            role.Permissions.Contains(nameof(UserPermission.ManageReporting)));
        catalog.Roles.Should().Contain(role =>
            role.Role == nameof(UserRole.Controller) &&
            role.Permissions.Contains(nameof(UserPermission.ApproveReporting)));
    }
}
