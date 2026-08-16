using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Operations;
using Meridian.Reporting;

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
    IReadOnlyList<SecurityValidationGateResultDto>? SecurityValidationResults = null,
    string? DataProvenanceToken = null);

public sealed class ReportPackValidationService
{
    public IReadOnlyList<FundReportPackValidationIssueDto> Validate(ReportPackValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<FundReportPackValidationIssueDto>();
        issues.AddRange(ReportPackValidationInterop
            .Validate(new ReportPackValidationFacts
            {
                RunCount = context.RunCount,
                TrialBalanceCount = context.Report.TrialBalance.Count,
                AssetClassSectionCount = context.Report.AssetClassSections.Count,
                SecurityMissingCount = context.SecurityMissingCount,
                OpenReconciliationBreakCount = context.Reconciliation.OpenBreakCount,
                StaleReplayCount = context.StaleReplayCount,
                UnresolvedSecurityMasterConflictCount = context.UnresolvedSecurityMasterConflictCount,
                JournalEntryCount = context.Ledger.JournalEntryCount,
                LedgerEntryCount = context.Ledger.LedgerEntryCount,
                FormatCount = context.Formats.Count
            })
            .Select(rule => Issue(
                context,
                rule.Code,
                Enum.Parse<GovernanceReportValidationSeverityDto>(rule.Severity),
                rule.Title,
                rule.Message,
                EmptyToNull(rule.AffectedSection),
                EmptyToNull(rule.SuggestedAction),
                EmptyToNull(rule.EvidenceLink))));

        issues.AddRange(BuildSecurityMasterValidationIssues(context));

        // W9-TRUTH-001 hard entry-time block: a pack derived from simulated, seeded, or sample
        // figures always carries a Critical provenance issue, so ResolveStatus can never return
        // Validated for it — the retained mark blocks the approvable-deliverable path outright
        // instead of relying on reviewers to notice a carried label.
        if (!string.IsNullOrWhiteSpace(context.DataProvenanceToken))
        {
            var declared = DataProvenanceExtensions.ParseTokenOrSimulated(context.DataProvenanceToken);
            if (declared.IsNonReal())
            {
                issues.Add(Issue(
                    context,
                    "report-pack.provenance.simulated-source",
                    GovernanceReportValidationSeverityDto.Critical,
                    $"{declared.Label()} data provenance",
                    $"This report pack derives from {declared.Token()} data and can never validate as an approvable deliverable. It remains review-required with its provenance mark retained.",
                    affectedSection: "provenance",
                    suggestedAction: "Generate the pack from real operational data, or keep it for demonstration and review only."));
            }
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

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

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
