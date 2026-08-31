using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel
{
    private static IReadOnlyList<string> BuildClosePlanConfigurationEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/setup/{workflowId:D}",
            $"evidence://close-plan-configuration/fund/{closePlan.FundProfileId}/period/{closePlan.PeriodId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-plan-configuration/ledger-book/{ledgerBookId}");
        }

        foreach (var task in closePlan.Tasks)
        {
            foreach (var evidence in task.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }
        }

        foreach (var adjustment in closePlan.LateAdjustments)
        {
            foreach (var evidence in adjustment.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }
        }

        return links.ToArray();
    }

    private static IReadOnlyList<string> BuildLateAdjustmentRequestEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        Guid journalEntryId)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/late-adjustment/{workflowId:D}/{journalEntryId:D}",
            $"evidence://late-adjustment-request/workflow/{workflowId:D}/journal/{journalEntryId:D}/period/{closePlan.PeriodId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://late-adjustment-request/book/{ledgerBookId:D}/journal/{journalEntryId:D}/period/{closePlan.PeriodId}");
        }

        return links.ToArray();
    }

    private static LockClosePeriodRequestDto BuildClosePeriodLockRequest(
        Guid workflowId,
        long workflowVersion,
        ClosePeriodPlanDto closePlan,
        string actor,
        bool prepareClosingEntriesOnly,
        string? controllerRole = null)
    {
        var reportPackId = BuildCloseReportPackId(closePlan);
        var closePackageId = $"close-package-{closePlan.FundProfileId}-{closePlan.PeriodId}";
        var manifestId = $"manifest-{closePlan.FundProfileId}-{closePlan.PeriodId}";
        return new LockClosePeriodRequestDto(
            workflowId,
            ExpectedWorkflowVersion: workflowVersion,
            Actor: actor,
            Rationale: prepareClosingEntriesOnly
                ? "Queue closing entries from WPF Accounting Close without hard-locking the accounting period."
                : "Lock close period from WPF Accounting Close after checklist, sign-off, reconciliation, report certification, and closing-entry posting review.",
            ReportPackId: reportPackId,
            EvidenceLinks: BuildClosePeriodLockEvidence(workflowId, closePlan, reportPackId, closePackageId, manifestId),
            ChecklistControlApprovals: BuildClosePeriodLockApprovals(closePlan),
            CorrelationId: prepareClosingEntriesOnly
                ? $"wpf-close-period-prepare-closing-entries-{workflowId:D}"
                : $"wpf-close-period-lock-{workflowId:D}",
            ClosePackageId: closePackageId,
            ClosePackageManifestId: manifestId,
            ClosePackageRetainedManifestRoute: $"/workstation/reporting/packages/{manifestId}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            PrepareClosingEntriesOnly: prepareClosingEntriesOnly,
            ControllerRole: controllerRole);
    }

    private SignOffCloseTaskRequestDto BuildCloseTaskSignOffRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        CloseTaskDto task,
        string roleText,
        string decisionText,
        string notesText,
        string actor)
    {
        var role = NormalizeRequired(roleText, task.SignOffRequirements.FirstOrDefault()?.Role ?? task.Owner);
        var decision = ParseCloseTaskSignOffDecision(decisionText);
        var notes = NormalizeRequired(
            notesText,
            decision == ManualJournalEntryStatusDto.Approved
                ? $"WPF Accounting Close retained {role} sign-off evidence for {task.DisplayName}."
                : $"WPF Accounting Close retained {role} rejection evidence for {task.DisplayName}.");
        return new SignOffCloseTaskRequestDto(
            workflowId,
            task.TaskId,
            role,
            decision,
            Actor: actor,
            Notes: notes,
            EvidenceLinks: BuildCloseTaskSignOffEvidence(workflowId, closePlan, task, role),
            CorrelationId: $"wpf-close-task-signoff-{workflowId:D}-{SanitizeForCorrelation(task.TaskId)}-{SanitizeForCorrelation(role)}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private ReviewLateAdjustmentRequestDto BuildReviewLateAdjustmentRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        LateAdjustmentRequestDto adjustment,
        string actor)
    {
        var decision = ParseCloseReviewDecision(LateAdjustmentReviewDecision);
        var decisionText = decision.ToString();
        var notes = NormalizeRequired(
            LateAdjustmentReviewNotes,
            $"WPF Accounting Close {decisionText.ToLowerInvariant()} late adjustment {adjustment.RequestId}.");
        return new ReviewLateAdjustmentRequestDto(
            workflowId,
            adjustment.RequestId,
            decision,
            Actor: actor,
            Notes: notes,
            EvidenceLinks: BuildLateAdjustmentReviewEvidence(workflowId, closePlan, adjustment, decision),
            CorrelationId: $"wpf-late-adjustment-review-{workflowId:D}-{SanitizeForCorrelation(adjustment.RequestId)}-{SanitizeForCorrelation(decisionText)}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private static IReadOnlyList<string> BuildLateAdjustmentReviewEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        LateAdjustmentRequestDto adjustment,
        ManualJournalEntryStatusDto decision)
    {
        var decisionToken = decision == ManualJournalEntryStatusDto.Rejected ? "rejection" : "approval";
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/late-adjustment-review/{workflowId:D}/{adjustment.RequestId}/{decisionToken}",
            $"evidence://late-adjustment-review/request/{adjustment.RequestId}/workflow/{workflowId:D}/period/{closePlan.PeriodId}/{decisionToken}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://late-adjustment-review/request/{adjustment.RequestId}/book/{ledgerBookId:D}/period/{closePlan.PeriodId}/{decisionToken}");
        }

        foreach (var evidence in adjustment.EvidenceLinks)
        {
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                links.Add(evidence.Trim());
            }
        }

        return links.ToArray();
    }

    private ReviewCloseEvidenceRequestDto BuildReviewCloseEvidenceRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        AccountingConfigurationValidationIssueDto issue,
        string actor)
    {
        var targetId = NormalizeOptional(issue.TargetId) ?? closePlan.ClosePlanId;
        return new ReviewCloseEvidenceRequestDto(
            workflowId,
            issue.Code,
            issue.TargetId,
            Actor: actor,
            Notes: NormalizeRequired(
                CloseEvidenceReviewNotes,
                $"WPF Accounting Close reviewed blocker {issue.Code} for {targetId}. {issue.Message}"),
            EvidenceLinks: BuildCloseEvidenceReviewEvidence(workflowId, closePlan, issue),
            CorrelationId: $"wpf-close-evidence-review-{workflowId:D}-{SanitizeForCorrelation(issue.Code)}-{SanitizeForCorrelation(targetId)}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator);
    }

    private static IReadOnlyList<string> BuildCloseEvidenceReviewEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        AccountingConfigurationValidationIssueDto issue)
    {
        var targetId = NormalizeOptional(issue.TargetId) ?? closePlan.ClosePlanId;
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/evidence-review/{workflowId:D}/{issue.Code}/{targetId}",
            $"evidence://close-review/workflow/{workflowId:D}/period/{closePlan.PeriodId}/issue/{issue.Code}/target/{targetId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-review/workflow/{workflowId:D}/period/{closePlan.PeriodId}/book/{ledgerBookId:D}/issue/{issue.Code}/target/{targetId}");
        }

        return links.ToArray();
    }

    private static IReadOnlyList<string> BuildCloseTaskSignOffEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        CloseTaskDto task,
        string role)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/task-signoff/{workflowId:D}/{task.TaskId}/{role}",
            $"evidence://close-task-signoff/workflow/{workflowId:D}/task/{task.TaskId}/role/{role}/period/{closePlan.PeriodId}"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-task-signoff/book/{ledgerBookId:D}/task/{task.TaskId}/role/{role}/period/{closePlan.PeriodId}");
        }

        foreach (var evidence in task.EvidenceLinks)
        {
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                links.Add(evidence.Trim());
            }
        }

        return links.ToArray();
    }

    private static CloseTaskDto? ResolveNextSignOffTask(ClosePeriodPlanDto closePlan)
        => closePlan.Tasks.FirstOrDefault(static task =>
            task.Status is not CloseTaskStatusDto.SignedOff and not CloseTaskStatusDto.Blocked and not CloseTaskStatusDto.WaitingOnDependency &&
            task.SignOffRequirements.Any(static requirement => !requirement.IsSatisfied));

    private CloseTaskDto? ResolveCloseTaskSignOffTask(ClosePeriodPlanDto closePlan)
    {
        var taskId = NormalizeOptional(CloseTaskSignOffTaskId);
        return taskId is null
            ? null
            : closePlan.Tasks.FirstOrDefault(task => string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
    }

    private static LateAdjustmentRequestDto? ResolveNextLateAdjustment(ClosePeriodPlanDto closePlan)
        => closePlan.LateAdjustments.FirstOrDefault(static adjustment =>
            adjustment.ApprovalState == ManualJournalEntryStatusDto.Submitted);

    private LateAdjustmentRequestDto? ResolveLateAdjustmentReviewDraft(ClosePeriodPlanDto closePlan)
    {
        if (!TryParseCloseReviewDecision(LateAdjustmentReviewDecision, out _))
        {
            return null;
        }

        var requestId = NormalizeOptional(LateAdjustmentReviewRequestId);
        return requestId is null
            ? null
            : closePlan.LateAdjustments.FirstOrDefault(adjustment =>
                string.Equals(adjustment.RequestId, requestId, StringComparison.OrdinalIgnoreCase) &&
                adjustment.ApprovalState == ManualJournalEntryStatusDto.Submitted);
    }

    private static AccountingConfigurationValidationIssueDto? ResolveNextCloseEvidenceReviewIssue(ClosePeriodPlanDto closePlan)
        => closePlan.ValidationIssues.FirstOrDefault(issue => FindCloseEvidenceReview(closePlan, issue) is null);

    private AccountingConfigurationValidationIssueDto? ResolveCloseEvidenceReviewIssue(ClosePeriodPlanDto closePlan)
    {
        var issueCode = NormalizeOptional(CloseEvidenceReviewIssueCode);
        if (issueCode is null)
        {
            return null;
        }

        var targetId = NormalizeOptional(CloseEvidenceReviewTargetId);
        return closePlan.ValidationIssues.FirstOrDefault(issue =>
            string.Equals(issue.Code, issueCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(issue.TargetId ?? string.Empty, targetId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            FindCloseEvidenceReview(closePlan, issue) is null);
    }

    private static CloseEvidenceReviewDto? FindCloseEvidenceReview(
        ClosePeriodPlanDto closePlan,
        AccountingConfigurationValidationIssueDto issue)
        => closePlan.EvidenceReviews
            .OrderByDescending(static review => review.ReviewedAtUtc)
            .FirstOrDefault(review =>
                string.Equals(review.IssueCode, issue.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(review.TargetId ?? string.Empty, issue.TargetId ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeForCorrelation(string value)
        => string.Concat((value ?? string.Empty)
            .Trim()
            .Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'))
            .Trim('-');

    private static IReadOnlyList<OperationsChecklistControlApprovalDto> BuildClosePeriodLockApprovals(
        ClosePeriodPlanDto closePlan)
        => closePlan.Tasks
            .SelectMany(static task => task.SignOffs
                .Where(static signOff => signOff.ApprovalState == ManualJournalEntryStatusDto.Approved)
                .Select(signOff => new OperationsChecklistControlApprovalDto(
                    task.TaskId,
                    string.IsNullOrWhiteSpace(signOff.Actor) ? "wpf-accounting-controller" : signOff.Actor.Trim(),
                    signOff.SignedAtUtc ?? DateTimeOffset.UtcNow)))
            .ToArray();

    private static IReadOnlyList<string> BuildClosePeriodLockEvidence(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        string reportPackId,
        string closePackageId,
        string manifestId)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"wpf://accounting/close/period-lock/{workflowId:D}",
            $"evidence://close-package/{closePackageId}/workflow/{workflowId:D}/period/{closePlan.PeriodId}/report-pack/{reportPackId}/manifest/{manifestId}/period-lock"
        };

        if (closePlan.LedgerBookId is { } ledgerBookId)
        {
            links.Add($"evidence://close-package/{closePackageId}/book/{ledgerBookId:D}/period/{closePlan.PeriodId}/report-pack/{reportPackId}/period-lock");
        }

        foreach (var task in closePlan.Tasks)
        {
            foreach (var evidence in task.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }

            foreach (var signOff in task.SignOffs)
            {
                foreach (var evidence in signOff.EvidenceLinks)
                {
                    if (!string.IsNullOrWhiteSpace(evidence))
                    {
                        links.Add(evidence.Trim());
                    }
                }
            }
        }

        foreach (var adjustment in closePlan.LateAdjustments)
        {
            foreach (var evidence in adjustment.EvidenceLinks)
            {
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    links.Add(evidence.Trim());
                }
            }
        }

        return links.ToArray();
    }

    private static string BuildCloseReportPackId(ClosePeriodPlanDto closePlan)
        => $"report-pack-{closePlan.FundProfileId}-{closePlan.PeriodId}";

    private void ApplyClosePeriodLockIssues(IReadOnlyList<AccountingConfigurationValidationIssueDto> issues)
    {
        ClosePeriodLockIssueRows.Clear();
        foreach (var issue in issues)
        {
            ClosePeriodLockIssueRows.Add(new AccountingWorkbenchRow(
                issue.Code,
                issue.Severity.ToString(),
                issue.Message,
                issue.SuggestedAction ?? "Resolve the close blocker before locking the period.",
            issue.TargetId ?? string.Empty));
        }
    }
}

public sealed record CloseSetupTaskOption(
    string TaskId,
    string DisplayName,
    string Status,
    string Owner,
    string DueDate,
    string SignOffSummary);

public sealed record AccountingClosePostingBalanceRow(
    string AccountName,
    string AccountType,
    string Balance,
    string Scope,
    string FinancialAccountId);

public sealed record CloseWorkflowStep(
    string StepId,
    string Label,
    string Status,
    string Detail,
    string Evidence,
    string? DisabledReason,
    IAsyncRelayCommand Command)
{
    public string ActionLabel => Label;
    public string DisabledReasonText => string.IsNullOrWhiteSpace(DisabledReason)
        ? "Ready"
        : DisabledReason;
}
