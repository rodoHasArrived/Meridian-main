using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public interface IAccountingCloseManagementService
{
    Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> GetPeriodPlanScopedAsync(
        Guid workflowId,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => GetPeriodPlanAsync(workflowId, ct);

    Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
        CreateLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> RequestLateAdjustmentScopedAsync(
        CreateLateAdjustmentRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => RequestLateAdjustmentAsync(request, actor, ct);

    Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
        ReviewLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> ReviewLateAdjustmentScopedAsync(
        ReviewLateAdjustmentRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => ReviewLateAdjustmentAsync(request, actor, ct);

    Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
        SignOffCloseTaskRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> SignOffCloseTaskScopedAsync(
        SignOffCloseTaskRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => SignOffCloseTaskAsync(request, actor, ct);

    Task<ClosePeriodPlanDto?> ReviewCloseEvidenceAsync(
        ReviewCloseEvidenceRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> ReviewCloseEvidenceScopedAsync(
        ReviewCloseEvidenceRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => ReviewCloseEvidenceAsync(request, actor, ct);

    Task<ClosePeriodPlanDto?> ConfigurePeriodPlanAsync(
        UpsertClosePeriodPlanConfigurationRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodPlanDto?> ConfigurePeriodPlanScopedAsync(
        UpsertClosePeriodPlanConfigurationRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => ConfigurePeriodPlanAsync(request, actor, ct);

    Task<ClosePeriodLockResultDto?> LockClosePeriodAsync(
        LockClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default);

    Task<ClosePeriodLockResultDto?> LockClosePeriodScopedAsync(
        LockClosePeriodRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => LockClosePeriodAsync(request, actor, ct);

    Task<ClosePeriodReopenResultDto?> ReopenClosePeriodAsync(
        ReopenClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default)
        => Task.FromException<ClosePeriodReopenResultDto?>(
            new NotSupportedException("This accounting close service does not support governed period reopen."));

    Task<ClosePeriodReopenResultDto?> ReopenClosePeriodScopedAsync(
        ReopenClosePeriodRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
        => ReopenClosePeriodAsync(request, actor, ct);
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
    private readonly IAccountingClosePostingWorkbench? _postingWorkbench;
    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private readonly ConcurrentDictionary<Guid, List<LateAdjustmentRequestDto>> _lateAdjustments = new();
    private readonly ConcurrentDictionary<Guid, List<WorkflowCloseTaskSignOffRecord>> _taskSignOffs = new();
    private readonly ConcurrentDictionary<Guid, ClosePeriodPlanConfigurationDto> _planConfigurations = new();
    private readonly ConcurrentDictionary<Guid, List<WorkflowCloseEvidenceReviewRecord>> _evidenceReviews = new();

    public AccountingCloseManagementService(IOperationsContinuityWorkflowService workflowService)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
    }

    public AccountingCloseManagementService(
        IOperationsContinuityWorkflowService workflowService,
        IAccountingClosePostingWorkbench postingWorkbench)
        : this(workflowService)
    {
        _postingWorkbench = postingWorkbench ?? throw new ArgumentNullException(nameof(postingWorkbench));
    }

    public AccountingCloseManagementService(
        IOperationsContinuityWorkflowService workflowService,
        StorageOptions storageOptions)
        : this(workflowService)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "accounting", "close-management-late-adjustments.json");
    }

    public AccountingCloseManagementService(
        IOperationsContinuityWorkflowService workflowService,
        StorageOptions storageOptions,
        IAccountingClosePostingWorkbench postingWorkbench)
        : this(workflowService, storageOptions)
    {
        _postingWorkbench = postingWorkbench ?? throw new ArgumentNullException(nameof(postingWorkbench));
    }

    public Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(
        Guid workflowId,
        CancellationToken ct = default)
        => GetPeriodPlanScopedAsync(workflowId, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodPlanDto?> GetPeriodPlanScopedAsync(
        Guid workflowId,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(workflowId));
        }

        var workflow = await _workflowService.GetAsync(workflowId, ct).ConfigureAwait(false);
        return workflow is null
            ? null
            : await BuildPeriodPlanWithGateAsync(workflow, ct, tenantId, companyId).ConfigureAwait(false);
    }

    public Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
        CreateLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default)
        => RequestLateAdjustmentScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodPlanDto?> RequestLateAdjustmentScopedAsync(
        CreateLateAdjustmentRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
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
            await SaveCloseManagementAsync(rows, ReadTaskSignOffs(), ReadPlanConfigurations(), ReadEvidenceReviews(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return await BuildPeriodPlanWithGateAsync(workflow, ct, tenantId, companyId).ConfigureAwait(false);
    }

    public Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
        ReviewLateAdjustmentRequestDto request,
        string actor,
        CancellationToken ct = default)
        => ReviewLateAdjustmentScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodPlanDto?> ReviewLateAdjustmentScopedAsync(
        ReviewLateAdjustmentRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
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

            if (string.Equals(current.RequestedBy, resolvedActor, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Late adjustment '{requestId}' must be reviewed by an actor independent from requester '{current.RequestedBy}'.");
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
            await SaveCloseManagementAsync(rows, ReadTaskSignOffs(), ReadPlanConfigurations(), ReadEvidenceReviews(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return await BuildPeriodPlanWithGateAsync(workflow, ct, tenantId, companyId).ConfigureAwait(false);
    }

    public Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
        SignOffCloseTaskRequestDto request,
        string actor,
        CancellationToken ct = default)
        => SignOffCloseTaskScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodPlanDto?> SignOffCloseTaskScopedAsync(
        SignOffCloseTaskRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
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

        if (HasRejectedSignOff(role, currentTask.SignOffs))
        {
            throw new InvalidOperationException(
                $"Close task '{taskId}' has a retained rejected sign-off for role '{role}' and must be remediated before another sign-off decision can be retained.");
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
        EnsureIndependentCloseTaskSignOffActor(checklistTask, resolvedActor);
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
            await SaveCloseManagementAsync(ReadLateAdjustments(), rows, ReadPlanConfigurations(), ReadEvidenceReviews(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return await BuildPeriodPlanWithGateAsync(workflow, ct, tenantId, companyId).ConfigureAwait(false);
    }

    public Task<ClosePeriodPlanDto?> ReviewCloseEvidenceAsync(
        ReviewCloseEvidenceRequestDto request,
        string actor,
        CancellationToken ct = default)
        => ReviewCloseEvidenceScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodPlanDto?> ReviewCloseEvidenceScopedAsync(
        ReviewCloseEvidenceRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "review close evidence and blockers");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        var issueCode = RequireText(request.IssueCode, "IssueCode");
        var notes = RequireText(request.Notes, "Notes");
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("At least one evidence link is required for close evidence review.", nameof(request));
        }

        if (!HasCloseEvidenceReviewEvidence(evidenceLinks))
        {
            throw new ArgumentException("Close evidence review requires retained close-review, blocker, evidence, audit, or remediation evidence.", nameof(request));
        }

        var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null)
        {
            throw new InvalidOperationException(
                $"Cannot review close evidence for period '{workflow.PeriodId}' because the close period is locked by close package '{workflow.ClosePackage.ClosePackageId}'.");
        }

        var currentPlan = BuildPeriodPlan(workflow);
        var targetId = NormalizeOptional(request.TargetId);
        var issue = currentPlan.ValidationIssues.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, issueCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.TargetId ?? string.Empty, targetId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (issue is null)
        {
            throw new InvalidOperationException(
                $"Close evidence review issue '{issueCode}' for target '{targetId ?? "close-plan"}' is not active on workflow '{request.WorkflowId}'.");
        }

        if (!HasCloseEvidenceReviewEvidenceWithProvenance(evidenceLinks, issueCode, targetId, workflow))
        {
            throw new ArgumentException("Close evidence review evidence must reference the issue, target, workflow, or exact close period and selected ledger book on the same artifact.", nameof(request));
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.Actor, "Actor") : actor.Trim();
        var review = new CloseEvidenceReviewDto(
            $"close-review-{Sanitize(issueCode)}-{Sanitize(targetId ?? "plan")}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            issue.Code,
            issue.TargetId,
            resolvedActor,
            DateTimeOffset.UtcNow,
            notes,
            evidenceLinks);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadEvidenceReviews().ToList();
            if (rows.Any(row =>
                    row.WorkflowId == request.WorkflowId &&
                    string.Equals(row.Review.IssueCode, issue.Code, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Review.TargetId ?? string.Empty, issue.TargetId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Review.ReviewedBy, resolvedActor, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Close evidence issue '{issue.Code}' for target '{issue.TargetId ?? "close-plan"}' already has a retained review by '{resolvedActor}'.");
            }

            rows.Add(new WorkflowCloseEvidenceReviewRecord(request.WorkflowId, review));
            await SaveCloseManagementAsync(
                ReadLateAdjustments(),
                ReadTaskSignOffs(),
                ReadPlanConfigurations(),
                rows,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return await BuildPeriodPlanWithGateAsync(workflow, ct, tenantId, companyId).ConfigureAwait(false);
    }

    public Task<ClosePeriodPlanDto?> ConfigurePeriodPlanAsync(
        UpsertClosePeriodPlanConfigurationRequestDto request,
        string actor,
        CancellationToken ct = default)
        => ConfigurePeriodPlanScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodPlanDto?> ConfigurePeriodPlanScopedAsync(
        UpsertClosePeriodPlanConfigurationRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "configure close period plans");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.Actor, "Actor") : actor.Trim();
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("At least one evidence link is required for close plan configuration.", nameof(request));
        }

        if (!HasClosePlanConfigurationEvidence(evidenceLinks))
        {
            throw new ArgumentException("Close plan configuration requires retained close-plan setup, configuration, policy, or approval evidence.", nameof(request));
        }

        var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            return null;
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null)
        {
            throw new InvalidOperationException(
                $"Cannot configure close plan '{request.WorkflowId}' for period '{workflow.PeriodId}' because the close period is locked by close package '{workflow.ClosePackage.ClosePackageId}'.");
        }

        if (!HasClosePlanConfigurationEvidenceWithProvenance(evidenceLinks, workflow))
        {
            throw new ArgumentException("Close plan configuration evidence must reference the workflow or exact close period and selected ledger book on the same artifact.", nameof(request));
        }

        var currentConfiguration = GetPlanConfiguration(request.WorkflowId);
        if (currentConfiguration?.ConfiguredAtUtc is { } configuredAtUtc &&
            request.ExpectedConfiguredAtUtc is { } expectedConfiguredAtUtc &&
            !CloseConfigurationVersionMatches(configuredAtUtc, expectedConfiguredAtUtc))
        {
            throw new InvalidOperationException(
                $"Close plan configuration for workflow '{request.WorkflowId}' changed at {configuredAtUtc:O}; reload the close plan before retaining setup changes.");
        }

        var materialityPolicy = NormalizeMaterialityPolicy(request.MaterialityPolicy, currentConfiguration?.MaterialityPolicy, workflow);
        var taskConfigurations = NormalizeTaskConfigurations(request.TaskConfigurations, workflow);
        if (request.MaterialityPolicy is null && taskConfigurations.Count == 0)
        {
            throw new ArgumentException("Close plan configuration must include a materiality policy or at least one task configuration.", nameof(request));
        }

        var configuration = new ClosePeriodPlanConfigurationDto(
            request.WorkflowId,
            materialityPolicy,
            taskConfigurations,
            resolvedActor,
            DateTimeOffset.UtcNow,
            evidenceLinks);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var configurations = ReadPlanConfigurations()
                .Where(row => row.WorkflowId != request.WorkflowId)
                .Append(configuration)
                .ToArray();
            await SaveCloseManagementAsync(ReadLateAdjustments(), ReadTaskSignOffs(), configurations, ReadEvidenceReviews(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return await BuildPeriodPlanWithGateAsync(workflow, ct, tenantId, companyId).ConfigureAwait(false);
    }

    public Task<ClosePeriodLockResultDto?> LockClosePeriodAsync(
        LockClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default)
        => LockClosePeriodScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodLockResultDto?> LockClosePeriodScopedAsync(
        LockClosePeriodRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "lock close periods");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.Actor, "Actor") : actor.Trim();
            var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                return null;
            }

            var plan = BuildPeriodPlan(workflow);
            if (plan.IsPeriodLocked)
            {
                plan = await AttachClosingEntriesGateAsync(plan, workflow, ct, tenantId, companyId).ConfigureAwait(false);
                return new ClosePeriodLockResultDto(
                    true,
                    plan,
                    null,
                    [
                        new AccountingConfigurationValidationIssueDto(
                        "ClosePeriodAlreadyLocked",
                        AccountingConfigurationValidationSeverityDto.Warning,
                        $"Close period '{plan.PeriodId}' is already locked.",
                        plan.ClosePlanId,
                        "Use governed reopen workflow before changing a locked period.")
                    ]);
            }

            var issues = BuildClosePeriodLockIssues(request, workflow, plan);
            if (issues.Count > 0)
            {
                plan = await AttachClosingEntriesGateAsync(plan, workflow, ct, tenantId, companyId).ConfigureAwait(false);
                return new ClosePeriodLockResultDto(false, plan, null, issues);
            }

            if (_postingWorkbench is null)
            {
                plan = AttachClosingEntriesGate(plan, UnavailableClosingEntriesGate(plan));
                return new ClosePeriodLockResultDto(
                    false,
                    plan,
                    null,
                    [ClosingEntriesIssue(plan.ClosingEntriesGate!)]);
            }

            ClosePostingGateDto closingGate;
            try
            {
                closingGate = await _postingWorkbench.EnsureClosingDraftQueuedAsync(
                        RequirePostingContext(workflow, plan, tenantId, companyId),
                        new AccountingClosePostingCommand(
                            resolvedActor,
                            RequireText(request.Rationale, "Rationale"),
                            NormalizeEvidenceLinks(request.EvidenceLinks),
                            request.ActionOrigin,
                            CorrelationId: request.CorrelationId),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                closingGate = new ClosePostingGateDto(
                    $"period-close-posting:{plan.ClosePlanId}",
                    "Post closing entries",
                    ClosePostingGateStateDto.Blocked,
                    false,
                    0m,
                    0,
                    ex.Message);
            }

            plan = AttachClosingEntriesGate(plan, closingGate);
            if (request.PrepareClosingEntriesOnly)
            {
                var preparationIssues = closingGate.State is ClosePostingGateStateDto.Blocked
                    or ClosePostingGateStateDto.Unavailable
                    ? new[] { ClosingEntriesIssue(closingGate) }
                    : Array.Empty<AccountingConfigurationValidationIssueDto>();
                return new ClosePeriodLockResultDto(
                    false,
                    plan,
                    null,
                    preparationIssues);
            }

            if (!closingGate.IsReadyForLock)
            {
                return new ClosePeriodLockResultDto(
                    false,
                    plan,
                    null,
                    [ClosingEntriesIssue(closingGate)]);
            }

            // Closing-entry preparation can await external stores. Re-read the governed workflow at the
            // irreversible mutation boundary so a concurrent sign-off/configuration/version change cannot
            // hard-close the ledger against a stale close plan.
            var boundaryWorkflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
            if (boundaryWorkflow is null)
            {
                return null;
            }

            var boundaryPlan = BuildPeriodPlan(boundaryWorkflow);
            var boundaryIssues = BuildClosePeriodLockIssues(request, boundaryWorkflow, boundaryPlan);
            if (boundaryIssues.Count > 0)
            {
                return new ClosePeriodLockResultDto(
                    false,
                    AttachClosingEntriesGate(boundaryPlan, closingGate),
                    null,
                    boundaryIssues);
            }

            workflow = boundaryWorkflow;
            plan = AttachClosingEntriesGate(boundaryPlan, closingGate);

            try
            {
                await _postingWorkbench.FinalizeHardCloseAsync(
                        RequirePostingContext(workflow, plan, tenantId, companyId),
                        new AccountingClosePostingCommand(
                            resolvedActor,
                            RequireText(request.Rationale, "Rationale"),
                            NormalizeEvidenceLinks(request.EvidenceLinks),
                            request.ActionOrigin,
                            Role: "Fund Controller",
                            CorrelationId: request.CorrelationId),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or LedgerBookServiceException)
            {
                return new ClosePeriodLockResultDto(
                    false,
                    plan,
                    null,
                    [new AccountingConfigurationValidationIssueDto(
                    "LedgerPeriodHardCloseFailed",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    ex.Message,
                    closingGate.GateId,
                    "Resolve the ledger-period hard-close failure and retry with the same retained closing-entry evidence.")]);
            }

            var transition = await _workflowService.CloseWorkflowAsync(
                request.WorkflowId,
                new OperationsCloseWorkflowRequestDto(
                    ExpectedVersion: request.ExpectedWorkflowVersion,
                    Actor: resolvedActor,
                    Rationale: RequireText(request.Rationale, "Rationale"),
                    ReportPackId: RequireText(request.ReportPackId, "ReportPackId"),
                    ChecklistControlApprovals: request.ChecklistControlApprovals,
                    CorrelationId: request.CorrelationId,
                    EvidenceLinks: ToOperationsEvidenceLinks(request.EvidenceLinks),
                    ClosePackageId: request.ClosePackageId,
                    ClosePackageManifestId: request.ClosePackageManifestId,
                    ClosePackageEvidenceHash: null,
                    ClosePackageRetainedManifestRoute: request.ClosePackageRetainedManifestRoute,
                    ActionOrigin: request.ActionOrigin),
                ct).ConfigureAwait(false);

            var updatedPlan = transition.Workflow is null
                ? plan
                : await BuildPeriodPlanWithGateAsync(transition.Workflow, ct, tenantId, companyId).ConfigureAwait(false);
            var transitionIssues = transition.Success
                ? Array.Empty<AccountingConfigurationValidationIssueDto>()
                : transition.Blockers
                    .Select(static blocker => ToValidationIssue(blocker))
                    .Append(new AccountingConfigurationValidationIssueDto(
                        "CloseWorkflowTransitionPendingAfterLedgerHardClose",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        "The ledger period is hard-closed, but the workflow close transition did not commit.",
                        plan.ClosePlanId,
                        "Refresh the close plan and retry the same close command with the current workflow version; ledger hard close is idempotent."))
                    .ToArray();
            return new ClosePeriodLockResultDto(
                transition.Success && updatedPlan.IsPeriodLocked,
                updatedPlan,
                transition,
                transitionIssues);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<ClosePeriodReopenResultDto?> ReopenClosePeriodAsync(
        ReopenClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default)
        => ReopenClosePeriodScopedAsync(request, actor, tenantId: null, companyId: null, ct: ct);

    public async Task<ClosePeriodReopenResultDto?> ReopenClosePeriodScopedAsync(
        ReopenClosePeriodRequestDto request,
        string actor,
        string? tenantId,
        string? companyId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "reopen close periods");
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var role = RequireText(request.Role, "Role");
            if (!string.Equals(role, "Controller", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Fund Controller", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Governed close-period reopen requires Controller or Fund Controller authority.");
            }

            var evidence = NormalizeEvidenceLinks(request.EvidenceLinks);
            if (evidence.Count == 0)
            {
                throw new InvalidOperationException("Governed close-period reopen requires retained reversal/restatement evidence.");
            }

            var resolvedActor = string.IsNullOrWhiteSpace(actor) ? RequireText(request.Actor, "Actor") : actor.Trim();
            var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                return null;
            }

            var plan = BuildPeriodPlan(workflow);
            if (!plan.IsPeriodLocked)
            {
                return new ClosePeriodReopenResultDto(
                    false,
                    await AttachClosingEntriesGateAsync(plan, workflow, ct, tenantId, companyId).ConfigureAwait(false),
                    null,
                    null,
                    [new AccountingConfigurationValidationIssueDto(
                    "ClosePeriodNotLocked",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close period '{plan.PeriodId}' is not locked and cannot enter governed reopen.",
                    plan.ClosePlanId,
                    "Use the normal late-adjustment workflow for a soft-closed period.")]);
            }

            if (workflow.Version != request.ExpectedWorkflowVersion)
            {
                return new ClosePeriodReopenResultDto(
                    false,
                    await AttachClosingEntriesGateAsync(plan, workflow, ct, tenantId, companyId).ConfigureAwait(false),
                    null,
                    null,
                    [new AccountingConfigurationValidationIssueDto(
                    "ClosePeriodReopenVersionMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Workflow version {workflow.Version} does not match expected version {request.ExpectedWorkflowVersion}.",
                    plan.ClosePlanId,
                    "Refresh the close plan before reopening the period.")]);
            }

            if (_postingWorkbench is null)
            {
                var unavailable = UnavailableClosingEntriesGate(plan);
                return new ClosePeriodReopenResultDto(
                    false,
                    AttachClosingEntriesGate(plan, unavailable),
                    null,
                    unavailable,
                    [ClosingEntriesIssue(unavailable)]);
            }

            // Re-read immediately before the durable ledger reopen. This prevents a stale version from
            // reopening the ledger after another close-plan mutation completed while the request waited.
            var boundaryWorkflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
            if (boundaryWorkflow is null)
            {
                return null;
            }

            var boundaryPlan = BuildPeriodPlan(boundaryWorkflow);
            if (!boundaryPlan.IsPeriodLocked)
            {
                return new ClosePeriodReopenResultDto(
                    false,
                    await AttachClosingEntriesGateAsync(boundaryPlan, boundaryWorkflow, ct, tenantId, companyId).ConfigureAwait(false),
                    null,
                    null,
                    [new AccountingConfigurationValidationIssueDto(
                    "ClosePeriodNotLocked",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Close period '{boundaryPlan.PeriodId}' is no longer locked and cannot enter governed reopen.",
                    boundaryPlan.ClosePlanId,
                    "Refresh the close plan before retrying the reopen command.")]);
            }

            if (boundaryWorkflow.Version != request.ExpectedWorkflowVersion)
            {
                return new ClosePeriodReopenResultDto(
                    false,
                    await AttachClosingEntriesGateAsync(boundaryPlan, boundaryWorkflow, ct, tenantId, companyId).ConfigureAwait(false),
                    null,
                    null,
                    [new AccountingConfigurationValidationIssueDto(
                    "ClosePeriodReopenVersionMismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Workflow version {boundaryWorkflow.Version} does not match expected version {request.ExpectedWorkflowVersion}.",
                    boundaryPlan.ClosePlanId,
                    "Refresh the close plan before reopening the period.")]);
            }

            workflow = boundaryWorkflow;
            plan = boundaryPlan;

            var reversalGate = await _postingWorkbench.ReopenAndQueueClosingReversalsAsync(
                    RequirePostingContext(workflow, plan, tenantId, companyId),
                    new AccountingClosePostingCommand(
                        resolvedActor,
                        RequireText(request.Rationale, "Rationale"),
                        evidence,
                        request.ActionOrigin,
                        role,
                        RequireText(request.ApprovalReference, "ApprovalReference"),
                        request.CorrelationId),
                    ct)
                .ConfigureAwait(false);

            var transition = await _workflowService.ReopenWorkflowAsync(
                    request.WorkflowId,
                    new OperationsReopenWorkflowRequestDto(
                        request.ExpectedWorkflowVersion,
                        resolvedActor,
                        RequireText(request.Rationale, "Rationale"),
                        RequireText(request.IncidentId, "IncidentId"),
                        IsGovernedAdmin: true,
                        RequireText(request.Justification, "Justification"),
                        RequireText(request.ApprovalReference, "ApprovalReference"),
                        RequireText(request.ImpactSummary, "ImpactSummary"),
                        request.CorrelationId,
                        ToOperationsEvidenceLinks(evidence),
                        request.ActionOrigin),
                    ct)
                .ConfigureAwait(false);

            var updatedPlan = transition.Workflow is null
                ? AttachClosingEntriesGate(plan, reversalGate)
                : AttachClosingEntriesGate(BuildPeriodPlan(transition.Workflow), reversalGate);
            var reopenIssues = transition.Success
                ? Array.Empty<AccountingConfigurationValidationIssueDto>()
                : transition.Blockers
                    .Select(static blocker => ToValidationIssue(blocker))
                    .Append(new AccountingConfigurationValidationIssueDto(
                        "CloseWorkflowReopenPendingAfterLedgerReopen",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        "The ledger period is reopened, but the workflow reopen transition did not commit.",
                        plan.ClosePlanId,
                        "Refresh the close plan and retry the exact reopen command with the current workflow version; the retained reversal receipt makes ledger reopen idempotent."))
                    .ToArray();
            return new ClosePeriodReopenResultDto(
                transition.Success,
                updatedPlan,
                transition,
                reversalGate,
                reopenIssues);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private ClosePeriodPlanDto BuildPeriodPlan(OperationsContinuityWorkflowDto workflow)
    {
        var period = ResolvePeriod(workflow.PeriodId);
        var planConfiguration = GetPlanConfiguration(workflow.WorkflowId);
        var materialityPolicy = planConfiguration?.MaterialityPolicy ?? ResolveMaterialityPolicy(workflow);
        var taskConfigurations = planConfiguration?.TaskConfigurations
            .ToDictionary(static configuration => configuration.TaskId, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CloseTaskConfigurationDto>(StringComparer.OrdinalIgnoreCase);
        var retainedSignOffs = GetTaskSignOffs(workflow.WorkflowId);
        var satisfiedTaskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<CloseTaskDto>(workflow.CloseChecklist.Count);
        for (var index = 0; index < workflow.CloseChecklist.Count; index++)
        {
            taskConfigurations.TryGetValue(workflow.CloseChecklist[index].TaskId, out var taskConfiguration);
            var task = ToCloseTask(workflow.CloseChecklist[index], index, workflow, retainedSignOffs, satisfiedTaskIds, taskConfiguration);
            tasks.Add(task);
            if (task.Status == CloseTaskStatusDto.SignedOff)
            {
                satisfiedTaskIds.Add(task.TaskId);
            }
        }

        var lateAdjustments = GetLateAdjustments(workflow.WorkflowId);
        var validationIssues = BuildValidationIssues(workflow, tasks, lateAdjustments, materialityPolicy);
        var isPeriodLocked = workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null;
        var evidenceReviews = GetEvidenceReviews(workflow.WorkflowId);
        var operatingCoverage = BuildOperatingCoverage(
            workflow,
            tasks,
            lateAdjustments,
            materialityPolicy,
            validationIssues,
            planConfiguration,
            evidenceReviews,
            isPeriodLocked);

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
            BuildCloseCalendar(tasks, isPeriodLocked),
            planConfiguration,
            evidenceReviews,
            operatingCoverage,
            WorkflowVersion: workflow.Version);
    }

    private async Task<ClosePeriodPlanDto> BuildPeriodPlanWithGateAsync(
        OperationsContinuityWorkflowDto workflow,
        CancellationToken ct,
        string? tenantId = null,
        string? companyId = null)
        => await AttachClosingEntriesGateAsync(
                BuildPeriodPlan(workflow),
                workflow,
                ct,
                tenantId,
                companyId)
            .ConfigureAwait(false);

    private async Task<ClosePeriodPlanDto> AttachClosingEntriesGateAsync(
        ClosePeriodPlanDto plan,
        OperationsContinuityWorkflowDto workflow,
        CancellationToken ct,
        string? tenantId = null,
        string? companyId = null)
    {
        if (_postingWorkbench is null)
        {
            return AttachClosingEntriesGate(plan, UnavailableClosingEntriesGate(plan));
        }

        ClosePostingGateDto gate;
        try
        {
            gate = await _postingWorkbench
                .EvaluateAsync(RequirePostingContext(workflow, plan, tenantId, companyId), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            gate = new ClosePostingGateDto(
                $"period-close-posting:{plan.ClosePlanId}",
                "Post closing entries",
                ClosePostingGateStateDto.Blocked,
                false,
                0m,
                0,
                ex.Message);
        }

        return AttachClosingEntriesGate(plan, gate);
    }

    private static ClosePeriodPlanDto AttachClosingEntriesGate(
        ClosePeriodPlanDto plan,
        ClosePostingGateDto gate)
    {
        var gateIssues = gate.IsReadyForLock
            ? Array.Empty<AccountingConfigurationValidationIssueDto>()
            : new[] { ClosingEntriesIssue(gate) };
        var coverageState = gate.IsReadyForLock
            ? AccountingReadinessStateDto.ReadyForReview
            : gate.State == ClosePostingGateStateDto.Unavailable
                ? AccountingReadinessStateDto.NeedsAttention
                : AccountingReadinessStateDto.Blocked;
        var coverage = new CloseOperatingCoverageItemDto(
            "post-closing-entries",
            "Post closing entries",
            coverageState,
            gate.EvidenceLinks.Count,
            gate.IsReadyForLock ? 0 : 1,
            gate.Detail,
            gate.EvidenceLinks,
            gateIssues);
        return plan with
        {
            ClosingEntriesGate = gate,
            OperatingCoverage = plan.OperatingCoverage
                .Where(static item => !string.Equals(item.ControlId, "post-closing-entries", StringComparison.OrdinalIgnoreCase))
                .Append(coverage)
                .ToArray()
        };
    }

    private static ClosePostingGateDto UnavailableClosingEntriesGate(ClosePeriodPlanDto plan)
        => new(
            $"period-close-posting:{plan.ClosePlanId}",
            "Post closing entries",
            ClosePostingGateStateDto.Unavailable,
            false,
            0m,
            0,
            "The governed closing-entry workbench is unavailable; period lock fails closed.");

    private static AccountingClosePostingContext RequirePostingContext(
        OperationsContinuityWorkflowDto workflow,
        ClosePeriodPlanDto plan,
        string? tenantId = null,
        string? companyId = null)
    {
        if (workflow.LedgerBookId is not { } ledgerBookId || ledgerBookId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Close workflow '{workflow.WorkflowId:D}' has no selected ledger book for closing entries.");
        }

        return new AccountingClosePostingContext(
            workflow.WorkflowId,
            workflow.FundAccountId,
            ledgerBookId,
            workflow.PeriodId,
            plan.MaterialityPolicy.Currency,
            tenantId,
            companyId);
    }

    private static AccountingConfigurationValidationIssueDto ClosingEntriesIssue(ClosePostingGateDto gate)
        => new(
            gate.State == ClosePostingGateStateDto.Unavailable
                ? "PeriodClosePostingGateUnavailable"
                : "PeriodCloseClosingEntriesPending",
            AccountingConfigurationValidationSeverityDto.Critical,
            gate.Detail,
            gate.GateId,
            "Open the Post closing entries gate, review the net-income roll, and independently approve/post the governed draft before period lock.");

    private static IReadOnlyList<CloseOperatingCoverageItemDto> BuildOperatingCoverage(
        OperationsContinuityWorkflowDto workflow,
        IReadOnlyList<CloseTaskDto> tasks,
        IReadOnlyList<LateAdjustmentRequestDto> lateAdjustments,
        MaterialityPolicyDto policy,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        ClosePeriodPlanConfigurationDto? planConfiguration,
        IReadOnlyList<CloseEvidenceReviewDto> evidenceReviews,
        bool isPeriodLocked)
    {
        var signOffIssues = FilterIssues(
            validationIssues,
            "CloseTaskSignOffMissing",
            "CloseTaskSignOffRejected");
        var dependencyIssues = FilterIssues(validationIssues, "CloseTaskWaitingOnDependency");
        var lateAdjustmentIssues = FilterIssues(validationIssues, "LateAdjustmentRequiresApproval");
        var closeBlockerIssues = validationIssues
            .Where(static issue => issue.Code.StartsWith("CloseBlocker:", StringComparison.OrdinalIgnoreCase)
                || issue.Code == "CloseTaskBlocked")
            .ToArray();
        var criticalIssues = validationIssues
            .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .ToArray();
        var reviewedIssueKeys = evidenceReviews
            .Select(static review => IssueKey(review.IssueCode, review.TargetId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreviewedIssues = validationIssues
            .Where(issue => !reviewedIssueKeys.Contains(IssueKey(issue.Code, issue.TargetId)))
            .ToArray();
        var taskSignOffEvidence = NormalizeEvidenceLinks(tasks.SelectMany(static task =>
            task.SignOffs.SelectMany(static signOff => signOff.EvidenceLinks)));
        var taskEvidence = NormalizeEvidenceLinks(tasks.SelectMany(static task => task.EvidenceLinks));
        var lateAdjustmentEvidence = NormalizeEvidenceLinks(lateAdjustments.SelectMany(static adjustment => adjustment.EvidenceLinks));
        var evidenceReviewLinks = NormalizeEvidenceLinks(evidenceReviews.SelectMany(static review => review.EvidenceLinks));
        var configurationEvidence = NormalizeEvidenceLinks(planConfiguration?.EvidenceLinks ?? []);
        var periodLockEvidence = NormalizeEvidenceLinks(workflow.ClosePackage?.EvidenceLinks.Select(static link => link.EvidenceId) ?? []);
        var blockerReviewIssues = NormalizeIssues([.. closeBlockerIssues, .. unreviewedIssues]);
        var hasConfiguredDependencies = planConfiguration?.TaskConfigurations.Any(static configuration =>
            configuration.DependencyConfigurations.Count > 0 || configuration.DependsOnTaskIds.Count > 0) == true;
        var hasTaskDependencies = tasks.Any(static task => task.Dependencies.Count > 0);
        var hasSignOffRequirements = tasks.Any(static task => task.SignOffRequirements.Count > 0);
        var allSignOffRequirementsSatisfied = hasSignOffRequirements &&
            tasks.SelectMany(static task => task.SignOffRequirements).All(static requirement => requirement.IsSatisfied);

        return
        [
            new CloseOperatingCoverageItemDto(
                "close-plan-setup",
                "Close plan setup",
                planConfiguration is null ? AccountingReadinessStateDto.NeedsAttention : AccountingReadinessStateDto.ReadyForReview,
                configurationEvidence.Count,
                0,
                planConfiguration is null
                    ? "Retain ledger-book scoped close-plan configuration evidence before final close."
                    : "Review the retained close-plan configuration before period lock.",
                configurationEvidence),
            new CloseOperatingCoverageItemDto(
                "dependency-graph",
                "Dependency graph",
                dependencyIssues.Count > 0
                    ? AccountingReadinessStateDto.Blocked
                    : hasConfiguredDependencies || hasTaskDependencies
                        ? AccountingReadinessStateDto.ReadyForReview
                        : AccountingReadinessStateDto.NeedsAttention,
                configurationEvidence.Count + taskEvidence.Count,
                dependencyIssues.Count,
                dependencyIssues.Count > 0
                    ? "Complete predecessor close tasks before dependent close work advances."
                    : "Review retained dependency reasons and predecessor evidence before final close.",
                NormalizeEvidenceLinks([.. configurationEvidence, .. taskEvidence]),
                dependencyIssues),
            new CloseOperatingCoverageItemDto(
                "sign-off-matrix",
                "Sign-off matrix",
                signOffIssues.Count > 0
                    ? AccountingReadinessStateDto.Blocked
                    : allSignOffRequirementsSatisfied
                        ? AccountingReadinessStateDto.ReadyForReview
                        : AccountingReadinessStateDto.NeedsAttention,
                configurationEvidence.Count + taskSignOffEvidence.Count,
                signOffIssues.Count,
                signOffIssues.Count > 0
                    ? "Retain required close sign-off approvals with scoped evidence."
                    : "Review retained sign-off matrix approvals before report certification.",
                NormalizeEvidenceLinks([.. configurationEvidence, .. taskSignOffEvidence]),
                signOffIssues),
            new CloseOperatingCoverageItemDto(
                "late-adjustments",
                "Late adjustments",
                lateAdjustmentIssues.Count > 0
                    ? AccountingReadinessStateDto.Blocked
                    : lateAdjustments.Count > 0
                        ? AccountingReadinessStateDto.ReadyForReview
                        : AccountingReadinessStateDto.NotStarted,
                lateAdjustmentEvidence.Count,
                lateAdjustmentIssues.Count,
                lateAdjustmentIssues.Count > 0
                    ? $"Approve or reject material late adjustments using the {policy.ReviewRole} review gate."
                    : "Review retained late-adjustment decisions before final close.",
                lateAdjustmentEvidence,
                lateAdjustmentIssues),
            new CloseOperatingCoverageItemDto(
                "blocker-evidence-review",
                "Blocker evidence review",
                blockerReviewIssues.Count > 0
                    ? AccountingReadinessStateDto.Blocked
                    : evidenceReviews.Count > 0
                        ? AccountingReadinessStateDto.ReadyForReview
                        : AccountingReadinessStateDto.NotStarted,
                evidenceReviewLinks.Count,
                blockerReviewIssues.Count,
                blockerReviewIssues.Count > 0
                    ? "Review active blocker evidence and remediate critical close validation issues."
                    : "Retain blocker-review notes when active close issues are investigated.",
                evidenceReviewLinks,
                blockerReviewIssues),
            new CloseOperatingCoverageItemDto(
                "period-lock",
                "Period lock",
                isPeriodLocked
                    ? AccountingReadinessStateDto.Certified
                    : criticalIssues.Length > 0
                        ? AccountingReadinessStateDto.Blocked
                        : AccountingReadinessStateDto.ReadyForReview,
                periodLockEvidence.Count,
                criticalIssues.Length,
                isPeriodLocked
                    ? "Period is locked with retained close-package evidence."
                    : criticalIssues.Length > 0
                        ? "Clear critical close validation issues before period lock."
                        : "Retain close-package evidence and lock the period.",
                periodLockEvidence,
                criticalIssues)
        ];
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> FilterIssues(
        IReadOnlyList<AccountingConfigurationValidationIssueDto> issues,
        params string[] codes)
    {
        var codeSet = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return issues
            .Where(issue => codeSet.Contains(issue.Code))
            .ToArray();
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> NormalizeIssues(
        IEnumerable<AccountingConfigurationValidationIssueDto> issues)
    {
        var unique = new List<AccountingConfigurationValidationIssueDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            if (seen.Add(IssueKey(issue.Code, issue.TargetId)))
            {
                unique.Add(issue);
            }
        }

        return unique;
    }

    private static string IssueKey(string issueCode, string? targetId)
        => $"{issueCode}|{targetId}";

    private IReadOnlyList<LateAdjustmentRequestDto> GetLateAdjustments(Guid workflowId)
    {
        return ReadLateAdjustments()
            .Where(record => record.WorkflowId == workflowId)
            .Select(static record => record.Adjustment)
            .OrderBy(static row => row.RequestedAtUtc)
            .ToArray();
    }

    private IReadOnlyList<CloseEvidenceReviewDto> GetEvidenceReviews(Guid workflowId)
    {
        return ReadEvidenceReviews()
            .Where(record => record.WorkflowId == workflowId)
            .Select(static record => record.Review)
            .OrderBy(static row => row.ReviewedAtUtc)
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
        IReadOnlyList<ClosePeriodPlanConfigurationDto> planConfigurationRows,
        IReadOnlyList<WorkflowCloseEvidenceReviewRecord> evidenceReviewRows,
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

                _planConfigurations.Clear();
                foreach (var configuration in planConfigurationRows)
                {
                    _planConfigurations[configuration.WorkflowId] = configuration;
                }

                _evidenceReviews.Clear();
                foreach (var group in evidenceReviewRows.GroupBy(static row => row.WorkflowId))
                {
                    _evidenceReviews[group.Key] = group.ToList();
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
                .ToArray(),
            planConfigurationRows
                .OrderBy(static row => row.WorkflowId)
                .ToArray(),
            evidenceReviewRows
                .OrderBy(static row => row.WorkflowId)
                .ThenBy(static row => row.Review.ReviewedAtUtc)
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

    private ClosePeriodPlanConfigurationDto? GetPlanConfiguration(Guid workflowId)
        => ReadPlanConfigurations()
            .FirstOrDefault(configuration => configuration.WorkflowId == workflowId);

    private IReadOnlyList<ClosePeriodPlanConfigurationDto> ReadPlanConfigurations()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            return ReadInMemoryPlanConfigurations();
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<CloseManagementSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.PlanConfigurations ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private IReadOnlyList<ClosePeriodPlanConfigurationDto> ReadInMemoryPlanConfigurations()
    {
        lock (_readGate)
        {
            return _planConfigurations.Values
                .OrderBy(static row => row.WorkflowId)
                .ToArray();
        }
    }

    private IReadOnlyList<WorkflowCloseEvidenceReviewRecord> ReadEvidenceReviews()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            return ReadInMemoryEvidenceReviews();
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<CloseManagementSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.EvidenceReviews ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private IReadOnlyList<WorkflowCloseEvidenceReviewRecord> ReadInMemoryEvidenceReviews()
    {
        lock (_readGate)
        {
            return _evidenceReviews
                .SelectMany(static pair => pair.Value)
                .ToArray();
        }
    }

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
