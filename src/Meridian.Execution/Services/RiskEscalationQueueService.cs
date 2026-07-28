using System.Collections.Concurrent;
using System.Globalization;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>Lifecycle of a parked risk escalation.</summary>
public enum RiskEscalationStatus
{
    PendingApproval,
    Approved,
    Denied,
    Released
}

/// <summary>
/// An order parked by the pre-trade risk gate awaiting governed operator approval.
/// The original <see cref="OrderRequest"/> is retained so an approval can release
/// exactly the order that was evaluated, never a caller-supplied variant.
/// </summary>
public sealed record RiskEscalationEntry(
    string EscalationId,
    OrderRequest Request,
    string Reason,
    string? RuleName,
    string? Actor,
    string? RunId,
    string? CorrelationId,
    DateTimeOffset ParkedAt,
    RiskEscalationStatus Status,
    string? ResolvedBy = null,
    string? ResolutionReason = null,
    DateTimeOffset? ResolvedAt = null);

/// <summary>
/// Governed-approval queue for risk escalations: the enforced pre-trade validator parks
/// escalated orders here instead of hard-rejecting them, operators approve or deny with an
/// audited actor and reason, and an approval releases exactly one resubmission of the
/// original order through the same risk gate. Metadata key
/// <see cref="ApprovalMetadataKey"/> carries the one-shot approval token; consumption
/// verifies the resubmitted order matches the parked fingerprint so an approval can never
/// be replayed onto a different order.
/// </summary>
public sealed class RiskEscalationQueueService
{
    /// <summary>Order-metadata key carrying the escalation id of a governed approval.</summary>
    public const string ApprovalMetadataKey = "riskEscalationId";

    private const int MaxRetainedEntries = 500;

    private readonly ConcurrentDictionary<string, RiskEscalationEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _entryOrder = new();
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger<RiskEscalationQueueService> _logger;
    private readonly Lock _resolveLock = new();

    public RiskEscalationQueueService(
        ILogger<RiskEscalationQueueService> logger,
        ExecutionAuditTrailService? auditTrail = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditTrail = auditTrail;
    }

    /// <summary>
    /// Parks <paramref name="request"/> for governed approval and returns the queue entry.
    /// </summary>
    public RiskEscalationEntry Park(
        OrderRequest request,
        string reason,
        string? ruleName = null,
        string? actor = null,
        string? runId = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = new RiskEscalationEntry(
            EscalationId: Guid.NewGuid().ToString("N"),
            Request: request,
            Reason: string.IsNullOrWhiteSpace(reason) ? "Escalated for governed approval." : reason,
            RuleName: ruleName,
            Actor: actor,
            RunId: runId,
            CorrelationId: correlationId,
            ParkedAt: DateTimeOffset.UtcNow,
            Status: RiskEscalationStatus.PendingApproval);

        _entries[entry.EscalationId] = entry;
        _entryOrder.Enqueue(entry.EscalationId);
        TrimRetainedEntries();

        _logger.LogWarning(
            "Order for {Symbol} ({Side} {Quantity}) parked for governed approval by rule {RuleName}: {Reason}",
            request.Symbol,
            request.Side,
            request.Quantity,
            ruleName ?? "unknown",
            entry.Reason);

        RecordAudit(
            action: "OrderParkedForApproval",
            outcome: "Parked",
            entry: entry,
            message: $"Order parked for governed approval: {entry.Reason}");

        return entry;
    }

    /// <summary>Returns entries awaiting approval, oldest first.</summary>
    public IReadOnlyList<RiskEscalationEntry> GetPending() =>
        _entries.Values
            .Where(static entry => entry.Status == RiskEscalationStatus.PendingApproval)
            .OrderBy(static entry => entry.ParkedAt)
            .ToArray();

    /// <summary>Returns the most recent entries in any status, newest first.</summary>
    public IReadOnlyList<RiskEscalationEntry> GetRecent(int take = 50) =>
        _entries.Values
            .OrderByDescending(static entry => entry.ParkedAt)
            .Take(Math.Max(1, take))
            .ToArray();

    /// <summary>Returns the entry with <paramref name="escalationId"/>, or null.</summary>
    public RiskEscalationEntry? TryGet(string escalationId) =>
        _entries.TryGetValue(escalationId, out var entry) ? entry : null;

    /// <summary>
    /// Approves a pending escalation, arming a one-shot release of the parked order.
    /// Returns the updated entry, or <see langword="null"/> when the entry does not exist
    /// or is no longer pending.
    /// </summary>
    public RiskEscalationEntry? Approve(string escalationId, string actor, string? reason = null)
        => Resolve(escalationId, RiskEscalationStatus.Approved, actor, reason);

    /// <summary>
    /// Denies a pending escalation. Returns the updated entry, or <see langword="null"/>
    /// when the entry does not exist or is no longer pending.
    /// </summary>
    public RiskEscalationEntry? Deny(string escalationId, string actor, string? reason = null)
        => Resolve(escalationId, RiskEscalationStatus.Denied, actor, reason);

    /// <summary>
    /// Consumes a one-shot governed approval carried on <paramref name="request"/> metadata.
    /// Returns <see langword="true"/> only when the token references an approved, unreleased
    /// entry whose order fingerprint (symbol, side, type, quantity, limit price) matches the
    /// resubmitted order; the entry then transitions to <see cref="RiskEscalationStatus.Released"/>.
    /// </summary>
    public bool TryConsumeApproval(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Metadata is null ||
            !request.Metadata.TryGetValue(ApprovalMetadataKey, out var escalationId) ||
            string.IsNullOrWhiteSpace(escalationId))
        {
            return false;
        }

        lock (_resolveLock)
        {
            if (!_entries.TryGetValue(escalationId, out var entry) ||
                entry.Status != RiskEscalationStatus.Approved)
            {
                return false;
            }

            if (!FingerprintMatches(entry.Request, request))
            {
                _logger.LogWarning(
                    "Governed approval {EscalationId} rejected: resubmitted order for {Symbol} does not match the parked order fingerprint",
                    escalationId,
                    request.Symbol);
                return false;
            }

            var released = entry with
            {
                Status = RiskEscalationStatus.Released,
                ResolvedAt = DateTimeOffset.UtcNow
            };
            _entries[escalationId] = released;

            RecordAudit(
                action: "ParkedOrderReleased",
                outcome: "Released",
                entry: released,
                message: $"Governed approval consumed; order released to the risk gate by {released.ResolvedBy ?? "operator"}.");

            return true;
        }
    }

    private RiskEscalationEntry? Resolve(
        string escalationId,
        RiskEscalationStatus status,
        string actor,
        string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationId);
        actor = string.IsNullOrWhiteSpace(actor) ? "operator" : actor.Trim();

        lock (_resolveLock)
        {
            if (!_entries.TryGetValue(escalationId, out var entry) ||
                entry.Status != RiskEscalationStatus.PendingApproval)
            {
                return null;
            }

            var resolved = entry with
            {
                Status = status,
                ResolvedBy = actor,
                ResolutionReason = reason,
                ResolvedAt = DateTimeOffset.UtcNow
            };
            _entries[escalationId] = resolved;

            var approved = status == RiskEscalationStatus.Approved;
            _logger.LogInformation(
                "Risk escalation {EscalationId} for {Symbol} {Outcome} by {Actor}: {Reason}",
                escalationId,
                entry.Request.Symbol,
                approved ? "approved" : "denied",
                actor,
                reason ?? "no reason supplied");

            RecordAudit(
                action: approved ? "ParkedOrderApproved" : "ParkedOrderDenied",
                outcome: approved ? "Approved" : "Denied",
                entry: resolved,
                message: approved
                    ? $"Governed approval granted by {actor}."
                    : $"Governed approval denied by {actor}.");

            return resolved;
        }
    }

    private static bool FingerprintMatches(OrderRequest parked, OrderRequest resubmitted) =>
        string.Equals(parked.Symbol, resubmitted.Symbol, StringComparison.OrdinalIgnoreCase) &&
        parked.Side == resubmitted.Side &&
        parked.Type == resubmitted.Type &&
        parked.Quantity == resubmitted.Quantity &&
        parked.LimitPrice == resubmitted.LimitPrice;

    private void TrimRetainedEntries()
    {
        while (_entries.Count > MaxRetainedEntries && _entryOrder.TryDequeue(out var oldestId))
        {
            // Never drop an unresolved escalation: re-queue and stop rather than losing a
            // pending governed approval to retention pressure.
            if (_entries.TryGetValue(oldestId, out var oldest) &&
                oldest.Status == RiskEscalationStatus.PendingApproval)
            {
                _entryOrder.Enqueue(oldestId);
                return;
            }

            _entries.TryRemove(oldestId, out _);
        }
    }

    private void RecordAudit(string action, string outcome, RiskEscalationEntry entry, string message)
    {
        if (_auditTrail is null)
        {
            return;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["escalationId"] = entry.EscalationId,
            ["quantity"] = entry.Request.Quantity.ToString(CultureInfo.InvariantCulture),
            ["side"] = entry.Request.Side.ToString()
        };
        if (entry.RuleName is not null)
        {
            metadata["ruleName"] = entry.RuleName;
        }

        // Fire-and-forget with logging: audit durability must not block or fail the
        // pre-trade path; the queue entry itself remains authoritative in-process.
        _ = RecordAuditSafelyAsync(action, outcome, entry, message, metadata);
    }

    private async Task RecordAuditSafelyAsync(
        string action,
        string outcome,
        RiskEscalationEntry entry,
        string message,
        IReadOnlyDictionary<string, string> metadata)
    {
        try
        {
            await _auditTrail!.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Risk",
                Action: action,
                Outcome: outcome,
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: entry.ResolvedBy ?? entry.Actor,
                OrderId: null,
                RunId: entry.RunId,
                Symbol: entry.Request.Symbol,
                CorrelationId: entry.CorrelationId,
                Message: message,
                Reason: entry.Status == RiskEscalationStatus.PendingApproval ? entry.Reason : entry.ResolutionReason ?? entry.Reason,
                Metadata: metadata)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to record risk escalation audit entry {Action} for {EscalationId}",
                action,
                entry.EscalationId);
        }
    }
}
