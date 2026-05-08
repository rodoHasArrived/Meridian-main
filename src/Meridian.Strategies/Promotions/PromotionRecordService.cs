using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Strategies.Promotions;

/// <summary>
/// Decision outcome for a run promotion request.
/// </summary>
public enum PromotionDecision : byte
{
    /// <summary>The promotion was approved by the operator.</summary>
    Approved = 0,

    /// <summary>The promotion was rejected by the operator.</summary>
    Rejected = 1
}

/// <summary>
/// Durable record of a single promotion approval or rejection decision.
/// </summary>
public sealed record PromotionRecord
{
    /// <summary>Strategy run identifier that was promoted or rejected.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Operator who made the decision.</summary>
    public required string OperatorId { get; init; }

    /// <summary>Reason provided by the operator for the decision.</summary>
    public required string Reason { get; init; }

    /// <summary>Approval or rejection outcome.</summary>
    public required PromotionDecision Decision { get; init; }

    /// <summary>UTC timestamp when the decision was recorded.</summary>
    public DateTimeOffset DecidedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Optional manual override reference for audit purposes.</summary>
    public string? AuditReference { get; init; }

    /// <summary>Optional manual override identifier.</summary>
    public string? ManualOverrideId { get; init; }
}

/// <summary>
/// Lightweight in-memory promotion record store.
/// Tracks operator approval and rejection decisions against strategy run IDs.
/// Intended for paper-trading cockpit and operator-triage workflows;
/// integrate with a durable store for production promotion audit trails.
/// </summary>
public sealed class PromotionRecordService
{
    private readonly ILogger<PromotionRecordService> _logger;
    private readonly List<PromotionRecord> _records = [];
    private readonly Lock _lock = new();

    public PromotionRecordService(ILogger<PromotionRecordService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Convenience constructor for test scenarios that do not require a real logger.
    /// </summary>
    public PromotionRecordService()
        : this(NullLogger<PromotionRecordService>.Instance)
    {
    }

    /// <summary>
    /// Records an approval decision for <paramref name="runId"/> and returns the persisted record.
    /// </summary>
    public Task<PromotionRecord> ApprovePromotionAsync(
        Guid runId,
        string operatorId,
        string reason,
        string? manualOverrideId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new PromotionRecord
        {
            RunId = runId,
            OperatorId = operatorId,
            Reason = reason,
            Decision = PromotionDecision.Approved,
            DecidedAt = DateTimeOffset.UtcNow,
            AuditReference = $"promo-approve-{Guid.NewGuid():N}",
            ManualOverrideId = manualOverrideId
        };

        lock (_lock)
        {
            _records.Add(record);
        }

        _logger.LogInformation(
            "Promotion approved for run {RunId} by {OperatorId}: {Reason}",
            runId,
            operatorId,
            reason);

        return Task.FromResult(record);
    }

    /// <summary>
    /// Records a rejection decision for <paramref name="runId"/> and returns the persisted record.
    /// </summary>
    public Task<PromotionRecord> RejectPromotionAsync(
        Guid runId,
        string operatorId,
        string reason,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var record = new PromotionRecord
        {
            RunId = runId,
            OperatorId = operatorId,
            Reason = reason,
            Decision = PromotionDecision.Rejected,
            DecidedAt = DateTimeOffset.UtcNow,
            AuditReference = $"promo-reject-{Guid.NewGuid():N}"
        };

        lock (_lock)
        {
            _records.Add(record);
        }

        _logger.LogInformation(
            "Promotion rejected for run {RunId} by {OperatorId}: {Reason}",
            runId,
            operatorId,
            reason);

        return Task.FromResult(record);
    }

    /// <summary>
    /// Returns all promotion records for <paramref name="runId"/> in decision order.
    /// </summary>
    public Task<IReadOnlyList<PromotionRecord>> GetPromotionHistoryAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PromotionRecord> result;
        lock (_lock)
        {
            result = _records
                .Where(r => r.RunId == runId)
                .OrderBy(static r => r.DecidedAt)
                .ToArray();
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Returns all promotion records across all runs, ordered by decision timestamp.
    /// </summary>
    public Task<IReadOnlyList<PromotionRecord>> GetAllHistoryAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PromotionRecord> result;
        lock (_lock)
        {
            result = _records
                .OrderBy(static r => r.DecidedAt)
                .ToArray();
        }

        return Task.FromResult(result);
    }
}
