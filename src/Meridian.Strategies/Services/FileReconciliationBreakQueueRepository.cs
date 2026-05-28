using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

public sealed class FileReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
{
    private readonly string _snapshotPath;
    private readonly string _auditPath;
    private readonly ILogger<FileReconciliationBreakQueueRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Dictionary<string, ReconciliationBreakQueueItem>? _items;
    private readonly IReconciliationCaseWorkflowService _workflowService = new ReconciliationCaseWorkflowService();

    public FileReconciliationBreakQueueRepository(string dataDirectory, ILogger<FileReconciliationBreakQueueRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Directory.CreateDirectory(dataDirectory);
        _snapshotPath = Path.Combine(dataDirectory, "reconciliation-break-queue.json");
        _auditPath = Path.Combine(dataDirectory, "reconciliation-break-queue-audit.jsonl");
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            IEnumerable<ReconciliationBreakQueueItem> items = _items!.Values;
            if (status.HasValue)
            {
                items = items.Where(item => item.Status == status.Value);
            }

            return items
                .OrderByDescending(static item => item.LastUpdatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _items!.GetValueOrDefault(breakId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (_items!.ContainsKey(item.BreakId))
            {
                return false;
            }

            _items[item.BreakId] = item with { Score = ComputeScore(item), SlaDueAt = ComputeSlaDueAt(item), SlaBreached = IsSlaBreached(item) };
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: item.BreakId,
                EventType: "Seeded",
                PreviousStatus: null,
                NewStatus: item.Status,
                PreviousLifecycleState: null,
                NewLifecycleState: item.LifecycleState,
                OccurredAt: item.DetectedAt,
                AssignedTo: item.AssignedTo,
                ReviewedBy: item.ReviewedBy,
                ResolvedBy: item.ResolvedBy,
                Note: item.ResolutionNote,
                ExceptionRoute: item.ExceptionRoute,
                ToleranceBand: item.ToleranceBand,
                RequiredSignoffRole: item.RequiredSignoffRole,
                SignoffStatus: item.SignoffStatus,
                ExternalAccountId: item.ExternalAccountId,
                CustodianId: item.CustodianId,
                UpstreamSyncCursor: item.UpstreamSyncCursor,
                Actor: item.AssignedTo ?? item.ReviewedBy ?? item.ResolvedBy,
                BeforePayload: null,
                AfterPayload: JsonSerializer.Serialize(item, _jsonOptions)), ct).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _items![item.BreakId] = item;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.Remove(breakId))
            {
                return false;
            }

            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(request.BreakId, out var item))
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.NotFound,
                    Item: null,
                    Error: "Break was not found.");
            }

            if (item.Status != ReconciliationBreakQueueStatus.Open)
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                    Item: item,
                    Error: $"Cannot move break from {item.Status} to {ReconciliationBreakQueueStatus.InReview}.");
            }

            var now = DateTimeOffset.UtcNow;
            var transitioned = _workflowService.Apply(item, new ReconciliationCaseTransitionCommand(
                request.BreakId,
                ReconciliationCaseTransitionAction.StartReview,
                request.ReviewedBy,
                request.ReviewNote ?? "Review started.",
                ["review-note"]), now);
            if (transitioned.Status != ReconciliationBreakQueueTransitionStatus.Success || transitioned.Item is null)
            {
                return transitioned;
            }
            var updated = transitioned.Item with
            {
                AssignedTo = request.AssignedTo,
                ReviewedBy = request.ReviewedBy,
                ReviewedAt = now,
                ResolutionNote = request.ReviewNote,
                SignoffStatus = "in-review"
            };

            _items[request.BreakId] = updated with { Score = ComputeScore(updated), SlaDueAt = ComputeSlaDueAt(updated), SlaBreached = IsSlaBreached(updated) };
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: request.BreakId,
                EventType: "ReviewStarted",
                PreviousStatus: item.Status,
                NewStatus: updated.Status,
                PreviousLifecycleState: item.LifecycleState,
                NewLifecycleState: updated.LifecycleState,
                OccurredAt: now,
                AssignedTo: request.AssignedTo,
                ReviewedBy: request.ReviewedBy,
                ResolvedBy: null,
                Note: request.ReviewNote,
                ExceptionRoute: updated.ExceptionRoute,
                ToleranceBand: updated.ToleranceBand,
                RequiredSignoffRole: updated.RequiredSignoffRole,
                SignoffStatus: updated.SignoffStatus,
                ExternalAccountId: updated.ExternalAccountId,
                CustodianId: updated.CustodianId,
                UpstreamSyncCursor: updated.UpstreamSyncCursor,
                Actor: request.ReviewedBy,
                BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
                AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions)), ct).ConfigureAwait(false);

            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Status is not ReconciliationBreakQueueStatus.Resolved and not ReconciliationBreakQueueStatus.Dismissed)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                Item: null,
                Error: "Resolve transition only supports Resolved or Dismissed.");
        }
        if (string.IsNullOrWhiteSpace(request.OperatorRationale))
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                Item: null,
                Error: "Operator rationale is required.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(request.BreakId, out var item))
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.NotFound,
                    Item: null,
                    Error: "Break was not found.");
            }

            if (item.Status != ReconciliationBreakQueueStatus.InReview)
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                    Item: item,
                    Error: $"Cannot move break from {item.Status} to {request.Status}.");
            }

            var now = DateTimeOffset.UtcNow;
            var approval = _workflowService.Apply(item, new ReconciliationCaseTransitionCommand(request.BreakId, ReconciliationCaseTransitionAction.RequestApproval, request.ResolvedBy, request.OperatorRationale, ["resolution-note"]), now);
            if (approval.Status != ReconciliationBreakQueueTransitionStatus.Success || approval.Item is null)
            {
                return approval;
            }
            var updated = approval.Item with
            {
                Status = request.Status,
                ResolvedBy = request.ResolvedBy,
                ResolvedAt = now,
                ResolutionNote = request.ResolutionNote,
                SignoffStatus = request.Status == ReconciliationBreakQueueStatus.Resolved
                    ? "signed-off"
                    : "dismissed",
                SignoffHistory = (item.SignoffHistory ?? []).Concat(
                [
                    new ReconciliationCaseSignoffRecord(request.ResolvedBy, item.RequiredSignoffRole ?? "operator", request.Status.ToString(), request.OperatorRationale, now)
                ]).ToArray()
            };

            _items[request.BreakId] = updated with { Score = ComputeScore(updated), SlaDueAt = ComputeSlaDueAt(updated), SlaBreached = IsSlaBreached(updated) };
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: request.BreakId,
                EventType: request.Status == ReconciliationBreakQueueStatus.Resolved ? "Resolved" : "Closed",
                PreviousStatus: item.Status,
                NewStatus: updated.Status,
                PreviousLifecycleState: item.LifecycleState,
                NewLifecycleState: updated.LifecycleState,
                OccurredAt: now,
                AssignedTo: updated.AssignedTo,
                ReviewedBy: updated.ReviewedBy,
                ResolvedBy: request.ResolvedBy,
                Note: request.ResolutionNote,
                ExceptionRoute: updated.ExceptionRoute,
                ToleranceBand: updated.ToleranceBand,
                RequiredSignoffRole: updated.RequiredSignoffRole,
                SignoffStatus: updated.SignoffStatus,
                ExternalAccountId: updated.ExternalAccountId,
                CustodianId: updated.CustodianId,
                UpstreamSyncCursor: updated.UpstreamSyncCursor,
                Actor: request.ResolvedBy,
                BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
                AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions)), ct).ConfigureAwait(false);

            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        if (!File.Exists(_auditPath))
        {
            return [];
        }

        var events = new List<ReconciliationBreakQueueAuditEvent>();

        await using var stream = File.OpenRead(_auditPath);
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
                var auditEvent = JsonSerializer.Deserialize<ReconciliationBreakQueueAuditEvent>(line, _jsonOptions);
                if (auditEvent is not null && string.Equals(auditEvent.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(auditEvent);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt reconciliation break queue audit event in {Path}", _auditPath);
            }
        }

        return events
            .OrderBy(static entry => entry.OccurredAt)
            .ToArray();
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_items is not null)
        {
            return;
        }

        if (!File.Exists(_snapshotPath))
        {
            _items = new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        var snapshot = await JsonSerializer.DeserializeAsync<BreakQueueSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
        var loaded = snapshot?.Items ?? [];
        _items = loaded.ToDictionary(static item => item.BreakId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new BreakQueueSnapshot(_items!.Values.OrderByDescending(static item => item.LastUpdatedAt).ToArray());
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }

    private async Task AppendAuditAsync(ReconciliationBreakQueueAuditEvent auditEvent, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(auditEvent, _jsonOptions);
        await AtomicFileWriter.AppendLinesAsync(_auditPath, [line], ct).ConfigureAwait(false);
    }



    private static ReconciliationBreakScore ComputeScore(ReconciliationBreakQueueItem item)
    {
        var materiality = Math.Min(50m, Math.Abs(item.Variance));
        var ageHours = Math.Max(0d, (DateTimeOffset.UtcNow - item.DetectedAt).TotalHours);
        var ageComponent = Math.Min(25, (int)Math.Round(ageHours / 4d, MidpointRounding.AwayFromZero));
        var counterparty = string.IsNullOrWhiteSpace(item.Counterparty) ? 0 : 15;
        var recurring = item.StateTransitions?.Count(t => t.To == ReconciliationCaseLifecycleState.InReview) > 1 ? 10 : 0;
        var severityScore = (int)Math.Min(100, materiality + ageComponent + counterparty + recurring);
        var priorityScore = Math.Min(100, severityScore + (item.Severity == ReconciliationBreakSeverity.Critical ? 20 : item.Severity == ReconciliationBreakSeverity.High ? 10 : 0));
        return new ReconciliationBreakScore(severityScore, priorityScore, materiality, ageHours, counterparty, recurring, priorityScore >= 70, ComputeSlaDueAt(item), DateTimeOffset.UtcNow > ComputeSlaDueAt(item) ? DateTimeOffset.UtcNow : null);
    }

    private static DateTimeOffset ComputeSlaDueAt(ReconciliationBreakQueueItem item)
    {
        var hours = item.Severity switch
        {
            ReconciliationBreakSeverity.Critical => 4,
            ReconciliationBreakSeverity.High => 8,
            ReconciliationBreakSeverity.Medium => 24,
            _ => 48
        };
        return item.DetectedAt.AddHours(hours);
    }

    private static bool IsSlaBreached(ReconciliationBreakQueueItem item)
        => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview && DateTimeOffset.UtcNow > ComputeSlaDueAt(item);

    private sealed record BreakQueueSnapshot(IReadOnlyList<ReconciliationBreakQueueItem> Items);
}
