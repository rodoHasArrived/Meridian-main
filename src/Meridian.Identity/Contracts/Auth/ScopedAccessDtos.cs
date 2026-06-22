using Meridian.Contracts.Workstation;

namespace Meridian.Identity.Auth;

public enum AccessPrincipalKindDto
{
    User,
    Group
}

public enum AccessScopeKindDto
{
    Global,
    Organization,
    Business,
    Client,
    Fund,
    Sleeve,
    Vehicle,
    InvestmentPortfolio,
    LegalEntity,
    Account
}

public sealed record UserAccessAssignmentDto(
    Guid AssignmentId,
    string PrincipalId,
    AccessPrincipalKindDto PrincipalKind,
    AccessScopeKindDto ScopeKind,
    Guid? ScopeId,
    string Role,
    string? RoleProfileName,
    IReadOnlyList<string> PermissionNames,
    long PermissionMask,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string GrantedBy,
    string Rationale,
    string CorrelationId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? RevokedBy = null,
    DateTimeOffset? RevokedAtUtc = null,
    string? RevocationReason = null,
    string? LastAuditId = null,
    decimal? ApprovalLimitAmount = null,
    string? ApprovalLimitCurrency = null,
    string? SegregationOfDutiesRule = null);

public sealed record UserAccessAssignmentCreateRequestDto(
    string PrincipalId,
    AccessPrincipalKindDto PrincipalKind,
    AccessScopeKindDto ScopeKind,
    Guid? ScopeId,
    string Role,
    string? RoleProfileName,
    IReadOnlyList<string> PermissionNames,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string RequestedBy,
    string Rationale,
    decimal? ApprovalLimitAmount = null,
    string? ApprovalLimitCurrency = null,
    string? SegregationOfDutiesRule = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record UserAccessAssignmentRevokeRequestDto(
    Guid AssignmentId,
    long ExpectedVersion,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record UserAccessAssignmentQueryDto(
    string? PrincipalId = null,
    AccessScopeKindDto? ScopeKind = null,
    Guid? ScopeId = null,
    bool IncludeRevoked = false,
    DateTimeOffset? AsOf = null);

public sealed record UserAccessAssignmentMutationResultDto(
    UserAccessAssignmentDto Assignment,
    UserAccessAssignmentAuditEventDto AuditEvent);

public sealed record UserAccessAssignmentAuditEventDto(
    string AuditId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Rationale,
    string CorrelationId,
    Guid AssignmentId,
    string PrincipalId,
    AccessScopeKindDto ScopeKind,
    Guid? ScopeId,
    IReadOnlyList<string> PermissionNames,
    long PermissionMask,
    long Version,
    decimal? ApprovalLimitAmount = null,
    string? ApprovalLimitCurrency = null,
    string? SegregationOfDutiesRule = null);

public sealed record ScopedAuthorizationDecisionDto(
    bool IsAllowed,
    string Actor,
    UserPermission RequiredPermission,
    AccessScopeKindDto ScopeKind,
    Guid? ScopeId,
    string Reason,
    Guid? MatchedAssignmentId = null,
    long? MatchedAssignmentVersion = null);
