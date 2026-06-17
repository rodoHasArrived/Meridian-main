using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Testing;
using Xunit;

namespace Meridian.Tests.Application.Auth;

/// <summary>
/// Operator scenario: multiple Meridian instances share governed fund data, so scoped access
/// must be consistent, effective-dated, and conflict-aware before operators act on a fund.
/// </summary>
public sealed class ScopedAccessServiceTests
{
    [Fact]
    public async Task AuthorizeAsync_OrganizationGrant_AllowsChildFundButNotUnrelatedFund()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(AuthorizeAsync_OrganizationGrant_AllowsChildFundButNotUnrelatedFund));
        var fundStructure = new InMemoryFundStructureService(new InMemoryFundAccountService());
        var created = await CreateTwoFundStructureAsync(fundStructure);
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store, new FundStructureAccessScopeLineageProvider(fundStructure));

        await service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "fund-controller",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Organization,
                ScopeId: created.OrganizationId,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: "Accounting Reviewer",
                PermissionNames: [nameof(UserPermission.ViewTrades), nameof(UserPermission.ManageDirectLending)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "admin",
                Rationale: "Controller owns this fund complex."),
            actor: "admin");

        var allowed = await service.AuthorizeAsync(
            "fund-controller",
            UserPermission.ManageDirectLending,
            AccessScopeKindDto.Fund,
            created.ChildFundId,
            UserPermission.None);
        var denied = await service.AuthorizeAsync(
            "fund-controller",
            UserPermission.ManageDirectLending,
            AccessScopeKindDto.Fund,
            created.UnrelatedFundId,
            UserPermission.None);

        allowed.IsAllowed.Should().BeTrue();
        allowed.MatchedAssignmentId.Should().NotBeNull();
        denied.IsAllowed.Should().BeFalse();
        denied.Reason.Should().Contain("No effective scoped access assignment");
    }

    [Fact]
    public async Task RevokeAsync_StaleVersion_RejectsSecondWriter()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(RevokeAsync_StaleVersion_RejectsSecondWriter));
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store);
        var result = await service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "ops-reviewer",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ViewTrades)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "admin",
                Rationale: "Temporary review coverage."),
            actor: "admin");

        var revoked = await service.RevokeAsync(
            new UserAccessAssignmentRevokeRequestDto(
                result.Assignment.AssignmentId,
                ExpectedVersion: result.Assignment.Version,
                RequestedBy: "admin",
                Rationale: "Coverage ended."),
            actor: "admin");

        revoked.Assignment.Version.Should().Be(2);
        Func<Task> staleWrite = () => service.RevokeAsync(
            new UserAccessAssignmentRevokeRequestDto(
                result.Assignment.AssignmentId,
                ExpectedVersion: result.Assignment.Version,
                RequestedBy: "admin",
                Rationale: "Duplicate revoke from another instance."),
            actor: "admin");

        await staleWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been revoked*");
    }

    [Fact]
    public async Task CreateAsync_GovernedAuthorityMetadata_RetainsApprovalLimitAndSegregationRule()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CreateAsync_GovernedAuthorityMetadata_RetainsApprovalLimitAndSegregationRule));
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store);

        var result = await service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "payment-approver",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: "Payment Approval",
                PermissionNames: [nameof(UserPermission.ManageDirectLending)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "admin",
                Rationale: "Authorize payment requests below treasury policy threshold.",
                ApprovalLimitAmount: 100_000m,
                ApprovalLimitCurrency: "usd",
                SegregationOfDutiesRule: "Requester cannot approve own payment request."),
            actor: "admin");

        result.Assignment.ApprovalLimitAmount.Should().Be(100_000m);
        result.Assignment.ApprovalLimitCurrency.Should().Be("USD");
        result.Assignment.SegregationOfDutiesRule.Should().Be("Requester cannot approve own payment request.");
        result.AuditEvent.ApprovalLimitAmount.Should().Be(100_000m);
        result.AuditEvent.ApprovalLimitCurrency.Should().Be("USD");
        result.AuditEvent.SegregationOfDutiesRule.Should().Be("Requester cannot approve own payment request.");

        var reloaded = await service.QueryAsync(new UserAccessAssignmentQueryDto(PrincipalId: "payment-approver"));
        reloaded.Should().ContainSingle()
            .Which.SegregationOfDutiesRule.Should().Be("Requester cannot approve own payment request.");
    }

    [Fact]
    public async Task CreateAsync_AssistantOrigin_RejectsBeforePersistingAuthority()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CreateAsync_AssistantOrigin_RejectsBeforePersistingAuthority));
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store);

        var act = () => service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "assistant-created-admin",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Admin),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ManageUsers)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "assistant-agent",
                Rationale: "Assistant drafted an authority grant.",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            actor: "admin");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot grant scoped access assignments*");

        var assignments = await service.QueryAsync(new UserAccessAssignmentQueryDto(
            PrincipalId: "assistant-created-admin",
            IncludeRevoked: true));
        assignments.Should().BeEmpty("assistant-origin authority grants must fail before persistence");
    }

    [Fact]
    public async Task RevokeAsync_AutomationOrigin_RejectsBeforeMutatingAuthority()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(RevokeAsync_AutomationOrigin_RejectsBeforeMutatingAuthority));
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store);
        var result = await service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "close-reviewer",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ViewTrades)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "admin",
                Rationale: "Temporary close review authority."),
            actor: "admin");

        var act = () => service.RevokeAsync(
            new UserAccessAssignmentRevokeRequestDto(
                result.Assignment.AssignmentId,
                ExpectedVersion: result.Assignment.Version,
                RequestedBy: "automation-agent",
                Rationale: "Automation requested authority revocation.",
                ActionOrigin: OperationsActionOriginDto.AutomationAssistant),
            actor: "admin");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot revoke scoped access assignments*");

        var assignments = await service.QueryAsync(new UserAccessAssignmentQueryDto(
            PrincipalId: "close-reviewer",
            IncludeRevoked: true));
        var assignment = assignments.Should().ContainSingle().Subject;
        assignment.Version.Should().Be(result.Assignment.Version);
        assignment.RevokedAtUtc.Should().BeNull();
        assignment.RevokedBy.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ApprovalLimitWithoutCurrency_IsRejected()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CreateAsync_ApprovalLimitWithoutCurrency_IsRejected));
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store);

        var act = () => service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "payment-approver",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Accounting),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ManageDirectLending)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddMinutes(-1),
                EffectiveTo: null,
                RequestedBy: "admin",
                Rationale: "Invalid threshold metadata.",
                ApprovalLimitAmount: 100_000m),
            actor: "admin");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ApprovalLimitCurrency is required*");
    }

    [Fact]
    public async Task QueryAsync_ExpiredAssignment_IsHiddenFromEffectiveLookup()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(QueryAsync_ExpiredAssignment_IsHiddenFromEffectiveLookup));
        var store = new FileScopedAccessAssignmentStore(Path.Combine(artifacts.RootPath, "governance", "user-access-assignments.json"));
        var service = new ScopedAccessService(store);

        await service.CreateAsync(
            new UserAccessAssignmentCreateRequestDto(
                PrincipalId: "former-analyst",
                PrincipalKind: AccessPrincipalKindDto.User,
                ScopeKind: AccessScopeKindDto.Global,
                ScopeId: null,
                Role: nameof(UserRole.Analysis),
                RoleProfileName: null,
                PermissionNames: [nameof(UserPermission.ViewAnalytics)],
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-2),
                EffectiveTo: DateTimeOffset.UtcNow.AddDays(-1),
                RequestedBy: "admin",
                Rationale: "Historical temporary assignment."),
            actor: "admin");

        var effective = await service.QueryAsync(new UserAccessAssignmentQueryDto(PrincipalId: "former-analyst"));
        var historical = await service.QueryAsync(new UserAccessAssignmentQueryDto(PrincipalId: "former-analyst", IncludeRevoked: true));

        effective.Should().BeEmpty();
        historical.Should().ContainSingle();
    }

    private static async Task<FundFixture> CreateTwoFundStructureAsync(InMemoryFundStructureService service)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var orgId = Guid.NewGuid();
        var bizId = Guid.NewGuid();
        var childFundId = Guid.NewGuid();
        var unrelatedOrgId = Guid.NewGuid();
        var unrelatedBizId = Guid.NewGuid();
        var unrelatedFundId = Guid.NewGuid();

        await service.CreateOrganizationAsync(new CreateOrganizationRequest(orgId, "ORG", "Organization", "USD", now, "test"));
        await service.CreateBusinessAsync(new CreateBusinessRequest(bizId, orgId, BusinessKindDto.FundManager, "BUS", "Business", "USD", now, "test"));
        await service.CreateFundAsync(new CreateFundRequest(childFundId, "FUND-A", "Fund A", "USD", now, "test", BusinessId: bizId));

        await service.CreateOrganizationAsync(new CreateOrganizationRequest(unrelatedOrgId, "ALT", "Unrelated Organization", "USD", now, "test"));
        await service.CreateBusinessAsync(new CreateBusinessRequest(unrelatedBizId, unrelatedOrgId, BusinessKindDto.FundManager, "ALT-BUS", "Unrelated Business", "USD", now, "test"));
        await service.CreateFundAsync(new CreateFundRequest(unrelatedFundId, "FUND-B", "Fund B", "USD", now, "test", BusinessId: unrelatedBizId));

        return new FundFixture(orgId, childFundId, unrelatedFundId);
    }

    private sealed record FundFixture(Guid OrganizationId, Guid ChildFundId, Guid UnrelatedFundId);
}
