using System.Globalization;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record RiskRuleStatusDto(
    string RuleName,
    string State,
    string Summary,
    bool IsBreached,
    string Threshold,
    string CurrentValue,
    DateTimeOffset AsOf,
    IReadOnlyList<string> RecentViolations);

public sealed record RiskRuleConfigDto(
    string RuleName,
    decimal? DefaultMaxPositionSize,
    IReadOnlyDictionary<string, decimal>? SymbolPositionLimits,
    decimal? MaxDrawdownPercent,
    int? MaxOrdersPerMinute);

public sealed record RiskRuleConfigUpdateRequest(
    decimal? DefaultMaxPositionSize = null,
    IReadOnlyDictionary<string, decimal?>? SymbolPositionLimits = null,
    decimal? MaxDrawdownPercent = null,
    int? MaxOrdersPerMinute = null,
    string? Reason = null);

public sealed class RiskRuleRuntimeService
{
    private const decimal DefaultDrawdownPercent = 5m;
    private const int DefaultMaxOrdersPerMinute = 60;

    private readonly IServiceProvider _services;
    private readonly ILogger<RiskRuleRuntimeService> _logger;
    private readonly Lock _gate = new();

    private decimal _maxDrawdownPercent = DefaultDrawdownPercent;
    private int _maxOrdersPerMinute = DefaultMaxOrdersPerMinute;

    public RiskRuleRuntimeService(
        IServiceProvider services,
        ILogger<RiskRuleRuntimeService> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<RiskRuleStatusDto>> GetAllStatusesAsync(CancellationToken ct = default)
    {
        var auditEntries = await GetAuditEntriesAsync(ct).ConfigureAwait(false);
        var asOf = DateTimeOffset.UtcNow;
        return
        [
            BuildPositionLimitStatus(auditEntries, asOf),
            BuildDrawdownStatus(auditEntries, asOf),
            BuildOrderRateStatus(auditEntries, asOf)
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
                if (request.MaxDrawdownPercent is <= 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxDrawdownPercent), "MaxDrawdownPercent must be greater than zero.");
                }

                var maxDrawdownPercent = request.MaxDrawdownPercent.GetValueOrDefault();

                lock (_gate)
                {
                    _maxDrawdownPercent = maxDrawdownPercent;
                }
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: {MaxDrawdownPercent}%",
                    normalizedRule,
                    actor,
                    maxDrawdownPercent);
                break;
            case "OrderRateThrottle":
                if (request.MaxOrdersPerMinute is <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.MaxOrdersPerMinute), "MaxOrdersPerMinute must be greater than zero.");
                }

                var maxOrdersPerMinute = request.MaxOrdersPerMinute.GetValueOrDefault();

                lock (_gate)
                {
                    _maxOrdersPerMinute = maxOrdersPerMinute;
                }
                _logger.LogInformation(
                    "Risk rule config updated for {RuleName} by {Actor}: {MaxOrdersPerMinute} orders/minute",
                    normalizedRule,
                    actor,
                    maxOrdersPerMinute);
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

        var maxAbsoluteQuantity = 0m;
        if (portfolio is not null)
        {
            foreach (var position in portfolio.Positions.Values)
            {
                var quantity = Math.Abs(position.Quantity);
                if (quantity > maxAbsoluteQuantity)
                {
                    maxAbsoluteQuantity = quantity;
                }
            }
        }
        var threshold = snapshot?.DefaultMaxPositionSize;

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
            CurrentValue: maxAbsoluteQuantity.ToString("G29", CultureInfo.InvariantCulture),
            AsOf: asOf,
            RecentViolations: violations);
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
            ? (totalPnl / portfolioValue) * 100m
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
            RecentViolations: violations);
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
            RecentViolations: violations);
    }

    private static List<string> FindViolations(
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
            .Select(entry => entry.Message ?? entry.Reason ?? $"{entry.Action} recorded at {entry.OccurredAt:O}.")
            .ToList();
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
            _ => null
        };
    }

    private T? Resolve<T>() where T : class => _services.GetService(typeof(T)) as T;
}
