using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.MiddleOffice;

/// <summary>Lifecycle state of a <see cref="WorkflowSlaTimer"/> at a point in time.</summary>
public enum WorkflowSlaState
{
    /// <summary>Within the on-track window (before the warning threshold).</summary>
    OnTrack,

    /// <summary>Past the warning threshold but not yet due.</summary>
    Warning,

    /// <summary>Past the due time without being stopped.</summary>
    Breached,

    /// <summary>Stopped (the tracked work completed) before breaching.</summary>
    Stopped,
}

/// <summary>
/// A generic service-level-agreement policy: how long a tracked activity has before it is due, and
/// the fraction of that window after which it enters a warning state. Unlike the reconciliation-only
/// <c>ReconciliationSlaCalculator</c>, this is a general middle-office timer usable for booking
/// cut-offs, break resolution, and file-distribution deadlines.
/// </summary>
public sealed record WorkflowSlaPolicy
{
    public WorkflowSlaPolicy(string policyId, TimeSpan duration, double warningFraction = 0.5)
    {
        if (string.IsNullOrWhiteSpace(policyId))
            throw new ArgumentException("SLA policy identifier must not be null or whitespace.", nameof(policyId));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "SLA duration must be positive.");
        if (warningFraction is <= 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(warningFraction), warningFraction, "Warning fraction must be in (0, 1].");

        PolicyId = policyId.Trim();
        Duration = duration;
        WarningFraction = warningFraction;
    }

    public string PolicyId { get; }

    public TimeSpan Duration { get; }

    public double WarningFraction { get; }
}

/// <summary>
/// A running SLA timer bound to a subject (a booking, a break, a delivery). Computes due and warning
/// instants from its policy and reports its state as of any evaluation time.
/// </summary>
public sealed record WorkflowSlaTimer
{
    public WorkflowSlaTimer(
        string timerId,
        string subject,
        WorkflowSlaPolicy policy,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? stoppedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(timerId))
            throw new ArgumentException("SLA timer identifier must not be null or whitespace.", nameof(timerId));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("SLA timer subject must not be null or whitespace.", nameof(subject));
        ArgumentNullException.ThrowIfNull(policy);

        TimerId = timerId.Trim();
        Subject = subject.Trim();
        Policy = policy;
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        StoppedAtUtc = stoppedAtUtc?.ToUniversalTime();
    }

    public string TimerId { get; }

    public string Subject { get; }

    public WorkflowSlaPolicy Policy { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? StoppedAtUtc { get; }

    public DateTimeOffset DueAtUtc => StartedAtUtc + Policy.Duration;

    public DateTimeOffset WarningAtUtc => StartedAtUtc + (Policy.Duration * Policy.WarningFraction);

    /// <summary>
    /// Returns a copy of this timer stopped at <paramref name="stoppedAtUtc"/>. The stop instant must
    /// not precede <see cref="StartedAtUtc"/>, so the timer can never record an impossible negative
    /// elapsed window (and thereby suppress breach evaluation over a bogus timeline).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the stop time precedes the start.</exception>
    public WorkflowSlaTimer Stop(DateTimeOffset stoppedAtUtc)
    {
        var stoppedAt = stoppedAtUtc.ToUniversalTime();
        if (stoppedAt < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stoppedAtUtc), stoppedAtUtc, "An SLA timer cannot stop before it started.");
        }

        return new(TimerId, Subject, Policy, StartedAtUtc, stoppedAt);
    }

    /// <summary>The timer's state as of <paramref name="asOfUtc"/>.</summary>
    public WorkflowSlaState StateAt(DateTimeOffset asOfUtc)
    {
        if (StoppedAtUtc is not null)
            return WorkflowSlaState.Stopped;

        var asOf = asOfUtc.ToUniversalTime();
        if (asOf >= DueAtUtc)
            return WorkflowSlaState.Breached;
        if (asOf >= WarningAtUtc)
            return WorkflowSlaState.Warning;

        return WorkflowSlaState.OnTrack;
    }

    /// <summary>True when the timer is running and has passed its due time as of <paramref name="asOfUtc"/>.</summary>
    public bool IsBreachedAt(DateTimeOffset asOfUtc) => StateAt(asOfUtc) == WorkflowSlaState.Breached;
}

/// <summary>The reconciliation dimension a middle-office activity applies to.</summary>
public enum ReconciliationDimension
{
    /// <summary>Trade / transaction reconciliation.</summary>
    Trade,

    /// <summary>Cash balance reconciliation.</summary>
    Cash,

    /// <summary>Security position reconciliation.</summary>
    Position,
}

/// <summary>Request to book a trade on trade date (T+0).</summary>
public sealed record TradeBookingRequest(
    string AccountId,
    string Symbol,
    ReconciliationDimension Dimension,
    decimal Quantity,
    decimal Amount,
    string Currency,
    DateOnly TradeDate,
    int SettlementCycleDays,
    string BookedBy,
    string? BookingId = null,
    DateTimeOffset? BookedAtUtc = null);

/// <summary>
/// A trade booked on trade date (T+0), carrying the settlement date derived from the instrument's
/// settlement cycle and the T+1 date its reconciliation is due.
/// </summary>
public sealed record TradeBooking(
    string BookingId,
    string AccountId,
    string Symbol,
    ReconciliationDimension Dimension,
    decimal Quantity,
    decimal Amount,
    string Currency,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    int SettlementCycleDays,
    DateTimeOffset BookedAtUtc,
    string BookedBy)
{
    /// <summary>
    /// The T+1 date on which this booking's trade/cash/position reconciliation is due, counted in
    /// business days (a Friday trade reconciles the following Monday).
    /// </summary>
    public DateOnly ReconciliationDueDate => MiddleOfficeBusinessDays.Add(TradeDate, 1);
}

/// <summary>
/// Weekend-aware business-day arithmetic for booking dates. Trade settlement and T+1 reconciliation
/// are conventionally counted in business days, so a Friday T+1 trade settles the following Monday
/// rather than Saturday.
/// </summary>
/// <remarks>
/// This skips Saturdays and Sundays but does not yet apply a market holiday calendar. Holiday-aware
/// settlement should consume the platform trading/holiday calendar (the security-master
/// <c>SettlementCycleDays</c> contract) as a follow-up.
/// </remarks>
internal static class MiddleOfficeBusinessDays
{
    public static DateOnly Add(DateOnly start, int businessDays)
    {
        if (businessDays <= 0)
            return start;

        var date = start;
        var added = 0;
        while (added < businessDays)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                added++;
        }

        return date;
    }
}

/// <summary>Request to raise a true-break escalation.</summary>
public sealed record TrueBreakEscalationRequest(
    string BreakId,
    BreakClassification Classification,
    ReconciliationBreakSeverity Severity,
    string Reason,
    string AssignedTo,
    WorkflowSlaPolicy? SlaPolicy = null,
    string? SubjectId = null,
    DateTimeOffset? RaisedAtUtc = null);

/// <summary>Lifecycle status of a <see cref="TrueBreakEscalation"/>.</summary>
public enum TrueBreakEscalationStatus
{
    Open,
    Escalated,
    Resolved,
}

/// <summary>
/// An escalation raised for a reconciliation break the platform classified as a genuine
/// (<see cref="BreakClassification.TrueBreak"/>) or potential break. Carries an escalation level that
/// advances when its SLA timer breaches, plus resolution provenance.
/// </summary>
public sealed record TrueBreakEscalation(
    string EscalationId,
    string BreakId,
    string SubjectId,
    BreakClassification Classification,
    ReconciliationBreakSeverity Severity,
    int Level,
    string AssignedTo,
    string Reason,
    TrueBreakEscalationStatus Status,
    DateTimeOffset RaisedAtUtc,
    WorkflowSlaTimer? Timer = null,
    DateTimeOffset? ResolvedAtUtc = null,
    string? ResolvedBy = null,
    string? ResolutionNote = null)
{
    /// <summary>True when the escalation is still open or escalated (not yet resolved).</summary>
    public bool IsOpen => Status != TrueBreakEscalationStatus.Resolved;
}

/// <summary>Normalized category of a file-distribution recipient.</summary>
public enum DistributionRecipientKind
{
    Administrator,
    Custodian,
    Counterparty,
}

/// <summary>A normalized distribution target: an administrator, custodian, or counterparty.</summary>
public sealed record DistributionRecipient
{
    public DistributionRecipient(DistributionRecipientKind kind, string name, string channel, string address)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Recipient name must not be null or whitespace.", nameof(name));
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Recipient channel must not be null or whitespace.", nameof(channel));
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Recipient address must not be null or whitespace.", nameof(address));

        Kind = kind;
        Name = name.Trim();
        Channel = channel.Trim();
        Address = address.Trim();
    }

    public DistributionRecipientKind Kind { get; }

    public string Name { get; }

    /// <summary>Transport channel, e.g. "SFTP", "Email", "SecurePortal".</summary>
    public string Channel { get; }

    /// <summary>Channel-specific destination (host, mailbox, portal id).</summary>
    public string Address { get; }
}

/// <summary>
/// Request to distribute one normalized file to a set of recipients. <see cref="ContentLocation"/> is
/// the durable, retrievable location of the deliverable — a storage handle or URI into the platform's
/// secure-distribution artifact store. A production transport fetches the bytes from there and verifies
/// them against <see cref="ContentSha256"/> before dispatch, so the archived delivery evidence reflects
/// a real transfer of an identified artifact rather than metadata alone.
/// </summary>
public sealed record FileDistributionRequest(
    string FileName,
    string ContentType,
    string ContentSha256,
    long ContentLength,
    string ContentLocation,
    IReadOnlyList<DistributionRecipient> Recipients,
    string DistributedBy,
    string? SubjectId = null,
    DateTimeOffset? DistributedAtUtc = null);

/// <summary>Terminal delivery outcome for one recipient.</summary>
public enum FileDeliveryStatus
{
    Delivered,
    Failed,
}

/// <summary>
/// An immutable archived delivery-log entry: one file, one recipient, one outcome. The middle-office
/// service retains these and also mirrors each into the tamper-evident fund-administration event log.
/// </summary>
public sealed record FileDeliveryRecord(
    string DeliveryId,
    string DistributionId,
    string FileName,
    string ContentSha256,
    long ContentLength,
    DistributionRecipient Recipient,
    FileDeliveryStatus Status,
    DateTimeOffset DeliveredAtUtc,
    string DistributedBy,
    string? FailureReason = null);

/// <summary>The outcome of a single transport dispatch attempt.</summary>
public sealed record FileDeliveryOutcome(bool Delivered, string? FailureReason = null);

/// <summary>
/// Transport seam that actually dispatches a distributed file to one recipient. A production transport
/// retrieves the deliverable from <see cref="FileDistributionRequest.ContentLocation"/>, verifies it
/// against <see cref="FileDistributionRequest.ContentSha256"/>, and dispatches it over the recipient's
/// channel. The middle-office service records <c>Delivered</c> only when the transport reports success,
/// so a real (SFTP/email/portal) transport injected in production yields honest delivery evidence.
/// </summary>
public interface IFileDistributionTransport
{
    FileDeliveryOutcome Deliver(DistributionRecipient recipient, FileDistributionRequest request);
}

/// <summary>
/// Default in-process transport that assumes success without performing real I/O. Suitable for tests
/// and local development; production hosts inject a transport that dispatches over the recipient's
/// channel and reports genuine success or failure.
/// </summary>
public sealed class LoopbackFileDistributionTransport : IFileDistributionTransport
{
    public FileDeliveryOutcome Deliver(DistributionRecipient recipient, FileDistributionRequest request)
        => new(Delivered: true);
}
