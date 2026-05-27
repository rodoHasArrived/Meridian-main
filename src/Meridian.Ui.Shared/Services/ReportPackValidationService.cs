using Meridian.Application.Services;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportPackValidationContext(
    Guid ReportId,
    DateTimeOffset AsOf,
    ReportPack Report,
    FundLedgerSummary Ledger,
    ReconciliationSummary Reconciliation,
    int RunCount,
    int SecurityMissingCount,
    IReadOnlyList<GovernanceReportArtifactFormatDto> Formats,
    int StaleReplayCount = 0,
    int UnresolvedSecurityMasterConflictCount = 0,
    IReadOnlyList<SecurityValidationGateResultDto>? SecurityValidationResults = null);

public sealed class ReportPackValidationService
{
    public IReadOnlyList<FundReportPackValidationIssueDto> Validate(ReportPackValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<FundReportPackValidationIssueDto>();
        if (context.RunCount == 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.no-contributing-runs",
                GovernanceReportValidationSeverityDto.Warning,
                "No contributing runs",
                "No recorded fund-scoped runs contributed ledger data for this report pack.",
                suggestedAction: "Record or select a fund-scoped strategy or paper run before approving the report pack.",
                evidenceLink: "/workstation/strategy/runs"));
        }

        if (context.Report.TrialBalance.Count == 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.empty-trial-balance",
                GovernanceReportValidationSeverityDto.Critical,
                "Empty trial balance",
                "The generated report pack contains no trial-balance rows.",
                affectedSection: "trial-balance",
                suggestedAction: "Confirm ledger postings are available for the reporting period before regenerating the pack.",
                evidenceLink: "/workstation/accounting"));
        }

        if (context.Report.AssetClassSections.Count == 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.empty-asset-class-sections",
                GovernanceReportValidationSeverityDto.Warning,
                "No asset-class sections",
                "The generated report pack contains no asset-class sections.",
                affectedSection: "asset-class-sections",
                suggestedAction: "Resolve ledger and Security Master classification inputs before report review.",
                evidenceLink: "/workstation/reporting/report-packs"));
        }

        if (context.SecurityMissingCount > 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.missing-security-master-classification",
                GovernanceReportValidationSeverityDto.Warning,
                "Missing Security Master classification",
                $"{context.SecurityMissingCount} trial-balance row(s) could not be resolved through Security Master.",
                affectedSection: "trial-balance",
                suggestedAction: "Complete Security Master classification for unresolved report symbols.",
                evidenceLink: "/workstation/data/security-master"));
        }

        issues.AddRange(BuildSecurityMasterValidationIssues(context));

        if (context.Reconciliation.OpenBreakCount > 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.open-reconciliation-breaks",
                GovernanceReportValidationSeverityDto.Critical,
                "Open reconciliation breaks",
                $"{context.Reconciliation.OpenBreakCount} open reconciliation break(s) were present at generation time.",
                affectedSection: "reconciliation",
                suggestedAction: "Resolve or approve reconciliation breaks before final report-pack sign-off.",
                evidenceLink: "/workstation/accounting/reconciliation"));
        }

        if (context.StaleReplayCount > 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.stale-replay-evidence",
                GovernanceReportValidationSeverityDto.Critical,
                "Stale replay evidence",
                $"{context.StaleReplayCount} replay verification evidence item(s) are stale for this report pack context.",
                affectedSection: "execution-replay",
                suggestedAction: "Re-run paper replay verification and regenerate report-pack evidence before publishing.",
                evidenceLink: "/workstation/trading/readiness"));
        }

        if (context.UnresolvedSecurityMasterConflictCount > 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.unresolved-security-master-conflicts",
                GovernanceReportValidationSeverityDto.Critical,
                "Unresolved Security Master conflicts",
                $"{context.UnresolvedSecurityMasterConflictCount} unresolved Security Master conflict(s) block publication readiness.",
                affectedSection: "security-master",
                suggestedAction: "Resolve Security Master conflicts and refresh downstream report-pack lineage evidence.",
                evidenceLink: "/workstation/data/security-master"));
        }

        if (context.Ledger.JournalEntryCount == 0 || context.Ledger.LedgerEntryCount == 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.missing-ledger-postings",
                GovernanceReportValidationSeverityDto.Critical,
                "Missing ledger postings",
                "The report pack was generated without journal or ledger entries in the source snapshot.",
                affectedSection: "ledger",
                suggestedAction: "Confirm ledger posting and period-close inputs before approving the report pack.",
                evidenceLink: "/workstation/accounting"));
        }

        if (context.Formats.Count == 0)
        {
            issues.Add(Issue(
                context,
                "report-pack.no-export-formats",
                GovernanceReportValidationSeverityDto.Critical,
                "No export formats",
                "The report pack has no requested export artifact formats.",
                suggestedAction: "Regenerate the report pack with at least one supported artifact format.",
                evidenceLink: "/workstation/reporting/report-packs"));
        }

        return issues
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal)
            .ToArray();
    }

    public GovernanceReportPackStatusDto ResolveStatus(IReadOnlyList<FundReportPackValidationIssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return issues.Any(static issue => issue.Severity is GovernanceReportValidationSeverityDto.Warning or GovernanceReportValidationSeverityDto.Critical)
            ? GovernanceReportPackStatusDto.ReviewRequired
            : GovernanceReportPackStatusDto.Validated;
    }

    public IReadOnlyList<FundReportPackLifecycleEventDto> BuildGenerationLifecycle(
        string actor,
        string correlationId,
        DateTimeOffset generatedAt,
        GovernanceReportPackStatusDto terminalStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return
        [
            new FundReportPackLifecycleEventDto(
                FromStatus: GovernanceReportPackStatusDto.Draft,
                ToStatus: GovernanceReportPackStatusDto.Generated,
                ChangedAt: generatedAt,
                Actor: actor,
                Reason: "Report pack generated from the source snapshot.",
                CorrelationId: correlationId),
            new FundReportPackLifecycleEventDto(
                FromStatus: GovernanceReportPackStatusDto.Generated,
                ToStatus: terminalStatus,
                ChangedAt: generatedAt,
                Actor: actor,
                Reason: terminalStatus == GovernanceReportPackStatusDto.Validated
                    ? "Generation validation completed without report blockers."
                    : "Generation validation found issues requiring review.",
                CorrelationId: correlationId)
        ];
    }

    private static FundReportPackValidationIssueDto Issue(
        ReportPackValidationContext context,
        string code,
        GovernanceReportValidationSeverityDto severity,
        string title,
        string message,
        string? affectedSection = null,
        string? suggestedAction = null,
        string? evidenceLink = null)
        => new(
            Code: code,
            Severity: severity,
            Title: title,
            Message: message,
            AffectedReportId: context.ReportId,
            AffectedSection: affectedSection,
            AffectedPeriod: context.AsOf,
            SuggestedAction: suggestedAction,
            EvidenceLink: evidenceLink);

    private static IReadOnlyList<FundReportPackValidationIssueDto> BuildSecurityMasterValidationIssues(
        ReportPackValidationContext context)
    {
        var validationResults = context.SecurityValidationResults ?? [];
        var issues = new List<FundReportPackValidationIssueDto>();

        foreach (var result in validationResults)
        {
            foreach (var issue in result.Report.Issues)
            {
                if (issue.Severity == SecurityValidationSeverityDto.Info)
                {
                    continue;
                }

                var securityLabel = result.Symbol ?? result.SecurityId?.ToString() ?? "unresolved-security";
                issues.Add(new FundReportPackValidationIssueDto(
                    Code: $"report-pack.security-master.{issue.Code.ToLowerInvariant()}",
                    Severity: issue.Severity is SecurityValidationSeverityDto.Critical or SecurityValidationSeverityDto.Error
                        ? GovernanceReportValidationSeverityDto.Critical
                        : GovernanceReportValidationSeverityDto.Warning,
                    Title: issue.Title,
                    Message: issue.Message,
                    AffectedReportId: context.ReportId,
                    AffectedSection: "security-master",
                    AffectedSecurity: securityLabel,
                    AffectedPeriod: context.AsOf,
                    SuggestedAction: issue.SuggestedAction,
                    EvidenceLink: issue.EvidenceLinks.FirstOrDefault()?.Route ?? "/workstation/data/security-master"));
            }
        }

        return issues;
    }
}
