using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>Evaluates the same admission/risk policies without dispatching or taking reservations.</summary>
public sealed class OrderPreviewService : IOrderPreviewService
{
    private readonly IExecutionGateway _gateway;
    private readonly IOrderManager _orders;
    private readonly IRiskValidator _risk;
    private readonly OrderAdmissionPolicy _admission;
    private readonly IOrderMarketReferenceSource _prices;
    private readonly TimeProvider _clock;
    private readonly IOrderInstrumentCoverageSource? _coverage;

    public OrderPreviewService(IExecutionGateway gateway, IOrderManager orders, IRiskValidator risk,
        IOrderMarketReferenceSource prices, ILogger<OrderPreviewService> logger,
        BrokerageConfiguration? configuration = null, ISecurityMasterGate? securityMaster = null,
        ExecutionOperatorControlService? controls = null, IPortfolioState? portfolio = null,
        IOrderSessionPolicy? sessions = null, TimeProvider? clock = null,
        IOrderInstrumentCoverageSource? coverage = null, ILiveOrderReadinessGate? liveReadiness = null)
    {
        _gateway = gateway;
        _orders = orders;
        _risk = risk;
        _prices = prices;
        _clock = clock ?? TimeProvider.System;
        _coverage = coverage;
        _admission = new OrderAdmissionPolicy(gateway, logger, configuration, securityMaster, controls,
            liveReadinessGate: liveReadiness, portfolioState: portfolio, sessionPolicy: sessions,
            marketReferences: prices, instrumentCoverage: coverage, clock: _clock);
    }

    public async Task<OrderPreviewResult> PreviewAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var now = _clock.GetUtcNow();
        var checks = new List<OrderCheck>();
        var prepared = _admission.Prepare(request, request.ClientOrderId ?? "preview");
        var normalized = prepared.SafeRequest;
        var decision = await _admission.EvaluatePreflightAsync(prepared, ct, observationOnly: true).ConfigureAwait(false);
        if (decision.Refusal is { } refusal)
            checks.Add(new(refusal.ReasonCode ?? "ADMISSION_REJECTED", "blocked", refusal.Reason ?? "Admission refused."));
        else
            checks.Add(new("ADMISSION_CHECKED", "passed", "Structural, sizing, session and execution controls checked."));

        if (request.ClientOrderId is { } id && (_orders is OrderManagementSystem oms
            ? oms.IsClientOrderIdUnavailable(id) : _orders.GetOrder(id) is not null))
            checks.Add(new("DUPLICATE_CLIENT_ORDER_ID", "blocked", "This client order ID is already retained; inspect its outcome before retrying."));

        var reference = _prices.GetReference(normalized.Symbol, normalized.Side);
        if (reference is null || reference.Price <= 0m || reference.ObservedAt > now || now - reference.ObservedAt >= TimeSpan.FromSeconds(30))
        {
            reference = null;
            checks.Add(new("REFERENCE_UNAVAILABLE", "blocked", "A fresh executable-side quote is required."));
        }
        var supported = _coverage is not null && await _coverage.IsUsdEquityAsync(normalized, ct).ConfigureAwait(false)
            && normalized.Legs is null or { Count: 0 }
            && normalized.OptionContract is null
            && normalized.Type is OrderType.Market or OrderType.Limit or OrderType.StopMarket or OrderType.StopLimit;
        if (!supported)
            checks.Add(new("PREVIEW_COVERAGE_UNSUPPORTED", "unassessed", "This preview covers single-leg equity market, limit and stop orders only."));
        if (decision.ResolvedOrder is { } admitted && _admission.EvaluateBrokerageNotionalLimit(admitted) is { } limitReason)
            checks.Add(new("BROKERAGE_NOTIONAL_LIMIT", "blocked", limitReason));
        if (decision.ResolvedOrder is { } sized && _orders is OrderManagementSystem manager
            && await manager.EvaluateBrokerageAdmissionLimitsAsync(sized, ct).ConfigureAwait(false) is { } admissionLimit)
            checks.Add(new("BROKERAGE_ADMISSION_LIMIT", "blocked", admissionLimit));
        var requiresApproval = false;
        if (decision.ResolvedOrder is { } resolved)
        {
            if (_risk is IRiskPreviewValidator preview)
            {
                var risk = await preview.PreviewResolvedOrderAsync(resolved, ct).ConfigureAwait(false);
                requiresApproval = risk.RequiresApproval;
                foreach (var violation in risk.Violations)
                    checks.Add(new(violation.Code, risk.RequiresApproval ? "approval" : violation.IsBlocking ? "blocked" : "warning", violation.Message));
                if (!risk.IsApproved && !requiresApproval && !checks.Any(check => check.Status == "blocked"))
                    checks.Add(new(risk.Code ?? "RISK_REJECTED", "blocked", risk.RejectReason ?? "Risk evaluation refused this order."));
                if (risk.IsApproved)
                    checks.Add(new("RISK_CHECKED", "passed", "Configured risk rules checked without reserving capacity."));
            }
            else
                checks.Add(new("RISK_PREVIEW_UNAVAILABLE", "unassessed", "The active risk validator does not support observation-only evaluation."));
        }
        decimal? notional = null;
        if (supported && reference is not null && normalized.Quantity > 0m)
        {
            try
            {
                notional = decision.ResolvedOrder?.Sizing is OrderSizing.BrokerNotional dollars
                ? dollars.Amount : checked(normalized.Quantity * (normalized.LimitPrice ?? reference.Price));
            }
            catch (OverflowException) { checks.Add(new("NOTIONAL_OVERFLOW", "blocked", "Order notional exceeds decimal capacity.")); }
        }
        checks.Add(new("FEES_UNKNOWN", "warning", "Broker commissions and fees are not yet known. Estimated notional excludes costs."));
        var expires = reference is null ? now : new[] { now.AddSeconds(30), reference.ObservedAt.AddSeconds(30) }.Min();
        var fingerprint = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(normalized, ExecutionJsonContext.Default.OrderRequest));
        return new(normalized, fingerprint, now, expires,
            checks.All(check => check.Status is not ("blocked" or "unassessed")), requiresApproval,
            notional, null, reference, checks);
    }
}
