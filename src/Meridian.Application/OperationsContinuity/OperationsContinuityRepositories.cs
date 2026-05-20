using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.OperationsContinuity;

public interface IOperationsContinuityRepository
{
    Task SaveAsync(OperationsContinuityWorkflow workflow, CancellationToken ct = default);
    Task<OperationsContinuityWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<OperationsContinuityWorkflow>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default);
}

public interface IOperationsWorkflowAuditStore
{
    Task<OperationsWorkflowAuditDto> AppendAsync(OperationsWorkflowAuditDraft draft, CancellationToken ct = default);
    Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default);
}

public sealed record OperationsWorkflowAuditDraft(
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    string EventType,
    OperationsWorkflowStatusDto FromState,
    OperationsWorkflowStatusDto ToState,
    OperationsGateKeyDto? Gate,
    OperationsGateStatusDto? FromGateStatus,
    OperationsGateStatusDto? ToGateStatus,
    string Actor,
    string? Rationale,
    string? CorrelationId,
    IReadOnlyList<OperationsEvidenceLinkDto> References);

public sealed class InMemoryOperationsContinuityRepository : IOperationsContinuityRepository
{
    private readonly Dictionary<Guid, OperationsContinuityWorkflow> _workflows = [];
    private readonly Lock _lock = new();
    private readonly IOperationsStatusDerivationService _statusDerivation;

    public InMemoryOperationsContinuityRepository(IOperationsStatusDerivationService statusDerivation)
    {
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
    }

    public Task SaveAsync(OperationsContinuityWorkflow workflow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _workflows[workflow.WorkflowId] = workflow;
        }

        return Task.CompletedTask;
    }

    public Task<OperationsContinuityWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _workflows.TryGetValue(workflowId, out var workflow);
            return Task.FromResult(workflow);
        }
    }

    public Task<IReadOnlyList<OperationsContinuityWorkflow>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            IEnumerable<OperationsContinuityWorkflow> query = _workflows.Values;
            if (fundAccountId.HasValue)
            {
                query = query.Where(workflow => workflow.FundAccountId == fundAccountId.Value);
            }

            if (!string.IsNullOrWhiteSpace(periodId))
            {
                query = query.Where(workflow => string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase));
            }

            if (status.HasValue)
            {
                query = query.Where(workflow => _statusDerivation.Derive(workflow) == status.Value);
            }

            return Task.FromResult<IReadOnlyList<OperationsContinuityWorkflow>>(
                query.OrderByDescending(static workflow => workflow.UpdatedAtUtc).ToArray());
        }
    }
}

public sealed class InMemoryOperationsWorkflowAuditStore : IOperationsWorkflowAuditStore
{
    private readonly Dictionary<Guid, List<OperationsWorkflowAuditDto>> _events = [];
    private readonly Lock _lock = new();

    public Task<OperationsWorkflowAuditDto> AppendAsync(OperationsWorkflowAuditDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_events.TryGetValue(draft.WorkflowId, out var timeline))
            {
                timeline = [];
                _events[draft.WorkflowId] = timeline;
            }

            var previousHash = timeline.LastOrDefault()?.CurrentHash;
            var entry = OperationsWorkflowAuditHashing.Create(draft, previousHash, DateTimeOffset.UtcNow);
            timeline.Add(entry);
            return Task.FromResult(entry);
        }
    }

    public Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            if (!_events.TryGetValue(workflowId, out var timeline))
            {
                return Task.FromResult<IReadOnlyList<OperationsWorkflowAuditDto>>([]);
            }

            return Task.FromResult<IReadOnlyList<OperationsWorkflowAuditDto>>(
                timeline.OrderBy(static entry => entry.OccurredAtUtc).ToArray());
        }
    }
}

public sealed class FileOperationsContinuityRepository : IOperationsContinuityRepository
{
    private readonly string _directory;
    private readonly ILogger<FileOperationsContinuityRepository> _logger;
    private readonly IOperationsStatusDerivationService _statusDerivation;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

    public FileOperationsContinuityRepository(
        string dataDirectory,
        IOperationsStatusDerivationService statusDerivation,
        ILogger<FileOperationsContinuityRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _directory = Path.Combine(dataDirectory, "operations-continuity", "workflows");
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(OperationsContinuityWorkflow workflow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(workflow, _jsonOptions);
            await AtomicFileWriter.WriteAsync(GetPath(workflow.WorkflowId), json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationsContinuityWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default)
    {
        var path = GetPath(workflowId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<OperationsContinuityWorkflow>(stream, _jsonOptions, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OperationsContinuityWorkflow>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default)
    {
        var rows = new List<OperationsContinuityWorkflow>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var workflow = await JsonSerializer.DeserializeAsync<OperationsContinuityWorkflow>(stream, _jsonOptions, ct)
                    .ConfigureAwait(false);
                if (workflow is not null)
                {
                    rows.Add(workflow);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt operations continuity workflow snapshot {Path}", path);
            }
        }

        IEnumerable<OperationsContinuityWorkflow> query = rows;
        if (fundAccountId.HasValue)
        {
            query = query.Where(workflow => workflow.FundAccountId == fundAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(periodId))
        {
            query = query.Where(workflow => string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase));
        }

        if (status.HasValue)
        {
            query = query.Where(workflow => _statusDerivation.Derive(workflow) == status.Value);
        }

        return query.OrderByDescending(static workflow => workflow.UpdatedAtUtc).ToArray();
    }

    private string GetPath(Guid workflowId) => Path.Combine(_directory, $"{workflowId:N}.json");

    private static JsonSerializerOptions CreateJsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
}

public sealed class FileOperationsWorkflowAuditStore : IOperationsWorkflowAuditStore
{
    private readonly string _directory;
    private readonly ILogger<FileOperationsWorkflowAuditStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public FileOperationsWorkflowAuditStore(string dataDirectory, ILogger<FileOperationsWorkflowAuditStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _directory = Path.Combine(dataDirectory, "operations-continuity", "audit");
        Directory.CreateDirectory(_directory);
    }

    public async Task<OperationsWorkflowAuditDto> AppendAsync(OperationsWorkflowAuditDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var timeline = await GetTimelineCoreAsync(draft.WorkflowId, ct).ConfigureAwait(false);
            var previousHash = timeline.LastOrDefault()?.CurrentHash;
            var entry = OperationsWorkflowAuditHashing.Create(draft, previousHash, DateTimeOffset.UtcNow);
            var line = JsonSerializer.Serialize(entry, _jsonOptions);
            await AtomicFileWriter.AppendLinesAsync(GetPath(draft.WorkflowId), [line], ct).ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await GetTimelineCoreAsync(workflowId, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineCoreAsync(Guid workflowId, CancellationToken ct)
    {
        var path = GetPath(workflowId);
        if (!File.Exists(path))
        {
            return [];
        }

        var events = new List<OperationsWorkflowAuditDto>();
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<OperationsWorkflowAuditDto>(line, _jsonOptions);
                if (entry is not null)
                {
                    events.Add(entry);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt operations continuity audit event in {Path}", path);
            }
        }

        return events.OrderBy(static entry => entry.OccurredAtUtc).ToArray();
    }

    private string GetPath(Guid workflowId) => Path.Combine(_directory, $"{workflowId:N}.jsonl");
}
