using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Risk.Rules;
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
    decimal? EscalateOrderNotional = null,
    decimal? MaxOrderQuantity = null,
    decimal? MaxPriceDeviationPercent = null,
    decimal? PriceCollarPercent = null);

public sealed record RiskRuleConfigUpdateRequest(
    decimal? DefaultMaxPositionSize = null,
    IReadOnlyDictionary<string, decimal?>? SymbolPositionLimits = null,
    decimal? MaxDrawdownPercent = null,
    int? MaxOrdersPerMinute = null,
    string? Reason = null,
    decimal? MaxGrossExposure = null,
    decimal? MaxSymbolConcentrationPercent = null,
    decimal? MaxOrderNotional = null,
    decimal? EscalateOrderNotional = null,
    decimal? MaxOrderQuantity = null,
    decimal? MaxPriceDeviationPercent = null,
    decimal? PriceCollarPercent = null);

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

    // Fat-finger bands. Null = unconfigured: the corresponding limb approves without measuring.
    private decimal? _maxOrderQuantity;
    private decimal? _maxPriceDeviationPercent;

    // Price collar. Null = unconfigured: the collar approves without measuring. Held beside
    // the fat-finger band rather than inside FatFingerThresholds because the two feed different
    // rules at different severities, and a single record would invite one rule reading the
    // other's band.
    private decimal? _priceCollarPercent;

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
    /// Reads live consumed rate capacity from the throttle instance that actually enforces the
    /// ceiling. Set by the composition root; null when no reserving throttle is composed, in which
    /// case the status falls back to counting audit entries.
    /// <para>
    /// The fallback cannot see reservations taken for orders still in flight, so it under-reports
    /// exactly when the desk is closest to the ceiling — the dashboard would show room the gate
    /// will refuse to give. The probe reports the number the gate itself compares.
    /// </para>
    /// </summary>
    public Func<int>? OrderRateUsageProbe { get; set; }

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
    /// Operator-tuned absolute per-order quantity ceiling, read per evaluation by the enforced
    /// fat-finger rule. Null when unconfigured (the quantity limb approves).
    /// </summary>
    public decimal? MaxOrderQuantity { get { lock (_gate) { return _maxOrderQuantity; } } }

    /// <summary>
    /// Operator-tuned maximum aggressive price deviation from the market reference, in percent,
    /// read per evaluation by the enforced fat-finger rule. Null when unconfigured (the price
    /// limb approves, and a priced order with no reference price is no longer refused).
    /// </summary>
    public decimal? MaxPriceDeviationPercent { get { lock (_gate) { return _maxPriceDeviationPercent; } } }

    /// <summary>
    /// Both fat-finger limbs read under one lock, which is what the enforced rule consumes. Reading
    /// the two properties above separately would let an evaluation straddle an update and observe a
    /// pair that was never configured — a two-field change from (null quantity, 50% band) to
    /// (100 quantity, null band) can otherwise be seen as (null, null), which the rule treats as
    /// entirely unconfigured and approves through.
    /// </summary>
    public FatFingerThresholds FatFingerThresholds
    {
        get { lock (_gate) { return new FatFingerThresholds(_maxOrderQuantity, _maxPriceDeviationPercent); } }
    }

    /// <summary>
    /// The collar band the escalating price rule consumes, read under the same lock. A single
    /// value needs no atomic pairing of its own, but it is exposed as a record so the rule takes
    /// a threshold type rather than a bare decimal and cannot be handed the fat-finger band by
    /// mistake.
    /// </summary>
    public PriceCollarThresholds PriceCollarThresholds
    {
        get { lock (_gate) { return new PriceCollarThresholds(_priceCollarPercent); } }
    }

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
        var totalPnl = portfolio.RealisedPnl + portfolio.UnrealisedPnl;
        if (portfolioValue <= 0m)
        {
            // Nonpositive value with a loss behind it is not "nothing to measure" — it is
            // the worst possible drawdown. A book that fell from $100k to -$10k on -$110k of
            // P&L has a valid baseline and a >100% drawdown; approving further trading there
            // is exactly the outcome the guardrail exists to prevent. Only a book that never
            // had value (no baseline at all) is genuinely unmeasurable.
            if (portfolioValue - totalPnl > 0m)
            {
                return RiskValidationResult.Rejected(
                    "Drawdown circuit breaker: portfolio value is exhausted.");
            }

            return RiskValidationResult.Approved();
        }

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
        var asOf = DateTimeOffset.UtcNow;
        var auditEntries = await GetAuditEntriesAsync(asOf, ct).ConfigureAwait(false);
        var statuses = new[]
        {
            BuildPositionLimitStatus(auditEntries, asOf),
            BuildDrawdownStatus(auditEntries, asOf),
            BuildOrderRateStatus(auditEntries, asOf),
            BuildGrossExposureStatus(auditEntries, asOf),
            BuildSymbolConcentrationStatus(auditEntries, asOf),
            BuildOrderNotionalStatus(auditEntries, asOf),
            BuildFatFingerStatus(auditEntries, asOf),
            BuildPriceCollarStatus(auditEntries, asOf)
        };

        return WithAuditCoverageApplied(statuses);
    }

    /// <summary>
    /// Refuses to report a rule healthy when the audit window it reasons over is incomplete.
    /// <para>
    /// Every rule above makes the same claim — no breach inside the liveness window — and that
    /// claim is unverifiable when a burst has exceeded the audit trail's retention ceiling. The
    /// fail-closed answer is to stop asserting it, not to log the shortfall and carry on: a
    /// readiness gate reading "Healthy" cannot tell the difference between a quiet hour and an
    /// hour nobody kept the records for. A rule already reporting a breach is left alone — it is
    /// already constrained, and an incomplete window cannot make that less true.
    /// </para>
    /// </summary>
    private IReadOnlyList<RiskRuleStatusDto> WithAuditCoverageApplied(RiskRuleStatusDto[] statuses)
    {
        var auditTrail = Resolve<ExecutionAuditTrailService>();
        if (auditTrail is null)
        {
            return statuses;
        }

        // Two ways the evidence can fall short of the claim, and they need the same answer.
        // Retention shorter than the liveness window is the subtler one: nothing is ever reported
        // as a gap, because completeness is measured against the audit trail's own shorter window,
        // so a 45-minute-old breach under a 30-minute retention window is trimmed silently while
        // this service still promises to treat it as live. The consumer states its horizon, so the
        // consumer is what has to check it.
        // Completeness is asked at *this* service's horizon, not at the trail's retention window.
        // A gap only hides something from this claim while it is inside the liveness window, so a
        // two-hour trail and a one-hour claim must stop reporting constrained once the discard is an
        // hour old. Asking the trail's own window instead would block order readiness through
        // BuildRiskRuleGate for a second hour in which nothing assertable here was ever missing.
        var horizonCovered = auditTrail.InMemoryRetentionWindow >= ViolationLivenessWindow;
        var horizonComplete = auditTrail.RetentionWindowCompleteFor(ViolationLivenessWindow);
        if (horizonCovered && horizonComplete)
        {
            return statuses;
        }

        _logger.LogWarning(
            "Risk rule status reported constrained: the execution audit trail cannot establish the absence "
            + "of a breach over the {LivenessWindow} liveness window (retention window {RetentionWindow}, "
            + "complete over that horizon: {Complete}).",
            ViolationLivenessWindow,
            auditTrail.InMemoryRetentionWindow,
            horizonComplete);

        return statuses
            .Select(static status => status.IsBreached
                ? status
                : status with
                {
                    State = "Constrained",
                    Summary = "Audit retention does not cover the liveness window, so a recent breach "
                        + "cannot be ruled out. Treating the rule as constrained until coverage recovers.",
                })
            .ToArray();
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
                "FatFinger" => new RiskRuleConfigDto(
                    RuleName: "FatFinger",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: null,
                    MaxOrderQuantity: _maxOrderQuantity,
                    MaxPriceDeviationPercent: _maxPriceDeviationPercent),
                "PriceCollar" => new RiskRuleConfigDto(
                    RuleName: "PriceCollar",
                    DefaultMaxPositionSize: null,
                    SymbolPositionLimits: null,
                    MaxDrawdownPercent: null,
                    MaxOrdersPerMinute: null,
                    PriceCollarPercent: _priceCollarPercent),
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
            case "FatFinger":
                if (!request.MaxOrderQuantity.HasValue && !request.MaxPriceDeviationPercent.HasValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxOrderQuantity), "Provide MaxOrderQuantity and/or MaxPriceDeviationPercent.");
                }

                // Each limb merges independently against whatever is current at commit time, so
                // setting one band never silently clears the other.
                decimal? maxOrderQuantity = null;
                decimal? maxPriceDeviationPercent = null;
                decimal? collarUnderFatFingerBand = null;
                await CommitThresholdsAsync(
                    current =>
                    {
                        maxOrderQuantity = request.MaxOrderQuantity.HasValue
                            ? NormalizeThreshold(request.MaxOrderQuantity, nameof(request.MaxOrderQuantity), required: false)
                            : current.MaxOrderQuantity;
                        maxPriceDeviationPercent = request.MaxPriceDeviationPercent.HasValue
                            ? NormalizeThreshold(request.MaxPriceDeviationPercent, nameof(request.MaxPriceDeviationPercent), required: false)
                            : current.MaxPriceDeviationPercent;

                        // A sell can never breach a band of 100 or more: its aggressive deviation
                        // is (reference - price) / reference, which for any positive price is
                        // strictly under 100%. Such a band silently disables the sell side while
                        // the dashboard still reports the rule configured, so a $0.01 sell against
                        // a $100 bid would pass. Refuse it rather than accept a half-dead control.
                        if (maxPriceDeviationPercent is >= 100m)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(request.MaxPriceDeviationPercent),
                                "MaxPriceDeviationPercent must be below 100; a band at or above 100 can never reject a sell, silently disabling the sell side.");
                        }

                        collarUnderFatFingerBand = current.PriceCollarPercent;

                        return current with
                        {
                            MaxOrderQuantity = maxOrderQuantity,
                            MaxPriceDeviationPercent = maxPriceDeviationPercent
                        };
                    },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: quantity ceiling {MaxOrderQuantity}, price-deviation band {MaxPriceDeviationPercent}%",
                    normalizedRule,
                    LogSanitizer.Sanitize(actor),
                    maxOrderQuantity?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured",
                    maxPriceDeviationPercent?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured");
                WarnIfCollarUnreachable(maxPriceDeviationPercent, collarUnderFatFingerBand);
                break;
            case "PriceCollar":
                if (!request.PriceCollarPercent.HasValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.PriceCollarPercent), "Provide PriceCollarPercent.");
                }

                decimal? priceCollarPercent = null;
                decimal? fatFingerBandOverCollar = null;
                await CommitThresholdsAsync(
                    current =>
                    {
                        priceCollarPercent = NormalizeThreshold(
                            request.PriceCollarPercent, nameof(request.PriceCollarPercent), required: false);

                        // Same bound as the fat-finger band, and for the same reason: a sell's
                        // aggressive deviation is strictly under 100%, so a collar at or above
                        // that can never park a sell while the dashboard still reports the collar
                        // configured. Refuse it rather than accept a half-dead control.
                        if (priceCollarPercent is >= 100m)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(request.PriceCollarPercent),
                                "PriceCollarPercent must be below 100; a collar at or above 100 can never park a sell, silently disabling the sell side.");
                        }

                        fatFingerBandOverCollar = current.MaxPriceDeviationPercent;

                        return current with { PriceCollarPercent = priceCollarPercent };
                    },
                    actor,
                    request.Reason,
                    ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: price collar {PriceCollarPercent}%",
                    normalizedRule,
                    LogSanitizer.Sanitize(actor),
                    priceCollarPercent?.ToString("G29", CultureInfo.InvariantCulture) ?? "unconfigured");
                WarnIfCollarUnreachable(fatFingerBandOverCollar, priceCollarPercent);
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

    /// <summary>
    /// Audit entries for the status projection: the newest 200 for history, and everything inside
    /// the liveness window regardless of how much unrelated activity followed it.
    /// <para>
    /// A fixed count alone was wrong here, because every rule below makes a <em>time</em> claim —
    /// "a breach in the last hour holds this rule constrained". Two hundred unrelated events after
    /// a fat-finger refusal is an ordinary morning on an active desk, and it silently turned a live
    /// breach into a healthy rule and reopened the readiness gate an hour early.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<ExecutionAuditEntry>> GetAuditEntriesAsync(
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var auditTrail = Resolve<ExecutionAuditTrailService>();
        return auditTrail is null
            ? Array.Empty<ExecutionAuditEntry>()
            : await auditTrail.GetRecentOrSinceAsync(200, asOf - ViolationLivenessWindow, ct).ConfigureAwait(false);
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
            textHint: "position",
            asOf);
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
        // Same exhausted-book rule the enforced guardrail applies. Forcing 0% whenever value
        // is nonpositive would show Healthy on the dashboard while EvaluateDrawdownGuardrail
        // is rejecting every order for a greater-than-100% loss on the same portfolio.
        var drawdownPercent = ComputeDrawdownPercent(portfolioValue, totalPnl);

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
            textHint: "drawdown",
            asOf);
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

    /// <summary>
    /// True when this audit entry represents capacity the throttle is still holding.
    /// <list type="bullet">
    /// <item><description>A submission the gateway accepted — the slot was committed.</description></item>
    /// <item><description>An amendment the gateway accepted — it revalidated through the same
    /// reserving rules and committed its own slot.</description></item>
    /// <item><description>A submission that threw after dispatch — ambiguous, so the slot was
    /// deliberately over-counted rather than released.</description></item>
    /// </list>
    /// A submission the broker rejected reached no venue and had its slot rolled back, so it does
    /// not count however it was recorded.
    /// </summary>
    private static bool CountsAgainstOrderRate(ExecutionAuditEntry entry)
    {
        // A routed amendment revalidates through the same reserving rules and commits its own
        // slot, so it counts exactly as a submission does. Recognising only OrderSubmitted made
        // accepted amendments vanish from reported utilization while still consuming capacity.
        if (string.Equals(entry.Action, "OrderSubmitted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Action, "OrderModified", StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(entry.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry.Action, "OrderRejected", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                entry.Reason,
                OrderManagementSystem.AmbiguousSubmissionReason,
                StringComparison.Ordinal);
    }

    private int? ReadOrderRateUsage()
    {
        var probe = OrderRateUsageProbe;
        if (probe is null)
        {
            return null;
        }

        try
        {
            return probe();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order-rate usage probe failed; falling back to audit reconstruction");
            return null;
        }
    }

    private RiskRuleStatusDto BuildOrderRateStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var maxOrdersPerMinute = GetMaxOrdersPerMinute();
        var cutoff = asOf.AddMinutes(-1);

        // Prefer the enforcing instance. Audit reconstruction counts only orders that were
        // submitted, so it misses capacity held for in-flight submissions and reports room the
        // gate will not honour.
        // The fallback reconstructs *committed slots*, not submissions. The throttle releases
        // capacity for anything that did not reach a venue, so counting every OrderSubmitted entry
        // regardless of outcome over-reports a desk whose orders the broker refused, while
        // ignoring the ambiguous rejections under-reports one whose slots are still held. Both
        // errors point the wrong way at once.
        var recentOrderCount = ReadOrderRateUsage() ?? auditEntries.Count(entry =>
            entry.OccurredAt >= cutoff && CountsAgainstOrderRate(entry));

        // At the ceiling the throttle already refuses, so the dashboard has to say Constrained at
        // the same count rather than one above it. Reporting Observe on an order the gate would
        // reject is the disagreement this probe exists to remove.
        var breached = recentOrderCount >= maxOrdersPerMinute;
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
            textHint: "rate",
            asOf);
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

        var violationEntries = FindViolationEntries(auditEntries, actionHint: "OrderRejected", textHint: "gross exposure", asOf);
        var violations = DescribeViolations(violationEntries, "gross exposure");
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

        var topPercent = 0m;
        if (snapshot is { PortfolioValue: > 0m })
        {
            foreach (var exposure in snapshot.SymbolExposures.Values)
            {
                var percent = exposure.GrossExposure / snapshot.PortfolioValue * 100m;
                if (percent > topPercent)
                {
                    topPercent = percent;
                }
            }
        }

        var violationEntries = FindViolationEntries(auditEntries, actionHint: "OrderRejected", textHint: "concentration", asOf);
        var violations = DescribeViolations(violationEntries, "concentration");
        var liveViolation = HasLiveViolation(violationEntries, asOf);
        var utilization = ComputeUtilization(topPercent, maxPercent);
        var breached = maxPercent.HasValue && topPercent > maxPercent.Value;
        var state = breached || liveViolation
            ? "Constrained"
            : !maxPercent.HasValue
                ? "Observe"
                : utilization >= 80m ? "Observe" : "Healthy";
        var summary = breached
            ? "Single-symbol concentration has breached the configured cap."
            : liveViolation
                ? "Recent orders were rejected by the concentration cap."
                : !maxPercent.HasValue
                    ? "No concentration cap is configured; the rule approves all orders."
                    : state == "Observe"
                        ? "Single-symbol concentration is approaching the configured cap."
                        : "Single-symbol concentration is inside the configured cap.";

        return new RiskRuleStatusDto(
            RuleName: "SymbolConcentration",
            State: state,
            Summary: summary,
            IsBreached: breached || liveViolation,
            Threshold: maxPercent.HasValue ? $"{maxPercent.Value:F2}%" : "unconfigured",
            // Percentage only, never the symbol. This status is served by the rules
            // endpoint, which authenticates but applies no trade-read permission or fund
            // scope, so naming the leading holding would tell any logged-in user what
            // another fund's largest position is — data the portfolio routes filter.
            CurrentValue: snapshot is { PortfolioValue: > 0m } ? $"{topPercent:F2}%" : "0.00%",
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
        // Unresolved, not merely pending: an entry approved with Release=false, or one whose
        // consumed approval was restored after a downstream refusal, is armed and can still
        // route. Counting only PendingApproval let the guardrail read Healthy with an
        // approved exception waiting to go.
        var pendingEscalations = (Resolve<RiskEscalationQueueService>()?.GetUnresolved() ?? [])
            .Where(static entry => string.Equals(entry.RuleName, "OrderNotional", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var violationEntries = FindViolationEntries(auditEntries, actionHint: "OrderRejected", textHint: "notional", asOf);
        var violations = DescribeViolations(violationEntries, "notional");
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
                    ? $"{pendingEscalations.Count} order(s) are parked awaiting governed approval or release."
                    : "Per-order notional limits are configured and no recent breaches were detected.";

        // Only the count, never the parked orders' details: this status is served by the
        // rules endpoint, which has no order-management permission or fund-scope check.
        // Symbol, side, and size belong to the escalation-list endpoint, which filters by
        // the caller's authorized accounts.
        var recentViolations = violations.Count > 0
            ? violations
            : pendingEscalations.Count > 0
                ? [$"{pendingEscalations.Count} order(s) parked awaiting governed approval or release."]
                : new List<string>();

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
    /// Whether a rejection was the fat-finger rule refusing an order it could not price, rather
    /// than measuring one past a band. Checked against the structured violation code first and the
    /// rendered text only as a fallback, so the classification does not depend on message wording.
    /// </summary>
    private static bool IsUnmeasurableRefusal(ExecutionAuditEntry entry) =>
        MatchesViolationMetadata(entry, FatFingerRule.UnmeasurableCode)
        || (entry.Reason?.Contains(FatFingerRule.UnmeasurableCode, StringComparison.OrdinalIgnoreCase) ?? false)
        || (entry.Message?.Contains("has no reference price", StringComparison.OrdinalIgnoreCase) ?? false)
        || (entry.Reason?.Contains("has no reference price", StringComparison.OrdinalIgnoreCase) ?? false);

    private RiskRuleStatusDto BuildFatFingerStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        // One reading of both limbs, for the same reason enforcement takes one: two separately
        // locked reads can straddle a two-field update and observe a pair that never existed.
        // Here the consequence is a status claim rather than an approval — the panel would report
        // the rule unconfigured while both the old and the new configuration enforce a rail — and
        // a guardrail that misreports itself is the failure this whole status surface exists to
        // prevent.
        var (maxQuantity, maxDeviationPercent) = FatFingerThresholds;

        // An unmeasurable refusal is not a breach: the rule refused an order it could not price
        // rather than measuring one past a band. Both rejections carry "fat-finger" text, so
        // without this split the dashboard would report a measured band violation for an hour
        // whenever a quote went missing - the exact claim the unmeasurable outcome exists to avoid.
        //
        // The split runs over the FULL audit set, before any truncation. FindViolationEntries keeps
        // only the five most recent matches, so classifying afterwards would let five fresh
        // missing-quote refusals push a real breach out of the window entirely and drop the rule
        // from Constrained back to Observe while the breach was still live.
        var unmeasurable = auditEntries.Where(IsUnmeasurableRefusal).ToList();
        var measured = auditEntries.Except(unmeasurable).ToList();

        // Both actions: the rule gates amendments as well as submissions, and a refused amendment
        // is audited as OrderModifyRejected. Matching only OrderRejected reported the rule healthy
        // while it was actively refusing aggressive modifications.
        string[] rejectionActions = ["OrderRejected", "OrderModifyRejected"];
        var violationEntries = FindViolationEntries(measured, rejectionActions, textHint: "fat-finger", asOf);
        var unmeasurableEntries = FindViolationEntries(unmeasurable, rejectionActions, textHint: "fat-finger", asOf);

        var violations = DescribeViolations(violationEntries, "fat-finger");
        var configured = maxQuantity.HasValue || maxDeviationPercent.HasValue;
        var breached = HasLiveViolation(violationEntries, asOf);
        var pricingGap = !breached && HasLiveViolation(unmeasurableEntries, asOf);
        var state = breached
            ? "Constrained"
            : pricingGap
                ? "Observe"
                : configured ? "Healthy" : "Observe";
        var summary = breached
            ? "Recent orders were rejected by the fat-finger quantity ceiling, price-deviation band, or wrong-side stop trigger."
            : pricingGap
                ? "Recent priced orders were refused because no reference price was available to measure them; no band was breached."
                : configured
                    ? "Fat-finger bands are configured and no recent breaches were detected."
                    : "No fat-finger bands are configured; the rule approves all orders.";

        var threshold = (maxQuantity, maxDeviationPercent) switch
        {
            (not null, not null) =>
                $"reject > {maxQuantity.Value.ToString("G29", CultureInfo.InvariantCulture)} qty, "
                + $"reject > {maxDeviationPercent.Value.ToString("G29", CultureInfo.InvariantCulture)}% through market",
            (not null, null) => $"reject > {maxQuantity.Value.ToString("G29", CultureInfo.InvariantCulture)} qty",
            (null, not null) => $"reject > {maxDeviationPercent.Value.ToString("G29", CultureInfo.InvariantCulture)}% through market",
            _ => "unconfigured"
        };

        return new RiskRuleStatusDto(
            RuleName: "FatFinger",
            State: state,
            Summary: summary,
            IsBreached: breached,
            Threshold: threshold,
            // The gate measures each order on its own, so there is no standing value to report
            // between orders. Saying so beats printing a zero that reads like measured headroom.
            CurrentValue: configured ? "per-order" : "not enforced",
            AsOf: asOf,
            RecentViolations: violations.Count > 0
                ? violations
                : pricingGap
                    ? DescribeViolations(unmeasurableEntries, "fat-finger")
                    : violations,
            UtilizationPercent: null,
            Severity: "Error");
    }

    /// <summary>
    /// Status for the escalating price collar.
    /// <para>
    /// Its evidence lives in two places rather than one, because the rule has two outcomes that
    /// are audited differently. A collar breach parks the order, which the composite validator
    /// records as <c>OrderParkedForApproval</c> and tracks in the escalation queue — so a query
    /// over rejections alone would report the collar Healthy while orders sat waiting on it. A
    /// priced order the collar cannot measure is a hard refusal instead: the validator excludes
    /// unmeasurable from release precisely so an approval cannot stand in for a measurement that
    /// never happened, and that refusal lands in the audit trail as a rejection.
    /// </para>
    /// </summary>
    private RiskRuleStatusDto BuildPriceCollarStatus(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        DateTimeOffset asOf)
    {
        var collarPercent = PriceCollarThresholds.CollarPercent;

        // The queue is shared by every escalate-capable rule, so this filters to its own parked
        // orders. Unresolved rather than merely pending, for the reason the notional guardrail
        // gives: an entry approved but not yet routed is armed and still owed a decision trail.
        var pendingEscalations = (Resolve<RiskEscalationQueueService>()?.GetUnresolved() ?? [])
            .Where(static entry => string.Equals(entry.RuleName, "PriceCollar", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Matched on the structured code, not on wording. The collar's refusal message shares the
        // phrase "has no reference price" with the fat-finger band's, so a text query would let
        // each rule claim the other's pricing gaps.
        var unmeasurableEntries = FindViolationEntries(
            auditEntries,
            actionHint: "OrderRejected",
            textHint: PriceCollarRule.UnmeasurableCode,
            asOf);

        var configured = collarPercent.HasValue;
        var pricingGap = HasLiveViolation(unmeasurableEntries, asOf);
        var parked = pendingEscalations.Count > 0;
        var state = !configured || pricingGap || parked ? "Observe" : "Healthy";
        var summary = !configured
            ? "No price collar is configured; the rule approves all orders."
            : pricingGap
                ? "Recent priced orders were refused because no reference price was available to measure them against the collar."
                : parked
                    ? $"{pendingEscalations.Count} order(s) are parked awaiting approval to price through the collar."
                    : "A price collar is configured and no orders are parked against it.";

        // Only the count, never the parked orders' details: this status is served by the rules
        // endpoint, which has no order-management permission or fund-scope check.
        var recentViolations = parked
            ? [$"{pendingEscalations.Count} order(s) parked awaiting approval to price through the collar."]
            : DescribeViolations(unmeasurableEntries, "price collar");

        return new RiskRuleStatusDto(
            RuleName: "PriceCollar",
            State: state,
            Summary: summary,
            // Never breached, by construction. A parked order has not broken a rail — it is the
            // rail working, and awaiting the judgement the collar exists to demand. Reporting it
            // as a breach would drive the readiness gate that reads this field to treat every
            // deliberate aggressive order as a fault. A pricing gap is not a breach either, for
            // the reason the fat-finger band gives: the rule refused an order it could not price
            // rather than measuring one past a band.
            IsBreached: false,
            Threshold: configured
                ? $"escalate > {collarPercent!.Value.ToString("G29", CultureInfo.InvariantCulture)}% through market"
                : "unconfigured",
            CurrentValue: configured ? $"{pendingEscalations.Count} pending approval(s)" : "not enforced",
            AsOf: asOf,
            RecentViolations: recentViolations,
            UtilizationPercent: null,
            Severity: "Escalate");
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

    /// <summary>
    /// Overload for rules whose breaches can also refuse an <em>amendment</em>. A modification the
    /// rule rejects is audited as <c>OrderModifyRejected</c>, not <c>OrderRejected</c>, so a
    /// single-action query reports the rule healthy while it is actively refusing amendments.
    /// </summary>
    private static List<ExecutionAuditEntry> FindViolationEntries(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        IReadOnlyList<string> actionHints,
        string textHint,
        DateTimeOffset asOf)
    {
        return auditEntries
            .Where(entry =>
                actionHints.Any(hint => string.Equals(entry.Action, hint, StringComparison.OrdinalIgnoreCase)) &&
                IsDatedPlausibly(entry, asOf) &&
                ((entry.Message?.Contains(textHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 (entry.Reason?.Contains(textHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 MatchesViolationMetadata(entry, textHint)))
            .OrderByDescending(static entry => entry.OccurredAt)
            .Take(5)
            .ToList();
    }

    private static List<ExecutionAuditEntry> FindViolationEntries(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        string actionHint,
        string textHint,
        DateTimeOffset asOf)
    {
        return auditEntries
            .Where(entry =>
                string.Equals(entry.Action, actionHint, StringComparison.OrdinalIgnoreCase) &&
                IsDatedPlausibly(entry, asOf) &&
                ((entry.Message?.Contains(textHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 (entry.Reason?.Contains(textHint, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 MatchesViolationMetadata(entry, textHint)))
            .OrderByDescending(static entry => entry.OccurredAt)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// Whether an entry's timestamp is believable enough to be evidence at all.
    /// <para>
    /// Misdated entries are dropped here, before the five-entry truncation, rather than only when
    /// live state is computed. Filtering later is not equivalent: these entries sort newest-first,
    /// so five far-future rows would take every slot and evict a genuine breach from half an hour
    /// ago — and the retained five would then all fail the liveness bound, reporting the rule
    /// Healthy at the exact moment it was constrained. An entry that cannot be dated is not
    /// evidence for a breach or against one, so it is excluded from both the live state and the
    /// displayed history. Only the future side is bounded: an entry older than the liveness window
    /// is ordinary history and belongs in the list.
    /// </para>
    /// </summary>
    private static bool IsDatedPlausibly(ExecutionAuditEntry entry, DateTimeOffset asOf) =>
        entry.OccurredAt - asOf <= ViolationClockSkewAllowance;

    /// <summary>
    /// Searches the structured violation set the rejection audit carries, not just its headline.
    /// <para>
    /// Every rule is evaluated before a decision is taken, so one rejection can record several
    /// breaches while only the most severe becomes the message. Matching on the headline alone made
    /// every other rule's breach invisible to rule status and history — a position-limit breach
    /// behind a drawdown headline simply disappeared.
    /// </para>
    /// </summary>
    private static bool MatchesViolationMetadata(ExecutionAuditEntry entry, string textHint) =>
        entry.Metadata is { } metadata &&
        metadata.Any(pair =>
            pair.Key.StartsWith(ViolationMetadataPrefix, StringComparison.Ordinal) &&
            (pair.Key.EndsWith(".rule", StringComparison.Ordinal) ||
             pair.Key.EndsWith(".code", StringComparison.Ordinal)) &&
            ContainsTokenIgnoringSeparators(pair.Value, textHint));

    /// <summary>
    /// Substring match that ignores word separators on both sides, because the same rule is named
    /// three ways across the system and a literal search finds only one of them. The status query
    /// asks for <c>fat-finger</c>, matching the prose in the rejection message, but the structured
    /// metadata stores the rule as <c>FatFinger</c> and its codes as <c>FAT_FINGER_*</c> — so a
    /// literal search read the headline and missed the metadata entirely. That mattered exactly
    /// when the metadata is the only evidence: a rule whose breach is recorded behind a more
    /// severe rule's headline is discoverable through <c>violation.*</c> and nowhere else, so a
    /// FatFinger breach alongside a Critical exposure breach vanished from its own status.
    /// </summary>
    private static bool ContainsTokenIgnoringSeparators(string value, string textHint)
    {
        if (value.Contains(textHint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return StripSeparators(value).Contains(StripSeparators(textHint), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripSeparators(string value) =>
        value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Reports the message belonging to the matched breach rather than the entry's headline, so a
    /// rule's status quotes what that rule found instead of what a more severe rule found.
    /// </summary>
    private static List<string> DescribeViolations(IEnumerable<ExecutionAuditEntry> entries, string textHint) =>
        entries
            .Select(entry => DescribeViolation(entry, textHint))
            .ToList();

    private static string DescribeViolation(ExecutionAuditEntry entry, string textHint)
    {
        if (entry.Metadata is { } metadata)
        {
            var matched = metadata
                .Where(pair =>
                    pair.Key.StartsWith(ViolationMetadataPrefix, StringComparison.Ordinal) &&
                    (pair.Key.EndsWith(".rule", StringComparison.Ordinal) ||
                     pair.Key.EndsWith(".code", StringComparison.Ordinal)) &&
                    ContainsTokenIgnoringSeparators(pair.Value, textHint))
                .Select(pair => pair.Key[..pair.Key.LastIndexOf('.')])
                .FirstOrDefault();

            if (matched is not null &&
                metadata.TryGetValue($"{matched}.message", out var message) &&
                !string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return entry.Message ?? entry.Reason ?? $"{entry.Action} recorded at {entry.OccurredAt:O}.";
    }

    /// <summary>Prefix for the per-violation audit metadata keys, e.g. <c>violation.0.rule</c>.</summary>
    private const string ViolationMetadataPrefix = "violation.";

    /// <summary>
    /// How far ahead of <c>asOf</c> an audit entry may sit and still be treated as live. Clock
    /// skew between a host and its audit sink is real and small; anything beyond this is a
    /// misdated entry, not a recent one.
    /// </summary>
    private static readonly TimeSpan ViolationClockSkewAllowance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether any of these breaches is recent enough to still describe the rule's live state.
    /// <para>
    /// The window is bounded at <em>both</em> ends. An unbounded upper end let a future-dated
    /// entry — a backward clock step after an append, or a misdated retained entry — produce a
    /// negative age, which trivially satisfies a one-hour ceiling and would hold the rule (and the
    /// operator readiness gate reading it) Constrained until an hour past that timestamp. A badly
    /// skewed entry could pin trading readiness for years. A bounded skew allowance keeps ordinary
    /// host/sink drift live while refusing to treat a misdated entry as evidence of anything.
    /// </para>
    /// <para>
    /// The upper bound is also applied by <see cref="IsDatedPlausibly"/> before candidates are
    /// truncated, so it is deliberately repeated here: this method is the guarantee for any caller
    /// that assembles its own entry list.
    /// </para>
    /// </summary>
    private static bool HasLiveViolation(IReadOnlyList<ExecutionAuditEntry> entries, DateTimeOffset asOf) =>
        entries.Any(entry =>
            asOf - entry.OccurredAt <= ViolationLivenessWindow &&
            IsDatedPlausibly(entry, asOf));

    private static List<string> FindViolations(
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        string actionHint,
        string textHint,
        DateTimeOffset asOf) =>
        DescribeViolations(FindViolationEntries(auditEntries, actionHint, textHint, asOf), textHint);

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
        // A baseline that never existed is genuinely unmeasurable and reads as 0%. A real
        // baseline that has been wiped out is the opposite of that: the ratio below carries
        // it past -100%, which is exactly what an exhausted book should report.
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
            "fatfinger" => "FatFinger",
            "pricecollar" => "PriceCollar",
            _ => null
        };
    }

    /// <summary>
    /// Records that a configured collar can never fire, because the fat-finger band refuses first
    /// at a tighter or equal distance.
    /// <para>
    /// This warns rather than refuses, because the two bands are set independently and either
    /// ordering can produce the state: refusing a wide collar would still let a later narrowing of
    /// the fat-finger band strand it. A rule that could only reject half the paths into a condition
    /// would read as a guarantee it does not provide, so both call sites emit the same evidence
    /// instead. The collar is not wrong here — it is redundant — and an operator deliberately
    /// running one control is entitled to do so knowingly.
    /// </para>
    /// </summary>
    private void WarnIfCollarUnreachable(decimal? fatFingerBand, decimal? collar)
    {
        if (fatFingerBand is not > 0m || collar is not > 0m || collar.Value < fatFingerBand.Value)
        {
            return;
        }

        _logger.LogWarning(
            "Price collar of {PriceCollarPercent}% can never park an order: the fat-finger band rejects at {MaxPriceDeviationPercent}% first. Set the collar below the band for it to take effect.",
            collar.Value.ToString("G29", CultureInfo.InvariantCulture),
            fatFingerBand.Value.ToString("G29", CultureInfo.InvariantCulture));
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
        decimal? EscalateOrderNotional,
        decimal? MaxOrderQuantity,
        decimal? MaxPriceDeviationPercent,
        decimal? PriceCollarPercent);

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
                    _escalateOrderNotional,
                    _maxOrderQuantity,
                    _maxPriceDeviationPercent,
                    _priceCollarPercent));
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
                EscalateOrderNotional: proposed.EscalateOrderNotional,
                MaxOrderQuantity: proposed.MaxOrderQuantity,
                MaxPriceDeviationPercent: proposed.MaxPriceDeviationPercent,
                PriceCollarPercent: proposed.PriceCollarPercent);
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
                _maxOrderQuantity = proposed.MaxOrderQuantity;
                _maxPriceDeviationPercent = proposed.MaxPriceDeviationPercent;
                _priceCollarPercent = proposed.PriceCollarPercent;
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
            var snapshot = JsonSerializer.Deserialize(payload, RiskRuleRuntimeSnapshotJsonContext.Default.RiskRuleRuntimeSnapshot)
                // Valid JSON "null" is an existing snapshot carrying no thresholds — the
                // same silent revert to "unconfigured" the catch below refuses, so it takes
                // the same fail-closed path rather than quietly disabling the rails.
                ?? throw new InvalidOperationException(
                    "The risk rule snapshot contains no configuration.");

            // Required-field integrity check. The optional rails are legitimately null when
            // an operator has not set them, so they cannot distinguish "unconfigured" from
            // "truncated". These two always carry a value in a snapshot this service wrote,
            // so their absence means the file is not a complete snapshot — and normalizing
            // an incomplete one would quietly disable every rail it happened to omit.
            if (snapshot.MaxDrawdownPercent <= 0m || snapshot.MaxOrdersPerMinute <= 0)
            {
                throw new InvalidOperationException(
                    "The risk rule snapshot is missing required fields and is not a complete configuration.");
            }

            lock (_gate)
            {
                _maxDrawdownPercent = snapshot.MaxDrawdownPercent > 0m ? snapshot.MaxDrawdownPercent : DefaultDrawdownPercent;
                _maxOrdersPerMinute = snapshot.MaxOrdersPerMinute > 0 ? snapshot.MaxOrdersPerMinute : DefaultMaxOrdersPerMinute;
                _maxGrossExposure = snapshot.MaxGrossExposure is > 0m ? snapshot.MaxGrossExposure : null;
                _maxSymbolConcentrationPercent = snapshot.MaxSymbolConcentrationPercent is > 0m ? snapshot.MaxSymbolConcentrationPercent : null;
                _maxOrderNotional = snapshot.MaxOrderNotional is > 0m ? snapshot.MaxOrderNotional : null;
                _escalateOrderNotional = snapshot.EscalateOrderNotional is > 0m ? snapshot.EscalateOrderNotional : null;
                // The two fat-finger rails are validated on the way in, not merely normalized. An
                // optional rail is legitimately absent when an operator has not set one, so null
                // hydrates as "unconfigured" — but a value the update endpoint would refuse is a
                // different thing entirely, and quietly turning it into null would disable that
                // limb while the dashboard still reported the rule configured. Reject rather than
                // clamp: a value this far out means the file is not a configuration this service
                // wrote, and the catch below fails closed on it.
                //
                // A negative ceiling is the same class of corruption as a band of 100.
                if (snapshot.MaxOrderQuantity is < 0m)
                {
                    throw new InvalidOperationException(
                        "The risk rule snapshot carries a negative fat-finger quantity ceiling. "
                        + "Refusing to start with a silently disabled quantity limb.");
                }

                // A deviation band of 100 or more can never reject a sell, so the update endpoint
                // refuses it; hydrating one would leave the sell side unprotected.
                if (snapshot.MaxPriceDeviationPercent is < 0m or >= 100m)
                {
                    throw new InvalidOperationException(
                        "The risk rule snapshot carries a fat-finger price-deviation band that is negative, or 100 or more and so unable to "
                        + "reject any sell. Refusing to start with a silently one-sided or disabled price control.");
                }

                // Same bound as the fat-finger band, and for the same reason: a sell's aggressive
                // deviation is strictly under 100%, so a collar at or above it can never park a
                // sell while the dashboard still reports it configured. Thrown rather than skipped
                // — abandoning the load here would leave the rails above already assigned and the
                // ones below at their defaults, which is the silent half-configuration this whole
                // block exists to refuse.
                if (snapshot.PriceCollarPercent is < 0m or >= 100m)
                {
                    throw new InvalidOperationException(
                        "The risk rule snapshot carries a price collar that is negative, or 100 or more and so unable to park any sell. "
                        + "Refusing to start with a silently one-sided or disabled price collar.");
                }

                _maxOrderQuantity = snapshot.MaxOrderQuantity is > 0m ? snapshot.MaxOrderQuantity : null;
                _maxPriceDeviationPercent = snapshot.MaxPriceDeviationPercent is > 0m ? snapshot.MaxPriceDeviationPercent : null;
                _priceCollarPercent = snapshot.PriceCollarPercent is > 0m ? snapshot.PriceCollarPercent : null;
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
    decimal? EscalateOrderNotional = null,
    decimal? MaxOrderQuantity = null,
    decimal? MaxPriceDeviationPercent = null,
    decimal? PriceCollarPercent = null);

[JsonSerializable(typeof(RiskRuleRuntimeSnapshot))]
internal sealed partial class RiskRuleRuntimeSnapshotJsonContext : JsonSerializerContext
{
}
