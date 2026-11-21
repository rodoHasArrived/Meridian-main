namespace Meridian.Contracts.Workstation;

/// <summary>
/// Version 1 reference to an existing subject-addressed evidence packet. Context alone is not
/// evidence, and a reference never asserts readiness. Generic metrics need no fund-specific scope.
/// </summary>
public sealed record NumberPassportReferenceDto(
    int SchemaVersion,
    string? SubjectType,
    string? SubjectId,
    NumberPassportDeclaredScopeDto? Scope,
    string? UnavailableReason = null,
    string? Amount = null,
    string? Currency = null,
    string? Basis = null,
    DateTimeOffset? AsOf = null);

/// <summary>Only dimensions declared by the retained subject are supplied; no scope is inferred.</summary>
public sealed record NumberPassportDeclaredScopeDto(
    string TenantId,
    string CompanyId,
    string? OrganizationId = null,
    string? EntityId = null,
    string? FundProfileId = null,
    string? LedgerBookId = null,
    string? PeriodId = null,
    string? AccountId = null,
    string? RunId = null);
