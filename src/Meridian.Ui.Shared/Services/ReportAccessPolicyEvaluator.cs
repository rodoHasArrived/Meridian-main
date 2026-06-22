using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportAccessQueryContext(
    string? ActorPrincipalId = null,
    IReadOnlyList<string>? GroupPrincipalIds = null,
    string? CompanyId = null,
    bool HasGlobalOverride = false);

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
            && string.IsNullOrWhiteSpace(normalized.OwnerPrincipalId)
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
        if (context?.HasGlobalOverride == true)
        {
            return Allow("Access granted by reporting administrator override.", []);
        }

        var actor = NormalizeId(context?.ActorPrincipalId);
        var company = NormalizeId(context?.CompanyId);
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
