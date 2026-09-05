using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
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
        => Task.FromException<ClosePeriodLockResultDto?>(
            new NotSupportedException(
                "This accounting close service does not implement tenant- and company-scoped hard close."));

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
        => Task.FromException<ClosePeriodReopenResultDto?>(
            new NotSupportedException(
                "This accounting close service does not implement tenant- and company-scoped reopen."));
}

public sealed partial class AccountingCloseManagementService : IAccountingCloseManagementService
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
    private readonly IAccountingCloseMutationGate? _mutationGate;
    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private int _hasEstablishedPersistedSnapshot;
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
        _mutationGate = postingWorkbench as IAccountingCloseMutationGate;
    }

    public AccountingCloseManagementService(
        IOperationsContinuityWorkflowService workflowService,
        StorageOptions storageOptions)
        : this(workflowService)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "accounting", "close-management-late-adjustments.json");
        _hasEstablishedPersistedSnapshot = File.Exists(_persistencePath) ? 1 : 0;
    }

    public AccountingCloseManagementService(
        IOperationsContinuityWorkflowService workflowService,
        StorageOptions storageOptions,
        IAccountingClosePostingWorkbench postingWorkbench)
        : this(workflowService, storageOptions)
    {
        _postingWorkbench = postingWorkbench ?? throw new ArgumentNullException(nameof(postingWorkbench));
        _mutationGate = postingWorkbench as IAccountingCloseMutationGate;
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
        var targetId = Meridian.Contracts.Text.TextPrimitives.NormalizeOptional(request.TargetId);
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
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.PrepareClosingEntriesOnly)
        {
            return Task.FromException<ClosePeriodLockResultDto?>(
                new InvalidOperationException(
                    "Governed hard close requires an authenticated tenant and company scope. Use the scoped close-period operation."));
        }

        return LockClosePeriodScopedAsync(
            request,
            actor,
            tenantId: null,
            companyId: null,
            ct: ct);
    }

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

        var controllerRole = request.PrepareClosingEntriesOnly
            ? null
            : RequireControllerRole(request.ControllerRole);
        if (!request.PrepareClosingEntriesOnly)
        {
            RequireCompleteMutationScope(tenantId, companyId);
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
            if (!request.PrepareClosingEntriesOnly && _mutationGate is null)
            {
                plan = await AttachClosingEntriesGateAsync(
                        plan,
                        workflow,
                        ct,
                        tenantId,
                        companyId)
                    .ConfigureAwait(false);
                return new ClosePeriodLockResultDto(
                    false,
                    plan,
                    null,
                    [MutationConsistencyGateUnavailableIssue(plan)]);
            }

            await using var closeConsistencyLease =
                !request.PrepareClosingEntriesOnly
                    ? await _mutationGate!
                        .AcquireAsync(
                            RequirePostingContext(workflow, plan, tenantId, companyId),
                            ct)
                        .ConfigureAwait(false)
                    : null;
            var consistencyLeaseHeld = closeConsistencyLease is not null;
            if (!request.PrepareClosingEntriesOnly && !consistencyLeaseHeld)
            {
                plan = await AttachClosingEntriesGateAsync(
                        plan,
                        workflow,
                        ct,
                        tenantId,
                        companyId)
                    .ConfigureAwait(false);
                return new ClosePeriodLockResultDto(
                    false,
                    plan,
                    null,
                    [MutationConsistencyGateUnavailableIssue(plan)]);
            }

            if (plan.IsPeriodLocked)
            {
                if (_postingWorkbench is not null && !request.PrepareClosingEntriesOnly)
                {
                    try
                    {
                        await _postingWorkbench.FinalizeHardCloseAsync(
                                RequirePostingContext(workflow, plan, tenantId, companyId),
                                new AccountingClosePostingCommand(
                                    resolvedActor,
                                    RequireText(request.Rationale, "Rationale"),
                                    NormalizeEvidenceLinks(request.EvidenceLinks),
                                    request.ActionOrigin,
                                    Role: controllerRole!,
                                    CorrelationId: request.CorrelationId)
                                {
                                    ConsistencyLeaseHeld = consistencyLeaseHeld
                                },
                                ct)
                            .ConfigureAwait(false);
                    }
                    catch (ReportingCloseEvidenceHandoffException ex)
                    {
                        plan = await AttachClosingEntriesGateAsync(
                                plan,
                                workflow,
                                ct,
                                tenantId,
                                companyId)
                            .ConfigureAwait(false);
                        plan = plan with
                        {
                            IsPeriodLocked = true,
                            CloseCalendar = BuildCloseCalendar(plan.Tasks, isPeriodLocked: true)
                        };
                        return ReportingEvidenceHandoffPending(plan, ex);
                    }
                }

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
                            Role: controllerRole!,
                            CorrelationId: request.CorrelationId)
                        {
                            ConsistencyLeaseHeld = consistencyLeaseHeld
                        },
                        ct)
                    .ConfigureAwait(false);
            }
            catch (ReportingCloseEvidenceHandoffException ex)
            {
                plan = await AttachClosingEntriesGateAsync(
                        plan,
                        workflow,
                        ct,
                        tenantId,
                        companyId)
                    .ConfigureAwait(false);
                plan = plan with
                {
                    IsPeriodLocked = true,
                    CloseCalendar = BuildCloseCalendar(plan.Tasks, isPeriodLocked: true)
                };
                return ReportingEvidenceHandoffPending(plan, ex);
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
            if (transition.Success)
            {
                try
                {
                    // The first hard-close call deliberately cannot retain certifiable reporting
                    // evidence while the Operations Continuity close transition is pending. Once
                    // that transition commits, repeat the idempotent handoff so the final receipt
                    // binds the closed workflow version, approval, checklist, close package, and
                    // close-audit hash without reopening or re-closing the ledger period.
                    await _postingWorkbench.FinalizeHardCloseAsync(
                            RequirePostingContext(transition.Workflow ?? workflow, updatedPlan, tenantId, companyId),
                            new AccountingClosePostingCommand(
                                resolvedActor,
                                RequireText(request.Rationale, "Rationale"),
                                NormalizeEvidenceLinks(request.EvidenceLinks),
                                request.ActionOrigin,
                                Role: controllerRole!,
                                CorrelationId: request.CorrelationId)
                            {
                                ConsistencyLeaseHeld = consistencyLeaseHeld
                            },
                            ct)
                        .ConfigureAwait(false);
                }
                catch (ReportingCloseEvidenceHandoffException ex)
                {
                    updatedPlan = updatedPlan with
                    {
                        IsPeriodLocked = true,
                        CloseCalendar = BuildCloseCalendar(updatedPlan.Tasks, isPeriodLocked: true)
                    };
                    return ReportingEvidenceHandoffPending(updatedPlan, ex);
                }
            }

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

    private static ClosePeriodLockResultDto ReportingEvidenceHandoffPending(
        ClosePeriodPlanDto plan,
        ReportingCloseEvidenceHandoffException exception) =>
        new(
            false,
            plan,
            null,
            [new AccountingConfigurationValidationIssueDto(
                "CloseReportingEvidenceHandoffPending",
                AccountingConfigurationValidationSeverityDto.Critical,
                exception.Message,
                exception.CompletionCheckpointId,
                "The ledger hard close is durable. Retry this same close command after restoring the reporting evidence store; do not reopen the period.")]);

    public Task<ClosePeriodReopenResultDto?> ReopenClosePeriodAsync(
        ReopenClosePeriodRequestDto request,
        string actor,
        CancellationToken ct = default)
        => Task.FromException<ClosePeriodReopenResultDto?>(
            new InvalidOperationException(
                "Governed close-period reopen requires an authenticated tenant and company scope. Use the scoped close-period operation."));

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
        RequireCompleteMutationScope(tenantId, companyId);

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

            if (_mutationGate is null)
            {
                plan = await AttachClosingEntriesGateAsync(
                        plan,
                        workflow,
                        ct,
                        tenantId,
                        companyId)
                    .ConfigureAwait(false);
                return new ClosePeriodReopenResultDto(
                    false,
                    plan,
                    null,
                    plan.ClosingEntriesGate,
                    [MutationConsistencyGateUnavailableIssue(plan)]);
            }

            await using var reopenConsistencyLease = await _mutationGate
                .AcquireAsync(
                    RequirePostingContext(workflow, plan, tenantId, companyId),
                    ct)
                .ConfigureAwait(false);
            var consistencyLeaseHeld = reopenConsistencyLease is not null;
            if (!consistencyLeaseHeld)
            {
                plan = await AttachClosingEntriesGateAsync(
                        plan,
                        workflow,
                        ct,
                        tenantId,
                        companyId)
                    .ConfigureAwait(false);
                return new ClosePeriodReopenResultDto(
                    false,
                    plan,
                    null,
                    plan.ClosingEntriesGate,
                    [MutationConsistencyGateUnavailableIssue(plan)]);
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
                        request.CorrelationId)
                    {
                        ConsistencyLeaseHeld = consistencyLeaseHeld
                    },
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
        // Read the retained collections together: separate file reads can cross an atomic
        // replacement and combine sign-offs with a different configuration or adjustment set.
        CloseManagementSnapshot snapshot;
        lock (_readGate)
        {
            snapshot = ReadPersistedSlice<CloseManagementSnapshot>(
                static value => [value],
                () => [new(ReadInMemoryLateAdjustments(), ReadInMemoryTaskSignOffs(),
                    ReadInMemoryPlanConfigurations(), ReadInMemoryEvidenceReviews())])[0];
        }
        snapshot = new(
            snapshot.LateAdjustments!.Where(row => row.WorkflowId == workflow.WorkflowId).ToArray(),
            snapshot.TaskSignOffs!.Where(row => row.WorkflowId == workflow.WorkflowId).ToArray(),
            snapshot.PlanConfigurations!.Where(row => row.WorkflowId == workflow.WorkflowId).ToArray(),
            snapshot.EvidenceReviews!.Where(row => row.WorkflowId == workflow.WorkflowId).ToArray());
        var period = ResolvePeriod(workflow.PeriodId);
        var planConfiguration = snapshot.PlanConfigurations!.FirstOrDefault();
        var materialityPolicy = planConfiguration?.MaterialityPolicy ?? ResolveMaterialityPolicy(workflow);
        var taskConfigurations = planConfiguration?.TaskConfigurations
            .ToDictionary(static configuration => configuration.TaskId, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CloseTaskConfigurationDto>(StringComparer.OrdinalIgnoreCase);
        var retainedSignOffs = snapshot.TaskSignOffs!;
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

        var lateAdjustments = snapshot.LateAdjustments!.Select(static row => row.Adjustment).ToArray();
        var validationIssues = BuildValidationIssues(workflow, tasks, lateAdjustments, materialityPolicy);
        var isPeriodLocked = workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null;
        var evidenceReviews = snapshot.EvidenceReviews!.Select(static row => row.Review).ToArray();
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
            WorkflowVersion: workflow.Version,
            WorkflowId: workflow.WorkflowId,
            FundAccountId: workflow.FundAccountId,
            EvidenceVersion: Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(snapshot, JsonOptions)),
            EvaluatedAtUtc: DateTimeOffset.UtcNow);
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

    private static AccountingConfigurationValidationIssueDto MutationConsistencyGateUnavailableIssue(
        ClosePeriodPlanDto plan)
        => new(
            "ClosePeriodMutationConsistencyGateUnavailable",
            AccountingConfigurationValidationSeverityDto.Critical,
            "The durable accounting-period mutation fence is unavailable; ledger close/reopen and the Operations workflow transition cannot be committed as one governed outcome.",
            plan.ClosePlanId,
            "Configure the canonical cross-host reporting release/close consistency authority, then retry the unchanged close or reopen command.");

    private static void RequireCompleteMutationScope(string? tenantId, string? companyId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            throw new ArgumentException(
                "Governed accounting-period mutation requires authenticated tenant and company scope.");
        }
    }

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
        => ReadPersistedSlice(
            static snapshot => snapshot.LateAdjustments,
            ReadInMemoryLateAdjustments);

    /// <summary>
    /// Reads one collection out of the persisted close-management snapshot, falling back to the
    /// in-memory set when no persistence path is configured or the file has never been initialized.
    /// </summary>
    /// <remarks>
    /// An unreadable snapshot throws rather than yielding an empty set. Every close mutation
    /// re-reads the three collections it is not changing and rewrites all four
    /// (see <see cref="SaveCloseManagementAsync"/>), so an empty fallback would let the next
    /// routine sign-off atomically overwrite the file with a snapshot missing every previously
    /// recorded late adjustment, task sign-off, plan configuration, and evidence review.
    /// Failing closed leaves the unreadable file intact on disk for recovery.
    /// </remarks>
    private IReadOnlyList<T> ReadPersistedSlice<T>(
        Func<CloseManagementSnapshot, IReadOnlyList<T>?> select,
        Func<IReadOnlyList<T>> readInMemory)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            return readInMemory();
        }

        lock (_readGate)
        {
            if (!File.Exists(_persistencePath))
            {
                if (Volatile.Read(ref _hasEstablishedPersistedSnapshot) != 0)
                {
                    throw new InvalidDataException(
                        $"Close-management snapshot '{_persistencePath}' is missing after durable " +
                        "close-management state was previously persisted or observed. Refusing to " +
                        "continue with an empty close-management set because the next close mutation " +
                        "would permanently discard retained close evidence.");
                }

                return readInMemory();
            }

            CloseManagementSnapshot normalizedSnapshot;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_persistencePath));
                var version = ReadPersistedSnapshotVersion(document.RootElement);
                var snapshot = document.RootElement.Deserialize<CloseManagementSnapshot>(JsonOptions);
                normalizedSnapshot = NormalizePersistedSnapshot(snapshot, version);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' is unreadable. Refusing to " +
                    "continue with an empty close-management set: the next close mutation would " +
                    "overwrite this file and permanently discard the recorded late adjustments, " +
                    "task sign-offs, plan configurations, and evidence reviews.",
                    ex);
            }

            var slice = select(normalizedSnapshot)
                ?? throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' is missing required state. " +
                    "Refusing to continue because the next close mutation would overwrite retained " +
                    "late adjustments, task sign-offs, plan configurations, or evidence reviews.");
            Volatile.Write(ref _hasEstablishedPersistedSnapshot, 1);
            return slice;
        }
    }

    /// <summary>
    /// Normalizes snapshots written before task sign-offs, plan configurations, and evidence
    /// reviews were added to the persisted close-management record.
    /// </summary>
    /// <remarks>
    /// Late adjustments are the original persisted authority and remain mandatory. A null root or
    /// missing late-adjustment collection is therefore incomplete and fails closed. Repository
    /// history contains three additive legacy generations: late adjustments only, then task
    /// sign-offs, then plan configurations. Only those exact contiguous prefixes may omit later
    /// collections; explicit nulls, unknown properties, and gapped shapes fail before typed
    /// normalization. The next mutation persists the fully normalized four-collection snapshot
    /// through <see cref="SaveCloseManagementAsync"/>.
    /// </remarks>
    private CloseManagementSnapshot NormalizePersistedSnapshot(
        CloseManagementSnapshot? snapshot,
        CloseManagementSnapshotVersion version)
    {
        if (snapshot?.LateAdjustments is null)
        {
            throw new InvalidDataException(
                $"Close-management snapshot '{_persistencePath}' is missing required state. " +
                "The legacy late-adjustment collection is the core persisted authority and must " +
                "be present before retained close-management state can be read or rewritten.");
        }

        return version switch
        {
            CloseManagementSnapshotVersion.LateAdjustmentsOnly => snapshot with
            {
                TaskSignOffs = [],
                PlanConfigurations = [],
                EvidenceReviews = []
            },
            CloseManagementSnapshotVersion.ThroughTaskSignOffs
                when snapshot.TaskSignOffs is not null => snapshot with
                {
                    PlanConfigurations = [],
                    EvidenceReviews = []
                },
            CloseManagementSnapshotVersion.ThroughPlanConfigurations
                when snapshot.TaskSignOffs is not null
                     && snapshot.PlanConfigurations is not null => snapshot with
                     {
                         EvidenceReviews = []
                     },
            CloseManagementSnapshotVersion.Current
                when snapshot.TaskSignOffs is not null
                     && snapshot.PlanConfigurations is not null
                     && snapshot.EvidenceReviews is not null => snapshot,
            _ => throw new InvalidDataException(
                $"Close-management snapshot '{_persistencePath}' is missing required state for " +
                $"recognized persisted generation '{version}'. Refusing to infer retained close " +
                "state from an incomplete typed snapshot.")
        };
    }

    private CloseManagementSnapshotVersion ReadPersistedSnapshotVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Close-management snapshot '{_persistencePath}' is missing required state. " +
                "The persisted root must be a JSON object.");
        }

        var presentCollections = new bool[4];
        foreach (var property in root.EnumerateObject())
        {
            var index = GetPersistedCollectionIndex(property.Name);
            if (index < 0)
            {
                throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' has an unsupported state " +
                    $"shape. Property '{property.Name}' is not part of a recognized persisted " +
                    "close-management generation.");
            }

            if (presentCollections[index])
            {
                throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' has an unsupported state " +
                    $"shape. Collection '{property.Name}' appears more than once.");
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' contains explicit null for " +
                    $"collection '{property.Name}'. Present close-management collections must be " +
                    "JSON arrays.");
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' has invalid state for " +
                    $"collection '{property.Name}'. Present close-management collections must be " +
                    "JSON arrays.");
            }

            presentCollections[index] = true;
        }

        if (!presentCollections[0])
        {
            throw new InvalidDataException(
                $"Close-management snapshot '{_persistencePath}' is missing required state. " +
                "The legacy late-adjustment collection is the core persisted authority.");
        }

        var lastPresentIndex = 0;
        for (var index = 1; index < presentCollections.Length; index++)
        {
            if (presentCollections[index])
            {
                lastPresentIndex = index;
            }
        }

        for (var index = 0; index <= lastPresentIndex; index++)
        {
            if (!presentCollections[index])
            {
                throw new InvalidDataException(
                    $"Close-management snapshot '{_persistencePath}' has an unsupported gapped " +
                    $"state shape. Collection '{GetPersistedCollectionName(index)}' is omitted " +
                    "before a later-generation collection.");
            }
        }

        return (CloseManagementSnapshotVersion)(lastPresentIndex + 1);
    }

    private static int GetPersistedCollectionIndex(string propertyName)
    {
        if (string.Equals(
                propertyName,
                nameof(CloseManagementSnapshot.LateAdjustments),
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(
                propertyName,
                nameof(CloseManagementSnapshot.TaskSignOffs),
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(
                propertyName,
                nameof(CloseManagementSnapshot.PlanConfigurations),
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return string.Equals(
            propertyName,
            nameof(CloseManagementSnapshot.EvidenceReviews),
            StringComparison.OrdinalIgnoreCase)
            ? 3
            : -1;
    }

    private static string GetPersistedCollectionName(int index)
        => index switch
        {
            0 => nameof(CloseManagementSnapshot.LateAdjustments),
            1 => nameof(CloseManagementSnapshot.TaskSignOffs),
            2 => nameof(CloseManagementSnapshot.PlanConfigurations),
            3 => nameof(CloseManagementSnapshot.EvidenceReviews),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

    private enum CloseManagementSnapshotVersion
    {
        LateAdjustmentsOnly = 1,
        ThroughTaskSignOffs = 2,
        ThroughPlanConfigurations = 3,
        Current = 4
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
        Volatile.Write(ref _hasEstablishedPersistedSnapshot, 1);
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
        => ReadPersistedSlice(
            static snapshot => snapshot.TaskSignOffs,
            ReadInMemoryTaskSignOffs);

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
        => ReadPersistedSlice(
            static snapshot => snapshot.PlanConfigurations,
            ReadInMemoryPlanConfigurations);

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
        => ReadPersistedSlice(
            static snapshot => snapshot.EvidenceReviews,
            ReadInMemoryEvidenceReviews);

    private IReadOnlyList<WorkflowCloseEvidenceReviewRecord> ReadInMemoryEvidenceReviews()
    {
        lock (_readGate)
        {
            return _evidenceReviews
                .SelectMany(static pair => pair.Value)
                .ToArray();
        }
    }

}
