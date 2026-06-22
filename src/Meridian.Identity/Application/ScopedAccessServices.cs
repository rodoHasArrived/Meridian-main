using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;

namespace Meridian.Identity;

public interface IScopedAccessAssignmentService
{
    Task<IReadOnlyList<UserAccessAssignmentDto>> QueryAsync(
        UserAccessAssignmentQueryDto query,
        CancellationToken ct = default);

    Task<UserAccessAssignmentMutationResultDto> CreateAsync(
        UserAccessAssignmentCreateRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<UserAccessAssignmentMutationResultDto> RevokeAsync(
        UserAccessAssignmentRevokeRequestDto request,
        string actor,
        CancellationToken ct = default);
}

public interface IScopedAuthorizationService
{
    Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
        string actor,
        UserPermission requiredPermission,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        UserPermission globalPermissions,
        CancellationToken ct = default);
}

public interface IAccessScopeLineageProvider
{
    Task<IReadOnlyList<AccessScopeRef>> ResolveLineageAsync(
        AccessScopeKindDto scopeKind,
        Guid scopeId,
        CancellationToken ct = default);
}

public sealed record AccessScopeRef(AccessScopeKindDto ScopeKind, Guid? ScopeId);

public sealed class ScopedAccessService : IScopedAccessAssignmentService, IScopedAuthorizationService
{
    private readonly IScopedAccessAssignmentStore _store;
    private readonly IAccessScopeLineageProvider? _scopeLineageProvider;

    public ScopedAccessService(
        IScopedAccessAssignmentStore store,
        IAccessScopeLineageProvider? scopeLineageProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scopeLineageProvider = scopeLineageProvider;
    }

    public async Task<IReadOnlyList<UserAccessAssignmentDto>> QueryAsync(
        UserAccessAssignmentQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var assignments = await _store.ListAsync(ct).ConfigureAwait(false);
        return assignments
            .Where(assignment => query.IncludeRevoked || assignment.RevokedAtUtc is null)
            .Where(assignment => string.IsNullOrWhiteSpace(query.PrincipalId) ||
                string.Equals(assignment.PrincipalId, query.PrincipalId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(assignment => query.ScopeKind is null || assignment.ScopeKind == query.ScopeKind)
            .Where(assignment => query.ScopeId is null || assignment.ScopeId == query.ScopeId)
            .Where(assignment => query.IncludeRevoked || IsEffective(assignment, asOf))
            .OrderBy(assignment => assignment.PrincipalId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(assignment => assignment.ScopeKind)
            .ThenBy(assignment => assignment.ScopeId)
            .ThenBy(assignment => assignment.EffectiveFrom)
            .ToArray();
    }

    public async Task<UserAccessAssignmentMutationResultDto> CreateAsync(
        UserAccessAssignmentCreateRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "grant scoped access assignments");
        var normalized = ValidateCreate(request, actor);
        var now = DateTimeOffset.UtcNow;
        var auditId = BuildAuditId("access-grant", now);
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"access-grant-{Guid.NewGuid():N}"
            : request.CorrelationId.Trim();

        var assignment = new UserAccessAssignmentDto(
            AssignmentId: Guid.NewGuid(),
            PrincipalId: normalized.PrincipalId,
            PrincipalKind: request.PrincipalKind,
            ScopeKind: request.ScopeKind,
            ScopeId: NormalizeScopeId(request.ScopeKind, request.ScopeId),
            Role: normalized.Role,
            RoleProfileName: normalized.RoleProfileName,
            PermissionNames: RolePermissions.GetPermissionNames(normalized.Permissions),
            PermissionMask: (long)normalized.Permissions,
            EffectiveFrom: request.EffectiveFrom ?? now,
            EffectiveTo: request.EffectiveTo,
            GrantedBy: normalized.Actor,
            Rationale: normalized.Rationale,
            CorrelationId: correlationId,
            Version: 1,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            LastAuditId: auditId,
            ApprovalLimitAmount: normalized.ApprovalLimitAmount,
            ApprovalLimitCurrency: normalized.ApprovalLimitCurrency,
            SegregationOfDutiesRule: normalized.SegregationOfDutiesRule);

        await _store.CreateAsync(assignment, ct).ConfigureAwait(false);
        return new UserAccessAssignmentMutationResultDto(
            assignment,
            BuildAuditEvent("user-access-assignment-granted", auditId, now, normalized.Actor, normalized.Rationale, correlationId, assignment));
    }

    public async Task<UserAccessAssignmentMutationResultDto> RevokeAsync(
        UserAccessAssignmentRevokeRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "revoke scoped access assignments");
        var resolvedActor = NormalizeRequired(actor, nameof(actor));
        var rationale = NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var existing = await _store.GetAsync(request.AssignmentId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Access assignment '{request.AssignmentId}' was not found.");

        if (existing.RevokedAtUtc is not null)
        {
            throw new InvalidOperationException($"Access assignment '{request.AssignmentId}' has already been revoked.");
        }

        var now = DateTimeOffset.UtcNow;
        var auditId = BuildAuditId("access-revoke", now);
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"access-revoke-{Guid.NewGuid():N}"
            : request.CorrelationId.Trim();

        var revoked = existing with
        {
            Version = existing.Version + 1,
            UpdatedAtUtc = now,
            RevokedBy = resolvedActor,
            RevokedAtUtc = now,
            RevocationReason = rationale,
            LastAuditId = auditId
        };

        await _store.ReplaceAsync(revoked, request.ExpectedVersion, ct).ConfigureAwait(false);
        return new UserAccessAssignmentMutationResultDto(
            revoked,
            BuildAuditEvent("user-access-assignment-revoked", auditId, now, resolvedActor, rationale, correlationId, revoked));
    }

    public async Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
        string actor,
        UserPermission requiredPermission,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        UserPermission globalPermissions,
        CancellationToken ct = default)
    {
        var normalizedActor = actor?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedActor))
        {
            return Denied(normalizedActor, requiredPermission, scopeKind, scopeId, "No authenticated actor was resolved.");
        }

        if ((globalPermissions & UserPermission.ManageUsers) == UserPermission.ManageUsers ||
            (globalPermissions & UserPermission.AdminMaintenance) == UserPermission.AdminMaintenance)
        {
            return Allowed(normalizedActor, requiredPermission, scopeKind, scopeId, "Global administrator permission granted access.");
        }

        if (scopeKind != AccessScopeKindDto.Global && scopeId is null)
        {
            return Denied(normalizedActor, requiredPermission, scopeKind, scopeId, "Scoped authorization requires a scope id.");
        }

        var candidateScopes = await ResolveScopeLineageAsync(scopeKind, scopeId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var assignments = await _store.ListAsync(ct).ConfigureAwait(false);

        var match = assignments
            .Where(assignment => assignment.PrincipalKind == AccessPrincipalKindDto.User)
            .Where(assignment => string.Equals(assignment.PrincipalId, normalizedActor, StringComparison.OrdinalIgnoreCase))
            .Where(assignment => assignment.RevokedAtUtc is null && IsEffective(assignment, now))
            .Where(assignment => (assignment.PermissionMask & (long)requiredPermission) == (long)requiredPermission)
            .Where(assignment => candidateScopes.Any(scope => ScopeMatches(assignment, scope)))
            .OrderByDescending(assignment => ScopeSpecificity(assignment.ScopeKind))
            .ThenByDescending(assignment => assignment.EffectiveFrom)
            .FirstOrDefault();

        if (match is null)
        {
            return Denied(normalizedActor, requiredPermission, scopeKind, scopeId, "No effective scoped access assignment grants the required permission.");
        }

        return Allowed(
            normalizedActor,
            requiredPermission,
            scopeKind,
            scopeId,
            "Scoped access assignment granted access.",
            match.AssignmentId,
            match.Version);
    }

    private async Task<IReadOnlyList<AccessScopeRef>> ResolveScopeLineageAsync(
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        CancellationToken ct)
    {
        if (scopeKind == AccessScopeKindDto.Global || scopeId is null)
        {
            return [new AccessScopeRef(AccessScopeKindDto.Global, null)];
        }

        var scopes = new List<AccessScopeRef>
        {
            new(scopeKind, scopeId),
            new(AccessScopeKindDto.Global, null)
        };

        if (_scopeLineageProvider is null)
        {
            return scopes;
        }

        var lineage = await _scopeLineageProvider.ResolveLineageAsync(scopeKind, scopeId.Value, ct).ConfigureAwait(false);
        scopes.AddRange(lineage.Where(scope => scopes.All(existing => existing != scope)));
        return scopes;
    }

    private static ValidatedCreateRequest ValidateCreate(UserAccessAssignmentCreateRequestDto request, string actor)
    {
        var principalId = NormalizeRequired(request.PrincipalId, nameof(request.PrincipalId));
        var resolvedActor = string.IsNullOrWhiteSpace(actor)
            ? NormalizeRequired(request.RequestedBy, nameof(request.RequestedBy))
            : actor.Trim();
        var rationale = NormalizeRequired(request.Rationale, nameof(request.Rationale));
        var roleName = NormalizeRequired(request.Role, nameof(request.Role));
        if (!Enum.TryParse<UserRole>(roleName, ignoreCase: true, out var role))
        {
            throw new ArgumentException($"Unknown role '{request.Role}'.", nameof(request));
        }

        var permissions = RolePermissions.TryParsePermissionNames(request.PermissionNames, out var parsed, out var invalid) && invalid.Count == 0
            ? parsed
            : throw new ArgumentException($"Unknown permissions: {string.Join(", ", invalid)}.", nameof(request));

        if (permissions == UserPermission.None)
        {
            throw new ArgumentException("At least one permission is required.", nameof(request));
        }

        _ = NormalizeScopeId(request.ScopeKind, request.ScopeId);
        if (request.EffectiveTo.HasValue && request.EffectiveFrom.HasValue && request.EffectiveTo <= request.EffectiveFrom)
        {
            throw new ArgumentException("EffectiveTo must be after EffectiveFrom.", nameof(request));
        }

        var approvalLimit = ValidateApprovalLimit(request.ApprovalLimitAmount, request.ApprovalLimitCurrency);

        return new ValidatedCreateRequest(
            principalId,
            role.ToString(),
            string.IsNullOrWhiteSpace(request.RoleProfileName) ? null : request.RoleProfileName.Trim(),
            permissions,
            resolvedActor,
            rationale,
            approvalLimit.Amount,
            approvalLimit.Currency,
            string.IsNullOrWhiteSpace(request.SegregationOfDutiesRule) ? null : request.SegregationOfDutiesRule.Trim());
    }

    private static (decimal? Amount, string? Currency) ValidateApprovalLimit(decimal? amount, string? currency)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? null
            : currency.Trim().ToUpperInvariant();

        if (amount.HasValue && amount.Value <= 0m)
        {
            throw new ArgumentException("ApprovalLimitAmount must be greater than zero.", nameof(amount));
        }

        if (amount.HasValue && string.IsNullOrWhiteSpace(normalizedCurrency))
        {
            throw new ArgumentException("ApprovalLimitCurrency is required when ApprovalLimitAmount is provided.", nameof(currency));
        }

        if (!amount.HasValue && !string.IsNullOrWhiteSpace(normalizedCurrency))
        {
            throw new ArgumentException("ApprovalLimitAmount is required when ApprovalLimitCurrency is provided.", nameof(amount));
        }

        if (normalizedCurrency is not null && normalizedCurrency.Length != 3)
        {
            throw new ArgumentException("ApprovalLimitCurrency must be a three-letter ISO currency code.", nameof(currency));
        }

        return (amount, normalizedCurrency);
    }

    private static Guid? NormalizeScopeId(AccessScopeKindDto scopeKind, Guid? scopeId)
    {
        if (scopeKind == AccessScopeKindDto.Global)
        {
            return null;
        }

        if (!scopeId.HasValue || scopeId.Value == Guid.Empty)
        {
            throw new ArgumentException("ScopeId is required for non-global scoped access assignments.");
        }

        return scopeId;
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    private static bool IsEffective(UserAccessAssignmentDto assignment, DateTimeOffset asOf)
        => assignment.EffectiveFrom <= asOf && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo > asOf);

    private static bool ScopeMatches(UserAccessAssignmentDto assignment, AccessScopeRef candidate)
        => assignment.ScopeKind == candidate.ScopeKind && assignment.ScopeId == candidate.ScopeId;

    private static int ScopeSpecificity(AccessScopeKindDto scopeKind)
        => scopeKind == AccessScopeKindDto.Global ? 0 : 1;

    private static ScopedAuthorizationDecisionDto Allowed(
        string actor,
        UserPermission permission,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        string reason,
        Guid? matchedAssignmentId = null,
        long? matchedAssignmentVersion = null)
        => new(true, actor, permission, scopeKind, scopeId, reason, matchedAssignmentId, matchedAssignmentVersion);

    private static ScopedAuthorizationDecisionDto Denied(
        string actor,
        UserPermission permission,
        AccessScopeKindDto scopeKind,
        Guid? scopeId,
        string reason)
        => new(false, actor, permission, scopeKind, scopeId, reason);

    private static string BuildAuditId(string prefix, DateTimeOffset now)
    {
        var value = $"{prefix}-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return value[..Math.Min(46, value.Length)];
    }

    private static UserAccessAssignmentAuditEventDto BuildAuditEvent(
        string eventType,
        string auditId,
        DateTimeOffset now,
        string actor,
        string rationale,
        string correlationId,
        UserAccessAssignmentDto assignment)
        => new(
            auditId,
            eventType,
            now,
            actor,
            rationale,
            correlationId,
            assignment.AssignmentId,
            assignment.PrincipalId,
            assignment.ScopeKind,
            assignment.ScopeId,
            assignment.PermissionNames,
            assignment.PermissionMask,
            assignment.Version,
            assignment.ApprovalLimitAmount,
            assignment.ApprovalLimitCurrency,
            assignment.SegregationOfDutiesRule);

    private sealed record ValidatedCreateRequest(
        string PrincipalId,
        string Role,
        string? RoleProfileName,
        UserPermission Permissions,
        string Actor,
        string Rationale,
        decimal? ApprovalLimitAmount,
        string? ApprovalLimitCurrency,
        string? SegregationOfDutiesRule);

}
