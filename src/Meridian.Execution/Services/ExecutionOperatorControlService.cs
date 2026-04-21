using System.Text.Json;
using Meridian.Execution.Models;
using Meridian.Execution.Serialization;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// Configuration for persisted execution operator controls.
/// </summary>
public sealed record ExecutionOperatorControlOptions(string RootDirectory)
{
    public static ExecutionOperatorControlOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "execution", "controls"));

    public string SnapshotPath => Path.Combine(RootDirectory, "controls.json");
}

/// <summary>
/// Supported manual override kinds for operator-managed execution governance.
/// </summary>
public static class ExecutionManualOverrideKinds
{
    public const string BypassOrderControls = "BypassOrderControls";
    public const string AllowLivePromotion = "AllowLivePromotion";
    public const string ForceBlockOrders = "ForceBlockOrders";

    public static bool IsSupported(string kind) =>
        string.Equals(kind, BypassOrderControls, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, AllowLivePromotion, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, ForceBlockOrders, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Current execution circuit-breaker state.
/// </summary>
public sealed record ExecutionCircuitBreakerState(
    bool IsOpen,
    string? Reason = null,
    string? ChangedBy = null,
    DateTimeOffset? ChangedAt = null);

/// <summary>
/// Operator-created manual override used to temporarily bypass or force execution controls.
/// </summary>
public sealed record ExecutionManualOverride(
    string OverrideId,
    string Kind,
    string Reason,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt = null,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null);

/// <summary>
/// Persisted snapshot of the live execution control state.
/// </summary>
public sealed record ExecutionControlSnapshot(
    ExecutionCircuitBreakerState CircuitBreaker,
    decimal? DefaultMaxPositionSize,
    IReadOnlyDictionary<string, decimal> SymbolPositionLimits,
    IReadOnlyList<ExecutionManualOverride> ManualOverrides,
    DateTimeOffset AsOf);

/// <summary>
/// Manual override creation request.
/// </summary>
public sealed record ManualOverrideRequest(
    string Kind,
    string Reason,
    string? CreatedBy = null,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null,
    DateTimeOffset? ExpiresAt = null,
    string? CorrelationId = null);

/// <summary>
/// Result of evaluating a new order against the current operator controls.
/// </summary>
public sealed record ExecutionControlDecision(
    bool IsApproved,
    string? RejectReason = null,
    string? AppliedManualOverrideId = null)
{
    public static ExecutionControlDecision Approved(string? appliedManualOverrideId = null) =>
        new(true, null, appliedManualOverrideId);

    public static ExecutionControlDecision Rejected(string reason) =>
        new(false, reason, null);
}

/// <summary>
/// Result of evaluating a Paper -&gt; Live promotion request against the current controls.
/// </summary>
public sealed record LivePromotionControlDecision(bool IsAllowed, string? RejectReason = null)
{
    public static LivePromotionControlDecision Allowed() => new(true, null);
    public static LivePromotionControlDecision Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Tracks operator-managed circuit breakers, position limits, and manual overrides.
/// Control changes are persisted atomically because they are infrequent, while order
/// evaluation stays entirely in-memory to keep routing latency predictable.
/// </summary>
public sealed class ExecutionOperatorControlService
{
    private readonly ExecutionOperatorControlOptions _options;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger<ExecutionOperatorControlService> _logger;
    private readonly Lock _lock = new();

    private ExecutionCircuitBreakerState _circuitBreaker = new(false);
    private decimal? _defaultMaxPositionSize;
    private Dictionary<string, decimal> _symbolPositionLimits = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ExecutionManualOverride> _manualOverrides = new(StringComparer.OrdinalIgnoreCase);

    public ExecutionOperatorControlService(
        ExecutionOperatorControlOptions? options,
        ILogger<ExecutionOperatorControlService> logger,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null)
    {
        _options = options ?? ExecutionOperatorControlOptions.Default;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditTrail = auditTrail;

        LoadSnapshot();

        if (_defaultMaxPositionSize is null && brokerageConfiguration?.MaxPositionSize > 0m)
        {
            _defaultMaxPositionSize = brokerageConfiguration.MaxPositionSize;
        }
    }

    /// <summary>
    /// Returns the current control state.
    /// </summary>
    public ExecutionControlSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
            return BuildSnapshotLocked();
        }
    }

    /// <summary>
    /// Opens or closes the global execution circuit breaker.
    /// </summary>
    public async Task<ExecutionControlSnapshot> SetCircuitBreakerAsync(
        bool isOpen,
        string? reason,
        string? changedBy,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ExecutionControlSnapshot snapshot;

        lock (_lock)
        {
            _circuitBreaker = new ExecutionCircuitBreakerState(
                IsOpen: isOpen,
                Reason: string.IsNullOrWhiteSpace(reason) ? null : reason,
                ChangedBy: NormalizeActor(changedBy),
                ChangedAt: DateTimeOffset.UtcNow);
            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
            snapshot = BuildSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        await RecordAuditAsync(
            isOpen ? "CircuitBreakerOpened" : "CircuitBreakerClosed",
            NormalizeActor(changedBy),
            reason ?? (isOpen ? "Execution circuit breaker opened." : "Execution circuit breaker closed."),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isOpen"] = isOpen.ToString()
            },
            NormalizeOptionalToken(correlationId),
            null,
            null,
            ct).ConfigureAwait(false);

        return snapshot;
    }

    /// <summary>
    /// Updates the default position limit used when no symbol-specific limit exists.
    /// </summary>
    public async Task<ExecutionControlSnapshot> SetDefaultPositionLimitAsync(
        decimal? maxPositionSize,
        string? changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        if (maxPositionSize is <= 0m)
        {
            maxPositionSize = null;
        }

        ExecutionControlSnapshot snapshot;
        lock (_lock)
        {
            _defaultMaxPositionSize = maxPositionSize;
            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
            snapshot = BuildSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        await RecordAuditAsync(
            "DefaultPositionLimitUpdated",
            NormalizeActor(changedBy),
            reason ?? "Default position limit updated.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = maxPositionSize?.ToString("G29") ?? "unlimited"
            },
            null,
            null,
            null,
            ct).ConfigureAwait(false);

        return snapshot;
    }

    /// <summary>
    /// Updates or clears the symbol-specific position limit.
    /// </summary>
    public async Task<ExecutionControlSnapshot> SetSymbolPositionLimitAsync(
        string symbol,
        decimal? maxPositionSize,
        string? changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (maxPositionSize is <= 0m)
        {
            maxPositionSize = null;
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        ExecutionControlSnapshot snapshot;

        lock (_lock)
        {
            if (maxPositionSize.HasValue)
            {
                _symbolPositionLimits[normalizedSymbol] = maxPositionSize.Value;
            }
            else
            {
                _symbolPositionLimits.Remove(normalizedSymbol);
            }

            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
            snapshot = BuildSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        await RecordAuditAsync(
            "SymbolPositionLimitUpdated",
            NormalizeActor(changedBy),
            reason ?? $"Position limit updated for {normalizedSymbol}.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = normalizedSymbol,
                ["limit"] = maxPositionSize?.ToString("G29") ?? "unlimited"
            },
            null,
            null,
            normalizedSymbol,
            ct).ConfigureAwait(false);

        return snapshot;
    }

    /// <summary>
    /// Creates a new manual override.
    /// </summary>
    public async Task<ExecutionManualOverride> CreateManualOverrideAsync(
        ManualOverrideRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ExecutionManualOverrideKinds.IsSupported(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Kind), request.Kind, "Unsupported manual override kind.");
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ExpiresAt), "Manual override expiration must be in the future.");
        }

        var overrideEntry = new ExecutionManualOverride(
            OverrideId: $"ovr-{Guid.NewGuid():N}",
            Kind: request.Kind,
            Reason: request.Reason,
            CreatedBy: NormalizeActor(request.CreatedBy),
            CreatedAt: DateTimeOffset.UtcNow,
            ExpiresAt: request.ExpiresAt,
            Symbol: NormalizeOptionalToken(request.Symbol),
            StrategyId: NormalizeOptionalToken(request.StrategyId),
            RunId: NormalizeOptionalToken(request.RunId));

        ExecutionControlSnapshot snapshot;
        lock (_lock)
        {
            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
            _manualOverrides[overrideEntry.OverrideId] = overrideEntry;
            snapshot = BuildSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        await RecordAuditAsync(
            action: "ManualOverrideCreated",
            actor: overrideEntry.CreatedBy,
            message: overrideEntry.Reason,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["overrideId"] = overrideEntry.OverrideId,
                ["kind"] = overrideEntry.Kind,
                ["symbol"] = overrideEntry.Symbol ?? string.Empty,
                ["strategyId"] = overrideEntry.StrategyId ?? string.Empty,
                ["runId"] = overrideEntry.RunId ?? string.Empty,
                ["expiresAt"] = overrideEntry.ExpiresAt?.ToString("O") ?? string.Empty
            },
            correlationId: NormalizeOptionalToken(request.CorrelationId),
            runId: overrideEntry.RunId,
            symbol: overrideEntry.Symbol,
            ct: ct).ConfigureAwait(false);

        return overrideEntry;
    }

    /// <summary>
    /// Clears an existing manual override.
    /// </summary>
    public async Task<bool> ClearManualOverrideAsync(
        string overrideId,
        string? changedBy,
        string? reason,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(overrideId);

        ExecutionManualOverride? removed = null;
        ExecutionControlSnapshot snapshot;

        lock (_lock)
        {
            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
            if (_manualOverrides.Remove(overrideId, out var existing))
            {
                removed = existing;
            }

            snapshot = BuildSnapshotLocked();
        }

        if (removed is null)
        {
            return false;
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);

        await RecordAuditAsync(
            action: "ManualOverrideCleared",
            actor: NormalizeActor(changedBy),
            message: reason ?? removed.Reason,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["overrideId"] = removed.OverrideId,
                ["kind"] = removed.Kind,
                ["symbol"] = removed.Symbol ?? string.Empty,
                ["strategyId"] = removed.StrategyId ?? string.Empty,
                ["runId"] = removed.RunId ?? string.Empty
            },
            correlationId: NormalizeOptionalToken(correlationId),
            runId: removed.RunId,
            symbol: removed.Symbol,
            ct: ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Evaluates a new order against the current operator controls.
    /// </summary>
    public ExecutionControlDecision EvaluateOrder(OrderRequest request, IPortfolioState? portfolioState)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            PurgeExpiredOverridesLocked(now);

            var forceBlock = _manualOverrides.Values.FirstOrDefault(overrideEntry =>
                string.Equals(overrideEntry.Kind, ExecutionManualOverrideKinds.ForceBlockOrders, StringComparison.OrdinalIgnoreCase) &&
                OverrideMatchesOrder(overrideEntry, request));

            if (forceBlock is not null)
            {
                return ExecutionControlDecision.Rejected(
                    $"Manual override {forceBlock.OverrideId} is blocking new orders: {forceBlock.Reason}");
            }

            string? requestedOverrideId = null;
            request.Metadata?.TryGetValue("manualOverrideId", out requestedOverrideId);
            var bypassOverride = TryResolveManualOverrideLocked(
                requestedOverrideId,
                ExecutionManualOverrideKinds.BypassOrderControls,
                request.Symbol,
                request.StrategyId,
                runId: null);

            if (_circuitBreaker.IsOpen && bypassOverride is null)
            {
                return ExecutionControlDecision.Rejected(
                    _circuitBreaker.Reason ?? "Execution circuit breaker is open.");
            }

            var limit = ResolvePositionLimitLocked(request.Symbol);
            if (limit is > 0m && bypassOverride is null)
            {
                var currentQuantity = 0m;
                var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();
                if (portfolioState?.Positions.TryGetValue(normalizedSymbol, out var existingPosition) == true)
                {
                    currentQuantity = existingPosition.Quantity;
                }

                var signedDelta = request.Side == OrderSide.Buy ? request.Quantity : -request.Quantity;
                var projectedQuantity = currentQuantity + signedDelta;

                if (Math.Abs(projectedQuantity) > limit.Value)
                {
                    return ExecutionControlDecision.Rejected(
                        $"Projected position {projectedQuantity:G29} exceeds limit {limit.Value:G29} for {normalizedSymbol}.");
                }
            }

            return ExecutionControlDecision.Approved(bypassOverride?.OverrideId);
        }
    }

    /// <summary>
    /// Evaluates whether a Paper -&gt; Live promotion may proceed.
    /// </summary>
    public LivePromotionControlDecision EvaluateLivePromotion(
        string runId,
        string? strategyId,
        string? manualOverrideId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        lock (_lock)
        {
            PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);

            if (_circuitBreaker.IsOpen)
            {
                return LivePromotionControlDecision.Rejected(
                    "Paper -> Live promotion is blocked while the execution circuit breaker is open.");
            }

            if (string.IsNullOrWhiteSpace(manualOverrideId))
            {
                return LivePromotionControlDecision.Rejected(
                    "Paper -> Live promotion requires an active AllowLivePromotion manual override.");
            }

            var livePromotionOverride = TryResolveManualOverrideLocked(
                manualOverrideId,
                ExecutionManualOverrideKinds.AllowLivePromotion,
                symbol: null,
                strategyId: strategyId,
                runId: runId);

            return livePromotionOverride is null
                ? LivePromotionControlDecision.Rejected(
                    $"Manual override {manualOverrideId} is not active for live promotion.")
                : LivePromotionControlDecision.Allowed();
        }
    }

    private void LoadSnapshot()
    {
        if (!File.Exists(_options.SnapshotPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_options.SnapshotPath);
            var snapshot = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.ExecutionControlSnapshot);
            if (snapshot is null)
            {
                return;
            }

            _circuitBreaker = snapshot.CircuitBreaker;
            _defaultMaxPositionSize = snapshot.DefaultMaxPositionSize;
            _symbolPositionLimits = new Dictionary<string, decimal>(
                snapshot.SymbolPositionLimits,
                StringComparer.OrdinalIgnoreCase);
            _manualOverrides = snapshot.ManualOverrides.ToDictionary(
                static entry => entry.OverrideId,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to load execution control snapshot from {Path}", _options.SnapshotPath);
        }
    }

    private async Task PersistSnapshotAsync(ExecutionControlSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, ExecutionJsonContext.Default.ExecutionControlSnapshot);
        await AtomicFileWriter.WriteAsync(_options.SnapshotPath, json, ct).ConfigureAwait(false);
    }

    private async Task RecordAuditAsync(
        string action,
        string actor,
        string message,
        IReadOnlyDictionary<string, string>? metadata,
        string? correlationId,
        string? runId,
        string? symbol,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            return;
        }

        await _auditTrail.RecordAsync(
            category: "Control",
            action: action,
            outcome: "Completed",
            actor: actor,
            runId: runId,
            symbol: symbol,
            correlationId: correlationId,
            message: message,
            metadata: metadata,
            ct: ct).ConfigureAwait(false);
    }

    private ExecutionControlSnapshot BuildSnapshotLocked()
    {
        return new ExecutionControlSnapshot(
            CircuitBreaker: _circuitBreaker,
            DefaultMaxPositionSize: _defaultMaxPositionSize,
            SymbolPositionLimits: new Dictionary<string, decimal>(_symbolPositionLimits, StringComparer.OrdinalIgnoreCase),
            ManualOverrides: _manualOverrides.Values
                .OrderByDescending(static entry => entry.CreatedAt)
                .ToArray(),
            AsOf: DateTimeOffset.UtcNow);
    }

    private void PurgeExpiredOverridesLocked(DateTimeOffset now)
    {
        var expiredOverrideIds = _manualOverrides.Values
            .Where(static entry => entry.ExpiresAt.HasValue)
            .Where(entry => entry.ExpiresAt <= now)
            .Select(static entry => entry.OverrideId)
            .ToArray();

        foreach (var expiredOverrideId in expiredOverrideIds)
        {
            _manualOverrides.Remove(expiredOverrideId);
        }
    }

    private ExecutionManualOverride? TryResolveManualOverrideLocked(
        string? overrideId,
        string requiredKind,
        string? symbol,
        string? strategyId,
        string? runId)
    {
        if (string.IsNullOrWhiteSpace(overrideId) ||
            !_manualOverrides.TryGetValue(overrideId, out var overrideEntry))
        {
            return null;
        }

        if (!string.Equals(overrideEntry.Kind, requiredKind, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!MatchesOptionalTarget(overrideEntry.Symbol, symbol) ||
            !MatchesOptionalTarget(overrideEntry.StrategyId, strategyId) ||
            !MatchesOptionalTarget(overrideEntry.RunId, runId))
        {
            return null;
        }

        return overrideEntry;
    }

    private decimal? ResolvePositionLimitLocked(string symbol)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        return _symbolPositionLimits.TryGetValue(normalizedSymbol, out var symbolLimit)
            ? symbolLimit
            : _defaultMaxPositionSize;
    }

    private static bool OverrideMatchesOrder(ExecutionManualOverride overrideEntry, OrderRequest request) =>
        MatchesOptionalTarget(overrideEntry.Symbol, request.Symbol) &&
        MatchesOptionalTarget(overrideEntry.StrategyId, request.StrategyId);

    private static bool MatchesOptionalTarget(string? configuredTarget, string? actualTarget)
    {
        if (string.IsNullOrWhiteSpace(configuredTarget))
        {
            return true;
        }

        return string.Equals(
            configuredTarget.Trim(),
            actualTarget?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeActor(string? actor) =>
        string.IsNullOrWhiteSpace(actor) ? "operator" : actor.Trim();

    private static string? NormalizeOptionalToken(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token.Trim();
}
