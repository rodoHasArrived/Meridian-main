using Meridian.Execution.Events;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Risk-outcome handling for the OMS pre-trade gate: the typed parked outcome for
/// governed-approval escalations and the durable retention of non-blocking risk warning
/// flags on both approved and rejected orders.
/// </summary>
public sealed partial class OrderManagementSystem
{
    /// <summary>
    /// Terminal handling for an order a risk escalation parked for governed approval: the
    /// order does not route, its tracked state mirrors a rejection (nothing is live at the
    /// broker), but the audit action and typed result distinguish "awaiting approval" from
    /// "rejected" so operators and downstream status surfaces do not count it as a breach.
    /// </summary>
    private async Task<OrderResult> ParkOrderForApprovalAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        RiskValidationResult riskResult,
        string? sessionId,
        CancellationToken ct)
    {
        var parkedState = CreateRejectedState(orderId, request, riskResult.RejectReason);
        if (_orders.TryAdd(orderId, parkedState))
        {
            TrimRetainedOrdersIfNeeded();
        }

        // The escalation is already committed to the governed queue and an operator can
        // act on it, so post-park bookkeeping must never turn that committed outcome into
        // a failed submission — the submitter would then never learn the order is parked
        // while the queue entry stays releasable. Session persistence and the audit append
        // are best-effort here; both failures are logged, not propagated.
        try
        {
            await RecordSessionOrderUpdateAsync(sessionId, parkedState, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Order {OrderId} parked for approval, but its paper-session update could not be recorded",
                LogSanitizer.Sanitize(orderId));
        }

        _logger.LogWarning(
            "Order {OrderId} parked for governed risk approval ({EscalationId})",
            LogSanitizer.Sanitize(orderId),
            LogSanitizer.Sanitize(riskResult.EscalationId));

        if (_auditTrail is not null)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["escalationId"] = riskResult.EscalationId ?? string.Empty
            };
            AppendRiskWarningsMetadata(metadata, riskResult.Warnings);

            try
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                    AuditId: Guid.NewGuid().ToString("N"),
                    Category: "Risk",
                    Action: "OrderParkedForApproval",
                    Outcome: "Parked",
                    OccurredAt: DateTimeOffset.UtcNow,
                    Actor: actor,
                    BrokerName: brokerName,
                    OrderId: orderId,
                    RunId: runId,
                    Symbol: request.Symbol,
                    CorrelationId: correlationId,
                    Message: riskResult.RejectReason,
                    Reason: "RISK_ESCALATION_PARKED",
                    Scope: BuildOrderAuditScope(request, runId),
                    Metadata: metadata), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Order {OrderId} parked for approval, but the parking audit entry could not be recorded",
                    LogSanitizer.Sanitize(orderId));
            }
        }

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = riskResult.RejectReason,
            OrderState = parkedState,
            RequiresApproval = true,
            EscalationId = riskResult.EscalationId,
            RiskWarnings = riskResult.Warnings.Count > 0 ? riskResult.Warnings : null
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetExposureReservingOrders()
    {
        var reserving = new List<OrderState>(GetOpenOrders());

        // A fill flips the tracked order terminal before ProcessFillReportAsync applies it
        // to the portfolio. During that window the exposure exists at the broker but sits
        // in neither book, so surface the un-applied increment as a reservation.
        foreach (var progress in _fillProcessing.Values)
        {
            if (progress.PortfolioApplied)
            {
                continue;
            }

            var increment = progress.FillIncrement;
            if (increment.FilledQuantity <= 0m)
            {
                continue;
            }

            reserving.Add(new OrderState
            {
                OrderId = increment.ClientOrderId ?? increment.OrderId,
                Symbol = increment.Symbol,
                Side = increment.Side,
                Type = OrderType.Market,
                Quantity = increment.FilledQuantity,
                LimitPrice = increment.FillPrice,
                Status = OrderStatus.PendingNew,
                CreatedAt = increment.Timestamp
            });
        }

        return reserving;
    }

    /// <summary>
    /// Undoes the speculative amended reservation when the gateway never accepted the
    /// modification, so the tracked order and every exposure snapshot fall back to the
    /// size the broker actually holds.
    /// </summary>
    private void RollBackSpeculativeReservation(string orderId, OrderState? speculative, OrderState original)
    {
        if (speculative is null)
        {
            return;
        }

        if (!_orders.TryUpdate(orderId, original, speculative))
        {
            // A report already advanced the order past the speculative state; the report
            // stream is authoritative from that point and must not be overwritten here.
            _logger.LogWarning(
                "Order {OrderId} amendment was refused, but its state had already advanced; leaving the tracked state to the report stream",
                LogSanitizer.Sanitize(orderId));
        }
    }

    /// <summary>
    /// Measured value of an order state under the same model the enforced rules use: the
    /// routed notional for dollar-sized orders, otherwise quantity times the order's own
    /// price. Returns null when the state carries no price of its own (a market order),
    /// where only the live mark — which the OMS does not hold — could measure it.
    /// </summary>
    private static decimal? MeasureOrderValue(decimal quantity, decimal? limitPrice, decimal? stopPrice, decimal? routedNotional)
    {
        if (routedNotional is { } notional && notional > 0m)
        {
            return notional;
        }

        var price = limitPrice ?? stopPrice;
        return price is { } resolved && resolved > 0m ? Math.Abs(quantity) * resolved : null;
    }

    /// <summary>
    /// True when a modification could increase the order's measured exposure. This mirrors
    /// the enforcement valuation (<c>OrderNotionalResolver</c>), which values an order at
    /// the larger of its own price and the live mark: a higher price raises the measured
    /// notional on either side, so a raised sell limit is risk-increasing too. Quantity
    /// increases always qualify. When neither the current nor the amended order carries a
    /// price, the amendment is treated as risk-increasing so the rules get to decide.
    /// </summary>
    private static bool IsRiskIncreasing(OrderState state, OrderModification modification)
    {
        if (modification.NewQuantity is { } newQuantity && Math.Abs(newQuantity) > Math.Abs(state.Quantity))
        {
            return true;
        }

        // Any price increase raises the measured notional under the enforcement model,
        // regardless of side. A price decrease can only lower it (a marketable order is
        // already valued at the mark), so it is never risk-increasing.
        if (modification.NewLimitPrice is { } newLimit &&
            newLimit > (state.LimitPrice ?? 0m))
        {
            return true;
        }

        return modification.NewStopPrice is { } newStop && newStop > (state.StopPrice ?? 0m);
    }

    /// <summary>
    /// Reconstructs the order the gateway would hold after <paramref name="modification"/>,
    /// so the pre-trade rules evaluate the proposed order rather than the original.
    /// </summary>
    private static OrderRequest BuildAmendedRequest(OrderState state, OrderModification modification) => new()
    {
        Symbol = state.Symbol,
        Side = state.Side,
        Type = state.Type,
        Quantity = modification.NewQuantity ?? state.Quantity,
        LimitPrice = modification.NewLimitPrice ?? state.LimitPrice,
        StopPrice = modification.NewStopPrice ?? state.StopPrice,
        ClientOrderId = state.OrderId,
        StrategyId = state.StrategyId,
        FundAccountId = state.FundAccountId
    };

    /// <summary>
    /// Builds the request the risk rules should evaluate for an amendment. The exposure
    /// snapshot already reserves the working order at its current size, so evaluating the
    /// full amended order would double-count it — raising a $1k working buy to $2k would
    /// project $3k. When both sizes are measurable, this returns a probe carrying only the
    /// incremental value, so snapshot + probe equals the post-amendment book. When the
    /// order cannot be measured from its own fields (a market order priced off the live
    /// mark), it falls back to the full amended request, which is conservative.
    /// </summary>
    private static OrderRequest BuildAmendmentProbe(OrderState state, OrderModification modification)
    {
        var amended = BuildAmendedRequest(state, modification);
        var probeMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [RiskEscalationQueueService.EvaluationOnlyMetadataKey] = "true"
        };

        var currentValue = MeasureOrderValue(state.Quantity, state.LimitPrice, state.StopPrice, state.RoutedNotional);
        var amendedValue = MeasureOrderValue(amended.Quantity, amended.LimitPrice, amended.StopPrice, routedNotional: null);
        if (currentValue is not { } current || amendedValue is not { } proposed || proposed <= current)
        {
            return amended with { Metadata = probeMetadata };
        }

        var incrementalValue = proposed - current;
        var probePrice = amended.LimitPrice ?? amended.StopPrice ?? 0m;
        if (probePrice <= 0m)
        {
            return amended with { Metadata = probeMetadata };
        }

        return amended with
        {
            Quantity = incrementalValue / probePrice,
            Metadata = probeMetadata
        };
    }

    private static IReadOnlyDictionary<string, string>? BuildRiskWarningsAuditMetadata(
        IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AppendRiskWarningsMetadata(metadata, warnings);
        return metadata;
    }

    private static void AppendRiskWarningsMetadata(
        IDictionary<string, string> metadata,
        IReadOnlyList<string> warnings)
    {
        for (var i = 0; i < warnings.Count; i++)
        {
            metadata[$"warning{i + 1}"] = warnings[i];
        }
    }

    private async Task RecordRiskWarningsAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        IReadOnlyList<string> warnings,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Order {OrderId} approved with {WarningCount} non-blocking risk flag(s)",
            LogSanitizer.Sanitize(orderId),
            warnings.Count);

        if (_auditTrail is null)
        {
            return;
        }

        try
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AppendRiskWarningsMetadata(metadata, warnings);

            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Risk",
                Action: "RiskWarningsFlagged",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: actor,
                BrokerName: brokerName,
                OrderId: orderId,
                RunId: runId,
                Symbol: request.Symbol,
                CorrelationId: correlationId,
                Message: $"Order approved with {warnings.Count} non-blocking risk flag(s).",
                Scope: BuildOrderAuditScope(request, runId),
                Metadata: metadata), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Order {OrderId} risk warnings could not be recorded to the audit trail",
                LogSanitizer.Sanitize(orderId));
        }
    }

    private sealed class FillProcessingProgress(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        bool isTrackedOrder)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ExecutionReport FillIncrement { get; } = fillIncrement;
        public decimal CumulativeFilledQuantity { get; } = cumulativeFilledQuantity;
        public bool IsTrackedOrder { get; } = isTrackedOrder;
        public TradeExecutedEvent? TradeEvent { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal NewCash { get; set; }
        public bool PortfolioApplied { get; set; }
        public bool TradeEventPublished { get; set; }
        public bool SessionRecorded { get; set; }
        public bool ExecutionReportPublished { get; set; }
        public volatile bool IsComplete;
    }
}
