using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Location of the persisted operator-tuned risk-rule thresholds (drawdown %, order-rate ceiling).
/// </summary>
public sealed record RiskRuleRuntimeOptions(string SnapshotPath)
{
    public static RiskRuleRuntimeOptions Default { get; } = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "workstation",
            "risk-rules.json"));
}

public sealed record RiskRuleStatusDto(
    string RuleName,
    string State,
    string Summary,
    bool IsBreached,
    string Threshold,
    string CurrentValue,
    DateTimeOffset AsOf,
    IReadOnlyList<string> RecentViolations,
    /// <summary>Live utilization of the rule's headroom (current/threshold, percent). Null when the rule has no measurable utilization or is unconfigured.</summary>
    decimal? UtilizationPercent = null,
    /// <summary>Enforced severity outcome of the rule: Warning flags, Error rejects, Escalate parks for approval, Critical trips the circuit breaker.</summary>
    string? Severity = null);

public sealed record RiskRuleConfigDto(
    string RuleName,
    decimal? DefaultMaxPositionSize,
    IReadOnlyDictionary<string, decimal>? SymbolPositionLimits,
    decimal? MaxDrawdownPercent,
    int? MaxOrdersPerMinute,
    decimal? MaxGrossExposure = null,
    decimal? MaxSymbolConcentrationPercent = null,
    decimal? MaxOrderNotional = null,
    decimal? EscalateOrderNotional = null);

public sealed record RiskRuleConfigUpdateRequest(
    decimal? DefaultMaxPositionSize = null,
    IReadOnlyDictionary<string, decimal?>? SymbolPositionLimits = null,
    decimal? MaxDrawdownPercent = null,
    int? MaxOrdersPerMinute = null,
    string? Reason = null,
    decimal? MaxGrossExposure = null,
    decimal? MaxSymbolConcentrationPercent = null,
    decimal? MaxOrderNotional = null,
    decimal? EscalateOrderNotional = null);

/// <summary>
/// Single source of truth for operator-managed risk guardrail thresholds: it powers the read-only
/// risk dashboard (<see cref="GetAllStatusesAsync"/>, config get/update) and supplies the live
/// thresholds and drawdown evaluation that the enforced pre-trade validator — Meridian.Risk's
/// <c>CompositeRiskValidator</c>, registered as the <see cref="IRiskValidator"/> the OMS invokes —
/// reads on every order. Position limits are additionally enforced by the operator-controls gate
/// (<see cref="ExecutionOperatorControlService"/>) that the OMS runs earlier in the pipeline with
/// its manual-override/bypass semantics.
/// </summary>
public sealed class RiskRuleRuntimeService
{
    private const decimal DefaultDrawdownPercent = 5m;
    private const int DefaultMaxOrdersPerMinute = 60;

    private readonly IServiceProvider _services;
    private readonly ILogger<RiskRuleRuntimeService> _logger;
    private readonly RiskRuleRuntimeOptions _options;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    private decimal _maxDrawdownPercent = DefaultDrawdownPercent;
    private int _maxOrdersPerMinute = DefaultMaxOrdersPerMinute;

    // Portfolio-aware thresholds. Null = unconfigured: the corresponding enforced rule
    // approves and the dashboard reports the rule in its unconfigured Observe state.
    private decimal? _maxGrossExposure;
    private decimal? _maxSymbolConcentrationPercent;
    private decimal? _maxOrderNotional;
    private decimal? _escalateOrderNotional;

    public RiskRuleRuntimeService(
        IServiceProvider services,
        ILogger<RiskRuleRuntimeService> logger,
        RiskRuleRuntimeOptions? options = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? RiskRuleRuntimeOptions.Default;
        LoadSnapshot();
    }

    /// <summary>
    /// Operator-tuned order-rate ceiling, read per evaluation by the enforced order-rate
    /// throttle rule so hot updates take effect immediately.
    /// </summary>
    public int MaxOrdersPerMinute => GetMaxOrdersPerMinute();

    /// <summary>
    /// Operator-tuned portfolio-wide gross exposure ceiling, read per evaluation by the
    /// enforced gross-exposure rule. Null when unconfigured (the rule approves).
    /// </summary>
    public decimal? MaxGrossExposure { get { lock (_gate) { return _maxGrossExposure; } } }

    /// <summary>
    /// Operator-tuned single-symbol concentration cap as a percentage of portfolio value,
    /// read per evaluation by the enforced concentration rule. Null when unconfigured.
    /// </summary>
    public decimal? MaxSymbolConcentrationPercent { get { lock (_gate) { return _maxSymbolConcentrationPercent; } } }

    /// <summary>
    /// Operator-tuned hard per-order notional ceiling, read per evaluation by the enforced
    /// order-notional rule. Null when unconfigured.
    /// </summary>
    public decimal? MaxOrderNotional { get { lock (_gate) { return _maxOrderNotional; } } }

    /// <summary>
    /// Operator-tuned notional band at or above which an order parks for governed approval,
    /// read per evaluation by the enforced order-notional rule. Null when unconfigured.
    /// </summary>
    public decimal? EscalateOrderNotional { get { lock (_gate) { return _escalateOrderNotional; } } }

    /// <summary>
    /// Evaluates the drawdown circuit breaker against the same live portfolio state and
    /// operator-tuned threshold this service reports on the dashboard, so the guardrail can
    /// never show "Healthy" while it silently fails to gate an order. Invoked by the enforced
    /// pre-trade validator on every order.
    /// </summary>
    public RiskValidationResult EvaluateDrawdownGuardrail()
    {
        var portfolio = Resolve<IPortfolioState>();
        if (portfolio is null)
        {
            // Execution state not yet wired — the drawdown circuit breaker cannot trip.
            return RiskValidationResult.Approved();
        }

        var portfolioValue = portfolio.PortfolioValue;
        if (portfolioValue <= 0m)
        {
            // No portfolio value to measure against, matching BuildDrawdownStatus's 0% baseline.
            return RiskValidationResult.Approved();
        }

        var totalPnl = portfolio.RealisedPnl + portfolio.UnrealisedPnl;
        var drawdownPercent = ComputeDrawdownPercent(portfolioValue, totalPnl);
        var maxDrawdownPercent = GetMaxDrawdownPercent();

        if (drawdownPercent <= -maxDrawdownPercent)
        {
            var reason =
                $"Drawdown circuit breaker: {drawdownPercent.ToString("F2", CultureInfo.InvariantCulture)}% breached max {maxDrawdownPercent.ToString("F2", CultureInfo.InvariantCulture)}%.";
            _logger.LogWarning("Pre-trade risk rejection (drawdown): {Reason}", reason);
            return RiskValidationResult.Rejected(reason);
        }

        return RiskValidationResult.Approved();
    }

    public async Task<IReadOnlyList<RiskRuleStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var auditEntries = await GetAuditEntriesAsync(ct).ConfigureAwait(false);
        var asOf = DateTimeOffset.UtcNow;
        return
        [
            BuildPositionLimitStatus(auditEntries, asOf),
            BuildDrawdownStatus(auditEntries, asOf),
            BuildOrderRateStatus(auditEntries, asOf),
            BuildGrossExposureStatus(auditEntries, asOf),
            BuildSymbolConcentrationStatus(auditEntries, asOf),
            BuildOrderNotionalStatus(auditEntries, asOf)
        ];
    }

    public async Task<RiskRuleStatusDto?> GetStatusAsync(string ruleName, CancellationToken ct = default)
    {
        var normalizedRule = NormalizeRuleName(ruleName);
        if (normalizedRule is null)
        {
            return null;
        }

        var statuses = await GetAllStatusesAsync(ct).ConfigureAwait(false);
        return statuses.FirstOrDefault(status => string.Equals(
            status.RuleName,
            normalizedRule,
            StringComparison.OrdinalIgnoreCase));
    }

    public RiskRuleConfigDto? GetConfig(string ruleName)
    {
        var normalizedRule = NormalizeRuleName(ruleName);
        if (normalizedRule is null)
        {
            return null;
        }

        var controlsSnapshot = Resolve<ExecutionOperatorControlService>()?.GetSnapshot();
        lock (_gate)
        {
            return normalizedRule switch
            {
                "PositionLimit" => new RiskRuleConfigDto(
                    RuleName: "PositionLimit",
                    DefaultMaxPositionSize: controlsSnapshot?.DefaultMaxPositionSize,
                    SymbolPositionLimits: controlsSnapshot?.SymbolPositionLimits,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: null),
                "DrawdownCircuitBreaker" => new RiskRuleConfigDto(
                    RuleName: "DrawdownCircuitBreaker",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: _maxDrawdownPercent,
                    MaxOrdersPerMinute: null),
                "OrderRateThrottle" => new RiskRuleConfigDto(
                    RuleName: "OrderRateThrottle",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: _maxOrdersPerMinute),
                "GrossExposure" => new RiskRuleConfigDto(
                    RuleName: "GrossExposure",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: null,
                    MaxGrossExposure: _maxGrossExposure),
                "SymbolConcentration" => new RiskRuleConfigDto(
                    RuleName: "SymbolConcentration",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: null,
                    MaxSymbolConcentrationPercent: _maxSymbolConcentrationPercent),
                "OrderNotional" => new RiskRuleConfigDto(
                    RuleName: "OrderNotional",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: null,
                    MaxOrderNotional: _maxOrderNotional,
                    EscalateOrderNotional: _escalateOrderNotional),
                _ => null
            };
        }
    }

    public async Task<RiskRuleConfigDto?> UpdateConfigAsync(
        string ruleName,
        RiskRuleConfigUpdateRequest request,
        string? actor,
        CancellationToken ct = default)
    {
        var normalizedRule = NormalizeRuleName(ruleName);
        if (normalizedRule is null)
        {
            return null;
        }

        actor = string.IsNullOrWhiteSpace(actor) ? "operator" : actor.Trim();

        switch (normalizedRule)
        {
            case "PositionLimit":
                await UpdatePositionLimitConfigAsync(request, actor, ct).ConfigureAwait(false);
                break;
            case "DrawdownCircuitBreaker":
                if (!request.MaxDrawdownPercent.HasValue || request.MaxDrawdownPercent.Value <= 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxDrawdownPercent), "MaxDrawdownPercent must be greater than zero.");
                }

                var maxDrawdownPercent = request.MaxDrawdownPercent.Value;

                await CommitThresholdsAsync(
                    current => current with { MaxDrawdownPercent = maxDrawdownPercent },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: {MaxDrawdownPercent}%",
                    normalizedRule,
                    actor,
                    maxDrawdownPercent);
                break;
            case "OrderRateThrottle":
                if (!request.MaxOrdersPerMinute.HasValue || request.MaxOrdersPerMinute.Value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxOrdersPerMinute), "MaxOrdersPerMinute must be greater than zero.");
                }

                var maxOrdersPerMinute = request.MaxOrdersPerMinute.Value;

                await CommitThresholdsAsync(
                    current => current with { MaxOrdersPerMinute = maxOrdersPerMinute },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: {MaxOrdersPerMinute} orders/minute",
                    normalizedRule,
                    actor,
                    maxOrdersPerMinute);
                break;
            case "GrossExposure":
                var maxGrossExposure = NormalizeThreshold(request.MaxGrossExposure, nameof(request.MaxGrossExposure), required: true);

                await CommitThresholdsAsync(
                    current => current with { MaxGrossExposure = maxGrossExposure },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: gross exposure ceiling {MaxGrossExposure}",
                    normalizedRule,
                    LogSanitizer.Sanitize(actor),
                    maxGrossExposure?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured");
                break;
            case "SymbolConcentration":
                var maxConcentration = NormalizeThreshold(request.MaxSymbolConcentrationPercent, nameof(request.MaxSymbolConcentrationPercent), required: true);
                if (maxConcentration is > 100m)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxSymbolConcentrationPercent), "MaxSymbolConcentrationPercent cannot exceed 100.");
                }

                await CommitThresholdsAsync(
                    current => current with { MaxSymbolConcentrationPercent = maxConcentration },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: concentration cap {MaxSymbolConcentrationPercent}%",
                    normalizedRule,
                    LogSanitizer.Sanitize(actor),
                    maxConcentration?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured");
                break;
            case "OrderNotional":
                if (!request.MaxOrderNotional.HasValue && !request.EscalateOrderNotional.HasValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxOrderNotional), "Provide MaxOrderNotional and/or EscalateOrderNotional.");
                }

                // The band merges against whatever thresholds are current at commit time,
                // so the ceiling/escalation locals are assigned inside the transform.
                decimal? maxOrderNotional = null;
                decimal? escalateOrderNotional = null;
                await CommitThresholdsAsync(
                    current =>
                    {
                        maxOrderNotional = request.MaxOrderNotional.HasValue
                            ? NormalizeThreshold(request.MaxOrderNotional, nameof(request.MaxOrderNotional), required: false)
                            : current.MaxOrderNotional;
                        escalateOrderNotional = request.EscalateOrderNotional.HasValue
                            ? NormalizeThreshold(request.EscalateOrderNotional, nameof(request.EscalateOrderNotional), required: false)
                            : current.EscalateOrderNotional;

                        if (maxOrderNotional.HasValue && escalateOrderNotional.HasValue &&
                            escalateOrderNotional.Value >= maxOrderNotional.Value)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(request.EscalateOrderNotional),
                                "EscalateOrderNotional must be below MaxOrderNotional so the governed-approval band exists.");
                        }

                        return current with
                        {
                            MaxOrderNotional = maxOrderNotional,
                            EscalateOrderNotional = escalateOrderNotional
                        };
                    },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: notional ceiling {MaxOrderNotional}, escalation band {EscalateOrderNotional}",
                    normalizedRule,
                    LogSanitizer.Sanitize(actor),
                    maxOrderNotional?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured",
                    escalateOrderNotional?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured");
                break;
            default:
                return null;
        }

        return GetConfig(normalizedRule);
    }

    private async Task UpdatePositionLimitConfigAsync(
        RiskRuleConfigUpdateRequest request,
        string actor,
        CancellationToken ct)
    {
        var controls = Resolve<ExecutionOperatorControlService>();
        if (controls is null)
        {
            throw new InvalidOperationException("Execution operator controls are not available.");
        }

        if (request.DefaultMaxPositionSize.HasValue)
        {
            await controls.SetDefaultPositionLimitAsync(
                request.DefaultMaxPositionSize,
                actor,
                request.Reason ?? "Risk rule config update.",
                ct).ConfigureAwait(false);
        }

        if (request.SymbolPositionLimits is null)
        {
            return;
        }

        foreach (var (symbol, limit) in request.SymbolPositionLimits)
        {
            await controls.SetSymbolPositionLimitAsync(
                symbol,
                limit,
                actor,
                request.Reason ?? "Risk rule config update.",
                ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ExecutionAuditEntry>> GetAuditEntriesAsync(CancellationToken ct)
    {
        var auditTrail = Resolve<ExecutionAuditTrailService>();
        return auditTrail is null
            ? Array.Empty<ExecutionAuditEntry>()
            : await auditTrail.GetRecentAsync(200, ct).ConfigureAwait(false);
    }

    private RiskRuleStatusDto BuildPositionLimitStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var controls = Resolve<ExecutionOperatorControlService>();
        var snapshot = controls?.GetSnapshot();
        var portfolio = Resolve<IPortfolioState>();

        // Utilization uses the same limit resolution as enforcement: a symbol-specific
        // limit overrides the default for that symbol, and the meter reports the most
        // constrained position rather than the largest raw quantity.
        // The reported measurement must be one consistent tuple: the symbol driving the
        // utilization bar, its own quantity, and the limit actually resolved for it.
        // Mixing the largest raw position with the default limit and another symbol's
        // utilization renders a bar that contradicts the numbers beside it.
        var maxAbsoluteQuantity = 0m;
        decimal? maxUtilization = null;
        decimal? constrainedQuantity = null;
        decimal? constrainedLimit = null;
        if (portfolio is not null)
        {
            foreach (var position in portfolio.Positions.Values)
            {
                var quantity = Math.Abs(position.Quantity);
                if (quantity > maxAbsoluteQuantity)
                {
                    maxAbsoluteQuantity = quantity;
                }

                decimal? symbolLimit = snapshot is not null &&
                    snapshot.SymbolPositionLimits.TryGetValue(position.Symbol, out var configured)
                        ? configured
                        : snapshot?.DefaultMaxPositionSize;
                var utilizationForPosition = ComputeUtilization(quantity, symbolLimit);
                if (utilizationForPosition is { } value && (maxUtilization is null || value > maxUtilization))
                {
                    maxUtilization = value;
                    constrainedQuantity = quantity;
                    constrainedLimit = symbolLimit;
                }
            }
        }

        // Fall back to the default limit and the largest raw position only when no
        // position produced a measurable utilization.
        var threshold = constrainedLimit ?? snapshot?.DefaultMaxPositionSize;
        var currentQuantity = constrainedQuantity ?? maxAbsoluteQuantity;

        var violations = FindViolations(
            auditEntries,
            actionHint: "OrderRejected",
            textHint: "position");
        var breached = violations.Count > 0;
        var state = breached
            ? "Constrained"
            : threshold.HasValue ? "Healthy" : "Observe";
        var summary = breached
            ? "Recent order rejections indicate position-limit breaches."
            : threshold.HasValue
                ? "Position limits are configured and no recent breaches were detected."
                : "No default position limit is configured.";

        return new RiskRuleStatusDto(
            RuleName: "PositionLimit",
            State: state,
            Summary: summary,
            IsBreached: breached,
            Threshold: threshold.HasValue
                ? threshold.Value.ToString("G29", CultureInfo.InvariantCulture)
                : "unlimited",
            CurrentValue: currentQuantity.ToString("G29", CultureInfo.InvariantCulture),
            AsOf: asOf,
            RecentViolations: violations,
            UtilizationPercent: maxUtilization ?? ComputeUtilization(currentQuantity, threshold),
            Severity: "Error");
    }

    private RiskRuleStatusDto BuildDrawdownStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var portfolio = Resolve<IPortfolioState>();
        var maxDrawdownPercent = GetMaxDrawdownPercent();

        var portfolioValue = portfolio?.PortfolioValue ?? 0m;
        var totalPnl = (portfolio?.RealisedPnl ?? 0m) + (portfolio?.UnrealisedPnl ?? 0m);
        var drawdownPercent = portfolioValue > 0m
            ? ComputeDrawdownPercent(portfolioValue, totalPnl)
            : 0m;

        var breached = drawdownPercent <= -maxDrawdownPercent;
        var state = breached
            ? "Constrained"
            : drawdownPercent <= -(maxDrawdownPercent / 2m) ? "Observe" : "Healthy";
        var summary = breached
            ? $"Drawdown threshold breached at {drawdownPercent:F2}%."
            : state == "Observe"
                ? $"Drawdown is approaching threshold at {drawdownPercent:F2}%."
                : "Drawdown remains inside configured threshold.";

        var violations = FindViolations(
            auditEntries,
            actionHint: "OrderRejected",
            textHint: "drawdown");
        if (breached && violations.Count == 0)
        {
            violations = [$"Current drawdown is {drawdownPercent:F2}%."];
        }

        return new RiskRuleStatusDto(
            RuleName: "DrawdownCircuitBreaker",
            State: state,
            Summary: summary,
            IsBreached: breached,
            Threshold: $"{maxDrawdownPercent:F2}%",
            CurrentValue: $"{drawdownPercent:F2}%",
            AsOf: asOf,
            RecentViolations: violations,
            UtilizationPercent: ComputeUtilization(Math.Max(0m, -drawdownPercent), maxDrawdownPercent),
            Severity: "Critical");
    }

    private RiskRuleStatusDto BuildOrderRateStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var maxOrdersPerMinute = GetMaxOrdersPerMinute();
        var cutoff = asOf.AddMinutes(-1);
        var recentOrderCount = auditEntries.Count(entry =>
            entry.OccurredAt >= cutoff &&
            string.Equals(entry.Action, "OrderSubmitted", StringComparison.OrdinalIgnoreCase));

        var breached = recentOrderCount > maxOrdersPerMinute;
        var state = breached
            ? "Constrained"
            : recentOrderCount >= (int)Math.Ceiling(maxOrdersPerMinute * 0.8m) ? "Observe" : "Healthy";
        var summary = breached
            ? $"Order throughput exceeded threshold ({recentOrderCount}/{maxOrdersPerMinute} in the last minute)."
            : state == "Observe"
                ? $"Order throughput is approaching threshold ({recentOrderCount}/{maxOrdersPerMinute} in the last minute)."
                : $"Order throughput is healthy ({recentOrderCount}/{maxOrdersPerMinute} in the last minute).";

        var violations = FindViolations(
            auditEntries,
            actionHint: "OrderRejected",
            textHint: "rate");
        if (breached && violations.Count == 0)
        {
            violations = [$"Observed {recentOrderCount} orders in the last minute."];
        }

        return new RiskRuleStatusDto(
            RuleName: "OrderRateThrottle",
            State: state,
            Summary: summary,
            IsBreached: breached,
            Threshold: $"{maxOrdersPerMinute} orders/minute",
            CurrentValue: $"{recentOrderCount} orders/minute",
            AsOf: asOf,
            RecentViolations: violations,
            UtilizationPercent: ComputeUtilization(recentOrderCount, maxOrdersPerMinute),
            Severity: "Error");
    }

    private RiskRuleStatusDto BuildGrossExposureStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var maxGrossExposure = MaxGrossExposure;
        var snapshot = Resolve<Meridian.Risk.IPortfolioExposureProvider>()?.GetSnapshot();
        var grossExposure = snapshot?.GrossExposure ?? 0m;

        var violationEntries = FindViolationEntries(auditEntries, actionHint: "OrderRejected", textHint: "gross exposure");
        var violations = DescribeViolations(violationEntries);
        // Live state follows current exposure plus breaches inside the liveness window;
        // older rejections stay as evidence without pinning the rule Constrained.
        var liveViolation = HasLiveViolation(violationEntries, asOf);
        var utilization = ComputeUtilization(grossExposure, maxGrossExposure);
        var breached = maxGrossExposure.HasValue && grossExposure > maxGrossExposure.Value;
        var state = breached || liveViolation
            ? "Constrained"
            : !maxGrossExposure.HasValue
                ? "Observe"
                : utilization >= 80m ? "Observe" : "Healthy";
        var summary = breached
            ? "Portfolio gross exposure has breached the configured ceiling; new orders are rejected and the circuit breaker trips."
            : liveViolation
                ? "Recent orders were rejected by the gross exposure ceiling."
                : !maxGrossExposure.HasValue
                    ? "No gross exposure ceiling is configured; the rule approves all orders."
                    : state == "Observe"
                        ? "Portfolio gross exposure is approaching the configured ceiling."
                        : "Portfolio gross exposure is inside the configured ceiling.";

        return new RiskRuleStatusDto(
            RuleName: "GrossExposure",
            State: state,
            Summary: summary,
            IsBreached: breached || liveViolation,
            Threshold: maxGrossExposure.HasValue
                ? maxGrossExposure.Value.ToString("G29", CultureInfo.InvariantCulture)
                : "unconfigured",
            CurrentValue: grossExposure.ToString("F2", CultureInfo.InvariantCulture),
            AsOf: asOf,
            RecentViolations: violations,
            UtilizationPercent: utilization,
            Severity: "Critical");
    }

    private RiskRuleStatusDto BuildSymbolConcentrationStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var maxPercent = MaxSymbolConcentrationPercent;
        var snapshot = Resolve<Meridian.Risk.IPortfolioExposureProvider>()?.GetSnapshot();

        var topSymbol = "—";
        var topPercent = 0m;
        if (snapshot is { PortfolioValue: > 0m })
        {
            foreach (var exposure in snapshot.SymbolExposures.Values)
            {
                var percent = exposure.GrossExposure / snapshot.PortfolioValue * 100m;
                if (percent > topPercent)
                {
                    topPercent = percent;
                    topSymbol = exposure.Symbol;
                }
            }
        }

        var violationEntries = FindViolationEntries(auditEntries, actionHint: "OrderRejected", textHint: "concentration");
        var violations = DescribeViolations(violationEntries);
        var liveViolation = HasLiveViolation(violationEntries, asOf);
        var utilization = ComputeUtilization(topPercent, maxPercent);
        var breached = maxPercent.HasValue && topPercent > maxPercent.Value;
        var state = breached || liveViolation
            ? "Constrained"
            : !maxPercent.HasValue
                ? "Observe"
                : utilization >= 80m ? "Observe" : "Healthy";
        var summary = breached
            ? $"{topSymbol} concentration has breached the configured cap."
            : liveViolation
                ? "Recent orders were rejected by the concentration cap."
                : !maxPercent.HasValue
                    ? "No concentration cap is configured; the rule approves all orders."
                    : state == "Observe"
                        ? $"{topSymbol} concentration is approaching the configured cap."
                        : "Single-symbol concentration is inside the configured cap.";

        return new RiskRuleStatusDto(
            RuleName: "SymbolConcentration",
            State: state,
            Summary: summary,
            IsBreached: breached || liveViolation,
            Threshold: maxPercent.HasValue ? $"{maxPercent.Value:F2}%" : "unconfigured",
            CurrentValue: snapshot is { PortfolioValue: > 0m } ? $"{topSymbol} {topPercent:F2}%" : "0.00%",
            AsOf: asOf,
            RecentViolations: violations,
            UtilizationPercent: utilization,
            Severity: "Error");
    }

    private RiskRuleStatusDto BuildOrderNotionalStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var maxNotional = MaxOrderNotional;
        var escalateAt = EscalateOrderNotional;
        // The queue is shared by every escalate-capable rule; this guardrail reports only
        // its own parked orders so host-contributed escalations are not misattributed.
        var pendingEscalations = (Resolve<RiskEscalationQueueService>()?.GetPending() ?? [])
            .Where(static entry => string.Equals(entry.RuleName, "OrderNotional", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var violationEntries = FindViolationEntries(auditEntries, actionHint: "OrderRejected", textHint: "notional");
        var violations = DescribeViolations(violationEntries);
        var configured = maxNotional.HasValue || escalateAt.HasValue;
        var breached = HasLiveViolation(violationEntries, asOf);
        var state = breached
            ? "Constrained"
            : !configured
                ? "Observe"
                : pendingEscalations.Count > 0 ? "Observe" : "Healthy";
        var summary = breached
            ? "Recent orders were rejected by the per-order notional ceiling."
            : !configured
                ? "No per-order notional limits are configured; the rule approves all orders."
                : pendingEscalations.Count > 0
                    ? $"{pendingEscalations.Count} order(s) are parked awaiting governed approval."
                    : "Per-order notional limits are configured and no recent breaches were detected.";

        var recentViolations = violations.Count > 0
            ? violations
            : pendingEscalations
                .Take(5)
                .Select(static entry =>
                    $"Parked for approval: {entry.Request.Symbol} {entry.Request.Side} {entry.Request.Quantity} — {entry.Reason}")
                .ToList();

        var threshold = (maxNotional, escalateAt) switch
        {
            (not null, not null) => $"escalate ≥ {escalateAt.Value.ToString("G29", CultureInfo.InvariantCulture)}, reject > {maxNotional.Value.ToString("G29", CultureInfo.InvariantCulture)}",
            (not null, null) => $"reject > {maxNotional.Value.ToString("G29", CultureInfo.InvariantCulture)}",
            (null, not null) => $"escalate ≥ {escalateAt.Value.ToString("G29", CultureInfo.InvariantCulture)}",
            _ => "unconfigured"
        };

        return new RiskRuleStatusDto(
            RuleName: "OrderNotional",
            State: state,
            Summary: summary,
            IsBreached: breached,
            Threshold: threshold,
            CurrentValue: $"{pendingEscalations.Count} pending approval(s)",
            AsOf: asOf,
            RecentViolations: recentViolations,
            UtilizationPercent: null,
            // The outcome this rule can actually produce: a ceiling alone can only reject,
            // and advertising "parks for approval" would label a guardrail with a
            // behaviour no order can reach.
            Severity: escalateAt.HasValue ? "Escalate" : "Error");
    }

    /// <summary>
    /// Percentage of the threshold consumed by the current value, clamped to [0, 999.99].
    /// Null when no threshold is configured.
    /// </summary>
    private static decimal? ComputeUtilization(decimal currentValue, decimal? threshold)
    {
        if (!threshold.HasValue || threshold.Value <= 0m)
        {
            return null;
        }

        var utilization = currentValue / threshold.Value * 100m;
        return Math.Clamp(Math.Round(utilization, 2), 0m, 999.99m);
    }

    /// <summary>
    /// How recent an audited breach must be to still describe the rule's live state. Older
    /// breaches remain visible as evidence in <see cref="RiskRuleStatusDto.RecentViolations"/>
    /// but no longer hold a guardrail Constrained: the audit window is bounded by entry
    /// count, not age, so on a quiet installation one old rejection would otherwise pin the
    /// rule (and the operator readiness gate that reads it) indefinitely.
    /// </summary>
    private static readonly TimeSpan ViolationLivenessWindow = TimeSpan.FromHours(1);

    private static List<ExecutionAuditEntry> FindViolationEntries(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        string actionHint,
        string textHint)
    {
        return auditEntries
            .Where(entry =>
                string.Equals(entry.Action, actionHint, StringComparison.OrdinalIgnoreCase) &&
                ((entry.Message?.Contains(textHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 (entry.Reason?.Contains(textHint, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(static entry => entry.OccurredAt)
            .Take(5)
            .ToList();
    }

    private static List<string> DescribeViolations(IEnumerable<ExecutionAuditEntry> entries) =>
        entries
            .Select(static entry => entry.Message ?? entry.Reason ?? $"{entry.Action} recorded at {entry.OccurredAt:O}.")
            .ToList();

    private static bool HasLiveViolation(IReadOnlyList<ExecutionAuditEntry> entries, DateTimeOffset asOf) =>
        entries.Any(entry => asOf - entry.OccurredAt <= ViolationLivenessWindow);

    private static List<string> FindViolations(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        string actionHint,
        string textHint) =>
        DescribeViolations(FindViolationEntries(auditEntries, actionHint, textHint));

    /// <summary>
    /// Drawdown as a percentage of the capital the P&amp;L was earned on, i.e. the starting
    /// value (current value minus cumulative P&amp;L) — not the current value. Dividing by
    /// the already-reduced current value overstates the loss: a fall from 100k to 95.24k
    /// is a 4.76% drawdown, but measured against the current value it reads as 5.0% and
    /// would breach a 5% limit that has not actually been hit. That matters more now that
    /// this guardrail trips the global circuit breaker. Shared by the enforced rule and
    /// the dashboard status so the two can never disagree.
    /// </summary>
    private static decimal ComputeDrawdownPercent(decimal portfolioValue, decimal totalPnl)
    {
        var baseline = portfolioValue - totalPnl;
        return baseline > 0m ? (totalPnl / baseline) * 100m : 0m;
    }

    private decimal GetMaxDrawdownPercent()
    {
        lock (_gate)
        {
            return _maxDrawdownPercent;
        }
    }

    private int GetMaxOrdersPerMinute()
    {
        lock (_gate)
        {
            return _maxOrdersPerMinute;
        }
    }

    private string? NormalizeRuleName(string? ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            return null;
        }

        return ruleName.Trim().ToLowerInvariant() switch
        {
            "positionlimit" => "PositionLimit",
            "drawdowncircuitbreaker" => "DrawdownCircuitBreaker",
            "orderratethrottle" => "OrderRateThrottle",
            "grossexposure" => "GrossExposure",
            "symbolconcentration" => "SymbolConcentration",
            "ordernotional" => "OrderNotional",
            _ => null
        };
    }

    /// <summary>
    /// Normalizes an operator-supplied threshold: positive sets the value, zero clears it
    /// (disabling the rule), negative is invalid. <paramref name="required"/> demands a value.
    /// </summary>
    private static decimal? NormalizeThreshold(decimal? value, string parameterName, bool required)
    {
        if (!value.HasValue)
        {
            if (required)
            {
                throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} is required.");
            }

            return null;
        }

        return value.Value switch
        {
            < 0m => throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} cannot be negative."),
            0m => null,
            _ => value.Value
        };
    }

    private T? Resolve<T>() where T : class => _services.GetService(typeof(T)) as T;

    /// <summary>
    /// Proposed live-threshold state produced by an update transform before it is durable.
    /// </summary>
    private sealed record ThresholdState(
        decimal MaxDrawdownPercent,
        int MaxOrdersPerMinute,
        decimal? MaxGrossExposure,
        decimal? MaxSymbolConcentrationPercent,
        decimal? MaxOrderNotional,
        decimal? EscalateOrderNotional);

    /// <summary>
    /// Applies a threshold change with persist-then-publish ordering: the proposed state is
    /// written durably first and only published to the live fields the enforcement rules read
    /// once the write succeeds. A failed snapshot write therefore leaves enforcement on the
    /// previous thresholds instead of enforcing values that would silently revert on restart.
    /// Updates serialize on <see cref="_updateGate"/> so the snapshot on disk always matches
    /// the last published state.
    /// </summary>
    private async Task CommitThresholdsAsync(
        Func<ThresholdState, ThresholdState> apply,
        string actor,
        string? reason,
        CancellationToken ct)
    {
        await _updateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThresholdState proposed;
            lock (_gate)
            {
                proposed = apply(new ThresholdState(
                    _maxDrawdownPercent,
                    _maxOrdersPerMinute,
                    _maxGrossExposure,
                    _maxSymbolConcentrationPercent,
                    _maxOrderNotional,
                    _escalateOrderNotional));
            }

            var snapshot = new RiskRuleRuntimeSnapshot(
                MaxDrawdownPercent: proposed.MaxDrawdownPercent,
                MaxOrdersPerMinute: proposed.MaxOrdersPerMinute,
                UpdatedAt: DateTimeOffset.UtcNow,
                UpdatedBy: actor,
                Reason: reason,
                MaxGrossExposure: proposed.MaxGrossExposure,
                MaxSymbolConcentrationPercent: proposed.MaxSymbolConcentrationPercent,
                MaxOrderNotional: proposed.MaxOrderNotional,
                EscalateOrderNotional: proposed.EscalateOrderNotional);
            var payload = JsonSerializer.Serialize(snapshot, RiskRuleRuntimeSnapshotJsonContext.Default.RiskRuleRuntimeSnapshot);
            await AtomicFileWriter.WriteAsync(_options.SnapshotPath, payload, ct).ConfigureAwait(false);

            lock (_gate)
            {
                _maxDrawdownPercent = proposed.MaxDrawdownPercent;
                _maxOrdersPerMinute = proposed.MaxOrdersPerMinute;
                _maxGrossExposure = proposed.MaxGrossExposure;
                _maxSymbolConcentrationPercent = proposed.MaxSymbolConcentrationPercent;
                _maxOrderNotional = proposed.MaxOrderNotional;
                _escalateOrderNotional = proposed.EscalateOrderNotional;
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private void LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return;
            }

            var payload = File.ReadAllText(_options.SnapshotPath);
            var snapshot = JsonSerializer.Deserialize(payload, RiskRuleRuntimeSnapshotJsonContext.Default.RiskRuleRuntimeSnapshot);
            if (snapshot is null)
            {
                return;
            }

            lock (_gate)
            {
                _maxDrawdownPercent = snapshot.MaxDrawdownPercent > 0m ? snapshot.MaxDrawdownPercent : DefaultDrawdownPercent;
                _maxOrdersPerMinute = snapshot.MaxOrdersPerMinute > 0 ? snapshot.MaxOrdersPerMinute : DefaultMaxOrdersPerMinute;
                _maxGrossExposure = snapshot.MaxGrossExposure is > 0m ? snapshot.MaxGrossExposure : null;
                _maxSymbolConcentrationPercent = snapshot.MaxSymbolConcentrationPercent is > 0m ? snapshot.MaxSymbolConcentrationPercent : null;
                _maxOrderNotional = snapshot.MaxOrderNotional is > 0m ? snapshot.MaxOrderNotional : null;
                _escalateOrderNotional = snapshot.EscalateOrderNotional is > 0m ? snapshot.EscalateOrderNotional : null;
            }
        }
        catch (Exception exception)
        {
            // A snapshot exists but cannot be read: its thresholds were configured by an
            // operator and silently reverting them to "unconfigured" would disable the
            // gross-exposure, concentration, and notional rails on the next restart while
            // the dashboard still lists them as the enforced rail. Fail closed instead.
            _logger.LogCritical(
                exception,
                "Risk runtime snapshot at {SnapshotPath} could not be read; refusing to start with silently unconfigured portfolio limits.",
                _options.SnapshotPath);
            throw new InvalidOperationException(
                $"The risk rule snapshot at '{_options.SnapshotPath}' exists but could not be read. " +
                "Refusing to continue with unconfigured portfolio risk limits; restore or remove the snapshot.",
                exception);
        }
    }
}

public sealed record RiskRuleRuntimeSnapshot(
    decimal MaxDrawdownPercent,
    int MaxOrdersPerMinute,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string? Reason,
    decimal? MaxGrossExposure = null,
    decimal? MaxSymbolConcentrationPercent = null,
    decimal? MaxOrderNotional = null,
    decimal? EscalateOrderNotional = null);

[JsonSerializable(typeof(RiskRuleRuntimeSnapshot))]
internal sealed partial class RiskRuleRuntimeSnapshotJsonContext : JsonSerializerContext
{
}
