using System.Collections.Concurrent;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Fills for orders this process does not track: what a restarted host does with a durably
/// admitted broker event whose order lives only in the previous incarnation's memory.
/// <para>
/// Separated from the main partial because the policy here is about <em>booking</em> rather than
/// order flow: the fill is real and belongs to the connected account, the one thing lost across the
/// restart is which fund the original submission attributed it to, and the choice made is to book
/// it unattributed and say so on the audit trail rather than to acknowledge it and post nothing.
/// </para>
/// </summary>
public sealed partial class OrderManagementSystem
{
    /// <summary>
    /// Cumulative quantity an adoption assumed was booked before it, per adopted order, that no
    /// event delivered to this process has yet accounted for. The durable inbox guarantees
    /// admission, not booking, and delivers out of order: when a completion is replayed before
    /// an earlier partial of the same order, the partial's own quantity is claimed against this
    /// gap when it arrives instead of being suppressed by the order's monotonic cumulative.
    /// </summary>
    private readonly ConcurrentDictionary<string, decimal> _adoptedFillGaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a gateway report for an order this OMS does not track. A fill carrying the
    /// broker's per-event quantity is adopted into tracked state and returned with the
    /// cumulative quantity a previous host already booked, so the ordinary increment arithmetic
    /// books exactly what this event executed; every other untracked report is logged, and a
    /// fill that cannot be adopted is audited as not booked rather than acknowledged quietly.
    /// </summary>
    private async Task<(OrderState? AdoptedState, decimal BookedBeforeAdoption)> ResolveUntrackedReportAsync(
        string? orderId,
        ExecutionReport report,
        bool isFillReport,
        CancellationToken ct)
    {
        if (isFillReport && IsSnapshotDerived(report))
        {
            // A fill re-read from the broker's REST snapshot or activity history cannot be told
            // apart from a re-sighting of one the previous host already booked under the
            // stream's event identity, so adopting it would double-post exactly the fills the
            // reconnect overlap window re-reads. Only the authenticated stream's own events,
            // deduplicated durably by broker event id, are adopted; a snapshot-derived fill for
            // an untracked order is left to the brokerage activity-sync lane.
            _logger.LogWarning(
                "Received a snapshot-derived fill report for order {OrderId} ({Symbol}) not tracked by this OMS; it was NOT booked, because a re-read fill cannot be told from one already booked before a restart",
                LogSanitizer.Sanitize(report.OrderId),
                LogSanitizer.Sanitize(report.Symbol));
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId ?? report.OrderId,
                null,
                report,
                "A snapshot-derived fill (REST reconciliation or activity backfill) for an order this OMS does not track was not booked: it cannot be distinguished from a fill already booked under its stream event before a restart. Reconcile it through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
            return (null, 0m);
        }

        if (isFillReport && !IsBookableFromReportAlone(report.AssetClass))
        {
            // An option fill needs the contract multiplier and a bond fill the face-value
            // sizing that the original submission carried and this process no longer has.
            // Booking either at share semantics would post a hundredth of an option's
            // exposure or a hundred times a bond's; refusing, loudly, is the only honest
            // answer until the sizing identity travels on the report.
            _logger.LogError(
                "Received a fill report for order {OrderId} ({Symbol}, asset class {AssetClass}) not tracked by this OMS whose instrument cannot be sized from the report alone; it was NOT booked to accounting",
                LogSanitizer.Sanitize(report.OrderId),
                LogSanitizer.Sanitize(report.Symbol),
                report.AssetClass ?? "unknown");
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId ?? report.OrderId,
                null,
                report,
                $"A fill for an order this OMS does not track carries asset class '{report.AssetClass ?? "unknown"}', whose contract multiplier or face-value sizing cannot be established from the report; it was not booked. Reconcile it through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
            return (null, 0m);
        }

        if (isFillReport
            && report.LastFillQuantity is { } untrackedQuantity
            && TryDescribeMissingPositionContext(_portfolioState, report, untrackedQuantity, out var missingContext))
        {
            // Unit sizing settles what the fill is worth, not what it did to the book. A sell
            // against a long the book does not hold cannot be told from the close of a position
            // lost with the previous host, and booking it as a fresh short would invent
            // exposure and post the proceeds as a zero-gain reduction. Refused, on the record,
            // until the book carries the lot the fill reduces.
            _logger.LogError(
                "Received a fill report for order {OrderId} ({Symbol}, {Side}) not tracked by this OMS whose economics cannot be established against the book; it was NOT booked to accounting: {Reason}",
                LogSanitizer.Sanitize(report.OrderId),
                LogSanitizer.Sanitize(report.Symbol),
                report.Side,
                missingContext);
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId ?? report.OrderId,
                null,
                report,
                $"A fill for an order this OMS does not track was not booked because the book cannot establish its economics: {missingContext} Reconcile it through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
            return (null, 0m);
        }

        if (isFillReport
            && TryAdoptUntrackedFilledOrder(orderId, report, out var adoptedState, out var bookedBeforeAdoption))
        {
            // A fill the gateway delivered for an order this process never registered -- most
            // often a durably admitted event replayed into a restarted host whose in-memory
            // book started empty. The fill is real and belongs to the connected account, so
            // it is adopted into tracked state and booked through the same durable
            // accounting handoff every other fill takes, as exactly the increment the broker
            // executed. What cannot be recovered is the fund attribution the original
            // submission carried, so the fill posts unattributed to the posting scope and
            // the audit trail flags it for operator review.
            // Logged here; audited only once the accounting handoff has actually accepted the
            // fill (see RecordUntrackedFillOutcomeAsync), so the trail never claims a booking
            // the ledger did not take.
            _logger.LogWarning(
                "Adopted a fill of {LastFillQuantity} {Symbol} for order {OrderId} that this OMS did not track; it will be booked to the posting scope without fund attribution",
                report.LastFillQuantity,
                LogSanitizer.Sanitize(report.Symbol),
                LogSanitizer.Sanitize(orderId!));
            return (adoptedState, bookedBeforeAdoption);
        }

        if (isFillReport)
        {
            // Not adoptable: the report carries no per-event quantity, so the increment the
            // broker executed cannot be told from the cumulative it reports, and booking the
            // cumulative could double-post a part already booked before the restart. Loud,
            // and on the audit trail, rather than a debug-level shrug.
            _logger.LogError(
                "Received a fill report for order {OrderId} ({ReportType}, {Status}) not tracked by this OMS and carrying no per-event quantity; it was NOT booked to accounting",
                LogSanitizer.Sanitize(report.OrderId), report.ReportType, report.OrderStatus);
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId ?? report.OrderId,
                null,
                report,
                "A fill for an order this OMS does not track carried no per-event quantity and was not booked; reconcile it through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Received execution report for order {OrderId} ({ReportType}, {Status}) not tracked by this OMS",
                LogSanitizer.Sanitize(report.OrderId), report.ReportType, report.OrderStatus);
        }

        return (null, 0m);
    }

    /// <summary>
    /// Registers tracked state for a filled order this OMS never placed, from the fill report
    /// alone, so the fill can be booked as the increment the broker executed.
    /// <para>
    /// Requires the report's per-event quantity: the report's <c>FilledQuantity</c> is cumulative,
    /// and without knowing how much of it a previous incarnation of this process already booked,
    /// booking the cumulative would double-post. The adopted state starts at the cumulative
    /// minus this event's quantity, so the ordinary increment arithmetic yields exactly the
    /// event's quantity. The order type is not on the report and is recorded as Market, which is
    /// the one shape every gateway can have filled; it does not participate in booking.
    /// </para>
    /// </summary>
    private bool TryAdoptUntrackedFilledOrder(
        string? orderId,
        ExecutionReport report,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out OrderState? adopted,
        out decimal bookedBeforeAdoption)
    {
        adopted = null;
        bookedBeforeAdoption = 0m;
        if (string.IsNullOrWhiteSpace(orderId)
            || report.LastFillQuantity is not { } executedQuantity
            || executedQuantity <= 0m
            || report.FillPrice is null
            || report.FilledQuantity < executedQuantity)
        {
            return false;
        }

        var orderQuantity = Math.Max(report.OrderQuantity, report.FilledQuantity);
        var seed = new OrderState
        {
            OrderId = orderId,
            Symbol = report.Symbol,
            Side = report.Side,
            Type = OrderType.Market,
            Quantity = orderQuantity,
            FilledQuantity = report.FilledQuantity - executedQuantity,
            Status = OrderStatus.PendingNew,
            CreatedAt = report.Timestamp
        };
        var merged = ApplyReport(seed, report);
        if (!TryRegisterOrder(orderId, merged))
        {
            return false;
        }

        RememberBrokerOrderId(orderId, report);
        bookedBeforeAdoption = seed.FilledQuantity;
        if (bookedBeforeAdoption > 0m)
        {
            // Assumed booked, not known booked: an earlier event of this order still pending
            // in the durable inbox claims its own quantity against this gap when it arrives.
            _adoptedFillGaps[orderId] = bookedBeforeAdoption;
        }

        adopted = merged;
        return true;
    }

    /// <summary>
    /// Claims an earlier increment of an adopted order that was delivered after the later event
    /// that adopted it. The order's cumulative already covers this event, so the ordinary
    /// arithmetic would book nothing; instead the event's own quantity is taken from the
    /// adoption gap and returned as the cumulative to book from, so the funnel posts exactly
    /// that quantity. Returns <see langword="null"/> when the report is not such an increment,
    /// auditing the cases where it is one that cannot be booked.
    /// </summary>
    private async Task<decimal?> TryClaimLateAdoptedIncrementAsync(
        string orderId,
        OrderState trackedState,
        ExecutionReport report,
        CancellationToken ct)
    {
        if (!_adoptedFillGaps.TryGetValue(orderId, out var remainingGap) || remainingGap <= 0m)
        {
            return null;
        }

        if (IsSnapshotDerived(report))
        {
            // A snapshot re-read of an earlier fill is the one the previous host most likely
            // booked itself; only the stream's own durably deduplicated events claim the gap.
            _logger.LogDebug(
                "Snapshot-derived report for adopted order {OrderId} does not claim its adoption gap of {Gap}",
                LogSanitizer.Sanitize(orderId),
                remainingGap);
            return null;
        }

        if (report.FilledQuantity >= trackedState.FilledQuantity)
        {
            // Not an earlier event: a re-sighting of the adopting event or of a later one,
            // which the tracked cumulative already covers.
            return null;
        }

        if (report.LastFillQuantity is not { } executedQuantity || executedQuantity <= 0m || report.FillPrice is null)
        {
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId,
                trackedState,
                report,
                $"An earlier fill of an adopted order arrived after the event that adopted it, but carried no per-event quantity, so its share of the {remainingGap:G29} not yet accounted for could not be booked. Reconcile it through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
            return null;
        }

        if (executedQuantity > remainingGap)
        {
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId,
                trackedState,
                report,
                $"An earlier fill of {executedQuantity:G29} for an adopted order exceeds the {remainingGap:G29} its adoption left unaccounted for; it was not booked, because the broker's cumulative does not admit it. Reconcile the order through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
            return null;
        }

        if (TryDescribeMissingPositionContext(_portfolioState, report, executedQuantity, out var missingContext))
        {
            await TryRecordUntrackedFillAuditAsync(
                "UntrackedFillNotBooked",
                orderId,
                trackedState,
                report,
                $"An earlier fill of an adopted order was not booked because the book cannot establish its economics: {missingContext} Reconcile it through the brokerage activity-sync lane.",
                ct).ConfigureAwait(false);
            return null;
        }

        while (true)
        {
            var claimed = remainingGap - executedQuantity;
            var settled = claimed <= 0m
                ? _adoptedFillGaps.TryRemove(new KeyValuePair<string, decimal>(orderId, remainingGap))
                : _adoptedFillGaps.TryUpdate(orderId, claimed, remainingGap);
            if (settled)
            {
                break;
            }

            if (!_adoptedFillGaps.TryGetValue(orderId, out remainingGap) || remainingGap < executedQuantity)
            {
                return null; // A concurrent claim took the gap first.
            }
        }

        _logger.LogWarning(
            "Booking an earlier fill of {ExecutedQuantity} {Symbol} for adopted order {OrderId} that arrived after the event that adopted it; {RemainingGap} of the order's earlier quantity remains unaccounted for",
            executedQuantity,
            LogSanitizer.Sanitize(report.Symbol),
            LogSanitizer.Sanitize(orderId),
            Math.Max(0m, remainingGap - executedQuantity));
        return trackedState.FilledQuantity - executedQuantity;
    }

    /// <summary>
    /// Whether the book lacks the position context this fill needs to be booked honestly, and
    /// why. A buy that opens or adds to a long is established by the fill itself; a buy that
    /// covers a known short, or a sell that reduces a known long, is established by the lot it
    /// reduces. A sell into no long, or either side reversing through zero, is not: the book
    /// cannot tell it from the close of a position it has lost, so it is not bookable.
    /// </summary>
    internal static bool TryDescribeMissingPositionContext(
        Meridian.Execution.Models.IPortfolioState? portfolioState,
        ExecutionReport report,
        decimal quantity,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? reason)
    {
        reason = null;
        if (portfolioState is null)
        {
            reason = "no portfolio state is composed, so the fill cannot be applied to a book at all.";
            return true;
        }

        var held = 0m;
        if (!string.IsNullOrWhiteSpace(report.Symbol)
            && (portfolioState.Positions.TryGetValue(report.Symbol, out var position)
                || portfolioState.Positions.TryGetValue(report.Symbol.Trim().ToUpperInvariant(), out position)))
        {
            held = position.ExactQuantity;
        }

        switch (report.Side)
        {
            case OrderSide.Buy when held < 0m && quantity > -held:
                reason = $"a buy of {quantity:G29} would cover the known short of {-held:G29} {report.Symbol} and open a long with the remainder, and the book cannot establish which part closes a lost position.";
                return true;
            case OrderSide.Sell when held <= 0m:
                reason = $"the book holds no long {report.Symbol} position for the sell of {quantity:G29} to reduce; it cannot be told from the close of a position lost with the previous host, and booking it would open a short.";
                return true;
            case OrderSide.Sell when quantity > held:
                reason = $"a sell of {quantity:G29} exceeds the known long of {held:G29} {report.Symbol} and would reverse through zero, and the book cannot establish which part closes a lost position.";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Writes the adoption outcome after the fill funnel has run: booked, when the durable
    /// accounting handoff accepted the increment; failed, naming the exception, when it did not.
    /// The entry is written after the fact precisely so that it is evidence of what the ledger
    /// holds rather than of what this process intended.
    /// </summary>
    private Task RecordUntrackedFillOutcomeAsync(
        string orderId,
        OrderState adoptedState,
        ExecutionReport report,
        Exception? handoffFailure,
        CancellationToken ct,
        bool lateIncrement = false)
    {
        var origin = lateIncrement
            ? "The order was adopted from a later fill event after a restart, and this earlier fill of it arrived afterwards; its own executed quantity"
            : "The order was not tracked by this OMS (restart or out-of-band submission); the reported fill increment";
        return handoffFailure is null
            ? TryRecordUntrackedFillAuditAsync(
                "UntrackedFillAdopted",
                orderId,
                adoptedState,
                report,
                $"{origin} was booked through the durable accounting handoff to the posting scope without fund attribution and needs operator review for attribution.",
                ct)
            : TryRecordUntrackedFillAuditAsync(
                "UntrackedFillHandoffFailed",
                orderId,
                adoptedState,
                report,
                $"{origin} was adopted, but the accounting handoff did not accept it: {handoffFailure.Message}. The fill is retained for replay where a handoff-failure store is configured; verify the ledger before relying on this fill.",
                ct);
    }

    /// <summary>
    /// Whether a fill for this broker asset class can be booked from the report alone: a unit
    /// quantity at the reported price, with no contract multiplier and no percentage-of-par
    /// scaling. Equities and crypto qualify; options and fixed income do not, and an absent
    /// class is treated as unknown rather than as equity.
    /// </summary>
    internal static bool IsBookableFromReportAlone(string? assetClass) =>
        assetClass?.Trim().ToLowerInvariant() is "us_equity" or "equity" or "crypto";

    /// <summary>
    /// Whether the report was produced from a broker snapshot or activity history rather than
    /// from the authenticated stream's own event. Gateways mark such reports with a diagnostics
    /// category naming reconciliation.
    /// </summary>
    internal static bool IsSnapshotDerived(ExecutionReport report) =>
        report.Diagnostics?.Category?.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Evidence, not control flow: an audit failure must not stall the report pump.</summary>
    private async Task TryRecordUntrackedFillAuditAsync(
        string action,
        string orderId,
        OrderState? state,
        ExecutionReport report,
        string message,
        CancellationToken ct)
    {
        try
        {
            await RecordOrderLifecycleAuditAsync(
                action: action,
                outcome: "AttentionRequired",
                orderId: orderId,
                state: state,
                report: report,
                message: message,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception auditException)
        {
            _logger.LogError(
                auditException,
                "Could not audit the untracked fill outcome {Action} for order {OrderId}",
                action,
                LogSanitizer.Sanitize(orderId));
        }
    }

}
