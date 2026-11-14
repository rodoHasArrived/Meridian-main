using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportAccessQueryContext(
    string? ActorPrincipalId = null,
    IReadOnlyList<string>? GroupPrincipalIds = null,
    string? CompanyId = null,
    bool HasGlobalOverride = false,
    string? TenantId = null,
    bool RequireBoundScope = false)
{
    /// <summary>Converts the compatibility context to the Reporting-owned query contract.</summary>
    public ReportingAccessQueryContext ToReportingContext() =>
        new(ActorPrincipalId, GroupPrincipalIds, CompanyId, HasGlobalOverride, TenantId, RequireBoundScope);
}

/// <summary>
/// Compatibility adapter for Reporting-owned access policy normalization and evaluation.
/// </summary>
public static class ReportAccessPolicyEvaluator
{
    public static ReportAccessPolicyDto Normalize(ReportAccessPolicyDto? policy, string? defaultOwnerPrincipalId = null) =>
        ReportingAccessPolicyEvaluator.Normalize(policy, defaultOwnerPrincipalId);

    public static IReadOnlyList<string> Validate(ReportAccessPolicyDto? policy) =>
        ReportingAccessPolicyEvaluator.Validate(policy);

    public static ReportAccessEvaluationDto Evaluate(ReportAccessPolicyDto? policy, ReportAccessQueryContext? context) =>
        ReportingAccessPolicyEvaluator.Evaluate(policy, context?.ToReportingContext());

    public static ReportAccessEvaluationDto Evaluate(ReportingOutputManifest manifest, ReportAccessQueryContext? context) =>
        ReportingAccessPolicyEvaluator.Evaluate(manifest, context?.ToReportingContext());

    public static string BuildSummary(ReportAccessPolicyDto? policy) =>
        ReportingAccessPolicyEvaluator.BuildSummary(policy);
}
