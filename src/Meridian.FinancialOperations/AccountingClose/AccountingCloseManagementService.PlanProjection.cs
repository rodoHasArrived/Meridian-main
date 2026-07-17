using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingCloseManagementService
{
    private static CloseTaskDto ToCloseTask(
        OperationsCloseChecklistTaskDto task,
        int index,
        OperationsContinuityWorkflowDto workflow,
        IReadOnlyList<WorkflowCloseTaskSignOffRecord> retainedSignOffs,
        ISet<string> satisfiedTaskIds,
        CloseTaskConfigurationDto? configuration)
    {
        var owner = NormalizeOptional(configuration?.Owner) ?? task.Owner;
        var signOffRequirementConfigurations = BuildSignOffRequirementConfigurations(task, owner, configuration);
        var dependencies = BuildDependencies(task.TaskId, index, workflow, configuration);
        var signOffs = workflow.Approvals
            .Select(approval => ToCloseSignOff(task, approval))
            .Where(static signOff => signOff is not null)
            .Cast<CloseSignOffDto>()
            .Concat(retainedSignOffs
                .Where(record => string.Equals(record.TaskId, task.TaskId, StringComparison.OrdinalIgnoreCase))
                .Select(static record => record.SignOff))
            .ToArray();
        var evidenceLinks = NormalizeEvidenceLinks(
            [task.EvidencePointer, .. signOffs.SelectMany(static signOff => signOff.EvidenceLinks)]);
        var dependenciesSatisfied = dependencies.Count == 0 ||
            dependencies.All(dependency => satisfiedTaskIds.Contains(dependency.DependsOnTaskId));

        return new CloseTaskDto(
            task.TaskId,
            NormalizeOptional(configuration?.DisplayName) ?? task.Label,
            ResolveTaskStatus(task, signOffRequirementConfigurations, dependencies, dependenciesSatisfied, workflow.Approvals, signOffs),
            owner,
            configuration?.DueDate ?? task.DueDate ?? task.ExpiresOn ?? DateOnly.FromDateTime(workflow.UpdatedAtUtc.UtcDateTime),
                dependencies,
                signOffs,
                evidenceLinks,
                task.BlockingReason,
                BuildSignOffRequirements(task, signOffRequirementConfigurations, signOffs));
    }

    private static IReadOnlyList<CloseDependencyDto> BuildDependencies(
        string taskId,
        int index,
        OperationsContinuityWorkflowDto workflow,
        CloseTaskConfigurationDto? configuration)
    {
        if (configuration is not null && configuration.DependencyConfigurations.Count > 0)
        {
            return configuration.DependencyConfigurations
                .Select(dependency => new CloseDependencyDto(
                    $"dependency-{Sanitize(taskId)}-{Sanitize(dependency.DependsOnTaskId)}",
                    dependency.DependsOnTaskId,
                    string.IsNullOrWhiteSpace(dependency.Reason)
                        ? "Configured close-plan dependency."
                        : dependency.Reason.Trim()))
                .ToArray();
        }

        return index == 0
            ? []
            :
            [
                new CloseDependencyDto(
                    $"dependency-{taskId}",
                    workflow.CloseChecklist[index - 1].TaskId,
                    "Close checklist tasks must be completed in workflow order.")
            ];
    }

    private static IReadOnlyList<CloseSignOffRequirementDto> BuildSignOffRequirements(
        OperationsCloseChecklistTaskDto task,
        IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> requirements,
        IReadOnlyList<CloseSignOffDto> signOffs)
        => requirements
            .Select(requirement =>
            {
                var requiredCount = requirement.RequiredApprovalCount <= 0 ? 1 : requirement.RequiredApprovalCount;
                var approvedCount = signOffs.Count(signOff =>
                    signOff.ApprovalState == ManualJournalEntryStatusDto.Approved &&
                    string.Equals(signOff.Role, requirement.Role, StringComparison.OrdinalIgnoreCase));
                return new CloseSignOffRequirementDto(
                    $"requirement-{Sanitize(task.TaskId)}-{Sanitize(requirement.Role)}",
                    requirement.Role,
                    requiredCount,
                    approvedCount,
                    approvedCount >= requiredCount,
                    string.IsNullOrWhiteSpace(requirement.EvidenceRequirement)
                        ? "Retained close-control sign-off evidence is required."
                        : requirement.EvidenceRequirement);
            })
            .ToArray();

    private static IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> BuildSignOffRequirementConfigurations(
        OperationsCloseChecklistTaskDto task,
        string owner,
        CloseTaskConfigurationDto? configuration)
    {
        if (configuration is not null && configuration.SignOffRequirementConfigurations.Count > 0)
        {
            return configuration.SignOffRequirementConfigurations;
        }

        var requiredEvidence = NormalizeOptional(configuration?.RequiredEvidence) ?? task.RequiredEvidence;
        var requiredApprovalCount = configuration?.RequiredApprovalCount ?? task.RequiredApprovalCount;
        var requiredApprovalRole = NormalizeOptional(configuration?.RequiredApprovalRole) ?? ResolveRequiredSignOffRole(owner);
        return
        [
            new CloseTaskSignOffRequirementConfigurationDto(
                requiredApprovalRole,
                requiredApprovalCount <= 0 ? 1 : requiredApprovalCount,
                requiredEvidence)
        ];
    }

    private static IReadOnlyList<CloseCalendarMilestoneDto> BuildCloseCalendar(
        IReadOnlyList<CloseTaskDto> tasks,
        bool isPeriodLocked)
    {
        return tasks
            .OrderBy(static task => task.DueDate)
            .ThenBy(static task => task.TaskId, StringComparer.OrdinalIgnoreCase)
            .Select(task =>
            {
                var requiredSignOffCount = task.SignOffRequirements.Sum(static requirement => Math.Max(0, requirement.RequiredApprovalCount));
                var approvedSignOffCount = task.SignOffRequirements.Sum(static requirement => Math.Max(0, requirement.ApprovedCount));
                var fallbackApprovedCount = task.SignOffs.Count(static signOff => signOff.ApprovalState == ManualJournalEntryStatusDto.Approved);
                return new CloseCalendarMilestoneDto(
                    $"close-calendar-{Sanitize(task.TaskId)}",
                    task.TaskId,
                    task.DisplayName,
                    task.Owner,
                    task.DueDate,
                    task.Status,
                    task.Status == CloseTaskStatusDto.Blocked || !string.IsNullOrWhiteSpace(task.BlockerReason),
                    task.Status == CloseTaskStatusDto.SignedOff || task.SignOffRequirements.Any(static requirement => requirement.IsSatisfied),
                    isPeriodLocked,
                    task.Dependencies.Count,
                    requiredSignOffCount,
                    task.SignOffRequirements.Count > 0 ? approvedSignOffCount : fallbackApprovedCount,
                    task.EvidenceLinks,
                    task.BlockerReason);
            })
            .ToArray();
    }

    private static string ResolveRequiredSignOffRole(OperationsCloseChecklistTaskDto task)
        => ResolveRequiredSignOffRole(task.Owner);

    private static string ResolveRequiredSignOffRole(string? owner)
        => string.IsNullOrWhiteSpace(owner) ? "Controller" : owner.Trim();

    private static CloseSignOffDto? ToCloseSignOff(
        OperationsCloseChecklistTaskDto task,
        OperationsApprovalDto approval)
    {
        if (approval.Status is OperationsApprovalStateDto.Pending)
        {
            return null;
        }

        return new CloseSignOffDto(
            $"{task.TaskId}:{approval.ApprovalId}",
            ResolveRequiredSignOffRole(task),
            approval.Reviewer ?? approval.Operator,
            approval.Status == OperationsApprovalStateDto.Approved
                ? ManualJournalEntryStatusDto.Approved
                : approval.Status == OperationsApprovalStateDto.Rejected
                    ? ManualJournalEntryStatusDto.Rejected
                    : ManualJournalEntryStatusDto.Submitted,
            approval.DecidedAtUtc ?? approval.SubmittedAtUtc,
            approval.EvidenceLinks.Select(static link => link.EvidenceId).ToArray());
    }

    private static CloseTaskStatusDto ResolveTaskStatus(
        OperationsCloseChecklistTaskDto task,
        IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> requirements,
        IReadOnlyList<CloseDependencyDto> dependencies,
        bool dependenciesSatisfied,
        IReadOnlyList<OperationsApprovalDto> approvals,
        IReadOnlyList<CloseSignOffDto> signOffs)
    {
        if (!string.IsNullOrWhiteSpace(task.BlockingReason)
            || string.Equals(task.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return CloseTaskStatusDto.Blocked;
        }

        if (dependencies.Count > 0
            && !dependenciesSatisfied
            && !string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase))
        {
            return CloseTaskStatusDto.WaitingOnDependency;
        }

        if (signOffs.Any(signOff =>
                signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
                requirements.Any(requirement =>
                    string.Equals(requirement.Role, signOff.Role, StringComparison.OrdinalIgnoreCase))))
        {
            return CloseTaskStatusDto.Blocked;
        }

        var isMatrixSatisfied = requirements.All(requirement =>
            signOffs.Count(signOff =>
                signOff.ApprovalState == ManualJournalEntryStatusDto.Approved &&
                string.Equals(signOff.Role, requirement.Role, StringComparison.OrdinalIgnoreCase)) >= Math.Max(1, requirement.RequiredApprovalCount));
        var hasApprovedSignOff = signOffs.Any(static signOff =>
            signOff.ApprovalState == ManualJournalEntryStatusDto.Approved);
        if (string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase)
            || isMatrixSatisfied)
        {
            return CloseTaskStatusDto.SignedOff;
        }

        if (hasApprovedSignOff)
        {
            return CloseTaskStatusDto.InProgress;
        }

        var approvedCount = approvals.Count(static approval => approval.Status == OperationsApprovalStateDto.Approved);
        if (task.RequiredApprovalCount > 0 && approvedCount >= task.RequiredApprovalCount)
        {
            return CloseTaskStatusDto.ReadyForSignOff;
        }

        return task.AcknowledgedAtUtc is null
            ? CloseTaskStatusDto.NotStarted
            : CloseTaskStatusDto.InProgress;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildValidationIssues(
        OperationsContinuityWorkflowDto workflow,
        IReadOnlyList<CloseTaskDto> tasks,
        IReadOnlyList<LateAdjustmentRequestDto> lateAdjustments,
        MaterialityPolicyDto policy)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        foreach (var blocker in workflow.Blockers)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                $"CloseBlocker:{blocker.Code}",
                string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
                    ? AccountingConfigurationValidationSeverityDto.Critical
                    : AccountingConfigurationValidationSeverityDto.Warning,
                blocker.Message,
                blocker.Code,
                "Resolve the workflow blocker before close sign-off."));
        }

        foreach (var task in tasks.Where(static task => task.Status == CloseTaskStatusDto.Blocked))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "CloseTaskBlocked",
                AccountingConfigurationValidationSeverityDto.Critical,
                task.BlockerReason ?? $"Close task '{task.DisplayName}' is blocked.",
                task.TaskId,
                "Resolve the close checklist blocker before period lock."));
        }

        foreach (var task in tasks.Where(static task => task.SignOffs.Any(signOff =>
                     signOff.ApprovalState == ManualJournalEntryStatusDto.Rejected &&
                     task.SignOffRequirements.Any(requirement =>
                         string.Equals(requirement.Role, signOff.Role, StringComparison.OrdinalIgnoreCase)))))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "CloseTaskSignOffRejected",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close task '{task.DisplayName}' has a rejected retained sign-off decision.",
                task.TaskId,
                "Remediate the rejected close sign-off before period lock or report certification."));
        }

        foreach (var task in tasks)
        {
            foreach (var requirement in task.SignOffRequirements.Where(static requirement => !requirement.IsSatisfied))
            {
                if (HasRejectedSignOff(requirement.Role, task.SignOffs))
                {
                    continue;
                }

                issues.Add(new AccountingConfigurationValidationIssueDto(
                    "CloseTaskSignOffMissing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close task '{task.DisplayName}' needs {requirement.RequiredApprovalCount:N0} approved '{requirement.Role}' sign-off decision(s); {requirement.ApprovedCount:N0} are retained.",
                    task.TaskId,
                    "Retain the required close sign-off approvals with scoped evidence before period lock or report certification."));
            }
        }

        foreach (var task in tasks.Where(static task => task.Status == CloseTaskStatusDto.WaitingOnDependency))
        {
            var dependencyList = string.Join(", ", task.Dependencies.Select(static dependency => dependency.DependsOnTaskId));
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "CloseTaskWaitingOnDependency",
                AccountingConfigurationValidationSeverityDto.Warning,
                $"Close task '{task.DisplayName}' is waiting on predecessor task(s): {dependencyList}.",
                task.TaskId,
                "Complete and sign off predecessor close tasks before this task can advance."));
        }

        foreach (var adjustment in lateAdjustments.Where(adjustment =>
                     RequiresLateAdjustmentApproval(adjustment.Amount, policy) &&
                     IsLateAdjustmentDecisionPending(adjustment)))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "LateAdjustmentRequiresApproval",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Late adjustment '{adjustment.RequestId}' exceeds the materiality policy and requires {policy.ReviewRole} approval.",
                adjustment.RequestId,
                "Approve or reject the late adjustment before final close certification."));
        }

        return issues;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> BuildClosePeriodLockIssues(
        LockClosePeriodRequestDto request,
        OperationsContinuityWorkflowDto workflow,
        ClosePeriodPlanDto plan)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        issues.AddRange(plan.ValidationIssues.Where(static issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical));

        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePeriodLockEvidenceMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close period lock requires retained close-package, report-pack, or period-lock evidence.",
                plan.ClosePlanId,
                "Retain close-package evidence with exact workflow or period and ledger-book scope before locking the period."));
        }
        else if (!HasClosePeriodLockEvidenceWithProvenance(evidenceLinks, workflow))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePeriodLockEvidenceScopeMismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close period lock evidence must reference the workflow or exact close period and selected ledger book on the same artifact.",
                plan.ClosePlanId,
                "Retain close-package evidence with exact workflow or period and ledger-book scope before locking the period."));
        }

        if (string.IsNullOrWhiteSpace(request.ReportPackId))
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePeriodLockReportPackMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close period lock requires a linked report package id.",
                plan.ClosePlanId,
                "Assemble and certify the report package before locking the period."));
        }

        if (request.ExpectedWorkflowVersion < 0)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePeriodLockVersionInvalid",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Close period lock requires a non-negative expected workflow version.",
                plan.ClosePlanId,
                "Refresh the workflow and retry period lock with the current version."));
        }
        else if (request.ExpectedWorkflowVersion != workflow.Version)
        {
            issues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePeriodLockVersionMismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Workflow version {workflow.Version} does not match expected version {request.ExpectedWorkflowVersion}.",
                plan.ClosePlanId,
                "Refresh the close plan before posting closing entries or locking the period."));
        }

        var unique = new List<AccountingConfigurationValidationIssueDto>(issues.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            if (seen.Add($"{issue.Code}|{issue.TargetId}"))
            {
                unique.Add(issue);
            }
        }

        return unique;
    }

    private static AccountingConfigurationValidationIssueDto ToValidationIssue(OperationsWorkflowBlockerDto blocker)
        => new(
            blocker.Code,
            string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
                ? AccountingConfigurationValidationSeverityDto.Critical
                : AccountingConfigurationValidationSeverityDto.Warning,
            blocker.Message,
            blocker.Gate?.ToString(),
            "Resolve the operations workflow blocker before locking the close period.");

    private static IReadOnlyList<OperationsEvidenceLinkDto> ToOperationsEvidenceLinks(IReadOnlyList<string> evidenceLinks)
        => NormalizeEvidenceLinks(evidenceLinks)
            .Select(static link => new OperationsEvidenceLinkDto(
                link,
                "Close period lock evidence",
                link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                link.StartsWith("/", StringComparison.OrdinalIgnoreCase)
                    ? link
                    : null,
                "Accounting close management",
                DateTimeOffset.UtcNow))
            .ToArray();

    private static MaterialityPolicyDto ResolveMaterialityPolicy(OperationsContinuityWorkflowDto workflow)
        => DefaultMaterialityPolicy with
        {
            PolicyId = $"materiality-{Sanitize(workflow.PeriodId)}",
            Currency = "USD"
        };

    private static MaterialityPolicyDto NormalizeMaterialityPolicy(
        MaterialityPolicyDto? requested,
        MaterialityPolicyDto? current,
        OperationsContinuityWorkflowDto workflow)
    {
        var fallback = current ?? ResolveMaterialityPolicy(workflow);
        if (requested is null)
        {
            return fallback;
        }

        if (requested.AmountThreshold < 0m)
        {
            throw new ArgumentException("Materiality amount threshold must be zero or greater.", nameof(requested));
        }

        if (requested.PercentThreshold < 0m)
        {
            throw new ArgumentException("Materiality percent threshold must be zero or greater.", nameof(requested));
        }

        var reviewRole = RequireText(requested.ReviewRole, "MaterialityPolicy.ReviewRole");
        var currency = RequireText(requested.Currency, "MaterialityPolicy.Currency").ToUpperInvariant();
        return requested with
        {
            PolicyId = string.IsNullOrWhiteSpace(requested.PolicyId)
                ? $"materiality-{Sanitize(workflow.PeriodId)}"
                : requested.PolicyId.Trim(),
            Currency = currency,
            ReviewRole = reviewRole
        };
    }

    private static IReadOnlyList<CloseTaskConfigurationDto> NormalizeTaskConfigurations(
        IReadOnlyList<CloseTaskConfigurationDto> taskConfigurations,
        OperationsContinuityWorkflowDto workflow)
    {
        var knownTaskIds = workflow.CloseChecklist
            .Select(static task => task.TaskId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<CloseTaskConfigurationDto>(taskConfigurations.Count);
        var seenTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuration in taskConfigurations)
        {
            var taskId = RequireText(configuration.TaskId, "TaskConfiguration.TaskId");
            if (!knownTaskIds.Contains(taskId))
            {
                throw new ArgumentException($"Close task '{taskId}' was not found for workflow '{workflow.WorkflowId}'.", nameof(taskConfigurations));
            }

            if (!seenTaskIds.Add(taskId))
            {
                throw new ArgumentException($"Close task '{taskId}' has duplicate configuration rows.", nameof(taskConfigurations));
            }

            if (configuration.RequiredApprovalCount is <= 0)
            {
                throw new ArgumentException($"Close task '{taskId}' requires a positive required approval count when configured.", nameof(taskConfigurations));
            }

            var signOffRequirements = NormalizeSignOffRequirementConfigurations(configuration, nameof(taskConfigurations));
            var dependencies = NormalizeDependencyConfigurations(configuration);
            foreach (var dependency in dependencies)
            {
                var dependsOn = dependency.DependsOnTaskId;
                if (!knownTaskIds.Contains(dependsOn))
                {
                    throw new ArgumentException($"Close task '{taskId}' depends on unknown task '{dependsOn}'.", nameof(taskConfigurations));
                }

                if (string.Equals(dependsOn, taskId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Close task '{taskId}' cannot depend on itself.", nameof(taskConfigurations));
                }
            }

            normalized.Add(configuration with
            {
                TaskId = taskId,
                DisplayName = NormalizeOptional(configuration.DisplayName),
                Owner = NormalizeOptional(configuration.Owner),
                RequiredApprovalRole = NormalizeOptional(configuration.RequiredApprovalRole),
                RequiredEvidence = NormalizeOptional(configuration.RequiredEvidence),
                DependsOnTaskIds = dependencies.Select(static dependency => dependency.DependsOnTaskId).ToArray(),
                DependencyConfigurations = dependencies,
                SignOffRequirementConfigurations = signOffRequirements
            });
        }

        return normalized
            .OrderBy(static configuration => configuration.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> NormalizeSignOffRequirementConfigurations(
        CloseTaskConfigurationDto configuration,
        string paramName)
    {
        var byRole = new Dictionary<string, CloseTaskSignOffRequirementConfigurationDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in configuration.SignOffRequirementConfigurations)
        {
            var role = RequireText(requirement.Role, "SignOffRequirementConfiguration.Role");
            if (requirement.RequiredApprovalCount <= 0)
            {
                throw new ArgumentException(
                    $"Close task '{configuration.TaskId}' sign-off requirement for role '{role}' must require at least one approval.",
                    paramName);
            }

            byRole[role] = new CloseTaskSignOffRequirementConfigurationDto(
                role,
                requirement.RequiredApprovalCount,
                NormalizeOptional(requirement.EvidenceRequirement));
        }

        return byRole.Values
            .OrderBy(static requirement => requirement.Role, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CloseTaskDependencyConfigurationDto> NormalizeDependencyConfigurations(
        CloseTaskConfigurationDto configuration)
    {
        var byTaskId = new Dictionary<string, CloseTaskDependencyConfigurationDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependsOn in configuration.DependsOnTaskIds)
        {
            var dependsOnTaskId = RequireText(dependsOn, "DependsOnTaskId");
            byTaskId[dependsOnTaskId] = new CloseTaskDependencyConfigurationDto(dependsOnTaskId);
        }

        foreach (var dependency in configuration.DependencyConfigurations)
        {
            var dependsOnTaskId = RequireText(dependency.DependsOnTaskId, "DependencyConfiguration.DependsOnTaskId");
            byTaskId[dependsOnTaskId] = new CloseTaskDependencyConfigurationDto(
                dependsOnTaskId,
                NormalizeOptional(dependency.Reason));
        }

        return byTaskId.Values
            .OrderBy(static dependency => dependency.DependsOnTaskId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}
