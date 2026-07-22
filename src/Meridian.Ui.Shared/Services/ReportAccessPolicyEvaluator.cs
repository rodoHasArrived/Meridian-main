using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportAccessQueryContext(
    string? ActorPrincipalId = null,
    IReadOnlyList<string>? GroupPrincipalIds = null,
    string? CompanyId = null,
    bool HasGlobalOverride = false,
    string? TenantId = null,
    bool RequireBoundScope = false);

public static class ReportAccessPolicyEvaluator
{
    private static readonly ReportAccessPolicyDto CompanyWidePolicy = new(ReportAccessModeDto.CompanyWide);

    public static ReportAccessPolicyDto Normalize(ReportAccessPolicyDto? policy, string? defaultOwnerPrincipalId = null)
    {
        if (policy is null)
        {
            return string.IsNullOrWhiteSpace(defaultOwnerPrincipalId)
                ? CompanyWidePolicy
                : CompanyWidePolicy with { OwnerPrincipalId = defaultOwnerPrincipalId.Trim() };
        }

        var owner = NormalizeId(policy.OwnerPrincipalId) ?? NormalizeId(defaultOwnerPrincipalId);
        var companyId = NormalizeId(policy.CompanyId);
        var principals = (policy.Principals ?? [])
            .Where(static principal => principal is not null && !string.IsNullOrWhiteSpace(principal.PrincipalId))
            .Select(static principal => new ReportAccessPrincipalDto(
                principal.Kind,
                principal.PrincipalId.Trim(),
                string.IsNullOrWhiteSpace(principal.DisplayName) ? null : principal.DisplayName.Trim()))
            .GroupBy(static principal => $"{principal.Kind}:{principal.PrincipalId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static principal => principal.Kind)
            .ThenBy(static principal => principal.PrincipalId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return policy with
        {
            OwnerPrincipalId = owner,
            CompanyId = companyId,
            Principals = principals
        };
    }

    public static IReadOnlyList<string> Validate(ReportAccessPolicyDto? policy)
    {
        if (policy is null)
        {
            return [];
        }

        var issues = new List<string>();
        if (policy.Principals?.Any(static principal => principal is null || string.IsNullOrWhiteSpace(principal.PrincipalId)) == true)
        {
            issues.Add("Report access principals require principal ids.");
        }

        var normalized = Normalize(policy);
        var principals = normalized.Principals ?? [];
        if (normalized.Mode is ReportAccessModeDto.Private or ReportAccessModeDto.Restricted
            && (!normalized.AllowOwnerAccess || string.IsNullOrWhiteSpace(normalized.OwnerPrincipalId))
            && principals.Count == 0)
        {
            issues.Add("Private or restricted report access requires an owner or at least one allowed principal.");
        }

        if (normalized.Mode == ReportAccessModeDto.Private
            && principals.Any(static principal => principal.Kind != ReportAccessPrincipalKindDto.User))
        {
            issues.Add("Private report access can only name user principals; use Restricted for group or company principals.");
        }

        return issues;
    }

    public static ReportAccessEvaluationDto Evaluate(ReportAccessPolicyDto? policy, ReportAccessQueryContext? context)
    {
        var normalized = Normalize(policy);
        var actor = NormalizeId(context?.ActorPrincipalId);
        var company = NormalizeId(context?.CompanyId);
        var tenant = NormalizeId(context?.TenantId);
        if (context?.RequireBoundScope == true
            && (actor is null || company is null || tenant is null))
        {
            return Deny("Report access requires authenticated actor, tenant, and company scope.");
        }
        if (context?.HasGlobalOverride == true)
        {
            return Allow("Access granted by reporting administrator override within the bound tenant scope.", []);
        }
        var groupIds = new HashSet<string>(
            context?.GroupPrincipalIds?
                .Select(NormalizeId)
                .Where(static value => value is not null)
                .Select(static value => value!)
                ?? [],
            StringComparer.OrdinalIgnoreCase);

        if (normalized.AllowOwnerAccess
            && !string.IsNullOrWhiteSpace(normalized.OwnerPrincipalId)
            && !string.IsNullOrWhiteSpace(actor)
            && string.Equals(normalized.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
        {
            return Allow("Access granted to report owner.", [BuildUserPrincipal(actor)]);
        }

        if (normalized.Mode == ReportAccessModeDto.CompanyWide)
        {
            if (context?.RequireBoundScope == true && string.IsNullOrWhiteSpace(normalized.CompanyId))
            {
                return Deny("Company-wide report access is not bound to a company snapshot.");
            }

            if (string.IsNullOrWhiteSpace(normalized.CompanyId)
                || string.Equals(normalized.CompanyId, company, StringComparison.OrdinalIgnoreCase))
            {
                var matches = string.IsNullOrWhiteSpace(normalized.CompanyId)
                    ? Array.Empty<ReportAccessPrincipalDto>()
                    : [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Company, normalized.CompanyId)];
                return Allow(BuildSummary(normalized), matches);
            }

            return Deny("Report is locked to another company.");
        }

        foreach (var principal in normalized.Principals ?? [])
        {
            var isMatch = principal.Kind switch
            {
                ReportAccessPrincipalKindDto.User => !string.IsNullOrWhiteSpace(actor)
                    && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase),
                ReportAccessPrincipalKindDto.Group => groupIds.Contains(principal.PrincipalId),
                ReportAccessPrincipalKindDto.Company => !string.IsNullOrWhiteSpace(company)
                    && string.Equals(principal.PrincipalId, company, StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            if (isMatch && (normalized.Mode == ReportAccessModeDto.Restricted || principal.Kind == ReportAccessPrincipalKindDto.User))
            {
                return Allow($"Access granted by {principal.Kind.ToString().ToLowerInvariant()} report audience.", [principal]);
            }
        }

        return Deny(BuildDenyReason(normalized));
    }

    public static ReportAccessEvaluationDto Evaluate(
        ReportingOutputManifest manifest,
        ReportAccessQueryContext? context)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var actor = NormalizeId(context?.ActorPrincipalId);
        var company = NormalizeId(context?.CompanyId);
        var tenant = NormalizeId(context?.TenantId);
        if (context?.RequireBoundScope == true && (actor is null || company is null || tenant is null))
        {
            return Deny("Report access requires authenticated actor, tenant, and company scope.");
        }

        if (manifest.OperationalScope is null || manifest.ImmutableAccessScope is null)
        {
            return context?.RequireBoundScope == true
                ? Deny("This legacy reporting run has no immutable tenant and access snapshot.")
                : Evaluate(manifest.AccessPolicy, context);
        }

        if (!string.Equals(manifest.OperationalScope.TenantId, tenant, StringComparison.Ordinal)
            || !string.Equals(manifest.OperationalScope.CompanyId, company, StringComparison.Ordinal))
        {
            return Deny("The reporting run belongs to another tenant or company scope.");
        }

        if (context?.HasGlobalOverride == true)
        {
            return Allow("Access granted by reporting administrator override within the immutable tenant scope.", []);
        }

        var access = manifest.ImmutableAccessScope;
        if (access.AllowOwnerAccess
            && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
            && string.Equals(access.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
        {
            return Allow("Access granted to the immutable report owner.", [BuildUserPrincipal(actor!)]);
        }

        if (access.Mode == ReportingGovernanceAccessMode.CompanyWide)
        {
            return Allow("Access granted by the immutable company-wide report policy.", []);
        }

        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in context?.GroupPrincipalIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(group))
            {
                groupIds.Add(group.Trim());
            }
        }

        bool Matches(ReportingAccessPrincipalScope principal) => principal.Kind switch
        {
            ReportingAccessPrincipalKind.User => string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase),
            ReportingAccessPrincipalKind.Group => groupIds.Contains(principal.PrincipalId),
            ReportingAccessPrincipalKind.Company => string.Equals(principal.PrincipalId, company, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        var namedPrincipalMatched = !access.Principals.IsDefaultOrEmpty
            && access.Principals.Any(principal =>
                Matches(principal)
                && (access.Mode == ReportingGovernanceAccessMode.Restricted
                    || principal.Kind == ReportingAccessPrincipalKind.User));
        return namedPrincipalMatched
            ? Allow("Access granted by the immutable named report audience.", [])
            : Deny("The caller is not included in the immutable report access snapshot.");
    }

    public static string BuildSummary(ReportAccessPolicyDto? policy)
    {
        var normalized = Normalize(policy);
        var principals = normalized.Principals ?? [];
        return normalized.Mode switch
        {
            ReportAccessModeDto.Private => string.IsNullOrWhiteSpace(normalized.OwnerPrincipalId)
                ? $"Private user-locked access for {principals.Count} named user(s)."
                : $"Private user-locked access owned by {normalized.OwnerPrincipalId}.",
            ReportAccessModeDto.Restricted => $"Restricted access for {principals.Count} named user/group/company principal(s).",
            ReportAccessModeDto.CompanyWide when !string.IsNullOrWhiteSpace(normalized.CompanyId) =>
                $"Company-wide access for {normalized.CompanyId}.",
            _ => "Company-wide access"
        };
    }

    private static ReportAccessEvaluationDto Allow(string reason, IReadOnlyList<ReportAccessPrincipalDto> matchedPrincipals) =>
        new(true, reason, matchedPrincipals);

    private static ReportAccessEvaluationDto Deny(string reason) =>
        new(false, reason, []);

    private static string BuildDenyReason(ReportAccessPolicyDto policy) =>
        policy.Mode switch
        {
            ReportAccessModeDto.Private => "Report is private to its owner or named users.",
            ReportAccessModeDto.Restricted => "Report is restricted to named users, groups, or companies.",
            _ => "Report access policy did not match the caller."
        };

    private static ReportAccessPrincipalDto BuildUserPrincipal(string actor) =>
        new(ReportAccessPrincipalKindDto.User, actor);

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
