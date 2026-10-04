using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Application.Auth;

public sealed class CorporateActionRolePermissionsTests
{
    [Fact]
    public void TechnicalAdministrators_CanManageSourceFacts_ButHaveNoImplicitBusinessAuthority()
    {
        foreach (var role in new[] { UserRole.Admin, UserRole.Developer })
        {
            var permissions = RolePermissions.For(role);
            permissions.Should().HaveFlag(UserPermission.ViewCorporateActions);
            permissions.Should().HaveFlag(UserPermission.IngestCorporateActions);
            permissions.Should().HaveFlag(UserPermission.ResolveCorporateActionTerms);
            permissions.Should().NotHaveFlag(UserPermission.ApproveCorporateActionAccounting);
            permissions.Should().NotHaveFlag(UserPermission.PostCorporateActionAccounting);
            permissions.Should().NotHaveFlag(UserPermission.ReviewCorporateActionTax);
            permissions.Should().NotHaveFlag(UserPermission.OverrideCorporateActionPolicy);
            permissions.Should().NotHaveFlag(UserPermission.ReopenCorporateActionCase);
        }
    }

    [Fact]
    public void OperatingRoles_ReceiveOnlyTheirMinimumCorporateActionAuthority()
    {
        AssertRole(
            UserRole.TradeDesk,
            required: UserPermission.ViewCorporateActions | UserPermission.RecordCorporateActionElection,
            forbidden: UserPermission.ResolveCorporateActionTerms |
                       UserPermission.PrepareCorporateActionAccounting |
                       UserPermission.ApproveCorporateActionAccounting |
                       UserPermission.PostCorporateActionAccounting);

        foreach (var role in new[] { UserRole.Accounting, UserRole.FundAccountant })
        {
            AssertRole(
                role,
                required: UserPermission.ViewCorporateActions | UserPermission.PrepareCorporateActionAccounting,
                forbidden: UserPermission.ApproveCorporateActionAccounting |
                           UserPermission.PostCorporateActionAccounting |
                           UserPermission.OverrideCorporateActionPolicy);
        }

        AssertRole(
            UserRole.Controller,
            required: UserPermission.ViewCorporateActions | UserPermission.ApproveCorporateActionAccounting,
            forbidden: UserPermission.PrepareCorporateActionAccounting |
                       UserPermission.PostCorporateActionAccounting |
                       UserPermission.OverrideCorporateActionPolicy);

        AssertRole(
            UserRole.Compliance,
            required: UserPermission.ViewCorporateActions,
            forbidden: UserPermission.ResolveCorporateActionTerms |
                       UserPermission.RecordCorporateActionElection |
                       UserPermission.PrepareCorporateActionAccounting |
                       UserPermission.ApproveCorporateActionAccounting |
                       UserPermission.PostCorporateActionAccounting |
                       UserPermission.ReviewCorporateActionTax |
                       UserPermission.OverrideCorporateActionPolicy |
                       UserPermission.ReopenCorporateActionCase);
    }

    [Fact]
    public void PermissionCatalog_ExposesSeparatedCorporateActionCapabilities()
    {
        var expected = new[]
        {
            nameof(UserPermission.ViewCorporateActions),
            nameof(UserPermission.IngestCorporateActions),
            nameof(UserPermission.ResolveCorporateActionTerms),
            nameof(UserPermission.RecordCorporateActionElection),
            nameof(UserPermission.PrepareCorporateActionAccounting),
            nameof(UserPermission.ApproveCorporateActionAccounting),
            nameof(UserPermission.PostCorporateActionAccounting),
            nameof(UserPermission.ReviewCorporateActionTax),
            nameof(UserPermission.OverrideCorporateActionPolicy),
            nameof(UserPermission.ReopenCorporateActionCase),
        };

        var catalog = RolePermissions.GetCatalog();

        foreach (var permissionName in expected)
        {
            catalog.Permissions.Should().Contain(permission =>
                permission.Name == permissionName && permission.Group == "Corporate actions");
        }
    }

    private static void AssertRole(UserRole role, UserPermission required, UserPermission forbidden)
    {
        var actual = RolePermissions.For(role);
        actual.Should().HaveFlag(required, $"{role} must receive its minimum corporate-action authority");
        (actual & forbidden).Should().Be(
            UserPermission.None,
            $"{role} must not inherit unrelated corporate-action mutation authority");
    }
}
