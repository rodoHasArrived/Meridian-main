using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel
{
    private string? ValidateCloseSetupDraft(ClosePeriodPlanDto closePlan)
    {
        if (CloseSetupAmountThreshold < 0)
        {
            return "Enter a non-negative materiality amount threshold before retaining close setup.";
        }

        if (CloseSetupPercentThreshold < 0)
        {
            return "Enter a non-negative materiality percent threshold before retaining close setup.";
        }

        var currency = NormalizeOptional(CloseSetupCurrency);
        if (currency is null || currency.Length != 3 || currency.Any(static character => !char.IsLetter(character)))
        {
            return "Enter a three-letter materiality currency before retaining close setup.";
        }

        if (NormalizeOptional(CloseSetupReviewRole) is null)
        {
            return "Enter a materiality review role before retaining close setup.";
        }

        var taskId = NormalizeOptional(CloseSetupTaskId);
        if (taskId is null)
        {
            return "Select a retained close checklist task before retaining close setup.";
        }

        if (!closePlan.Tasks.Any(task => string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Close checklist task {taskId} is not loaded in this close plan.";
        }

        if (!string.IsNullOrWhiteSpace(CloseSetupTaskDueDateText) &&
            !DateOnly.TryParseExact(CloseSetupTaskDueDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return "Close task due date must use yyyy-MM-dd format before retaining close setup.";
        }

        if (CloseSetupTaskRequiredApprovalCount <= 0)
        {
            return "Enter a positive required approval count before retaining close setup.";
        }

        if (NormalizeOptional(CloseSetupTaskRequiredApprovalRole) is null)
        {
            return "Enter an approval role before retaining close setup.";
        }

        if (NormalizeOptional(CloseSetupTaskRequiredEvidence) is null)
        {
            return "Enter required sign-off evidence before retaining close setup.";
        }

        foreach (var entry in SplitCloseSetupSignOffRequirements(CloseSetupTaskSignOffRequirementsText))
        {
            var requirement = ParseCloseSetupSignOffRequirement(entry);
            if (NormalizeOptional(requirement.Role) is null)
            {
                return "Enter a role for every sign-off matrix row before retaining close setup.";
            }

            if (requirement.RequiredApprovalCount <= 0)
            {
                return $"Enter a positive approval count for {requirement.Role} before retaining close setup.";
            }
        }

        return null;
    }

    private string? ValidateCloseTaskSignOffDraft(ClosePeriodPlanDto closePlan)
    {
        var taskId = NormalizeOptional(CloseTaskSignOffTaskId);
        if (taskId is null)
        {
            return "Select a retained close checklist task before retaining sign-off evidence.";
        }

        var task = closePlan.Tasks.FirstOrDefault(task =>
            string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return $"Close checklist task {taskId} is not loaded in this close plan.";
        }

        if (task.Status is CloseTaskStatusDto.SignedOff)
        {
            return $"Close checklist task {task.TaskId} is already signed off.";
        }

        if (task.Status is CloseTaskStatusDto.Blocked or CloseTaskStatusDto.WaitingOnDependency)
        {
            return $"Close checklist task {task.TaskId} is {task.Status} and cannot be signed off yet.";
        }

        var role = NormalizeOptional(CloseTaskSignOffRole);
        if (role is null)
        {
            return "Enter a sign-off role before retaining close task sign-off evidence.";
        }

        if (task.SignOffRequirements.Count > 0 &&
            !task.SignOffRequirements.Any(requirement => string.Equals(requirement.Role, role, StringComparison.OrdinalIgnoreCase)))
        {
            return $"Close checklist task {task.TaskId} does not allow sign-off role {role}.";
        }

        if (!TryParseCloseTaskSignOffDecision(out _))
        {
            return "Select Approved or Rejected before retaining close task sign-off evidence.";
        }

        return null;
    }

    private string? ValidateLateAdjustmentDraft(ClosePeriodPlanDto closePlan)
    {
        if (!Guid.TryParse(LateAdjustmentJournalEntryIdText, out var journalEntryId) || journalEntryId == Guid.Empty)
        {
            return "Enter a journal entry id before requesting a late adjustment.";
        }

        if (!TryParseLateAdjustmentAmount(out var amount) || amount == 0m)
        {
            return "Enter a non-zero late adjustment amount before retaining the request.";
        }

        var currency = NormalizeOptional(LateAdjustmentCurrency);
        if (currency is null || currency.Length != 3 || currency.Any(static character => !char.IsLetter(character)))
        {
            return "Enter a three-letter late adjustment currency before retaining the request.";
        }

        if (NormalizeOptional(LateAdjustmentReason) is null)
        {
            return "Enter a late adjustment reason before retaining the request.";
        }

        if (closePlan.LateAdjustments.Any(adjustment =>
                adjustment.JournalEntryId == journalEntryId &&
                adjustment.ApprovalState is not ManualJournalEntryStatusDto.Rejected))
        {
            return $"Journal entry {journalEntryId:D} already has a retained late adjustment request.";
        }

        return null;
    }

    private CreateLateAdjustmentRequestDto BuildCreateLateAdjustmentRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        string actor)
    {
        var journalEntryId = Guid.Parse(LateAdjustmentJournalEntryIdText.Trim());
        var amount = ParseLateAdjustmentAmount();
        var currency = NormalizeRequired(LateAdjustmentCurrency, closePlan.MaterialityPolicy.Currency).ToUpperInvariant();
        var reason = NormalizeRequired(LateAdjustmentReason, "WPF late adjustment request.");

        return new CreateLateAdjustmentRequestDto(
            workflowId,
            journalEntryId,
            amount,
            currency,
            reason,
            actor,
            BuildLateAdjustmentRequestEvidence(workflowId, closePlan, journalEntryId),
            $"wpf-late-adjustment-request-{workflowId:D}-{journalEntryId:D}",
            OperationsActionOriginDto.HumanOperator);
    }

    private UpsertClosePeriodPlanConfigurationRequestDto BuildClosePlanConfigurationRequest(
        Guid workflowId,
        ClosePeriodPlanDto closePlan,
        string actor)
    {
        var materialityPolicy = new MaterialityPolicyDto(
            closePlan.MaterialityPolicy.PolicyId,
            CloseSetupAmountThreshold,
            CloseSetupPercentThreshold,
            NormalizeRequired(CloseSetupCurrency, closePlan.MaterialityPolicy.Currency),
            NormalizeRequired(CloseSetupReviewRole, closePlan.MaterialityPolicy.ReviewRole),
            CloseSetupRequiresLateAdjustmentApproval);
        var editableTaskId = NormalizeOptional(CloseSetupTaskId)
                             ?? closePlan.Tasks.FirstOrDefault()?.TaskId
                             ?? "close-task";
        var editableTaskDueDate = ParseCloseSetupDueDate(CloseSetupTaskDueDateText);
        var editableTaskDependencies = ParseCloseSetupDependencies(CloseSetupTaskDependsOnTaskIdsText);
        var editableTaskDependencyIdReasons = ParseCloseSetupDependencyReasonOverrides(CloseSetupTaskDependsOnTaskIdsText);
        var editableTaskDependencyReasonOverrides = ParseCloseSetupDependencyReasonOverrides(CloseSetupTaskDependencyReason);
        var editableTaskDependencyReason = editableTaskDependencyReasonOverrides.Count == 0
            ? NormalizeOptional(CloseSetupTaskDependencyReason)
            : null;
        var editableTaskSignOffRequirements = ParseCloseSetupSignOffRequirements(CloseSetupTaskSignOffRequirementsText);
        var taskConfigurations = closePlan.Tasks
            .Select(task =>
            {
                var requiredApprovalCount = Math.Max(
                    1,
                    task.SignOffRequirements.Count == 0
                        ? 1
                        : task.SignOffRequirements.Max(static requirement => requirement.RequiredApprovalCount));
                var requiredEvidence = string.Join(
                    "; ",
                    task.SignOffRequirements
                        .Select(static requirement => requirement.EvidenceRequirement.Trim())
                        .Where(static value => value.Length > 0));
                var fallbackSignOffRequirements = BuildCloseSetupSignOffRequirementConfigurations(task.SignOffRequirements);
                if (fallbackSignOffRequirements.Count == 0)
                {
                    fallbackSignOffRequirements =
                    [
                        new CloseTaskSignOffRequirementConfigurationDto(
                            task.SignOffRequirements.FirstOrDefault()?.Role ?? task.Owner,
                            requiredApprovalCount,
                            string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence)
                    ];
                }

                if (!string.Equals(task.TaskId, editableTaskId, StringComparison.OrdinalIgnoreCase))
                {
                    var primaryRequirement = fallbackSignOffRequirements[0];
                    return new CloseTaskConfigurationDto(
                        task.TaskId,
                        task.DisplayName,
                        task.Owner,
                        task.DueDate,
                        primaryRequirement.RequiredApprovalCount,
                        primaryRequirement.Role,
                        primaryRequirement.EvidenceRequirement,
                        task.Dependencies.Select(static dependency => dependency.DependsOnTaskId).ToArray(),
                        task.Dependencies.Select(static dependency => new CloseTaskDependencyConfigurationDto(
                            dependency.DependsOnTaskId,
                            dependency.Reason)).ToArray(),
                        fallbackSignOffRequirements);
                }

                var editableFallbackRequirement = new CloseTaskSignOffRequirementConfigurationDto(
                    NormalizeOptional(CloseSetupTaskRequiredApprovalRole)
                        ?? task.SignOffRequirements.FirstOrDefault()?.Role
                        ?? task.Owner,
                    Math.Max(1, CloseSetupTaskRequiredApprovalCount),
                    NormalizeOptional(CloseSetupTaskRequiredEvidence)
                        ?? (string.IsNullOrWhiteSpace(requiredEvidence) ? "Retained close checklist evidence" : requiredEvidence));
                var editableSignOffRequirements = editableTaskSignOffRequirements.Count == 0
                    ? [editableFallbackRequirement]
                    : editableTaskSignOffRequirements;
                var editablePrimaryRequirement = editableSignOffRequirements[0];
                return new CloseTaskConfigurationDto(
                    task.TaskId,
                    NormalizeOptional(CloseSetupTaskDisplayName) ?? task.DisplayName,
                    NormalizeOptional(CloseSetupTaskOwner) ?? task.Owner,
                    editableTaskDueDate ?? task.DueDate,
                    editablePrimaryRequirement.RequiredApprovalCount,
                    editablePrimaryRequirement.Role,
                    editablePrimaryRequirement.EvidenceRequirement,
                    editableTaskDependencies,
                    BuildCloseSetupDependencyConfigurations(
                        editableTaskDependencies,
                        editableTaskDependencyIdReasons,
                        editableTaskDependencyReasonOverrides,
                        editableTaskDependencyReason,
                        task.Dependencies),
                    editableSignOffRequirements);
            })
            .ToArray();

        return new UpsertClosePeriodPlanConfigurationRequestDto(
            workflowId,
            materialityPolicy,
            taskConfigurations,
            Actor: actor,
            EvidenceLinks: BuildClosePlanConfigurationEvidence(workflowId, closePlan),
            CorrelationId: $"wpf-close-plan-configuration-{workflowId:D}",
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            ExpectedConfiguredAtUtc: closePlan.Configuration?.ConfiguredAtUtc);
    }

    private void ApplyCloseSetupDraft(ClosePeriodPlanDto closePlan)
    {
        var materiality = closePlan.MaterialityPolicy;
        CloseSetupAmountThreshold = materiality.AmountThreshold;
        CloseSetupPercentThreshold = materiality.PercentThreshold;
        CloseSetupCurrency = materiality.Currency;
        CloseSetupReviewRole = materiality.ReviewRole;
        CloseSetupRequiresLateAdjustmentApproval = materiality.RequiresLateAdjustmentApproval;

        CloseSetupTaskOptions.Clear();
        foreach (var task in closePlan.Tasks)
        {
            CloseSetupTaskOptions.Add(BuildCloseSetupTaskOption(task));
        }

        var firstTask = closePlan.Tasks.FirstOrDefault();
        var firstTaskId = firstTask?.TaskId ?? string.Empty;
        if (string.Equals(SelectedCloseSetupTaskId, firstTaskId, StringComparison.Ordinal))
        {
            ApplyCloseSetupTaskDraft(firstTask);
        }
        else
        {
            SelectedCloseSetupTaskId = firstTaskId;
        }
    }

    private void ApplyCloseSetupTaskDraft(CloseTaskDto? task)
    {
        if (task is null)
        {
            CloseSetupTaskId = string.Empty;
            CloseSetupTaskDisplayName = string.Empty;
            CloseSetupTaskOwner = string.Empty;
            CloseSetupTaskDueDateText = string.Empty;
            CloseSetupTaskRequiredApprovalCount = 1;
            CloseSetupTaskRequiredApprovalRole = "Controller";
            CloseSetupTaskRequiredEvidence = "Retained close checklist evidence";
            CloseSetupTaskSignOffRequirementsText = string.Empty;
            CloseSetupTaskDependsOnTaskIdsText = string.Empty;
            CloseSetupTaskDependencyReason = "Configured close-plan dependency.";
            return;
        }

        var requiredApprovalCount = Math.Max(
            1,
            task.SignOffRequirements.Count == 0
                ? 1
                : task.SignOffRequirements.Max(static requirement => requirement.RequiredApprovalCount));
        var requiredEvidence = string.Join(
            "; ",
            task.SignOffRequirements
                .Select(static requirement => requirement.EvidenceRequirement.Trim())
                .Where(static value => value.Length > 0));

        CloseSetupTaskId = task.TaskId;
        CloseSetupTaskDisplayName = task.DisplayName;
        CloseSetupTaskOwner = task.Owner;
        CloseSetupTaskDueDateText = task.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        CloseSetupTaskRequiredApprovalCount = requiredApprovalCount;
        CloseSetupTaskRequiredApprovalRole = task.SignOffRequirements.FirstOrDefault()?.Role ?? task.Owner;
        CloseSetupTaskRequiredEvidence = string.IsNullOrWhiteSpace(requiredEvidence)
            ? "Retained close checklist evidence"
            : requiredEvidence;
        CloseSetupTaskSignOffRequirementsText = BuildCloseSetupSignOffRequirementText(task.SignOffRequirements);
        CloseSetupTaskDependsOnTaskIdsText = string.Join(", ", task.Dependencies.Select(static dependency => dependency.DependsOnTaskId));
        CloseSetupTaskDependencyReason = BuildCloseSetupDependencyReason(task.Dependencies);
    }

    private static CloseSetupTaskOption BuildCloseSetupTaskOption(CloseTaskDto task)
    {
        var signOffSummary = task.SignOffRequirements.Count == 0
            ? "No sign-off requirement"
            : string.Join(
                "; ",
                task.SignOffRequirements.Select(static requirement =>
                    $"{requirement.Role}: {requirement.ApprovedCount}/{requirement.RequiredApprovalCount}"));

        return new CloseSetupTaskOption(
            task.TaskId,
            string.IsNullOrWhiteSpace(task.DisplayName) ? task.TaskId : task.DisplayName,
            task.Status.ToString(),
            task.Owner,
            task.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            signOffSummary);
    }

    private CloseTaskDto? ApplyCloseTaskSignOffDraft(ClosePeriodPlanDto closePlan)
    {
        var task = ResolveNextSignOffTask(closePlan);
        if (task is null)
        {
            CloseTaskSignOffTaskId = string.Empty;
            CloseTaskSignOffRole = string.Empty;
            CloseTaskSignOffDecision = ManualJournalEntryStatusDto.Approved.ToString();
            CloseTaskSignOffNotes = string.Empty;
            return null;
        }

        var requirement = task.SignOffRequirements.FirstOrDefault(static row => !row.IsSatisfied)
                          ?? task.SignOffRequirements.FirstOrDefault();
        CloseTaskSignOffTaskId = task.TaskId;
        CloseTaskSignOffRole = string.IsNullOrWhiteSpace(requirement?.Role) ? task.Owner : requirement!.Role.Trim();
        CloseTaskSignOffDecision = ManualJournalEntryStatusDto.Approved.ToString();
        CloseTaskSignOffNotes = $"WPF Accounting Close retained {CloseTaskSignOffRole} sign-off evidence for {task.DisplayName}.";
        return task;
    }

    private LateAdjustmentRequestDto? ApplyLateAdjustmentReviewDraft(ClosePeriodPlanDto closePlan)
    {
        var adjustment = ResolveNextLateAdjustment(closePlan);
        if (adjustment is null)
        {
            LateAdjustmentReviewRequestId = string.Empty;
            LateAdjustmentReviewDecision = ManualJournalEntryStatusDto.Approved.ToString();
            LateAdjustmentReviewNotes = string.Empty;
            return null;
        }

        LateAdjustmentReviewRequestId = adjustment.RequestId;
        LateAdjustmentReviewDecision = ManualJournalEntryStatusDto.Approved.ToString();
        LateAdjustmentReviewNotes = $"WPF Accounting Close approved late adjustment {adjustment.RequestId}.";
        return adjustment;
    }

    private AccountingConfigurationValidationIssueDto? ApplyCloseEvidenceReviewDraft(ClosePeriodPlanDto closePlan)
    {
        var issue = ResolveNextCloseEvidenceReviewIssue(closePlan);
        if (issue is null)
        {
            CloseEvidenceReviewIssueCode = string.Empty;
            CloseEvidenceReviewTargetId = string.Empty;
            CloseEvidenceReviewNotes = string.Empty;
            return null;
        }

        CloseEvidenceReviewIssueCode = issue.Code;
        CloseEvidenceReviewTargetId = issue.TargetId ?? string.Empty;
        CloseEvidenceReviewNotes = $"WPF Accounting Close reviewed blocker {issue.Code} for {NormalizeOptional(issue.TargetId) ?? closePlan.ClosePlanId}. {issue.Message}";
        return issue;
    }

}
