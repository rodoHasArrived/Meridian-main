using System.Globalization;
using System.Text.Json;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record RiskRuleRuntimeOptions(string SnapshotPath)
{
    public static RiskRuleRuntimeOptions Default { get; } = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "workstation",
            "risk-rules.json"));
}

public sealed record RiskRuleConfigSnapshot(
    string RuleName,
    IReadOnlyDictionary<string, string> Config,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string Reason);

public sealed record RiskRuleStatusSnapshot(
    string RuleName,
    string DisplayName,
    string State,
    bool Breached,
    string Summary,
    string ThresholdLabel,
    string ThresholdValue,
    string CurrentValueLabel,
    string CurrentValue,
    DateTimeOffset LastEvaluatedAt);

public sealed record RiskRuleAggregateStatus(
    string State,
    string Summary,
    IReadOnlyList<RiskRuleStatusSnapshot> Rules);

public sealed class OperatorRiskRuleService : IRiskValidator
{
    private const string PositionLimitRuleName = "PositionLimit";
    private const string DrawdownRuleName = "DrawdownCircuitBreaker";
    private const string OrderRateRuleName = "OrderRateThrottle";

    private readonly ILogger<OperatorRiskRuleService> _logger;
    private readonly IPositionTracker? _positionTracker;
    private readonly RiskRuleRuntimeOptions _options;
    private readonly Lock _lock = new();
    private readonly Queue<DateTimeOffset> _recentOrders = new();

    private decimal _positionLimitMaxQuantity = 100m;
    private decimal _drawdownInitialCapital = 100_000m;
    private decimal _drawdownMaxPercent = 5m;
    private int _orderRateMaxOrdersPerMinute = 30;

    private DateTimeOffset _lastConfigUpdatedAt = DateTimeOffset.UtcNow;
    private string _lastConfigUpdatedBy = "system";
    private string _lastConfigReason = "Initial defaults";

    public OperatorRiskRuleService(
        ILogger<OperatorRiskRuleService> logger,
        IPositionTracker? positionTracker = null,
        RiskRuleRuntimeOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _positionTracker = positionTracker;
        _options = options ?? RiskRuleRuntimeOptions.Default;

        LoadSnapshot();
    }

    public Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            PurgeExpiredOrderRateSamplesLocked(DateTimeOffset.UtcNow);

            var positionDecision = EvaluatePositionLimitLocked(request);
            if (!positionDecision.IsApproved)
            {
                return Task.FromResult(positionDecision);
            }

            var drawdownDecision = EvaluateDrawdownLocked();
            if (!drawdownDecision.IsApproved)
            {
                return Task.FromResult(drawdownDecision);
            }

            var orderRateDecision = EvaluateOrderRateLocked(DateTimeOffset.UtcNow);
            if (!orderRateDecision.IsApproved)
            {
                return Task.FromResult(orderRateDecision);
            }

            _recentOrders.Enqueue(DateTimeOffset.UtcNow);
            return Task.FromResult(RiskValidationResult.Approved());
        }
    }

    public IReadOnlyList<RiskRuleStatusSnapshot> GetRuleStatuses()
    {
        lock (_lock)
        {
            return BuildRuleStatusesLocked(DateTimeOffset.UtcNow);
        }
    }

    public RiskRuleStatusSnapshot? GetRuleStatus(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        lock (_lock)
        {
            var normalized = NormalizeRuleName(ruleName);
            return BuildRuleStatusesLocked(DateTimeOffset.UtcNow)
                .FirstOrDefault(rule => string.Equals(rule.RuleName, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    public RiskRuleAggregateStatus GetAggregateStatus()
    {
        lock (_lock)
        {
            return BuildAggregateStatusLocked(DateTimeOffset.UtcNow);
        }
    }

    public RiskRuleConfigSnapshot GetRuleConfig(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        lock (_lock)
        {
            return BuildRuleConfigLocked(NormalizeRuleName(ruleName));
        }
    }

    public async Task<RiskRuleConfigSnapshot> UpdateRuleConfigAsync(
        string ruleName,
        IReadOnlyDictionary<string, string> config,
        string? updatedBy,
        string? reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentNullException.ThrowIfNull(config);
        ct.ThrowIfCancellationRequested();

        RiskRuleConfigSnapshot snapshot;

        lock (_lock)
        {
            var normalized = NormalizeRuleName(ruleName);
            ApplyConfigLocked(normalized, config);
            _lastConfigUpdatedAt = DateTimeOffset.UtcNow;
            _lastConfigUpdatedBy = NormalizeActor(updatedBy);
            _lastConfigReason = string.IsNullOrWhiteSpace(reason) ? "Operator updated rule configuration." : reason.Trim();
            snapshot = BuildRuleConfigLocked(normalized);
        }

        await PersistSnapshotAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Updated risk rule config for {RuleName} by {Actor} reason {Reason}",
            snapshot.RuleName,
            snapshot.UpdatedBy,
            snapshot.Reason);

        return snapshot;
    }

    private RiskValidationResult EvaluatePositionLimitLocked(OrderRequest request)
    {
        if (_positionTracker is null)
        {
            return RiskValidationResult.Approved();
        }

        var currentPosition = _positionTracker.GetPosition(request.Symbol).Quantity;
        var signedDelta = request.Side == OrderSide.Buy ? request.Quantity : -request.Quantity;
        var projectedQuantity = currentPosition + signedDelta;

        if (Math.Abs(projectedQuantity) > _positionLimitMaxQuantity)
        {
            return RiskValidationResult.Rejected(
                $"Position limit exceeded: projected {projectedQuantity.ToString("G29", CultureInfo.InvariantCulture)} > max {_positionLimitMaxQuantity.ToString("G29", CultureInfo.InvariantCulture)} for {request.Symbol}");
        }

        return RiskValidationResult.Approved();
    }

    private RiskValidationResult EvaluateDrawdownLocked()
    {
        if (_positionTracker is null || _drawdownInitialCapital <= 0m)
        {
            return RiskValidationResult.Approved();
        }

        var portfolioValue = _positionTracker.GetPortfolioValue();
        var drawdownPercent = ((_drawdownInitialCapital - portfolioValue) / _drawdownInitialCapital) * 100m;

        if (drawdownPercent >= _drawdownMaxPercent)
        {
            return RiskValidationResult.Rejected(
                $"Drawdown circuit breaker: {drawdownPercent.ToString("F2", CultureInfo.InvariantCulture)}% drawdown exceeds {_drawdownMaxPercent.ToString("F2", CultureInfo.InvariantCulture)}% threshold");
        }

        return RiskValidationResult.Approved();
    }

    private RiskValidationResult EvaluateOrderRateLocked(DateTimeOffset now)
    {
        PurgeExpiredOrderRateSamplesLocked(now);

        if (_recentOrders.Count >= _orderRateMaxOrdersPerMinute)
        {
            return RiskValidationResult.Rejected(
                $"Order rate limit: {_recentOrders.Count.ToString(CultureInfo.InvariantCulture)} orders/min exceeds {_orderRateMaxOrdersPerMinute.ToString(CultureInfo.InvariantCulture)} limit");
        }

        return RiskValidationResult.Approved();
    }

    private IReadOnlyList<RiskRuleStatusSnapshot> BuildRuleStatusesLocked(DateTimeOffset now)
    {
        PurgeExpiredOrderRateSamplesLocked(now);

        var statuses = new List<RiskRuleStatusSnapshot>(3)
        {
            BuildPositionLimitStatusLocked(now),
            BuildDrawdownStatusLocked(now),
            BuildOrderRateStatusLocked(now)
        };

        return statuses;
    }

    private RiskRuleAggregateStatus BuildAggregateStatusLocked(DateTimeOffset now)
    {
        var statuses = BuildRuleStatusesLocked(now);

        var constrained = statuses.Where(status => string.Equals(status.State, "Constrained", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (constrained.Length > 0)
        {
            return new RiskRuleAggregateStatus(
                State: "Constrained",
                Summary: $"Risk guardrails breached: {string.Join(", ", constrained.Select(static status => status.DisplayName))}.",
                Rules: statuses);
        }

        var observe = statuses.Where(status => string.Equals(status.State, "Observe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (observe.Length > 0)
        {
            return new RiskRuleAggregateStatus(
                State: "Observe",
                Summary: $"Risk guardrails require attention: {string.Join(", ", observe.Select(static status => status.DisplayName))}.",
                Rules: statuses);
        }

        return new RiskRuleAggregateStatus(
            State: "Healthy",
            Summary: "All configured risk guardrails are healthy.",
            Rules: statuses);
    }

    private RiskRuleStatusSnapshot BuildPositionLimitStatusLocked(DateTimeOffset now)
    {
        var currentMaxAbsPosition = 0m;

        if (_positionTracker is not null)
        {
            foreach (var position in _positionTracker.GetAllPositions().Values)
            {
                var absoluteQuantity = Math.Abs(position.Quantity);
                if (absoluteQuantity > currentMaxAbsPosition)
                {
                    currentMaxAbsPosition = absoluteQuantity;
                }
            }
        }

        var observeThreshold = _positionLimitMaxQuantity * 0.8m;
        var breached = currentMaxAbsPosition > _positionLimitMaxQuantity;
        var observe = !breached && currentMaxAbsPosition >= observeThreshold;

        var state = breached ? "Constrained" : observe ? "Observe" : "Healthy";
        var summary = breached
            ? $"Largest position {FormatDecimal(currentMaxAbsPosition)} exceeds max {FormatDecimal(_positionLimitMaxQuantity)}."
            : observe
                ? $"Largest position {FormatDecimal(currentMaxAbsPosition)} is nearing max {FormatDecimal(_positionLimitMaxQuantity)}."
                : $"Largest position {FormatDecimal(currentMaxAbsPosition)} is within max {FormatDecimal(_positionLimitMaxQuantity)}.";

        return new RiskRuleStatusSnapshot(
            RuleName: PositionLimitRuleName,
            DisplayName: "Position Limit",
            State: state,
            Breached: breached,
            Summary: summary,
            ThresholdLabel: "Max position",
            ThresholdValue: FormatDecimal(_positionLimitMaxQuantity),
            CurrentValueLabel: "Largest position",
            CurrentValue: FormatDecimal(currentMaxAbsPosition),
            LastEvaluatedAt: now);
    }

    private RiskRuleStatusSnapshot BuildDrawdownStatusLocked(DateTimeOffset now)
    {
        var drawdownPercent = 0m;

        if (_positionTracker is not null && _drawdownInitialCapital > 0m)
        {
            var portfolioValue = _positionTracker.GetPortfolioValue();
            drawdownPercent = ((_drawdownInitialCapital - portfolioValue) / _drawdownInitialCapital) * 100m;
        }

        var observeThreshold = _drawdownMaxPercent * 0.7m;
        var breached = drawdownPercent >= _drawdownMaxPercent;
        var observe = !breached && drawdownPercent >= observeThreshold;

        var state = breached ? "Constrained" : observe ? "Observe" : "Healthy";
        var summary = breached
            ? $"Drawdown {FormatPercent(drawdownPercent)} breached max {FormatPercent(_drawdownMaxPercent)}."
            : observe
                ? $"Drawdown {FormatPercent(drawdownPercent)} is nearing max {FormatPercent(_drawdownMaxPercent)}."
                : $"Drawdown {FormatPercent(drawdownPercent)} remains below max {FormatPercent(_drawdownMaxPercent)}.";

        return new RiskRuleStatusSnapshot(
            RuleName: DrawdownRuleName,
            DisplayName: "Drawdown Circuit Breaker",
            State: state,
            Breached: breached,
            Summary: summary,
            ThresholdLabel: "Max drawdown",
            ThresholdValue: FormatPercent(_drawdownMaxPercent),
            CurrentValueLabel: "Current drawdown",
            CurrentValue: FormatPercent(drawdownPercent),
            LastEvaluatedAt: now);
    }

    private RiskRuleStatusSnapshot BuildOrderRateStatusLocked(DateTimeOffset now)
    {
        var currentOrderRate = _recentOrders.Count;
        var observeThreshold = Math.Max(1, (int)Math.Floor(_orderRateMaxOrdersPerMinute * 0.8m));
        var breached = currentOrderRate >= _orderRateMaxOrdersPerMinute;
        var observe = !breached && currentOrderRate >= observeThreshold;

        var state = breached ? "Constrained" : observe ? "Observe" : "Healthy";
        var summary = breached
            ? $"Order rate {currentOrderRate.ToString(CultureInfo.InvariantCulture)}/min breached max {_orderRateMaxOrdersPerMinute.ToString(CultureInfo.InvariantCulture)}/min."
            : observe
                ? $"Order rate {currentOrderRate.ToString(CultureInfo.InvariantCulture)}/min is nearing max {_orderRateMaxOrdersPerMinute.ToString(CultureInfo.InvariantCulture)}/min."
                : $"Order rate {currentOrderRate.ToString(CultureInfo.InvariantCulture)}/min is within max {_orderRateMaxOrdersPerMinute.ToString(CultureInfo.InvariantCulture)}/min.";

        return new RiskRuleStatusSnapshot(
            RuleName: OrderRateRuleName,
            DisplayName: "Order Rate Throttle",
            State: state,
            Breached: breached,
            Summary: summary,
            ThresholdLabel: "Max orders per minute",
            ThresholdValue: _orderRateMaxOrdersPerMinute.ToString(CultureInfo.InvariantCulture),
            CurrentValueLabel: "Current order rate",
            CurrentValue: currentOrderRate.ToString(CultureInfo.InvariantCulture),
            LastEvaluatedAt: now);
    }

    private RiskRuleConfigSnapshot BuildRuleConfigLocked(string normalizedRuleName)
    {
        var config = normalizedRuleName switch
        {
            PositionLimitRuleName => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["maxPositionSize"] = FormatDecimal(_positionLimitMaxQuantity)
            },
            DrawdownRuleName => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["initialCapital"] = FormatDecimal(_drawdownInitialCapital),
                ["maxDrawdownPercent"] = FormatDecimal(_drawdownMaxPercent)
            },
            OrderRateRuleName => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["maxOrdersPerMinute"] = _orderRateMaxOrdersPerMinute.ToString(CultureInfo.InvariantCulture)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(normalizedRuleName), normalizedRuleName, "Unsupported rule name.")
        };

        return new RiskRuleConfigSnapshot(
            RuleName: normalizedRuleName,
            Config: config,
            UpdatedAt: _lastConfigUpdatedAt,
            UpdatedBy: _lastConfigUpdatedBy,
            Reason: _lastConfigReason);
    }

    private void ApplyConfigLocked(string normalizedRuleName, IReadOnlyDictionary<string, string> config)
    {
        switch (normalizedRuleName)
        {
            case PositionLimitRuleName:
                _positionLimitMaxQuantity = ParsePositiveDecimal(config, "maxPositionSize");
                return;
            case DrawdownRuleName:
                _drawdownInitialCapital = ParsePositiveDecimal(config, "initialCapital");
                _drawdownMaxPercent = ParsePositiveDecimal(config, "maxDrawdownPercent");
                return;
            case OrderRateRuleName:
                _orderRateMaxOrdersPerMinute = ParsePositiveInt(config, "maxOrdersPerMinute");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(normalizedRuleName), normalizedRuleName, "Unsupported rule name.");
        }
    }

    private void PurgeExpiredOrderRateSamplesLocked(DateTimeOffset now)
    {
        var cutoff = now.AddMinutes(-1);
        while (_recentOrders.Count > 0 && _recentOrders.Peek() < cutoff)
        {
            _recentOrders.Dequeue();
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        var snapshot = new PersistedRiskRuleSnapshot(
            PositionLimitMaxQuantity: _positionLimitMaxQuantity,
            DrawdownInitialCapital: _drawdownInitialCapital,
            DrawdownMaxPercent: _drawdownMaxPercent,
            OrderRateMaxOrdersPerMinute: _orderRateMaxOrdersPerMinute,
            UpdatedAt: _lastConfigUpdatedAt,
            UpdatedBy: _lastConfigUpdatedBy,
            Reason: _lastConfigReason);

        var payload = JsonSerializer.Serialize(snapshot, SerializerOptions);
        await AtomicFileWriter.WriteAsync(_options.SnapshotPath, payload, ct).ConfigureAwait(false);
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
            var snapshot = JsonSerializer.Deserialize<PersistedRiskRuleSnapshot>(payload, SerializerOptions);
            if (snapshot is null)
            {
                return;
            }

            _positionLimitMaxQuantity = snapshot.PositionLimitMaxQuantity > 0m
                ? snapshot.PositionLimitMaxQuantity
                : _positionLimitMaxQuantity;
            _drawdownInitialCapital = snapshot.DrawdownInitialCapital > 0m
                ? snapshot.DrawdownInitialCapital
                : _drawdownInitialCapital;
            _drawdownMaxPercent = snapshot.DrawdownMaxPercent > 0m
                ? snapshot.DrawdownMaxPercent
                : _drawdownMaxPercent;
            _orderRateMaxOrdersPerMinute = snapshot.OrderRateMaxOrdersPerMinute > 0
                ? snapshot.OrderRateMaxOrdersPerMinute
                : _orderRateMaxOrdersPerMinute;
            _lastConfigUpdatedAt = snapshot.UpdatedAt;
            _lastConfigUpdatedBy = NormalizeActor(snapshot.UpdatedBy);
            _lastConfigReason = string.IsNullOrWhiteSpace(snapshot.Reason)
                ? "Loaded persisted risk rule configuration."
                : snapshot.Reason;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load risk rule snapshot from {Path}", _options.SnapshotPath);
        }
    }

    private static string NormalizeRuleName(string ruleName)
    {
        return ruleName.Trim().ToLowerInvariant() switch
        {
            "positionlimit" => PositionLimitRuleName,
            "drawdowncircuitbreaker" => DrawdownRuleName,
            "orderratethrottle" => OrderRateRuleName,
            _ => throw new ArgumentOutOfRangeException(nameof(ruleName), ruleName, "Unsupported rule name.")
        };
    }

    private static string NormalizeActor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();

    private static decimal ParsePositiveDecimal(IReadOnlyDictionary<string, string> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0m)
        {
            throw new ArgumentException($"Configuration key '{key}' must be a positive decimal value.", nameof(config));
        }

        return parsed;
    }

    private static int ParsePositiveInt(IReadOnlyDictionary<string, string> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException($"Configuration key '{key}' must be a positive integer value.", nameof(config));
        }

        return parsed;
    }

    private static string FormatDecimal(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal value)
        => $"{value.ToString("F2", CultureInfo.InvariantCulture)}%";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record PersistedRiskRuleSnapshot(
        decimal PositionLimitMaxQuantity,
        decimal DrawdownInitialCapital,
        decimal DrawdownMaxPercent,
        int OrderRateMaxOrdersPerMinute,
        DateTimeOffset UpdatedAt,
        string UpdatedBy,
        string Reason);
}
