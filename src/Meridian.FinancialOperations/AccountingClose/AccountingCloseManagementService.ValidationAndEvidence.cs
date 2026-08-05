using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseManagementService
{
    private static bool CloseConfigurationVersionMatches(DateTimeOffset actual, DateTimeOffset expected)
        => actual.ToUniversalTime().Ticks == expected.ToUniversalTime().Ticks;

    private static bool RequiresLateAdjustmentApproval(decimal amount, MaterialityPolicyDto policy)
        => policy.RequiresLateAdjustmentApproval && Math.Abs(amount) >= policy.AmountThreshold;

    private static bool IsLateAdjustmentDecisionPending(LateAdjustmentRequestDto adjustment)
        => adjustment.ApprovalState is not ManualJournalEntryStatusDto.Approved
            and not ManualJournalEntryStatusDto.Rejected;

    private static bool IsLateAdjustmentRequestRetained(LateAdjustmentRequestDto adjustment)
        => adjustment.ApprovalState is not ManualJournalEntryStatusDto.Rejected;

    private static bool HasRejectedSignOff(string requiredRole, IReadOnlyList<CloseSignOffDto> signOffs)
        => signOffs.Any(signOff =>
            signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
            string.Equals(signOff.Role, requiredRole, StringComparison.OrdinalIgnoreCase));

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException($"Reviewed automation cannot {action}; a human operator must perform this accounting close action.");
        }
    }

    private static void EnsureIndependentCloseTaskSignOffActor(
        OperationsCloseChecklistTaskDto task,
        string actor)
    {
        if (!string.IsNullOrWhiteSpace(task.AcknowledgedBy) &&
            string.Equals(task.AcknowledgedBy.Trim(), actor, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Close task '{task.TaskId}' must be signed off by an actor independent from acknowledgement actor '{task.AcknowledgedBy.Trim()}'.");
        }
    }

    // Shared evidence-classification pattern: a link classifies as a given evidence kind when it
    // carries one of the kind's keywords. Provenance additionally requires the same link to
    // reference the review subject (or fall back to the workflow/period scope) and the workflow's
    // ledger book.
    private static readonly string[] CloseTaskSignOffEvidenceKeywords =
        ["signoff", "sign-off", "approval", "control", "review"];

    private static readonly string[] LateAdjustmentRequestEvidenceKeywords =
        ["late-adjustment", "late adjustment"];

    private static readonly string[] LateAdjustmentReviewEvidenceKeywords =
        ["approval", "rejection", "decision", "review"];

    private static readonly string[] CloseEvidenceReviewEvidenceKeywords =
        ["close-review", "blocker", "evidence", "audit", "remediation", "review"];

    private static readonly string[] ClosePlanConfigurationEvidenceKeywords =
        ["close-plan", "close plan", "close-setup", "configuration", "materiality", "approval"];

    private static readonly string[] ClosePeriodLockEvidenceKeywords =
        ["period-lock", "close-package", "close package", "report-pack", "report package", "manifest", "certification"];

    private static bool EvidenceLinkContainsAnyKeyword(string link, string[] keywords)
        => keywords.Any(keyword => link.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool HasEvidenceOfKind(IReadOnlyList<string> evidenceLinks, string[] keywords)
        => evidenceLinks.Any(link => EvidenceLinkContainsAnyKeyword(link, keywords));

    private static bool HasEvidenceOfKindWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string[] keywords,
        OperationsContinuityWorkflowDto workflow,
        Func<string, bool>? subjectMatches = null)
        => evidenceLinks.Any(link =>
            EvidenceLinkContainsAnyKeyword(link, keywords) &&
            (subjectMatches?.Invoke(link) ?? EvidenceLinkContainsWorkflowScope(link, workflow)) &&
            EvidenceLinkContainsLedgerBook(link, workflow));

    private static bool EvidenceLinkContainsWorkflowScope(string link, OperationsContinuityWorkflowDto workflow)
        => EvidenceLinkContainsGuidToken(link, workflow.WorkflowId) ||
           EvidenceLinkContainsIdentifierToken(link, workflow.PeriodId);

    private static bool HasCloseTaskSignOffEvidence(IReadOnlyList<string> evidenceLinks)
        => HasEvidenceOfKind(evidenceLinks, CloseTaskSignOffEvidenceKeywords);

    private static bool HasCloseTaskSignOffEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string taskId,
        string role,
        OperationsContinuityWorkflowDto workflow)
        => HasEvidenceOfKindWithProvenance(
            evidenceLinks,
            CloseTaskSignOffEvidenceKeywords,
            workflow,
            link => EvidenceLinkContainsIdentifierToken(link, taskId) &&
                EvidenceLinkContainsRoleToken(link, role) &&
                EvidenceLinkContainsWorkflowScope(link, workflow));

    private static bool HasLateAdjustmentRequestEvidence(IReadOnlyList<string> evidenceLinks)
        => HasEvidenceOfKind(evidenceLinks, LateAdjustmentRequestEvidenceKeywords);

    private static bool HasLateAdjustmentRequestEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        Guid journalEntryId,
        OperationsContinuityWorkflowDto workflow)
        => HasEvidenceOfKindWithProvenance(
            evidenceLinks,
            LateAdjustmentRequestEvidenceKeywords,
            workflow,
            link => EvidenceLinkContainsGuidToken(link, journalEntryId) ||
                EvidenceLinkContainsWorkflowScope(link, workflow));

    private static bool HasLateAdjustmentReviewEvidence(IReadOnlyList<string> evidenceLinks)
        => HasEvidenceOfKind(evidenceLinks, LateAdjustmentReviewEvidenceKeywords);

    private static bool HasLateAdjustmentReviewEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string requestId,
        LateAdjustmentRequestDto adjustment,
        OperationsContinuityWorkflowDto workflow)
        => HasEvidenceOfKindWithProvenance(
            evidenceLinks,
            LateAdjustmentReviewEvidenceKeywords,
            workflow,
            link => EvidenceLinkContainsIdentifierToken(link, requestId) ||
                EvidenceLinkContainsGuidToken(link, adjustment.JournalEntryId) ||
                EvidenceLinkContainsWorkflowScope(link, workflow));

    private static bool HasCloseEvidenceReviewEvidence(IReadOnlyList<string> evidenceLinks)
        => HasEvidenceOfKind(evidenceLinks, CloseEvidenceReviewEvidenceKeywords);

    private static bool HasCloseEvidenceReviewEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string issueCode,
        string? targetId,
        OperationsContinuityWorkflowDto workflow)
        => HasEvidenceOfKindWithProvenance(
            evidenceLinks,
            CloseEvidenceReviewEvidenceKeywords,
            workflow,
            link => EvidenceLinkContainsIdentifierToken(link, issueCode) ||
                (!string.IsNullOrWhiteSpace(targetId) && EvidenceLinkContainsIdentifierToken(link, targetId)) ||
                EvidenceLinkContainsWorkflowScope(link, workflow));

    private static bool HasClosePlanConfigurationEvidence(IReadOnlyList<string> evidenceLinks)
        => HasEvidenceOfKind(evidenceLinks, ClosePlanConfigurationEvidenceKeywords);

    private static bool HasClosePlanConfigurationEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        OperationsContinuityWorkflowDto workflow)
        => HasEvidenceOfKindWithProvenance(evidenceLinks, ClosePlanConfigurationEvidenceKeywords, workflow);

    private static bool HasClosePeriodLockEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        OperationsContinuityWorkflowDto workflow)
        => HasEvidenceOfKindWithProvenance(evidenceLinks, ClosePeriodLockEvidenceKeywords, workflow);

    private static bool EvidenceLinkContainsGuidToken(string link, Guid value)
        => EvidenceLinkContainsIdentifierToken(link, value.ToString("D")) ||
           EvidenceLinkContainsIdentifierToken(link, value.ToString("N"));

    private static bool EvidenceLinkContainsRoleToken(string link, string role)
    {
        if (EvidenceLinkContainsIdentifierToken(link, role))
        {
            return true;
        }

        var roleSlug = string.Join(
            '-',
            role.Split([' ', '\t', '\r', '\n', '_', '/'], StringSplitOptions.RemoveEmptyEntries));
        return !string.Equals(roleSlug, role, StringComparison.OrdinalIgnoreCase) &&
            EvidenceLinkContainsIdentifierToken(link, roleSlug);
    }

    private static bool EvidenceLinkContainsIdentifierToken(string link, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var searchIndex = 0;
        while (searchIndex < link.Length)
        {
            var tokenIndex = link.IndexOf(token, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
            {
                return false;
            }

            if (EvidenceTokenBoundaryAt(link, tokenIndex - 1) &&
                EvidenceTokenBoundaryAt(link, tokenIndex + token.Length))
            {
                return true;
            }

            searchIndex = tokenIndex + token.Length;
        }

        return false;
    }

    private static bool EvidenceLinkContainsLedgerBook(string link, OperationsContinuityWorkflowDto workflow)
    {
        if (workflow.LedgerBookId is not { } ledgerBookId)
        {
            return true;
        }

        return EvidenceLinkContainsScopedLedgerBookValue(link, ledgerBookId.ToString("D")) ||
            EvidenceLinkContainsScopedLedgerBookValue(link, ledgerBookId.ToString("N"));
    }

    private static bool EvidenceLinkContainsScopedLedgerBookValue(string link, string ledgerBookValue)
    {
        var prefixes = new[]
        {
            "ledger-book:",
            "ledger-book/",
            "ledger-book=",
            "ledgerbook:",
            "ledgerbook/",
            "ledgerbook=",
            "ledgerBookId:",
            "ledgerBookId/",
            "ledgerBookId=",
            "book:",
            "book/",
            "book="
        };

        foreach (var prefix in prefixes)
        {
            var searchIndex = 0;
            while (searchIndex < link.Length)
            {
                var prefixIndex = link.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (prefixIndex < 0)
                {
                    break;
                }

                var valueIndex = prefixIndex + prefix.Length;
                if (valueIndex + ledgerBookValue.Length <= link.Length &&
                    string.Compare(
                        link,
                        valueIndex,
                        ledgerBookValue,
                        0,
                        ledgerBookValue.Length,
                        StringComparison.OrdinalIgnoreCase) == 0 &&
                    EvidenceLedgerBookValueEndsAtBoundary(link, valueIndex + ledgerBookValue.Length))
                {
                    return true;
                }

                searchIndex = valueIndex;
            }
        }

        return false;
    }

    private static bool EvidenceLedgerBookValueEndsAtBoundary(string link, int valueEndIndex)
        => EvidenceTokenBoundaryAt(link, valueEndIndex);

    private static bool EvidenceTokenBoundaryAt(string link, int index)
    {
        if (index < 0 || index >= link.Length)
        {
            return true;
        }

        return link[index] switch
        {
            ':' or '/' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' => true,
            ' ' or '\t' or '\r' or '\n' => true,
            _ => false
        };
    }

    private static DateOnly ResolveCloseDueDate(IReadOnlyList<CloseTaskDto> tasks, DateOnly fallback)
        => tasks.Count == 0 ? fallback : tasks.Max(static task => task.DueDate);

    private static (DateOnly Start, DateOnly End) ResolvePeriod(string periodId)
    {
        if (periodId.Length >= 7
            && int.TryParse(periodId[..4], out var year)
            && int.TryParse(periodId[5..7], out var month)
            && month is >= 1 and <= 12)
        {
            var start = new DateOnly(year, month, 1);
            return (start, start.AddMonths(1).AddDays(-1));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentStart = new DateOnly(today.Year, today.Month, 1);
        return (currentStart, currentStart.AddMonths(1).AddDays(-1));
    }

    private static IReadOnlyList<string> NormalizeEvidenceLinks(IEnumerable<string?> evidenceLinks)
        => evidenceLinks
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string RequireControllerRole(string? value)
    {
        var role = RequireText(value, "ControllerRole");
        if (!string.Equals(role, "Controller", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Fund Controller", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Governed close-period hard close requires Controller or Fund Controller authority.");
        }

        return role;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Sanitize(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private sealed record CloseManagementSnapshot(
        IReadOnlyList<WorkflowLateAdjustmentRecord>? LateAdjustments = null,
        IReadOnlyList<WorkflowCloseTaskSignOffRecord>? TaskSignOffs = null,
        IReadOnlyList<ClosePeriodPlanConfigurationDto>? PlanConfigurations = null,
        IReadOnlyList<WorkflowCloseEvidenceReviewRecord>? EvidenceReviews = null);

    private sealed record WorkflowLateAdjustmentRecord(
        Guid WorkflowId,
        LateAdjustmentRequestDto Adjustment);

    private sealed record WorkflowCloseTaskSignOffRecord(
        Guid WorkflowId,
        string TaskId,
        CloseSignOffDto SignOff);

    private sealed record WorkflowCloseEvidenceReviewRecord(
        Guid WorkflowId,
        CloseEvidenceReviewDto Review);
}
