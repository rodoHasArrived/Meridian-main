using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.OperationsContinuity;

public interface IOperationsContinuityRepository
{
    Task SaveAsync(OperationsContinuityWorkflow workflow, CancellationToken ct = default);
    Task<OperationsContinuityWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<OperationsContinuityWorkflow>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default,
        Guid? ledgerBookId = null);
}

public interface IOperationsWorkflowAuditStore
{
    Task<OperationsWorkflowAuditDto> AppendAsync(OperationsWorkflowAuditDraft draft, CancellationToken ct = default);
    Task<IReadOnlyList<OperationsWorkflowAuditDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default);
}

public interface IOperationsContinuityTransitionCommitStore
{
    Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowTransitionAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        bool persistWorkflowState,
        CancellationToken ct = default);
}

public interface IOperationsContinuityTransactionalCommitStore : IOperationsContinuityTransitionCommitStore
{
    Task<OperationsContinuityTransactionalCommitResult> CommitLedgerPostingAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        LedgerJournalEntryWrite journalEntry,
        CancellationToken ct = default);
}

public interface IOperationsContinuityWorkflowStartCommitStore
{
    Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowStartAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        CancellationToken ct = default);
}

public sealed record OperationsContinuityTransactionalCommitResult(
    OperationsContinuityWorkflow Workflow,
    OperationsWorkflowAuditDto Audit);

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
    IReadOnlyList<OperationsEvidenceLinkDto> References,
    OperationsContinuityCorrelationKeysDto? CorrelationKeys = null,
    Meridian.Contracts.Operations.VerifiedOperationOutcome? Outcome = null);

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
            SaveUnsafe(workflow);
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
        CancellationToken ct = default,
        Guid? ledgerBookId = null)
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

            if (ledgerBookId.HasValue)
            {
                query = query.Where(workflow => workflow.LedgerBookId == ledgerBookId.Value);
            }

            if (status.HasValue)
            {
                query = query.Where(workflow => _statusDerivation.Derive(workflow) == status.Value);
            }

            return Task.FromResult<IReadOnlyList<OperationsContinuityWorkflow>>(
                query.OrderByDescending(static workflow => workflow.UpdatedAtUtc).ToArray());
        }
    }

    internal Lock SyncRoot => _lock;

    internal void SaveUnsafe(OperationsContinuityWorkflow workflow) =>
        _workflows[workflow.WorkflowId] = workflow;
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
            return Task.FromResult(AppendUnsafe(draft));
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

    internal Lock SyncRoot => _lock;

    internal OperationsWorkflowAuditDto AppendUnsafe(OperationsWorkflowAuditDraft draft)
    {
        if (!_events.TryGetValue(draft.WorkflowId, out var timeline))
        {
            timeline = [];
            _events[draft.WorkflowId] = timeline;
        }

        var previousHash = timeline.LastOrDefault()?.CurrentHash;
        var entry = OperationsWorkflowAuditHashing.Create(draft, previousHash, DateTimeOffset.UtcNow);
        timeline.Add(entry);
        return entry;
    }
}

internal sealed class InMemoryOperationsContinuityTransitionCommitStore(
    InMemoryOperationsContinuityRepository repository,
    IOperationsWorkflowAuditStore auditStore) : IOperationsContinuityTransitionCommitStore
{
    public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowTransitionAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        bool persistWorkflowState,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(auditDraft);
        ct.ThrowIfCancellationRequested();

        if (auditStore is InMemoryOperationsWorkflowAuditStore inMemoryAuditStore)
        {
            lock (repository.SyncRoot)
            {
                lock (inMemoryAuditStore.SyncRoot)
                {
                    var audit = inMemoryAuditStore.AppendUnsafe(auditDraft);
                    if (persistWorkflowState)
                    {
                        workflow.Touch(audit.OccurredAtUtc);
                        repository.SaveUnsafe(workflow);
                    }

                    return new OperationsContinuityTransactionalCommitResult(workflow, audit);
                }
            }
        }

        // The in-memory repository cannot fail after validation. Append first so an injected audit
        // failure leaves no state change, then publish the snapshot while still in this admitted call.
        var appendedAudit = await auditStore.AppendAsync(auditDraft, ct).ConfigureAwait(false);
        if (persistWorkflowState)
        {
            workflow.Touch(appendedAudit.OccurredAtUtc);
            await repository.SaveAsync(workflow, ct).ConfigureAwait(false);
        }

        return new OperationsContinuityTransactionalCommitResult(workflow, appendedAudit);
    }
}

public sealed class FileOperationsContinuityRepository :
    IOperationsContinuityRepository,
    IOperationsContinuityWorkflowStartCommitStore,
    IOperationsContinuityTransitionCommitStore
{
    private readonly string _dataDirectory;
    private readonly string _directory;
    private readonly ILogger<FileOperationsContinuityRepository> _logger;
    private readonly IOperationsStatusDerivationService _statusDerivation;
    private readonly SemaphoreSlim _gate;
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

    public FileOperationsContinuityRepository(
        string dataDirectory,
        IOperationsStatusDerivationService statusDerivation,
        ILogger<FileOperationsContinuityRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _statusDerivation = statusDerivation ?? throw new ArgumentNullException(nameof(statusDerivation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _directory = Path.Combine(_dataDirectory, "operations-continuity", "workflows");
        _gate = OperationsContinuityFileCommitPersistence.GetGate(_dataDirectory);
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
        OperationsContinuityWorkflow? snapshot = null;
        if (File.Exists(path))
        {
            snapshot = await LoadLegacySnapshotAsync(path, workflowId, ct).ConfigureAwait(false);
        }

        var envelope = await OperationsContinuityFileCommitPersistence
            .LoadEnvelopeAsync(_dataDirectory, workflowId, _jsonOptions, ct)
            .ConfigureAwait(false);
        return SelectLatest(snapshot, envelope?.Workflow);
    }

    public async Task<IReadOnlyList<OperationsContinuityWorkflow>> ListAsync(
        Guid? fundAccountId = null,
        string? periodId = null,
        OperationsWorkflowStatusDto? status = null,
        CancellationToken ct = default,
        Guid? ledgerBookId = null)
    {
        var rows = new Dictionary<Guid, OperationsContinuityWorkflow>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(fileName, "N", out var workflowId))
            {
                throw new InvalidDataException(
                    $"Operations continuity workflow snapshot '{path}' has an invalid workflow identity.");
            }

            var workflow = await LoadLegacySnapshotAsync(path, workflowId, ct).ConfigureAwait(false);
            rows[workflow.WorkflowId] = workflow;
        }

        foreach (var path in OperationsContinuityFileCommitPersistence.EnumerateEnvelopePaths(_dataDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(fileName, "N", out var workflowId))
            {
                throw new InvalidDataException(
                    $"Operations continuity transition envelope '{path}' has an invalid workflow identity.");
            }

            var envelope = await OperationsContinuityFileCommitPersistence
                .LoadEnvelopeAsync(_dataDirectory, workflowId, _jsonOptions, ct)
                .ConfigureAwait(false);
            if (envelope is null)
            {
                continue;
            }

            rows.TryGetValue(workflowId, out var snapshot);
            rows[workflowId] = SelectLatest(snapshot, envelope.Workflow)!;
        }

        IEnumerable<OperationsContinuityWorkflow> query = rows.Values;
        if (fundAccountId.HasValue)
        {
            query = query.Where(workflow => workflow.FundAccountId == fundAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(periodId))
        {
            query = query.Where(workflow => string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase));
        }

        if (ledgerBookId.HasValue)
        {
            query = query.Where(workflow => workflow.LedgerBookId == ledgerBookId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(workflow => _statusDerivation.Derive(workflow) == status.Value);
        }

        return query.OrderByDescending(static workflow => workflow.UpdatedAtUtc).ToArray();
    }

    public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowStartAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(auditDraft);
        if (workflow.WorkflowId != auditDraft.WorkflowId)
        {
            throw new ArgumentException("Workflow and audit draft identities must match.", nameof(auditDraft));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var envelopePath = OperationsContinuityFileCommitPersistence.GetEnvelopePath(
                _dataDirectory,
                workflow.WorkflowId);
            var timeline = await OperationsContinuityFileCommitPersistence
                .LoadTimelineAsync(_dataDirectory, workflow.WorkflowId, _jsonOptions, ct)
                .ConfigureAwait(false);
            if (File.Exists(GetPath(workflow.WorkflowId)) ||
                File.Exists(envelopePath) ||
                timeline.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Operations continuity workflow '{workflow.WorkflowId}' already has retained state or audit evidence.");
            }

            var audit = OperationsWorkflowAuditHashing.Create(auditDraft, previousHash: null, DateTimeOffset.UtcNow);
            workflow.Touch(audit.OccurredAtUtc);
            var envelope = new OperationsContinuityFileCommitEnvelope(workflow, [audit]);
            var json = JsonSerializer.Serialize(envelope, _jsonOptions);
            await AtomicFileWriter.WriteAsync(envelopePath, json, ct).ConfigureAwait(false);
            return new OperationsContinuityTransactionalCommitResult(workflow, audit);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationsContinuityTransactionalCommitResult> CommitWorkflowTransitionAsync(
        OperationsContinuityWorkflow workflow,
        OperationsWorkflowAuditDraft auditDraft,
        bool persistWorkflowState,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(auditDraft);
        if (workflow.WorkflowId != auditDraft.WorkflowId)
        {
            throw new ArgumentException("Workflow and audit draft identities must match.", nameof(auditDraft));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var timeline = await OperationsContinuityFileCommitPersistence
                .LoadTimelineAsync(_dataDirectory, workflow.WorkflowId, _jsonOptions, ct)
                .ConfigureAwait(false);
            var audit = OperationsWorkflowAuditHashing.Create(
                auditDraft,
                timeline.LastOrDefault()?.CurrentHash,
                DateTimeOffset.UtcNow);
            if (persistWorkflowState)
            {
                workflow.Touch(audit.OccurredAtUtc);
            }

            var envelope = new OperationsContinuityFileCommitEnvelope(
                workflow,
                timeline.Append(audit).ToArray());
            var json = JsonSerializer.Serialize(envelope, _jsonOptions);
            await AtomicFileWriter
                .WriteAsync(
                    OperationsContinuityFileCommitPersistence.GetEnvelopePath(_dataDirectory, workflow.WorkflowId),
                    json,
                    ct)
                .ConfigureAwait(false);
            return new OperationsContinuityTransactionalCommitResult(workflow, audit);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(Guid workflowId) => Path.Combine(_directory, $"{workflowId:N}.json");

    private async Task<OperationsContinuityWorkflow> LoadLegacySnapshotAsync(
        string path,
        Guid expectedWorkflowId,
        CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var workflow = await JsonSerializer
                .DeserializeAsync<OperationsContinuityWorkflow>(stream, _jsonOptions, ct)
                .ConfigureAwait(false);
            if (workflow is null)
            {
                throw new InvalidDataException(
                    $"Operations continuity workflow snapshot '{path}' deserialized to null.");
            }

            if (workflow.WorkflowId != expectedWorkflowId)
            {
                throw new InvalidDataException(
                    $"Operations continuity workflow snapshot '{path}' contains workflow '{workflow.WorkflowId}' instead of '{expectedWorkflowId}'.");
            }

            return workflow;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Operations continuity workflow snapshot is corrupt at {Path}", path);
            throw new InvalidDataException(
                $"Operations continuity workflow snapshot '{path}' is corrupt.",
                ex);
        }
    }

    private static OperationsContinuityWorkflow? SelectLatest(
        OperationsContinuityWorkflow? first,
        OperationsContinuityWorkflow? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return second.Version > first.Version ||
               (second.Version == first.Version && second.UpdatedAtUtc > first.UpdatedAtUtc)
            ? second
            : first;
    }

    private static JsonSerializerOptions CreateJsonOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
}

public sealed class FileOperationsWorkflowAuditStore : IOperationsWorkflowAuditStore
{
    private readonly string _dataDirectory;
    private readonly string _directory;
    private readonly ILogger<FileOperationsWorkflowAuditStore> _logger;
    private readonly SemaphoreSlim _gate;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public FileOperationsWorkflowAuditStore(string dataDirectory, ILogger<FileOperationsWorkflowAuditStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _directory = Path.Combine(_dataDirectory, "operations-continuity", "audit");
        _gate = OperationsContinuityFileCommitPersistence.GetGate(_dataDirectory);
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
        try
        {
            return await OperationsContinuityFileCommitPersistence
                .LoadTimelineAsync(_dataDirectory, workflowId, _jsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Operations continuity audit history is corrupt for workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    private string GetPath(Guid workflowId) => Path.Combine(_directory, $"{workflowId:N}.jsonl");
}

internal sealed record OperationsContinuityFileCommitEnvelope(
    OperationsContinuityWorkflow Workflow,
    IReadOnlyList<OperationsWorkflowAuditDto> Timeline);

internal static class OperationsContinuityFileCommitPersistence
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim GetGate(string dataDirectory) =>
        Gates.GetOrAdd(Path.GetFullPath(dataDirectory), static _ => new SemaphoreSlim(1, 1));

    public static string GetEnvelopePath(string dataDirectory, Guid workflowId)
    {
        var directory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "operations-continuity",
            "transition-commits");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{workflowId:N}.json");
    }

    public static IEnumerable<string> EnumerateEnvelopePaths(string dataDirectory)
    {
        var directory = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "operations-continuity",
            "transition-commits");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            : [];
    }

    public static async Task<OperationsContinuityFileCommitEnvelope?> LoadEnvelopeAsync(
        string dataDirectory,
        Guid workflowId,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
    {
        var path = GetEnvelopePath(dataDirectory, workflowId);
        if (!File.Exists(path))
        {
            return null;
        }

        OperationsContinuityFileCommitEnvelope envelope;
        try
        {
            await using var stream = File.OpenRead(path);
            envelope = await JsonSerializer
                .DeserializeAsync<OperationsContinuityFileCommitEnvelope>(stream, jsonOptions, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Operations continuity transition envelope '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Operations continuity transition envelope '{path}' is corrupt.",
                ex);
        }

        if (envelope.Workflow is null || envelope.Workflow.WorkflowId != workflowId)
        {
            throw new InvalidDataException(
                $"Operations continuity transition envelope '{path}' does not match workflow '{workflowId}'.");
        }

        if (envelope.Timeline is null ||
            envelope.Timeline.Any(entry =>
                entry.WorkflowId != workflowId ||
                entry.FundAccountId != envelope.Workflow.FundAccountId ||
                !string.Equals(entry.PeriodId, envelope.Workflow.PeriodId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Operations continuity transition envelope '{path}' contains mismatched workflow audit evidence.");
        }

        if (!OperationsWorkflowAuditHashing.TryValidateChain(
                envelope.Timeline,
                out var blockerCode,
                out var message))
        {
            throw new InvalidDataException(
                $"Operations continuity transition envelope '{path}' has an invalid audit chain ({blockerCode}): {message}");
        }

        return envelope;
    }

    public static async Task<IReadOnlyList<OperationsWorkflowAuditDto>> LoadTimelineAsync(
        string dataDirectory,
        Guid workflowId,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
    {
        var events = new Dictionary<Guid, OperationsWorkflowAuditDto>();
        var legacyPath = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "operations-continuity",
            "audit",
            $"{workflowId:N}.jsonl");
        if (File.Exists(legacyPath))
        {
            await using var stream = File.OpenRead(legacyPath);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<OperationsWorkflowAuditDto>(line, jsonOptions)
                    ?? throw new InvalidDataException(
                        $"Operations continuity audit event in '{legacyPath}' deserialized to null.");
                events[entry.AuditId] = entry;
            }
        }

        var envelope = await LoadEnvelopeAsync(dataDirectory, workflowId, jsonOptions, ct)
            .ConfigureAwait(false);
        foreach (var entry in envelope?.Timeline ?? [])
        {
            events[entry.AuditId] = entry;
        }

        var timeline = events.Values
            .OrderBy(static entry => entry.OccurredAtUtc)
            .ThenBy(static entry => entry.AuditId)
            .ToArray();
        if (timeline.Length > 0 &&
            !OperationsWorkflowAuditHashing.TryValidateChain(timeline, out var blockerCode, out var message))
        {
            throw new InvalidDataException(
                $"Operations continuity audit history for workflow '{workflowId}' is invalid ({blockerCode}): {message}");
        }

        return timeline;
    }
}
