using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.AccountingClose;

public interface IAccountingCloseManagementService
{
    Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
        CreateLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
        ReviewLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
        SignOffCloseTaskRequestDto request,
        string actor,
        CancellationToken ct = default);
}

public sealed class AccountingCloseManagementService : IAccountingCloseManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly MaterialityPolicyDto DefaultMaterialityPolicy = new(
        "default-close-materiality",
        AmountThreshold: 10_000m,
        PercentThreshold: 0.01m,
        Currency: "USD",
        ReviewRole: "Controller",
        RequiresLateAdjustmentApproval: true);

    private readonly IOperationsContinuityWorkflowService _workflowService;
    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private readonly ConcurrentDictionary<Guid, List<LateAdjustmentRequestDto>> _lateAdjustments = new();
    private readonly ConcurrentDictionary<Guid, List<WorkflowCloseTaskSignOffRecord>> _taskSignOffs = new();

    public AccountingCloseManagementService(IOperationsContinuityWorkflowService workflowService)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
    }

    public AccountingCloseManagementService(
        IOperationsContinuityWorkflowService workflowService,
        StorageOptions storageOptions)
        : this(workflowService)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "accounting", "close-management-late-adjustments.json");
    }

    public async Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default)
    {
        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(workflowId));
        }

        var workflow = await _workflowService.GetAsync(workflowId, ct).ConfigureAwait(false);
        return workflow is null ? null : BuildPeriodPlan(workflow);
    }

    public async Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
        CreateLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "request late adjustments");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        if (request.JournalEntryId == Guid.Empty)
        {
            throw new ArgumentException("JournalEntryId is required.", nameof(request));
        }

        if (request.Amount == 0m)
        {
            throw new ArgumentException("Amount must be non-zero.", nameof(request));
        }

        var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null)
        {
            throw new InvalidOperationException(
                $"Cannot request a late adjustment for period '{workflow.PeriodId}' because the close period is locked by close package '{workflow.ClosePackage.ClosePackageId}'.");
        }

        var requestEvidence = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (requestEvidence.Count == 0)
        {
            throw new ArgumentException("At least one evidence link is required for late adjustment request.", nameof(request));
        }

        if (!HasLateAdjustmentRequestEvidence(requestEvidence))
        {
            throw new ArgumentException("Late adjustment request requires retained late-adjustment evidence.", nameof(request));
        }

        if (!HasLateAdjustmentRequestEvidenceWithProvenance(requestEvidence, request.JournalEntryId, workflow))
        {
            throw new ArgumentException("Late adjustment request evidence must reference the journal entry, workflow, or exact close period and selected ledger book on the same artifact.", nameof(request));
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.RequestedBy, "RequestedBy") : actor.Trim();
        var policy = ResolveMaterialityPolicy(workflow);
        var approvalState = RequiresLateAdjustmentApproval(request.Amount, policy)
            ? ManualJournalEntryStatusDto.Submitted
            : ManualJournalEntryStatusDto.Approved;
        var adjustment = new LateAdjustmentRequestDto(
            $"late-adjustment-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..55],
            request.JournalEntryId,
            resolvedActor,
            DateTimeOffset.UtcNow,
            request.Amount,
            string.IsNullOrWhiteSpace(request.Currency) ? policy.Currency : request.Currency.Trim().ToUpperInvariant(),
            RequireText(request.Reason, "Reason"),
            approvalState,
            policy,
            requestEvidence);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadLateAdjustments().ToList();
            if (rows.Any(row =>
                    row.WorkflowId == request.WorkflowId &&
                    row.Adjustment.JournalEntryId == request.JournalEntryId &&
                    IsLateAdjustmentRequestRetained(row.Adjustment)))
            {
                throw new InvalidOperationException(
                    $"A retained late adjustment request already exists for journal entry '{request.JournalEntryId}' in close workflow '{request.WorkflowId}'.");
            }

            rows.Add(new WorkflowLateAdjustmentRecord(request.WorkflowId, adjustment));
            await SaveCloseManagementAsync(rows, ReadTaskSignOffs(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return BuildPeriodPlan(workflow);
    }

    public async Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
        ReviewLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "review late adjustments");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        var requestId = RequireText(request.RequestId, "RequestId");
        if (request.Decision is not ManualJournalEntryStatusDto.Approved and not ManualJournalEntryStatusDto.Rejected)
        {
            throw new ArgumentException("Decision must be Approved or Rejected.", nameof(request));
        }

        var decisionNotes = RequireText(request.Notes, "Notes");
        var reviewEvidence = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (reviewEvidence.Count == 0)
        {
            throw new ArgumentException("At least one evidence link is required for late adjustment review.", nameof(request));
        }

        if (!HasLateAdjustmentReviewEvidence(reviewEvidence))
        {
            throw new ArgumentException("Late adjustment review requires retained approval, rejection, decision, or review evidence.", nameof(request));
        }

        var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null)
        {
            throw new InvalidOperationException(
                $"Cannot review late adjustment '{requestId}' for period '{workflow.PeriodId}' because the close period is locked by close package '{workflow.ClosePackage.ClosePackageId}'.");
        }

        LateAdjustmentRequestDto current;
        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.Actor, "Actor") : actor.Trim();
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadLateAdjustments().ToList();
            var index = rows.FindIndex(row =>
                row.WorkflowId == request.WorkflowId &&
                string.Equals(row.Adjustment.RequestId, requestId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Late adjustment '{requestId}' was not found for workflow '{request.WorkflowId}'.");
            }

            current = rows[index].Adjustment;
            if (!HasLateAdjustmentReviewEvidenceWithProvenance(reviewEvidence, requestId, current, workflow))
            {
                throw new ArgumentException("Late adjustment review evidence must reference the request, journal entry, workflow, or exact close period and selected ledger book on the same artifact.", nameof(request));
            }

            if (current.ApprovalState is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected)
            {
                throw new InvalidOperationException($"Late adjustment '{requestId}' has already been {current.ApprovalState}.");
            }

            var updated = current with
            {
                ApprovalState = request.Decision,
                DecidedBy = resolvedActor,
                DecidedAtUtc = DateTimeOffset.UtcNow,
                DecisionNotes = decisionNotes,
                EvidenceLinks = NormalizeEvidenceLinks([.. current.EvidenceLinks, .. reviewEvidence])
            };
            rows[index] = rows[index] with { Adjustment = updated };
            await SaveCloseManagementAsync(rows, ReadTaskSignOffs(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return BuildPeriodPlan(workflow);
    }

    public async Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
        SignOffCloseTaskRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "sign off close tasks");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        var taskId = RequireText(request.TaskId, "TaskId");
        var role = RequireText(request.Role, "Role");
        if (request.Decision is not ManualJournalEntryStatusDto.Approved and not ManualJournalEntryStatusDto.Rejected)
        {
            throw new ArgumentException("Decision must be Approved or Rejected.", nameof(request));
        }

        var notes = RequireText(request.Notes, "Notes");
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("At least one evidence link is required for close task sign-off.", nameof(request));
        }

        if (!HasCloseTaskSignOffEvidence(evidenceLinks))
        {
            throw new ArgumentException("Close task sign-off requires retained approval, sign-off, control, or review evidence.", nameof(request));
        }

        var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        if (!HasCloseTaskSignOffEvidenceWithProvenance(evidenceLinks, taskId, role, workflow))
        {
            throw new ArgumentException("Close task sign-off evidence must reference the close task, sign-off role, workflow or exact close period, and selected ledger book on the same artifact.", nameof(request));
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null)
        {
            throw new InvalidOperationException(
                $"Cannot sign off close task '{taskId}' for period '{workflow.PeriodId}' because the close period is locked by close package '{workflow.ClosePackage.ClosePackageId}'.");
        }

        var checklistTask = workflow.CloseChecklist.FirstOrDefault(task =>
            string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (checklistTask is null)
        {
            throw new InvalidOperationException($"Close task '{taskId}' was not found for workflow '{request.WorkflowId}'.");
        }

        if (!string.IsNullOrWhiteSpace(checklistTask.BlockingReason)
            || string.Equals(checklistTask.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Close task '{taskId}' is blocked and cannot be signed off.");
        }

        var currentPlan = BuildPeriodPlan(workflow);
        var currentTask = currentPlan.Tasks.FirstOrDefault(task =>
            string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
        if (currentTask is null)
        {
            throw new InvalidOperationException($"Close task '{taskId}' was not found for workflow '{request.WorkflowId}'.");
        }

        var matchingRequirement = currentTask.SignOffRequirements.FirstOrDefault(requirement =>
            string.Equals(requirement.Role, role, StringComparison.OrdinalIgnoreCase));
        if (currentTask.SignOffRequirements.Count > 0 && matchingRequirement is null)
        {
            throw new InvalidOperationException(
                $"Close task '{taskId}' does not allow sign-off role '{role}'. Required role(s): {string.Join(", ", currentTask.SignOffRequirements.Select(static requirement => requirement.Role))}.");
        }

        var blockedDependencies = currentTask.Dependencies
            .Select(dependency => currentPlan.Tasks.FirstOrDefault(task =>
                string.Equals(task.TaskId, dependency.DependsOnTaskId, StringComparison.OrdinalIgnoreCase)))
            .Where(static dependencyTask => dependencyTask is null || dependencyTask.Status != CloseTaskStatusDto.SignedOff)
            .Select(static dependencyTask => dependencyTask?.TaskId ?? "unknown")
            .ToArray();
        if (blockedDependencies.Length > 0)
        {
            throw new InvalidOperationException(
                $"Close task '{taskId}' cannot be signed off until dependency task(s) '{string.Join(", ", blockedDependencies)}' are signed off.");
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.Actor, "Actor") : actor.Trim();
        var signOff = new CloseSignOffDto(
            $"signoff-{Sanitize(taskId)}-{Sanitize(role)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            role,
            resolvedActor,
            request.Decision,
            DateTimeOffset.UtcNow,
            evidenceLinks,
            notes);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadTaskSignOffs().ToList();
            var existingRoleRows = rows
                .Where(row =>
                    row.WorkflowId == request.WorkflowId &&
                    string.Equals(row.TaskId, taskId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.SignOff.Role, role, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (existingRoleRows.Any(row =>
                    string.Equals(row.SignOff.Actor, resolvedActor, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Close task '{taskId}' already has a retained sign-off decision for role '{role}' by actor '{resolvedActor}'.");
            }

            var requiredApprovalCount = matchingRequirement?.RequiredApprovalCount ?? 1;
            var approvedRoleRows = existingRoleRows
                .Count(static row => row.SignOff.ApprovalState == ManualJournalEntryStatusDto.Approved);
            if (request.Decision == ManualJournalEntryStatusDto.Approved && approvedRoleRows >= requiredApprovalCount)
            {
                throw new InvalidOperationException(
                    $"Close task '{taskId}' already has {requiredApprovalCount} approved sign-off decision(s) for role '{role}'.");
            }

            rows.Add(new WorkflowCloseTaskSignOffRecord(request.WorkflowId, taskId, signOff));
            await SaveCloseManagementAsync(ReadLateAdjustments(), rows, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return BuildPeriodPlan(workflow);
    }

    private ClosePeriodPlanDto BuildPeriodPlan(OperationsContinuityWorkflowDto workflow)
    {
        var period = ResolvePeriod(workflow.PeriodId);
        var materialityPolicy = ResolveMaterialityPolicy(workflow);
        var retainedSignOffs = GetTaskSignOffs(workflow.WorkflowId);
        var satisfiedTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<CloseTaskDto>(workflow.CloseChecklist.Count);
        for (var index = 0; index < workflow.CloseChecklist.Count; index++)
        {
            var task = ToCloseTask(workflow.CloseChecklist[index], index, workflow, retainedSignOffs, satisfiedTaskIds);
            tasks.Add(task);
            if (task.Status == CloseTaskStatusDto.SignedOff)
            {
                satisfiedTaskIds.Add(task.TaskId);
            }
        }

        var lateAdjustments = GetLateAdjustments(workflow.WorkflowId);
        var validationIssues = BuildValidationIssues(workflow, tasks, lateAdjustments, materialityPolicy);
        var isPeriodLocked = workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null;

        return new ClosePeriodPlanDto(
            $"close-plan-{workflow.WorkflowId:D}",
            workflow.FundAccountId.ToString("D"),
            workflow.LedgerBookId,
            workflow.PeriodId,
            period.Start,
            period.End,
            ResolveCloseDueDate(tasks, period.End),
            IsPeriodLocked: isPeriodLocked,
            tasks,
            lateAdjustments,
            materialityPolicy,
            validationIssues,
            BuildCloseCalendar(tasks, isPeriodLocked));
    }

    private IReadOnlyList<LateAdjustmentRequestDto> GetLateAdjustments(Guid workflowId)
    {
        return ReadLateAdjustments()
            .Where(record => record.WorkflowId == workflowId)
            .Select(static record => record.Adjustment)
            .OrderBy(static row => row.RequestedAtUtc)
            .ToArray();
    }

    private IReadOnlyList<WorkflowLateAdjustmentRecord> ReadLateAdjustments()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            return ReadInMemoryLateAdjustments();
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<CloseManagementSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.LateAdjustments ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private async Task SaveCloseManagementAsync(
        IReadOnlyList<WorkflowLateAdjustmentRecord> rows,
        IReadOnlyList<WorkflowCloseTaskSignOffRecord> taskSignOffRows,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            lock (_readGate)
            {
                _lateAdjustments.Clear();
                foreach (var group in rows.GroupBy(static row => row.WorkflowId))
                {
                    _lateAdjustments[group.Key] = group.Select(static row => row.Adjustment).ToList();
                }

                _taskSignOffs.Clear();
                foreach (var group in taskSignOffRows.GroupBy(static row => row.WorkflowId))
                {
                    _taskSignOffs[group.Key] = group.ToList();
                }
            }
            return;
        }

        var snapshot = new CloseManagementSnapshot(
            rows
                .OrderBy(static row => row.WorkflowId)
                .ThenBy(static row => row.Adjustment.RequestedAtUtc)
                .ToArray(),
            taskSignOffRows
                .OrderBy(static row => row.WorkflowId)
                .ThenBy(static row => row.TaskId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.SignOff.SignedAtUtc)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
    }

    private IReadOnlyList<WorkflowLateAdjustmentRecord> ReadInMemoryLateAdjustments()
    {
        lock (_readGate)
        {
            return _lateAdjustments
                .SelectMany(static pair => pair.Value.Select(adjustment => new WorkflowLateAdjustmentRecord(pair.Key, adjustment)))
                .ToArray();
        }
    }

    private IReadOnlyList<WorkflowCloseTaskSignOffRecord> GetTaskSignOffs(Guid workflowId)
        => ReadTaskSignOffs()
            .Where(record => record.WorkflowId == workflowId)
            .OrderBy(static record => record.SignOff.SignedAtUtc)
            .ToArray();

    private IReadOnlyList<WorkflowCloseTaskSignOffRecord> ReadTaskSignOffs()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            return ReadInMemoryTaskSignOffs();
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<CloseManagementSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.TaskSignOffs ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private IReadOnlyList<WorkflowCloseTaskSignOffRecord> ReadInMemoryTaskSignOffs()
    {
        lock (_readGate)
        {
            return _taskSignOffs
                .SelectMany(static pair => pair.Value)
                .ToArray();
        }
    }

    private static CloseTaskDto ToCloseTask(
        OperationsCloseChecklistTaskDto task,
        int index,
        OperationsContinuityWorkflowDto workflow,
        IReadOnlyList<WorkflowCloseTaskSignOffRecord> retainedSignOffs,
        ISet<string> satisfiedTaskIds)
    {
        CloseDependencyDto[] dependencies = index == 0
            ? []
            :
            [
                new CloseDependencyDto(
                    $"dependency-{task.TaskId}",
                    workflow.CloseChecklist[index - 1].TaskId,
                    "Close checklist tasks must be completed in workflow order.")
            ];
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
        var dependenciesSatisfied = dependencies.Length == 0 ||
            dependencies.All(dependency => satisfiedTaskIds.Contains(dependency.DependsOnTaskId));

        return new CloseTaskDto(
            task.TaskId,
            task.Label,
            ResolveTaskStatus(task, dependencies, dependenciesSatisfied, workflow.Approvals, signOffs),
            task.Owner,
            task.DueDate ?? task.ExpiresOn ?? DateOnly.FromDateTime(workflow.UpdatedAtUtc.UtcDateTime),
                dependencies,
                signOffs,
                evidenceLinks,
                task.BlockingReason,
                BuildSignOffRequirements(task, signOffs));
    }

    private static IReadOnlyList<CloseSignOffRequirementDto> BuildSignOffRequirements(
        OperationsCloseChecklistTaskDto task,
        IReadOnlyList<CloseSignOffDto> signOffs)
    {
        var role = ResolveRequiredSignOffRole(task);
        var requiredCount = task.RequiredApprovalCount <= 0 ? 1 : task.RequiredApprovalCount;
        var approvedCount = signOffs.Count(signOff =>
            signOff.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            string.Equals(signOff.Role, role, StringComparison.OrdinalIgnoreCase));
        return
        [
            new CloseSignOffRequirementDto(
                $"requirement-{Sanitize(task.TaskId)}-{Sanitize(role)}",
                role,
                requiredCount,
                approvedCount,
                approvedCount >= requiredCount,
                string.IsNullOrWhiteSpace(task.RequiredEvidence)
                    ? "Retained close-control sign-off evidence is required."
                    : task.RequiredEvidence)
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
        => string.IsNullOrWhiteSpace(task.Owner) ? "Controller" : task.Owner.Trim();

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

        var requiredRole = ResolveRequiredSignOffRole(task);
        var approvedSignOffCount = signOffs.Count(signOff =>
            signOff.ApprovalState == ManualJournalEntryStatusDto.Approved &&
            string.Equals(signOff.Role, requiredRole, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase)
            || (approvedSignOffCount > 0 && (task.RequiredApprovalCount == 0 || approvedSignOffCount >= task.RequiredApprovalCount)))
        {
            return CloseTaskStatusDto.SignedOff;
        }

        if (approvedSignOffCount > 0)
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
                AccountingConfigurationValidationSeverityDto.Warning,
                $"Late adjustment '{adjustment.RequestId}' exceeds the materiality policy and requires {policy.ReviewRole} approval.",
                adjustment.RequestId,
                "Approve or reject the late adjustment before final close certification."));
        }

        return issues;
    }

    private static MaterialityPolicyDto ResolveMaterialityPolicy(OperationsContinuityWorkflowDto workflow)
        => DefaultMaterialityPolicy with
        {
            PolicyId = $"materiality-{Sanitize(workflow.PeriodId)}",
            Currency = "USD"
        };

    private static bool RequiresLateAdjustmentApproval(decimal amount, MaterialityPolicyDto policy)
        => policy.RequiresLateAdjustmentApproval && Math.Abs(amount) >= policy.AmountThreshold;

    private static bool IsLateAdjustmentDecisionPending(LateAdjustmentRequestDto adjustment)
        => adjustment.ApprovalState is not ManualJournalEntryStatusDto.Approved
            and not ManualJournalEntryStatusDto.Rejected;

    private static bool IsLateAdjustmentRequestRetained(LateAdjustmentRequestDto adjustment)
        => adjustment.ApprovalState is not ManualJournalEntryStatusDto.Rejected;

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException($"Reviewed automation cannot {action}; a human operator must perform this accounting close action.");
        }
    }

    private static bool HasCloseTaskSignOffEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("control", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasCloseTaskSignOffEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string taskId,
        string role,
        OperationsContinuityWorkflowDto workflow)
        => evidenceLinks.Any(link =>
            HasCloseTaskSignOffEvidence([link]) &&
            EvidenceLinkContainsIdentifierToken(link, taskId) &&
            EvidenceLinkContainsRoleToken(link, role) &&
            (EvidenceLinkContainsGuidToken(link, workflow.WorkflowId) ||
             EvidenceLinkContainsIdentifierToken(link, workflow.PeriodId)) &&
            EvidenceLinkContainsLedgerBook(link, workflow));

    private static bool HasLateAdjustmentRequestEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("late-adjustment", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("late adjustment", StringComparison.OrdinalIgnoreCase));

    private static bool HasLateAdjustmentRequestEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        Guid journalEntryId,
        OperationsContinuityWorkflowDto workflow)
        => evidenceLinks.Any(link =>
            HasLateAdjustmentRequestEvidence([link]) &&
            (EvidenceLinkContainsGuidToken(link, journalEntryId) ||
             EvidenceLinkContainsGuidToken(link, workflow.WorkflowId) ||
             EvidenceLinkContainsIdentifierToken(link, workflow.PeriodId)) &&
            EvidenceLinkContainsLedgerBook(link, workflow));

    private static bool HasLateAdjustmentReviewEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rejection", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("decision", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasLateAdjustmentReviewEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string requestId,
        LateAdjustmentRequestDto adjustment,
        OperationsContinuityWorkflowDto workflow)
        => evidenceLinks.Any(link =>
            HasLateAdjustmentReviewEvidence([link]) &&
            (EvidenceLinkContainsIdentifierToken(link, requestId) ||
             EvidenceLinkContainsGuidToken(link, adjustment.JournalEntryId) ||
             EvidenceLinkContainsGuidToken(link, workflow.WorkflowId) ||
             EvidenceLinkContainsIdentifierToken(link, workflow.PeriodId)) &&
            EvidenceLinkContainsLedgerBook(link, workflow));

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

    private static string Sanitize(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private sealed record CloseManagementSnapshot(
        IReadOnlyList<WorkflowLateAdjustmentRecord>? LateAdjustments = null,
        IReadOnlyList<WorkflowCloseTaskSignOffRecord>? TaskSignOffs = null);

    private sealed record WorkflowLateAdjustmentRecord(
        Guid WorkflowId,
        LateAdjustmentRequestDto Adjustment);

    private sealed record WorkflowCloseTaskSignOffRecord(
        Guid WorkflowId,
        string TaskId,
        CloseSignOffDto SignOff);
}
