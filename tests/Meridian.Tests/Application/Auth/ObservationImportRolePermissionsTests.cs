using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Application.Auth;

public sealed class ObservationImportRolePermissionsTests
{
    [Fact]
    public void PermissionFlagsRemainDistinctPositiveSingleBits()
    {
        var flags = Enum.GetValues<UserPermission>();
        flags.Should().OnlyHaveUniqueItems("persisted permission masks must not alias unrelated financial authorities");
        flags.Where(flag => flag != UserPermission.None).Should().OnlyContain(flag =>
            (long)flag > 0 && ((long)flag & ((long)flag - 1)) == 0,
            "each named permission must own one bit in the retained mask");
    }

    [Theory]
    [InlineData(UserPermission.ManageSinglePrincipalDeclarations, 1L << 40)]
    [InlineData(UserPermission.RecordPreparerAttestations, 1L << 41)]
    [InlineData(UserPermission.ViewPreparerAttestations, 1L << 42)]
    [InlineData(UserPermission.ViewPrivateCapitalRecords, 1L << 43)]
    [InlineData(UserPermission.ManagePrivateCapitalRecords, 1L << 44)]
    [InlineData(UserPermission.ViewOptionAssessments, 1L << 45)]
    [InlineData(UserPermission.ManageOptionAssessments, 1L << 46)]
    public void ShippedAttestationAndObservationPermissionBitsStaySeparate(UserPermission permission, long expected)
    {
        ((long)permission).Should().Be(expected);
    }

    [Fact]
    public void ObservationRoleGrantsDoNotConferNativeAttestationAuthority()
    {
        var attestationPermissions = UserPermission.ManageSinglePrincipalDeclarations |
            UserPermission.RecordPreparerAttestations | UserPermission.ViewPreparerAttestations;
        foreach (var role in Enum.GetValues<UserRole>())
            (RolePermissions.For(role) & attestationPermissions).Should().Be(UserPermission.None,
                $"the {role} observation grants must not activate separately assigned preparer-attestation authority");
    }

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
