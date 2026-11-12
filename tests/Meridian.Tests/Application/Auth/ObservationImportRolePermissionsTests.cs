using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Application.Auth;

public sealed class ObservationImportRolePermissionsTests
{
    [Theory]
    [InlineData(UserRole.Developer)]
    [InlineData(UserRole.Analysis)]
    [InlineData(UserRole.ReportingAnalyst)]
    [InlineData(UserRole.Compliance)]
    [InlineData(UserRole.Executive)]
    [InlineData(UserRole.ReadOnly)]
    public void ViewingAndTechnicalRoles_DoNotAcquireObservationWriteAuthority(UserRole role)
    {
        var permissions = RolePermissions.For(role);
        permissions.Should().NotHaveFlag(UserPermission.ManagePrivateCapitalRecords);
        permissions.Should().NotHaveFlag(UserPermission.ManageOptionAssessments);
    }

    [Fact]
    public void BusinessImportGrants_AreSeparatedByDomain()
    {
        RolePermissions.For(UserRole.FundAccountant).Should().HaveFlag(UserPermission.ManagePrivateCapitalRecords);
        RolePermissions.For(UserRole.FundAccountant).Should().NotHaveFlag(UserPermission.ManageOptionAssessments);
        RolePermissions.For(UserRole.TradeDesk).Should().HaveFlag(UserPermission.ManageOptionAssessments);
        RolePermissions.For(UserRole.TradeDesk).Should().NotHaveFlag(UserPermission.ManagePrivateCapitalRecords);
    }

    [Fact]
    public void Catalog_DescribesBothObservationAuthorities()
    {
        var catalog = RolePermissions.GetCatalog();
        catalog.Permissions.Should().Contain(p => p.Name == nameof(UserPermission.ManagePrivateCapitalRecords) && p.Group == "Private capital");
        catalog.Permissions.Should().Contain(p => p.Name == nameof(UserPermission.ManageOptionAssessments) && p.Group == "Option assessments");
    }
}
