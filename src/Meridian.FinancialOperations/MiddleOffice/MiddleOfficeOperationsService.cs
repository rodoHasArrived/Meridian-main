using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.MiddleOffice;

/// <summary>
/// In-memory middle-office managed-service workflow primitive. Turns the day's managed-service pattern
/// into internal operations: book trades on T+0, drive T+1 trade/cash/position reconciliation,
/// escalate true breaks against SLA timers, and distribute normalized files to administrators,
/// custodians, and counterparties with an archived, tamper-evident delivery log.
/// </summary>
/// <remarks>
/// This composes over — rather than replaces — the platform's existing reconciliation matching,
/// break classification (<see cref="BreakClassification"/>), and secure-distribution pipeline. Every
/// escalation, SLA breach, and delivery is mirrored into the shared
/// <see cref="IFundAdministrationEventSink"/> so the governance trail is append-only and hash-chained.
/// </remarks>
public sealed class MiddleOfficeOperationsService
{
    private readonly object _gate = new();
    private readonly IFundAdministrationEventSink _eventSink;
    private readonly IFileDistributionTransport _transport;
    private readonly Dictionary<string, TradeBooking> _bookings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrueBreakEscalation> _escalations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileDeliveryRecord> _deliveryLog = [];

    public MiddleOfficeOperationsService(IFundAdministrationEventSink eventSink, IFileDistributionTransport? transport = null)
    {
        _eventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
        _transport = transport ?? new LoopbackFileDistributionTransport();
    }

    // ── T+0 booking / T+1 reconciliation ───────────────────────────────────────

    /// <summary>Books a trade on trade date (T+0), deriving its settlement and T+1 reconciliation dates.</summary>
    public TradeBooking BookTrade(TradeBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AccountId))
            throw new ArgumentException("Trade booking must reference an account.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Symbol))
            throw new ArgumentException("Trade booking must reference a symbol.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Currency))
            throw new ArgumentException("Trade booking must reference a currency.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.BookedBy))
            throw new ArgumentException("Trade booking must record who booked it.", nameof(request));
        if (request.SettlementCycleDays < 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.SettlementCycleDays, "Settlement cycle days must be non-negative.");

        var booking = new TradeBooking(
            BookingId: string.IsNullOrWhiteSpace(request.BookingId) ? $"booking-{Guid.NewGuid():N}" : request.BookingId.Trim(),
            AccountId: request.AccountId.Trim(),
            Symbol: request.Symbol.Trim().ToUpperInvariant(),
            Dimension: request.Dimension,
            Quantity: request.Quantity,
            Amount: request.Amount,
            Currency: request.Currency.Trim().ToUpperInvariant(),
            TradeDate: request.TradeDate,
            SettlementDate: MiddleOfficeBusinessDays.Add(request.TradeDate, request.SettlementCycleDays),
            SettlementCycleDays: request.SettlementCycleDays,
            BookedAtUtc: (request.BookedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            BookedBy: request.BookedBy.Trim());

        lock (_gate)
        {
            if (_bookings.TryGetValue(booking.BookingId, out var existing))
            {
                // An exact retry (identical economics) is idempotent; a conflicting reuse of the id is
                // rejected so the original trade record cannot be silently overwritten and lost from
                // reconciliation with no amendment trail.
                if (IsSameEconomics(existing, booking))
                    return existing;

                throw new InvalidOperationException(
                    $"Trade booking '{booking.BookingId}' already exists with different details; " +
                    "use a distinct id or an audited amend/rebook flow.");
            }

            _bookings[booking.BookingId] = booking;
        }

        return booking;
    }

    private static bool IsSameEconomics(TradeBooking existing, TradeBooking candidate)
        => string.Equals(existing.AccountId, candidate.AccountId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(existing.Symbol, candidate.Symbol, StringComparison.OrdinalIgnoreCase)
           && existing.Dimension == candidate.Dimension
           && existing.Quantity == candidate.Quantity
           && existing.Amount == candidate.Amount
           && string.Equals(existing.Currency, candidate.Currency, StringComparison.OrdinalIgnoreCase)
           && existing.TradeDate == candidate.TradeDate
           && existing.SettlementCycleDays == candidate.SettlementCycleDays;

    /// <summary>All bookings, ordered by trade date then booking id.</summary>
    public IReadOnlyList<TradeBooking> Bookings
    {
        get
        {
            lock (_gate)
            {
                return _bookings.Values
                    .OrderBy(static booking => booking.TradeDate)
                    .ThenBy(static booking => booking.BookingId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <summary>Bookings whose T+1 reconciliation is due on or before <paramref name="asOf"/>.</summary>
    public IReadOnlyList<TradeBooking> BookingsDueForReconciliation(DateOnly asOf)
    {
        lock (_gate)
        {
            return _bookings.Values
                .Where(booking => booking.ReconciliationDueDate <= asOf)
                .OrderBy(static booking => booking.ReconciliationDueDate)
                .ThenBy(static booking => booking.BookingId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    // ── True-break escalation with SLA timers ──────────────────────────────────

    /// <summary>
    /// Raises an escalation for a reconciliation break. Only genuine
    /// (<see cref="BreakClassification.TrueBreak"/>) or potential breaks may be escalated; matched
    /// rows are not exceptions and are rejected.
    /// </summary>
    public TrueBreakEscalation RaiseTrueBreak(TrueBreakEscalationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BreakId))
            throw new ArgumentException("Escalation must reference a break.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Escalation must record a reason.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AssignedTo))
            throw new ArgumentException("Escalation must record an assignee.", nameof(request));
        if (request.Classification is not (BreakClassification.TrueBreak or BreakClassification.PotentialBreak))
        {
            throw new ArgumentException(
                $"Only true or potential breaks can be escalated; '{request.Classification}' is not an exception.",
                nameof(request));
        }

        var raisedAt = (request.RaisedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var escalationId = $"esc-{Guid.NewGuid():N}";
        var breakId = request.BreakId.Trim();
        // Correlate the governance event with the caller's subject (fund/account/workflow) when
        // supplied, so EventsFor(subject) finds the escalation; otherwise key it by the break itself.
        var subject = string.IsNullOrWhiteSpace(request.SubjectId) ? breakId : request.SubjectId.Trim();
        var policy = request.SlaPolicy ?? DefaultPolicyForSeverity(request.Severity);
        var timer = new WorkflowSlaTimer($"sla-{escalationId}", breakId, policy, raisedAt);

        var escalation = new TrueBreakEscalation(
            EscalationId: escalationId,
            BreakId: breakId,
            SubjectId: subject,
            Classification: request.Classification,
            Severity: request.Severity,
            Level: 0,
            AssignedTo: request.AssignedTo.Trim(),
            Reason: request.Reason.Trim(),
            Status: TrueBreakEscalationStatus.Open,
            RaisedAtUtc: raisedAt,
            Timer: timer);

        lock (_gate)
        {
            _escalations[escalationId] = escalation;
            _eventSink.Append(
                FundAdministrationEventKind.ReconciliationBreakEscalated,
                escalation.AssignedTo,
                escalation.SubjectId,
                $"True-break escalation raised at level 0 ({escalation.Severity}).",
                new Dictionary<string, string>
                {
                    ["escalationId"] = escalationId,
                    ["breakId"] = escalation.BreakId,
                    ["classification"] = escalation.Classification.ToString(),
                    ["severity"] = escalation.Severity.ToString(),
                    ["assignedTo"] = escalation.AssignedTo,
                    ["dueAtUtc"] = timer.DueAtUtc.ToString("O"),
                },
                occurredAtUtc: raisedAt);
        }

        return escalation;
    }

    /// <summary>
    /// Advances every open escalation whose SLA timer has breached as of <paramref name="asOfUtc"/> to
    /// the next escalation level, restarting its SLA for the receiving tier, and records an
    /// <see cref="FundAdministrationEventKind.SlaBreached"/> event. Returns the escalations advanced.
    /// </summary>
    public IReadOnlyList<TrueBreakEscalation> EscalateOverdue(DateTimeOffset asOfUtc)
    {
        var asOf = asOfUtc.ToUniversalTime();
        var advanced = new List<TrueBreakEscalation>();

        lock (_gate)
        {
            foreach (var escalation in _escalations.Values.Where(e => e.IsOpen && e.Timer is not null && e.Timer.IsBreachedAt(asOf)).ToArray())
            {
                var newLevel = escalation.Level + 1;
                var restartedTimer = new WorkflowSlaTimer(
                    $"sla-{escalation.EscalationId}-L{newLevel}",
                    escalation.BreakId,
                    escalation.Timer!.Policy,
                    asOf);

                var updated = escalation with
                {
                    Level = newLevel,
                    Status = TrueBreakEscalationStatus.Escalated,
                    Timer = restartedTimer,
                };
                _escalations[escalation.EscalationId] = updated;
                advanced.Add(updated);

                _eventSink.Append(
                    FundAdministrationEventKind.SlaBreached,
                    escalation.AssignedTo,
                    escalation.SubjectId,
                    $"SLA breached; escalated to level {newLevel}.",
                    new Dictionary<string, string>
                    {
                        ["escalationId"] = escalation.EscalationId,
                        ["breakId"] = escalation.BreakId,
                        ["level"] = newLevel.ToString(),
                        ["severity"] = escalation.Severity.ToString(),
                        ["breachedDueAtUtc"] = escalation.Timer!.DueAtUtc.ToString("O"),
                    },
                    occurredAtUtc: asOf);
            }
        }

        return advanced;
    }

    /// <summary>Resolves an escalation, stopping its SLA timer.</summary>
    public TrueBreakEscalation ResolveBreak(string escalationId, string resolvedBy, string resolutionNote, DateTimeOffset resolvedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionNote);

        var resolvedAt = resolvedAtUtc.ToUniversalTime();
        lock (_gate)
        {
            if (!_escalations.TryGetValue(escalationId.Trim(), out var escalation))
                throw new KeyNotFoundException($"Escalation '{escalationId}' was not found.");

            var updated = escalation with
            {
                Status = TrueBreakEscalationStatus.Resolved,
                ResolvedAtUtc = resolvedAt,
                ResolvedBy = resolvedBy.Trim(),
                ResolutionNote = resolutionNote.Trim(),
                Timer = escalation.Timer?.Stop(resolvedAt),
            };
            _escalations[updated.EscalationId] = updated;
            return updated;
        }
    }

    /// <summary>All open escalations, most severe first.</summary>
    public IReadOnlyList<TrueBreakEscalation> OpenEscalations
    {
        get
        {
            lock (_gate)
            {
                return _escalations.Values
                    .Where(static escalation => escalation.IsOpen)
                    .OrderByDescending(static escalation => escalation.Severity)
                    .ThenByDescending(static escalation => escalation.Level)
                    .ThenBy(static escalation => escalation.RaisedAtUtc)
                    .ToArray();
            }
        }
    }

    // ── Normalized file distribution + archived delivery log ────────────────────

    /// <summary>
    /// Distributes one normalized file to each recipient, appending an immutable delivery-log entry and
    /// a <see cref="FundAdministrationEventKind.FileDelivered"/> event per recipient. Returns the
    /// delivery records produced for this distribution.
    /// </summary>
    public IReadOnlyList<FileDeliveryRecord> Distribute(FileDistributionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("Distribution must reference a file name.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ContentType))
            throw new ArgumentException("Distribution must reference a content type.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ContentSha256))
            throw new ArgumentException("Distribution must carry a content checksum.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DistributedBy))
            throw new ArgumentException("Distribution must record who distributed it.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Recipients);
        if (request.Recipients.Count == 0)
            throw new ArgumentException("Distribution must target at least one recipient.", nameof(request));
        if (request.ContentLength < 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.ContentLength, "Content length must be non-negative.");

        var distributionId = $"dist-{Guid.NewGuid():N}";
        var distributedAt = (request.DistributedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var fileName = request.FileName.Trim();
        var checksum = request.ContentSha256.Trim();
        var distributedBy = request.DistributedBy.Trim();
        var subject = string.IsNullOrWhiteSpace(request.SubjectId) ? fileName : request.SubjectId.Trim();
        var records = new List<FileDeliveryRecord>(request.Recipients.Count);

        foreach (var recipient in request.Recipients)
        {
            ArgumentNullException.ThrowIfNull(recipient);

            // Attempt the actual dispatch through the transport (outside the lock, since a real
            // transport performs I/O) and drive the recorded status from its outcome, so an
            // unreachable host or invalid mailbox is captured as Failed rather than false success.
            FileDeliveryOutcome outcome;
            try
            {
                outcome = _transport.Deliver(recipient, request);
            }
            catch (Exception ex)
            {
                outcome = new FileDeliveryOutcome(false, ex.Message);
            }

            var record = new FileDeliveryRecord(
                DeliveryId: $"delivery-{Guid.NewGuid():N}",
                DistributionId: distributionId,
                FileName: fileName,
                ContentSha256: checksum,
                ContentLength: request.ContentLength,
                Recipient: recipient,
                Status: outcome.Delivered ? FileDeliveryStatus.Delivered : FileDeliveryStatus.Failed,
                DeliveredAtUtc: distributedAt,
                DistributedBy: distributedBy,
                FailureReason: outcome.Delivered ? null : outcome.FailureReason);

            records.Add(record);

            lock (_gate)
            {
                _deliveryLog.Add(record);

                // Only a genuinely-delivered file produces a FileDelivered governance event; failed
                // attempts remain in the archived delivery log but are never recorded as delivery
                // evidence.
                if (outcome.Delivered)
                {
                    _eventSink.Append(
                        FundAdministrationEventKind.FileDelivered,
                        distributedBy,
                        subject,
                        $"Delivered '{fileName}' to {recipient.Kind} '{recipient.Name}' via {recipient.Channel}.",
                        new Dictionary<string, string>
                        {
                            ["distributionId"] = distributionId,
                            ["deliveryId"] = record.DeliveryId,
                            ["recipientKind"] = recipient.Kind.ToString(),
                            ["recipientName"] = recipient.Name,
                            ["channel"] = recipient.Channel,
                            ["contentSha256"] = checksum,
                        },
                        occurredAtUtc: distributedAt);
                }
            }
        }

        return records;
    }

    /// <summary>The full archived delivery log, oldest first.</summary>
    public IReadOnlyList<FileDeliveryRecord> DeliveryLog
    {
        get
        {
            lock (_gate)
            {
                return _deliveryLog.ToArray();
            }
        }
    }

    private static WorkflowSlaPolicy DefaultPolicyForSeverity(ReconciliationBreakSeverity severity)
    {
        var hours = severity switch
        {
            ReconciliationBreakSeverity.Critical => 4,
            ReconciliationBreakSeverity.High => 8,
            ReconciliationBreakSeverity.Medium => 24,
            _ => 40,
        };

        return new WorkflowSlaPolicy($"sla-default-{severity}".ToLowerInvariant(), TimeSpan.FromHours(hours));
    }
}
