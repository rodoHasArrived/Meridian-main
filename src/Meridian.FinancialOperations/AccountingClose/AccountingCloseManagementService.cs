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
            NormalizeEvidenceLinks(request.EvidenceLinks));

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadLateAdjustments().ToList();
            rows.Add(new WorkflowLateAdjustmentRecord(request.WorkflowId, adjustment));
            await SaveLateAdjustmentsAsync(rows, ct).ConfigureAwait(false);
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
        var tasks = workflow.CloseChecklist
            .Select((task, index) => ToCloseTask(task, index, workflow))
            .ToArray();
        var lateAdjustments = GetLateAdjustments(workflow.WorkflowId);
        var validationIssues = BuildValidationIssues(workflow, tasks, lateAdjustments, materialityPolicy);

        return new ClosePeriodPlanDto(
            $"close-plan-{workflow.WorkflowId:D}",
            workflow.FundAccountId.ToString("D"),
            LedgerBookId: null,
            workflow.PeriodId,
            period.Start,
            period.End,
            ResolveCloseDueDate(tasks, period.End),
            IsPeriodLocked: workflow.Status == OperationsWorkflowStatusDto.Closed && workflow.ClosePackage is not null,
            tasks,
            lateAdjustments,
            materialityPolicy,
            validationIssues);
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

    private async Task SaveLateAdjustmentsAsync(
        IReadOnlyList<WorkflowLateAdjustmentRecord> rows,
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
            }
            return;
        }

        var snapshot = new CloseManagementSnapshot(
            rows
                .OrderBy(static row => row.WorkflowId)
                .ThenBy(static row => row.Adjustment.RequestedAtUtc)
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

    private static CloseTaskDto ToCloseTask(
        OperationsCloseChecklistTaskDto task,
        int index,
        OperationsContinuityWorkflowDto workflow)
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
            .ToArray();
        var evidenceLinks = NormalizeEvidenceLinks(
            [task.EvidencePointer, .. signOffs.SelectMany(static signOff => signOff.EvidenceLinks)]);

        return new CloseTaskDto(
            task.TaskId,
            task.Label,
            ResolveTaskStatus(task, dependencies, workflow.Approvals),
            task.Owner,
            task.DueDate ?? task.ExpiresOn ?? DateOnly.FromDateTime(workflow.UpdatedAtUtc.UtcDateTime),
            dependencies,
            signOffs,
            evidenceLinks,
            task.BlockingReason);
    }

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
            string.IsNullOrWhiteSpace(approval.Reviewer) ? task.Owner : "Reviewer",
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
        IReadOnlyList<OperationsApprovalDto> approvals)
    {
        if (!string.IsNullOrWhiteSpace(task.BlockingReason)
            || string.Equals(task.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return CloseTaskStatusDto.Blocked;
        }

        if (dependencies.Count > 0 && !string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase))
        {
            return CloseTaskStatusDto.WaitingOnDependency;
        }

        if (string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase))
        {
            return CloseTaskStatusDto.SignedOff;
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

        foreach (var adjustment in lateAdjustments.Where(adjustment => RequiresLateAdjustmentApproval(adjustment.Amount, policy)))
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
        IReadOnlyList<WorkflowLateAdjustmentRecord> LateAdjustments);

    private sealed record WorkflowLateAdjustmentRecord(
        Guid WorkflowId,
        LateAdjustmentRequestDto Adjustment);
}
