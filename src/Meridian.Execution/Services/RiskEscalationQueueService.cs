using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
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
    DateTimeOffset? ResolvedAt = null,
    // Release is a distinct act from approval: overwriting the approver's identity and
    // timestamp when the order is later released would claim the approver performed the
    // release and lose when the decision was actually made.
    string? ReleasedBy = null,
    DateTimeOffset? ReleasedAt = null,
    bool ReleaseInFlight = false);

/// <summary>Persisted snapshot of the governed-approval queue.</summary>
public sealed record RiskEscalationSnapshot(IReadOnlyList<RiskEscalationEntry> Entries);

/// <summary>
/// Location of the durable governed-approval queue snapshot.
/// </summary>
public sealed record RiskEscalationQueueOptions(string SnapshotPath, int MaxRetainedEntries = 500)
{
    public static RiskEscalationQueueOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "execution", "risk-escalations", "escalations.json"));
}

/// <summary>
/// Governed-approval queue for risk escalations: the enforced pre-trade validator parks
/// escalated orders here instead of hard-rejecting them, operators approve or deny with an
/// audited actor and reason, and an approval releases exactly one resubmission of the
/// original order through the same risk gate. Metadata key
/// <see cref="ApprovalMetadataKey"/> carries the one-shot approval token; consumption
/// verifies the resubmitted order matches the full parked order fingerprint (every
/// routing- and payoff-relevant field) so an approval can never release a different
/// executable order than the risk desk reviewed. Queue transitions persist atomically so
/// parked decisions and armed approvals survive process restarts.
/// </summary>
public sealed class RiskEscalationQueueService : IAsyncDisposable
{
    /// <summary>Order-metadata key carrying the escalation id(s) of granted governed approvals.</summary>
    public const string ApprovalMetadataKey = "riskEscalationId";

    /// <summary>
    /// Separator for the approval token set. An order breaching several escalation-capable
    /// rules carries one token per granted decision.
    /// </summary>
    public const string TokenSeparator = ",";

    /// <summary>
    /// Order-metadata key carrying the incremental notional an amendment adds. Quantity
    /// stays the full amended quantity so position limits evaluate the real post-amendment
    /// position, while notional-based rules measure only the exposure being added (the
    /// snapshot already reserves the working order at its current size).
    /// </summary>
    public const string IncrementalNotionalMetadataKey = "riskIncrementalNotional";

    /// <summary>
    /// Order-metadata flag marking a probe evaluation: the rules should decide, but an
    /// escalation must not park a queue entry. Amendment validation uses this, since an
    /// escalated amendment could never be released as a modification and would leave an
    /// unusable approval behind.
    /// </summary>
    public const string EvaluationOnlyMetadataKey = "riskEvaluationOnly";

    private readonly ConcurrentDictionary<string, RiskEscalationEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _entryOrder = new();
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger<RiskEscalationQueueService> _logger;
    private readonly RiskEscalationQueueOptions _options;
    private readonly Lock _resolveLock = new();
    private readonly ConcurrentDictionary<Task, byte> _pendingAuditWrites = new();

    public RiskEscalationQueueService(
        ILogger<RiskEscalationQueueService> logger,
        ExecutionAuditTrailService? auditTrail = null,
        RiskEscalationQueueOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditTrail = auditTrail;
        _options = options ?? RiskEscalationQueueOptions.Default;
        LoadSnapshot();
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
            // Freeze the request: Metadata and Legs are read-only interfaces, not immutable
            // values, so retaining the caller's references would let an in-process caller
            // mutate the parked order after approval and have the release fingerprint —
            // which compares against these same objects — accept the altered order.
            Request: FreezeRequest(request),
            Reason: string.IsNullOrWhiteSpace(reason) ? "Escalated for governed approval." : reason,
            RuleName: ruleName,
            Actor: actor,
            RunId: runId,
            CorrelationId: correlationId,
            ParkedAt: DateTimeOffset.UtcNow,
            Status: RiskEscalationStatus.PendingApproval);

        lock (_resolveLock)
        {
            _entries[entry.EscalationId] = entry;
            _entryOrder.Enqueue(entry.EscalationId);
            TrimRetainedEntries();
            // Strict: an escalation the operator can see and act on must survive a restart.
            // A park that only lived in memory would hand out an escalation id, audit it,
            // and then silently vanish on the next deployment while the submitter believes
            // the order is awaiting a decision.
            if (!TryPersistSnapshotLocked())
            {
                _entries.TryRemove(entry.EscalationId, out _);
                _logger.LogError(
                    "Escalation {EscalationId} rolled back: the parked order could not be durably persisted",
                    entry.EscalationId);
                throw new InvalidOperationException(
                    "The escalation could not be durably persisted; the order was not parked for approval.");
            }
        }

        // Order details stay out of the log; the audit entry and the persisted queue
        // snapshot retain the full context under governed storage.
        _logger.LogWarning(
            "Escalation {EscalationId} parked for governed approval by rule {RuleName}",
            entry.EscalationId,
            ruleName ?? "unknown");

        RecordAudit(
            action: "OrderParkedForApproval",
            outcome: "Parked",
            entry: entry,
            message: $"Order parked for governed approval: {entry.Reason}");

        return entry;
    }

    /// <summary>
    /// Deep-copies the mutable collections on an order request so the retained escalation
    /// cannot be altered after the risk desk reviewed it.
    /// </summary>
    private static OrderRequest FreezeRequest(OrderRequest request) => request with
    {
        Metadata = request.Metadata is null
            ? null
            : new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase),
        Legs = request.Legs is null ? null : request.Legs.ToArray()
    };

    /// <summary>
    /// Atomically claims an approved entry for release. Two concurrent approve requests on
    /// the same entry would otherwise both submit the retained order; the first can fill
    /// and free its client order id before the second reaches the OMS, letting the second
    /// park a fresh escalation for an order that already executed. Returns false when the
    /// entry is not approved or a release is already in flight.
    /// </summary>
    public bool TryBeginRelease(string escalationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationId);

        lock (_resolveLock)
        {
            if (!_entries.TryGetValue(escalationId, out var entry) ||
                entry.Status != RiskEscalationStatus.Approved ||
                entry.ReleaseInFlight)
            {
                return false;
            }

            _entries[escalationId] = entry with { ReleaseInFlight = true };
            return true;
        }
    }

    /// <summary>Clears a release claim that did not result in a released order.</summary>
    public void EndRelease(string escalationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationId);

        lock (_resolveLock)
        {
            if (_entries.TryGetValue(escalationId, out var entry) && entry.ReleaseInFlight)
            {
                _entries[escalationId] = entry with { ReleaseInFlight = false };
            }
        }
    }

    /// <summary>
    /// Returns every entry that can still route an order — awaiting approval or approved
    /// and not yet released — oldest first. Used to rebuild the client-order-id
    /// reservations a restarted host would otherwise lose while this durable queue keeps
    /// the escalations themselves.
    /// </summary>
    public IReadOnlyList<RiskEscalationEntry> GetUnresolved() =>
        _entries.Values
            .Where(static entry => entry.Status is RiskEscalationStatus.PendingApproval or RiskEscalationStatus.Approved)
            .OrderBy(static entry => entry.ParkedAt)
            .ToArray();

    /// <summary>Returns entries awaiting approval, oldest first.</summary>
    public IReadOnlyList<RiskEscalationEntry> GetPending() =>
        _entries.Values
            .Where(static entry => entry.Status == RiskEscalationStatus.PendingApproval)
            .OrderBy(static entry => entry.ParkedAt)
            .ToArray();

    /// <summary>
    /// Returns recent entries newest first. Unresolved entries (pending or armed
    /// approvals) are always included regardless of age — an old escalation that still
    /// needs a decision must never scroll out of the only queue listing — while terminal
    /// Denied/Released history fills whatever remains of the <paramref name="take"/> window.
    /// </summary>
    public IReadOnlyList<RiskEscalationEntry> GetRecent(int take = 50)
    {
        var snapshot = _entries.Values.ToArray();
        var unresolved = snapshot
            .Where(static entry => entry.Status is RiskEscalationStatus.PendingApproval or RiskEscalationStatus.Approved)
            .ToArray();
        var historyBudget = Math.Max(0, Math.Max(1, take) - unresolved.Length);
        var history = snapshot
            .Where(static entry => entry.Status is not RiskEscalationStatus.PendingApproval and not RiskEscalationStatus.Approved)
            .OrderByDescending(static entry => entry.ParkedAt)
            .Take(historyBudget);

        return unresolved
            .Concat(history)
            .OrderByDescending(static entry => entry.ParkedAt)
            .ToArray();
    }

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
    /// Returns the released entry only when the token references an approved, unreleased
    /// entry whose full order fingerprint matches the resubmitted order; the entry then
    /// transitions to <see cref="RiskEscalationStatus.Released"/>. The release commits
    /// durably before it is honored: if the snapshot cannot be persisted the in-memory
    /// transition rolls back and consumption fails closed, so a restart can never reload a
    /// stale approved token for an order that already routed.
    /// </summary>
    public RiskEscalationEntry? TryConsumeApproval(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryConsumeApprovals(request).FirstOrDefault();
    }

    /// <summary>
    /// Consumes every governed approval carried on <paramref name="request"/> metadata. An
    /// order that breaches several escalation-capable rules accumulates one token per
    /// granted decision (the token value is a delimited set), and all of them must be
    /// honored in a single evaluation — otherwise each release satisfies one rule while
    /// re-parking another and the order can never route despite every decision being
    /// granted. Each token is still one-shot and fingerprint-bound individually.
    /// </summary>
    public IReadOnlyList<RiskEscalationEntry> TryConsumeApprovals(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Metadata is null ||
            !request.Metadata.TryGetValue(ApprovalMetadataKey, out var tokenValue) ||
            string.IsNullOrWhiteSpace(tokenValue))
        {
            return [];
        }

        var released = new List<RiskEscalationEntry>();
        foreach (var escalationId in SplitTokens(tokenValue))
        {
            if (TryConsumeSingleApproval(request, escalationId) is { } entry)
            {
                released.Add(entry);
            }
        }

        return released;
    }

    /// <summary>Splits a governed-approval token value into its individual escalation ids.</summary>
    public static IReadOnlyList<string> SplitTokens(string? tokenValue) =>
        string.IsNullOrWhiteSpace(tokenValue)
            ? []
            : tokenValue.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Joins escalation ids into a single governed-approval token value.</summary>
    public static string JoinTokens(IEnumerable<string> escalationIds) =>
        string.Join(TokenSeparator, escalationIds.Distinct(StringComparer.OrdinalIgnoreCase));

    private RiskEscalationEntry? TryConsumeSingleApproval(OrderRequest request, string escalationId)
    {
        lock (_resolveLock)
        {
            if (!_entries.TryGetValue(escalationId, out var entry) ||
                entry.Status != RiskEscalationStatus.Approved)
            {
                return null;
            }

            if (!FingerprintMatches(entry.Request, request))
            {
                _logger.LogWarning(
                    "Governed approval {EscalationId} rejected: the resubmitted order does not match the parked order fingerprint",
                    entry.EscalationId);
                return null;
            }

            string? releaseActor = null;
            request.Metadata?.TryGetValue("actor", out releaseActor);
            var released = entry with
            {
                Status = RiskEscalationStatus.Released,
                ReleaseInFlight = false,
                ReleasedBy = string.IsNullOrWhiteSpace(releaseActor) ? entry.ResolvedBy : releaseActor,
                ReleasedAt = DateTimeOffset.UtcNow
            };
            _entries[escalationId] = released;
            if (!TryPersistSnapshotLocked())
            {
                _entries[escalationId] = entry;
                _logger.LogError(
                    "Governed approval {EscalationId} not consumed: the release could not be durably persisted",
                    entry.EscalationId);
                return null;
            }

            RecordAudit(
                action: "ParkedOrderReleased",
                outcome: "Released",
                entry: released,
                message: $"Governed approval by {released.ResolvedBy ?? "operator"} consumed; order released to the risk gate by {released.ReleasedBy ?? "operator"}.");

            return released;
        }
    }

    /// <summary>
    /// Re-arms a released approval whose order did not route (a later hard rule rejected
    /// it), restoring the entry to <see cref="RiskEscalationStatus.Approved"/> so the
    /// operator's decision remains retryable once the blocking condition clears. Only a
    /// caller that observed the failed release may restore, and only from Released.
    /// </summary>
    public bool TryRestoreApproval(string escalationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationId);

        lock (_resolveLock)
        {
            if (!_entries.TryGetValue(escalationId, out var entry) ||
                entry.Status != RiskEscalationStatus.Released)
            {
                return false;
            }

            var restored = entry with { Status = RiskEscalationStatus.Approved };
            _entries[escalationId] = restored;
            if (!TryPersistSnapshotLocked())
            {
                // Fail closed: without a durable restore the release stays consumed.
                _entries[escalationId] = entry;
                return false;
            }

            RecordAudit(
                action: "ParkedOrderReleaseReverted",
                outcome: "Approved",
                entry: restored,
                message: "Release did not route (a later rule rejected the order); the approval is re-armed.");

            return true;
        }
    }

    /// <summary>
    /// Withdraws an escalation whose order can no longer be routed — the submitter
    /// cancelled it, or the run that owned it ended. Unlike <see cref="Deny"/> this also
    /// resolves an entry an operator already <see cref="RiskEscalationStatus.Approved"/>,
    /// because an approval that has not been released is still only a permission: the
    /// order behind it is gone. A release already in flight is left alone — that order is
    /// on its way to the broker and only its own outcome may resolve the entry. Returns
    /// null when there was nothing withdrawable, so callers can fail closed rather than
    /// reporting a cancellation the queue never accepted.
    /// </summary>
    public RiskEscalationEntry? Withdraw(string escalationId, string actor, string? reason)
        => Resolve(
            escalationId,
            RiskEscalationStatus.Denied,
            actor,
            reason,
            allowApproved: true);

    private RiskEscalationEntry? Resolve(
        string escalationId,
        RiskEscalationStatus status,
        string actor,
        string? reason,
        bool allowApproved = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationId);
        actor = string.IsNullOrWhiteSpace(actor) ? "operator" : actor.Trim();

        lock (_resolveLock)
        {
            if (!_entries.TryGetValue(escalationId, out var entry))
            {
                return null;
            }

            var resolvable = entry.Status == RiskEscalationStatus.PendingApproval ||
                (allowApproved && entry.Status == RiskEscalationStatus.Approved && !entry.ReleaseInFlight);
            if (!resolvable)
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
            if (status == RiskEscalationStatus.Denied)
            {
                // A denial must never be resurrectable: if it cannot be durably committed,
                // a restart would reload the entry as pending and a later approval could
                // release an order the desk refused. Fail the denial instead.
                if (!TryPersistSnapshotLocked())
                {
                    _entries[escalationId] = entry;
                    _logger.LogError(
                        "Risk escalation {EscalationId} denial rolled back: the decision could not be durably persisted",
                        resolved.EscalationId);
                    throw new InvalidOperationException(
                        "The escalation denial could not be durably persisted; the entry remains pending approval.");
                }
            }
            else
            {
                // Approval persistence stays best-effort because an unpersisted approval
                // fails safe: a restart reloads the entry as pending and the operator must
                // approve again before anything can release.
                PersistSnapshotLocked();
            }

            var approved = status == RiskEscalationStatus.Approved;
            _logger.LogInformation(
                "Risk escalation {EscalationId} {Outcome} by {Actor}",
                resolved.EscalationId,
                approved ? "approved" : "denied",
                LogSanitizer.Sanitize(actor));

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

    /// <summary>
    /// Metadata keys the release path itself stamps (or rewrites) on a resubmission and
    /// which therefore cannot participate in the fingerprint: the approval token, the
    /// approving actor, and the release correlation id. Every other metadata key must
    /// match the parked order exactly.
    /// </summary>
    private static readonly HashSet<string> ReleaseMetadataKeys =
        new(StringComparer.OrdinalIgnoreCase) { ApprovalMetadataKey, "actor", "correlationId" };

    /// <summary>
    /// Compares every routing- and payoff-relevant field of the parked order against the
    /// resubmission, so an approval cannot release a stop, trailing, time-in-force,
    /// account, strategy, option, or leg variation the risk desk never reviewed. The
    /// client order id is part of the fingerprint: the parking and approval audit entries
    /// are filed under it, and the OMS reserves it for this escalation alone, so a release
    /// that routed under a different id would break both correlation and that reservation.
    /// </summary>
    private static bool FingerprintMatches(OrderRequest parked, OrderRequest resubmitted) =>
        string.Equals(parked.ClientOrderId, resubmitted.ClientOrderId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(parked.Symbol, resubmitted.Symbol, StringComparison.OrdinalIgnoreCase) &&
        parked.Side == resubmitted.Side &&
        parked.Type == resubmitted.Type &&
        parked.Quantity == resubmitted.Quantity &&
        parked.LimitPrice == resubmitted.LimitPrice &&
        parked.StopPrice == resubmitted.StopPrice &&
        parked.TrailPrice == resubmitted.TrailPrice &&
        parked.TrailPercent == resubmitted.TrailPercent &&
        parked.TimeInForce == resubmitted.TimeInForce &&
        parked.FundAccountId == resubmitted.FundAccountId &&
        string.Equals(parked.StrategyId, resubmitted.StrategyId, StringComparison.Ordinal) &&
        parked.PositionIntent == resubmitted.PositionIntent &&
        Equals(parked.OptionContract, resubmitted.OptionContract) &&
        LegsMatch(parked.Legs, resubmitted.Legs) &&
        MetadataMatches(parked.Metadata, resubmitted.Metadata);

    /// <summary>
    /// Compares order metadata outside the release keys. Gateways consume metadata for
    /// notional sizing, bracket legs, extended-hours routing, and position effect, so an
    /// unconstrained metadata bag would let an approval release a materially different
    /// executable order. The comparison is default-deny: any added, removed, or altered
    /// non-release key fails the fingerprint, without this queue needing to know which
    /// keys a particular gateway happens to read.
    /// </summary>
    private static bool MetadataMatches(
        IReadOnlyDictionary<string, string>? parked,
        IReadOnlyDictionary<string, string>? resubmitted)
    {
        static IEnumerable<KeyValuePair<string, string>> Executable(IReadOnlyDictionary<string, string>? metadata) =>
            metadata is null
                ? []
                : metadata.Where(static pair => !ReleaseMetadataKeys.Contains(pair.Key));

        // Built key-by-key rather than via ToDictionary: a source bag with case-colliding
        // keys must fail the fingerprint, never throw out of a security check.
        var parkedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in Executable(parked))
        {
            if (!parkedKeys.TryAdd(key, value))
            {
                return false;
            }
        }

        var resubmittedPairs = Executable(resubmitted).ToArray();
        if (parkedKeys.Count != resubmittedPairs.Length)
        {
            return false;
        }

        foreach (var (key, value) in resubmittedPairs)
        {
            if (!parkedKeys.TryGetValue(key, out var parkedValue) ||
                !string.Equals(parkedValue, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LegsMatch(IReadOnlyList<OrderLeg>? parked, IReadOnlyList<OrderLeg>? resubmitted)
    {
        if (parked is null || parked.Count == 0)
        {
            return resubmitted is null || resubmitted.Count == 0;
        }

        return resubmitted is not null && parked.SequenceEqual(resubmitted);
    }

    private void TrimRetainedEntries()
    {
        // Never drop an unresolved escalation: Pending awaits a decision and Approved is
        // an armed one-shot release; those re-queue and the scan continues, so a single
        // long-lived unresolved entry at the head cannot shield newer terminal history
        // from trimming. The scan budget is one pass over the current order queue —
        // when everything left is protected, retention yields to correctness.
        var scanBudget = _entryOrder.Count;
        while (_entries.Count > _options.MaxRetainedEntries &&
               scanBudget-- > 0 &&
               _entryOrder.TryDequeue(out var oldestId))
        {
            if (_entries.TryGetValue(oldestId, out var oldest) &&
                oldest.Status is RiskEscalationStatus.PendingApproval or RiskEscalationStatus.Approved)
            {
                _entryOrder.Enqueue(oldestId);
                continue;
            }

            _entries.TryRemove(oldestId, out _);
        }
    }

    private void PersistSnapshotLocked()
    {
        // Best-effort form for park/approve: the in-memory transition already happened
        // and is audited; a persistence failure must not fail the pre-trade path, and an
        // unpersisted approval fails safe (a restart demands re-approval). Denial and
        // release use the strict form because their loss is fail-dangerous.
        TryPersistSnapshotLocked();
    }

    private bool TryPersistSnapshotLocked()
    {
        try
        {
            var snapshot = new RiskEscalationSnapshot(
                _entries.Values.OrderBy(static entry => entry.ParkedAt).ToArray());
            var payload = JsonSerializer.Serialize(snapshot, ExecutionJsonContext.Default.RiskEscalationSnapshot);
            AtomicFileWriter.Write(_options.SnapshotPath, payload);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to persist risk escalation queue snapshot to {SnapshotPath}",
                _options.SnapshotPath);
            return false;
        }
    }

    private void LoadSnapshot()
    {
        if (!File.Exists(_options.SnapshotPath))
        {
            // No snapshot is a legitimate first start; an unreadable one is not.
            return;
        }

        RiskEscalationSnapshot? snapshot;
        try
        {
            var payload = File.ReadAllText(_options.SnapshotPath);
            snapshot = JsonSerializer.Deserialize(payload, ExecutionJsonContext.Default.RiskEscalationSnapshot);
        }
        catch (Exception exception)
        {
            // Fail closed. Starting empty would silently erase every parked order and
            // armed approval this queue already reported as durable, leaving operators
            // unable to approve, deny, or audit decisions they were told were pending.
            throw new InvalidOperationException(
                $"The risk escalation queue snapshot at '{_options.SnapshotPath}' exists but could not be read; " +
                "refusing to start with an empty governed-approval queue.",
                exception);
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"The risk escalation queue snapshot at '{_options.SnapshotPath}' exists but contains no queue state; " +
                "refusing to start with an empty governed-approval queue.");
        }

        foreach (var entry in snapshot.Entries.OrderBy(static entry => entry.ParkedAt))
        {
            if (string.IsNullOrWhiteSpace(entry.EscalationId) || entry.Request is null)
            {
                continue;
            }

            // A release claim belongs to the process that took it. Any snapshot written
            // while one was outstanding would otherwise reload an approval no future
            // TryBeginRelease could ever claim, wedging it permanently.
            _entries[entry.EscalationId] = entry with { ReleaseInFlight = false };
            _entryOrder.Enqueue(entry.EscalationId);
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

        // Fire-and-forget with logging so audit latency never blocks the pre-trade path;
        // outstanding writes are tracked and drained on disposal so a graceful shutdown
        // cannot lose the durable approval evidence.
        var task = RecordAuditSafelyAsync(action, outcome, entry, message, metadata);
        _pendingAuditWrites.TryAdd(task, 0);
        _ = task.ContinueWith(
            completed => _pendingAuditWrites.TryRemove(completed, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Drains outstanding audit writes so shutdown cannot lose approval evidence.</summary>
    public async ValueTask DisposeAsync()
    {
        var outstanding = _pendingAuditWrites.Keys.ToArray();
        if (outstanding.Length > 0)
        {
            await Task.WhenAll(outstanding).ConfigureAwait(false);
        }
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
                // Correlate the governed decision with the order it authorized: without
                // this, an audit search keyed by the executed order id finds the
                // submission and parking entries but none of the approval lifecycle.
                OrderId: entry.Request.ClientOrderId,
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
