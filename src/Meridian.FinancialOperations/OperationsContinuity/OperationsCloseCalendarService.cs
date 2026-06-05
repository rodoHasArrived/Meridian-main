using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Storage;
using Meridian.Storage.Archival;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.FinancialOperations.OperationsContinuity;

public interface IOperationsCloseCalendarService
{
    Task<OperationsCloseCalendarDto> GetCalendarAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        CancellationToken ct = default);

    Task<OperationsCloseCalendarItemUpsertResultDto> UpsertItemAsync(
        OperationsCloseCalendarItemUpsertRequestDto request,
        string actor,
        CancellationToken ct = default);
}

public sealed class OperationsCloseCalendarService : IOperationsCloseCalendarService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IOperationsContinuityWorkflowService _workflowService;
    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private IReadOnlyList<OperationsCloseCalendarItemConfiguration> _inMemoryConfigurations = [];

    public OperationsCloseCalendarService(IOperationsContinuityWorkflowService workflowService)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
    }

    public OperationsCloseCalendarService(
        IOperationsContinuityWorkflowService workflowService,
        StorageOptions storageOptions)
        : this(workflowService)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "governance", "operations-close-calendar-items.json");
    }

    public async Task<OperationsCloseCalendarDto> GetCalendarAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        CancellationToken ct = default)
    {
        var summaries = await _workflowService
            .ListAsync(fundAccountId, periodId, status: null, ct)
            .ConfigureAwait(false);
        var items = new List<OperationsCloseCalendarItemDto>(summaries.Count);
        var configurations = ReadConfigurations();

        foreach (var summary in summaries)
        {
            ct.ThrowIfCancellationRequested();
            var workflow = await _workflowService.GetAsync(summary.WorkflowId, ct).ConfigureAwait(false);
            if (workflow is null)
            {
                continue;
            }

            var configuration = FindConfiguration(configurations, workflow.WorkflowId);
            items.Add(ToCalendarItem(workflow, configuration));
        }

        return new OperationsCloseCalendarDto(
            DateTimeOffset.UtcNow,
            items
                .OrderBy(static item => item.NextDueDate ?? DateOnly.MaxValue)
                .ThenBy(static item => item.PeriodId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.FundAccountId)
                .ToArray());
    }

    public async Task<OperationsCloseCalendarItemUpsertResultDto> UpsertItemAsync(
        OperationsCloseCalendarItemUpsertRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowId is required.", nameof(request));
        }

        var taskId = Required(request.TaskId, nameof(request.TaskId));
        var owner = Required(request.Owner, nameof(request.Owner));
        var resolvedActor = string.IsNullOrWhiteSpace(actor)
            ? Required(request.RequestedBy, nameof(request.RequestedBy))
            : actor.Trim();
        var rationale = Required(request.Rationale, nameof(request.Rationale));

        var workflow = await _workflowService.GetAsync(request.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
        {
            throw new ArgumentException("Workflow was not found.", nameof(request));
        }

        if (!workflow.CloseChecklist.Any(task => string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("TaskId must identify an existing close checklist task.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"settings-close-calendar-{Guid.NewGuid():N}"
            : request.CorrelationId.Trim();
        var configuration = new OperationsCloseCalendarItemConfiguration(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            taskId,
            request.DueDate,
            owner,
            resolvedActor,
            now,
            rationale,
            correlationId);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var configurations = ReadConfigurations().ToList();
            var index = configurations.FindIndex(item => item.WorkflowId == workflow.WorkflowId);
            if (index >= 0)
            {
                configurations[index] = configuration;
            }
            else
            {
                configurations.Add(configuration);
            }

            await SaveConfigurationsAsync(configurations, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        var item = ToCalendarItem(workflow, configuration);
        var calendar = await GetCalendarAsync(workflow.FundAccountId, workflow.PeriodId, ct).ConfigureAwait(false);
        var auditEvent = new OperationsCloseCalendarItemAuditEventDto(
            AuditId: $"ops-close-calendar-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..54],
            EventType: "operations-close-calendar-item-upserted",
            OccurredAtUtc: now,
            Actor: resolvedActor,
            Rationale: rationale,
            CorrelationId: correlationId,
            WorkflowId: workflow.WorkflowId,
            FundAccountId: workflow.FundAccountId,
            PeriodId: workflow.PeriodId,
            TaskId: taskId,
            DueDate: request.DueDate,
            Owner: owner);

        return new OperationsCloseCalendarItemUpsertResultDto(item, calendar, auditEvent);
    }

    private static OperationsCloseCalendarItemDto ToCalendarItem(
        OperationsContinuityWorkflowDto workflow,
        OperationsCloseCalendarItemConfiguration? configuration)
    {
        var readiness = workflow.CloseReadiness;
        var openTasks = workflow.CloseChecklist
            .Where(static task => !string.Equals(task.Status, "Done", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var configuredTask = configuration is null
            ? null
            : openTasks.FirstOrDefault(task => string.Equals(task.TaskId, configuration.TaskId, StringComparison.OrdinalIgnoreCase))
                ?? workflow.CloseChecklist.FirstOrDefault(task => string.Equals(task.TaskId, configuration.TaskId, StringComparison.OrdinalIgnoreCase));
        var nextTask = configuredTask ?? openTasks
            .OrderBy(static task => task.DueDate ?? DateOnly.MaxValue)
            .FirstOrDefault();
        var requiredApprovalCount = workflow.CloseChecklist.Sum(static task => Math.Max(0, task.RequiredApprovalCount));
        var completedApprovalCount = workflow.Approvals
            .Where(static approval => approval.Status is OperationsApprovalStateDto.Approved)
            .Select(static approval => approval.Reviewer ?? approval.Operator)
            .Where(static actor => !string.IsNullOrWhiteSpace(actor))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new OperationsCloseCalendarItemDto(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            workflow.Status,
            workflow.Version,
            configuration?.DueDate ?? nextTask?.DueDate,
            nextTask?.TaskId,
            nextTask?.Label,
            configuration?.Owner ?? nextTask?.Owner,
            workflow.CloseReadiness?.Severity,
            workflow.CloseReadiness?.Score,
            workflow.CloseReadiness?.IsReadyToClose == true,
            workflow.Blockers.Count,
            openTasks.Length,
            requiredApprovalCount,
            completedApprovalCount,
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflow.WorkflowId.ToString()),
            readiness?.Components ?? [],
            readiness?.Blockers ?? [],
            readiness?.NextActions ?? []);
    }

    private IReadOnlyList<OperationsCloseCalendarItemConfiguration> ReadConfigurations()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            lock (_readGate)
            {
                return _inMemoryConfigurations;
            }
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<OperationsCloseCalendarConfigurationSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.Items ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private async Task SaveConfigurationsAsync(
        IReadOnlyList<OperationsCloseCalendarItemConfiguration> configurations,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            lock (_readGate)
            {
                _inMemoryConfigurations = configurations.ToArray();
            }
            return;
        }

        var snapshot = new OperationsCloseCalendarConfigurationSnapshot(
            configurations
                .OrderBy(static item => item.PeriodId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.FundAccountId)
                .ThenBy(static item => item.WorkflowId)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
    }

    private static OperationsCloseCalendarItemConfiguration? FindConfiguration(
        IReadOnlyList<OperationsCloseCalendarItemConfiguration> configurations,
        Guid workflowId) =>
        configurations.FirstOrDefault(item => item.WorkflowId == workflowId);

    private static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return normalized;
    }

    private sealed record OperationsCloseCalendarConfigurationSnapshot(
        IReadOnlyList<OperationsCloseCalendarItemConfiguration> Items);

    private sealed record OperationsCloseCalendarItemConfiguration(
        Guid WorkflowId,
        Guid FundAccountId,
        string PeriodId,
        string TaskId,
        DateOnly DueDate,
        string Owner,
        string UpdatedBy,
        DateTimeOffset UpdatedAtUtc,
        string Rationale,
        string CorrelationId);
}
