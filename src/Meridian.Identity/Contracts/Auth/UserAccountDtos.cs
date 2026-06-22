namespace Meridian.Identity.Auth;

public sealed record UserAccountDto(
    string Username,
    string Role,
    string? RoleProfileName,
    IReadOnlyList<string> PermissionNames,
    long PermissionMask,
    bool IsDisabled,
    bool PasswordResetRequired,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset? LastPasswordResetAtUtc = null,
    DateTimeOffset? DisabledAtUtc = null,
    string? DisabledBy = null,
    string? LastAuditId = null,
    string? CompanyId = null);

public sealed record UserAccountUpsertRequestDto(
    string Username,
    string Role,
    string? RoleProfileName,
    IReadOnlyList<string>? PermissionNames,
    string? NewPassword,
    string? PasswordHash,
    bool? IsDisabled,
    bool PasswordResetRequired,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null,
    string? CompanyId = null);

public sealed record UserPasswordResetRequestDto(
    string Username,
    string? NewPassword,
    string? PasswordHash,
    bool PasswordResetRequired,
    bool RevokeSessions,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record UserAccountDisableRequestDto(
    string Username,
    bool IsDisabled,
    bool RevokeSessions,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record UserAccountMutationResultDto(
    UserAccountDto Account,
    UserAccountAuditEventDto AuditEvent,
    int RevokedSessionCount = 0);

public sealed record UserSessionRevokeRequestDto(
    string? Username,
    bool RevokeAll,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record UserSessionRevokeResultDto(
    string AuditId,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string? Username,
    bool RevokedAll,
    int RevokedSessionCount,
    string Rationale,
    string CorrelationId);

public sealed record UserAccountAuditEventDto(
    string AuditId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Username,
    string Rationale,
    string CorrelationId,
    string Role,
    IReadOnlyList<string> PermissionNames,
    long PermissionMask,
    bool IsDisabled,
    bool PasswordResetRequired,
    int RevokedSessionCount = 0,
    string? CompanyId = null);
