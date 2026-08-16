using System.Text.Json;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.Services;

/// <summary>
/// Configuration for persisted execution operator controls.
/// </summary>
public sealed record ExecutionOperatorControlOptions(
    string RootDirectory,
    bool FailClosedOnMissingOrCorruptSnapshot = false)
{
    public static ExecutionOperatorControlOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "execution", "controls"));

    public string SnapshotPath => Path.Combine(RootDirectory, "controls.json");

    /// <summary>
    /// Marker recording a circuit-breaker trip that was demanded but could not be written
    /// into the snapshot. It is independent of the snapshot on purpose: the snapshot write
    /// is exactly what failed, so the halt needs somewhere else to survive a restart.
    /// </summary>
    public string PendingTripPath => Path.Combine(RootDirectory, "pending-circuit-breaker-trip.txt");
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
    DateTimeOffset AsOf,
    long Version = 0);

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
    string? AppliedManualOverrideId = null,
    string? RejectCode = null)
{
    public static ExecutionControlDecision Approved(string? appliedManualOverrideId = null) =>
        new(true, null, appliedManualOverrideId);

    public static ExecutionControlDecision Rejected(string reason, string? rejectCode = null) =>
        new(false, reason, null, rejectCode);
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
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    private ExecutionCircuitBreakerState _circuitBreaker = new(false);
    private decimal? _defaultMaxPositionSize;
    private Dictionary<string, decimal> _symbolPositionLimits = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ExecutionManualOverride> _manualOverrides = new(StringComparer.OrdinalIgnoreCase);
    private long _version;

    /// <summary>
    /// Absolute quantity already working in orders that would reduce the given symbol's position
    /// for the given fund account, excluding the order being evaluated.
    /// <para>
    /// Set by the composition that owns the open book — the gate itself cannot see working orders.
    /// Close-only admission compares settled position against <em>committed</em> reduction, not
    /// against this one order alone: two 10-share sells against a 10-share long each pass in
    /// isolation and together leave the account short 10, reopening risk behind the kill switch.
    /// </para>
    /// <para>
    /// An unset probe means working reductions cannot be established, so close-only refuses. That
    /// is the same fail-closed posture the rest of this exception takes, and it is why production
    /// compositions wire it: a host that does not is telling the gate it cannot answer the
    /// question, and under an open breaker that is not a reason to route.
    /// </para>
    /// </summary>
    public Func<string, Guid?, decimal>? WorkingReductionQuantityProbe { get; set; }

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
        ApplyPendingTripMarker();

        if (_defaultMaxPositionSize is null && brokerageConfiguration?.MaxPositionSize > 0m)
        {
            _defaultMaxPositionSize = brokerageConfiguration.MaxPositionSize;
        }
    }

    /// <summary>
    /// Convenience constructor for test scenarios that do not need explicit options or a logger.
    /// Uses <see cref="ExecutionOperatorControlOptions.Default"/> and a null logger.
    /// </summary>
    public ExecutionOperatorControlService()
        : this(ExecutionOperatorControlOptions.Default, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExecutionOperatorControlService>.Instance)
    {
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
        var (_, snapshot, _) = await MutateAndPersistAsync(
            () =>
            {
                _circuitBreaker = new ExecutionCircuitBreakerState(
                IsOpen: isOpen,
                Reason: string.IsNullOrWhiteSpace(reason) ? null : reason,
                ChangedBy: NormalizeActor(changedBy),
                ChangedAt: DateTimeOffset.UtcNow);
                return true;
            },
            shouldPersist: null,
            ct).ConfigureAwait(false);

        // Immediately after the snapshot commit and before the audit write: an audit
        // failure or a cancelled request must not leave a superseded marker on disk, or
        // the next restart would reopen the breaker an operator just durably closed.
        //
        // Marker removal is part of the close decision, not bookkeeping after it. A marker
        // that survives will reopen the breaker on the next restart, so reporting a
        // successful close while it is still there would promise a state the next start
        // silently revokes.
        if (!ClearPendingTripMarker())
        {
            throw new InvalidOperationException(
                $"The circuit breaker decision was persisted, but the pending-trip marker at "
                + $"'{_options.PendingTripPath}' could not be removed; a restart would reopen the breaker. "
                + "Remove the marker before relying on this state.");
        }

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

        var (_, snapshot, _) = await MutateAndPersistAsync(
            () =>
            {
                _defaultMaxPositionSize = maxPositionSize;
                return true;
            },
            shouldPersist: null,
            ct).ConfigureAwait(false);

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
        var (_, snapshot, _) = await MutateAndPersistAsync(
            () =>
            {
                if (maxPositionSize.HasValue)
                {
                    _symbolPositionLimits[normalizedSymbol] = maxPositionSize.Value;
                }
                else
                {
                    _symbolPositionLimits.Remove(normalizedSymbol);
                }

                return true;
            },
            shouldPersist: null,
            ct).ConfigureAwait(false);

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

        await MutateAndPersistAsync(
            () =>
            {
                _manualOverrides[overrideEntry.OverrideId] = overrideEntry;
                return true;
            },
            shouldPersist: null,
            ct).ConfigureAwait(false);

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

        var (removed, _, persisted) = await MutateAndPersistAsync(
            () => _manualOverrides.Remove(overrideId, out var existing) ? existing : null,
            static value => value is not null,
            ct).ConfigureAwait(false);

        if (!persisted || removed is null)
        {
            return false;
        }

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
    public ExecutionControlDecision EvaluateOrder(OrderRequest request, IPortfolioState? portfolioState, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            PurgeExpiredOverridesLocked(now);

            var forceBlock = _manualOverrides.Values.FirstOrDefault(overrideEntry =>
                string.Equals(overrideEntry.Kind, ExecutionManualOverrideKinds.ForceBlockOrders, StringComparison.OrdinalIgnoreCase) &&
                OverrideMatchesTarget(overrideEntry, ManualOverrideTarget.ForOrder(request, runId)));

            if (forceBlock is not null)
            {
                return ExecutionControlDecision.Rejected(
                    $"Manual override {forceBlock.OverrideId} is blocking new orders: {forceBlock.Reason}",
                    "MANUAL_FORCE_BLOCK");
            }

            string? requestedOverrideId = null;
            request.Metadata?.TryGetValue("manualOverrideId", out requestedOverrideId);
            var bypassOverride = TryResolveManualOverrideLocked(
                requestedOverrideId,
                ExecutionManualOverrideKinds.BypassOrderControls,
                ManualOverrideTarget.ForOrder(request, runId));

            if (_circuitBreaker.IsOpen)
            {
                if (bypassOverride is null)
                {
                    return ExecutionControlDecision.Rejected(
                        _circuitBreaker.Reason ?? "Execution circuit breaker is open.",
                        "CIRCUIT_BREAKER_OPEN");
                }

                // Close-only. A bypass override exists so an operator can flatten a book the kill
                // switch just halted, and revoking it outright would trap the desk in exactly the
                // positions the halt was raised over. But the kill switch is also supposed to block
                // new submissions, and an unrestricted bypass let an override admit fresh risk while
                // the breaker was open — the book refilling behind the sweep that emptied it.
                //
                // The narrow exception is the one the override is actually for: an order that moves
                // an existing position toward flat, and no further.
                if (!ReducesExistingPosition(request, portfolioState))
                {
                    return ExecutionControlDecision.Rejected(
                        $"Execution circuit breaker is open: manual override {bypassOverride.OverrideId} admits only orders that "
                        + $"reduce an existing {request.Symbol} position. Clear the breaker to open or increase risk.",
                        "CIRCUIT_BREAKER_CLOSE_ONLY");
                }
            }

            var limit = ResolvePositionLimitLocked(request.Symbol);
            if (limit is > 0m && bypassOverride is null)
            {
                var currentQuantity = 0m;
                var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();
                if (portfolioState?.Positions.TryGetValue(normalizedSymbol, out var existingPosition) == true)
                {
                    // Unrounded: IPosition.Quantity is whole shares, and the order delta
                    // below is decimal. Fractional and broker-native notional fills are real,
                    // so rounding the held side lets a 0.9-share position plus a 0.2-share
                    // buy read as 0.2 against a 1-share cap and approve a 1.1-share position.
                    currentQuantity = existingPosition.ExactQuantity;
                }

                var signedDelta = request.Side == OrderSide.Buy ? request.Quantity : -request.Quantity;
                var projectedQuantity = currentQuantity + signedDelta;

                if (Math.Abs(projectedQuantity) > limit.Value)
                {
                    return ExecutionControlDecision.Rejected(
                        $"Projected position {projectedQuantity:G29} exceeds limit {limit.Value:G29} for {normalizedSymbol}.",
                        "POSITION_LIMIT_EXCEEDED");
                }
            }

            return ExecutionControlDecision.Approved(bypassOverride?.OverrideId);
        }
    }

    /// <summary>
    /// Whether an order moves an existing position toward flat without crossing through it.
    /// <para>
    /// This is the one door left open in a halted desk, so it refuses anything it cannot establish
    /// is a reduction. It measures the position that actually backs the order, not merely a
    /// position in the same symbol.
    /// </para>
    /// </summary>
    private bool ReducesExistingPosition(OrderRequest request, IPortfolioState? portfolioState)
    {
        if (portfolioState is null)
        {
            return false;
        }

        // A multi-leg package's parent fields are not what routes. The gateway replaces the parent
        // symbol with the legs, so a parent that reads as a close can carry legs that open fresh
        // option exposure. Verifying each leg against its own position is real work this gate does
        // not do, so packages are refused rather than approximated.
        if (request.Legs is { Count: > 0 })
        {
            return false;
        }

        // An explicit opening intent settles the question regardless of what the arithmetic below
        // would say: SellToOpen against a long is a new short, not a reduction.
        if (request.PositionIntent is Sdk.PositionIntent.BuyToOpen or Sdk.PositionIntent.SellToOpen)
        {
            return false;
        }

        // Broker-native notional orders route a dollar amount and the gateway discards Quantity, so
        // comparing Quantity with a share count compares two different things: a placeholder
        // quantity would pass while the routed dollars crossed through flat into a short. Converting
        // requires an authoritative price this gate does not have, so they are refused.
        if (BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is not null)
        {
            return false;
        }

        var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();
        if (!portfolioState.Positions.TryGetValue(normalizedSymbol, out var position))
        {
            return false;
        }

        var held = ResolveHeldQuantity(position, request.FundAccountId);
        if (held == 0m)
        {
            return false;
        }

        var opposesPosition = held > 0m
            ? request.Side == OrderSide.Sell
            : request.Side == OrderSide.Buy;

        if (!opposesPosition)
        {
            return false;
        }

        var probe = WorkingReductionQuantityProbe;
        if (probe is null)
        {
            return false;
        }

        decimal alreadyWorking;
        try
        {
            alreadyWorking = Math.Abs(probe(normalizedSymbol, request.FundAccountId));
        }
        catch (Exception exception)
        {
            // A probe that throws has established nothing. Refusing is the same answer as an
            // absent probe, and it keeps a faulting book from admitting an unbounded close.
            _logger.LogWarning(
                exception,
                "Close-only admission refused: the working-reduction probe failed for {Symbol}",
                normalizedSymbol);
            return false;
        }

        return Math.Abs(request.Quantity) + alreadyWorking <= Math.Abs(held);
    }

    /// <summary>
    /// The signed quantity backing this order: the requesting fund's share when the order names a
    /// fund account, and the aggregate otherwise.
    /// <para>
    /// A shared execution book nets several funds' positions onto one symbol. Measuring a
    /// fund-scoped close against that aggregate lets fund B sell against fund A's long and acquire
    /// a new short, and conversely stops two funds with offsetting holdings from closing at all
    /// because the net reads flat. A named fund with no attributed quantity holds nothing here, so
    /// it reduces nothing — deliberately not falling back to the aggregate, which is the very
    /// number that would be wrong.
    /// </para>
    /// </summary>
    private static decimal ResolveHeldQuantity(IPosition position, Guid? fundAccountId)
    {
        // Unrounded, for the reason the position-limit gate gives: a fractional holding rounded to
        // zero would read as flat and refuse a legitimate close.
        if (fundAccountId is not { } fundAccount)
        {
            return position.ExactQuantity;
        }

        var owner = fundAccount.ToString("D");
        foreach (var pair in position.OwnerQuantities)
        {
            if (string.Equals(pair.Key, owner, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return 0m;
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
                new ManualOverrideTarget(Symbol: null, StrategyId: strategyId, RunId: runId));

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
            EnterFailClosedStateIfRequired("Execution control snapshot is missing.");
            return;
        }

        try
        {
            var json = File.ReadAllText(_options.SnapshotPath);
            var snapshot = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.ExecutionControlSnapshot);
            if (snapshot is null)
            {
                throw new JsonException("Execution control snapshot deserialized to null.");
            }

            ApplySnapshotLocked(snapshot);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to load execution control snapshot from {Path}", _options.SnapshotPath);
            EnterFailClosedStateIfRequired("Execution control snapshot is unreadable or corrupt.");
        }
    }

    /// <summary>
    /// Durably records a circuit-breaker trip whose snapshot write failed, so a restart
    /// before the retry succeeds still comes up halted. Best-effort by nature — the caller
    /// already holds an in-memory fail-closed latch — but a snapshot write can fail for
    /// reasons a small independent marker survives. Returns whether the marker landed.
    /// </summary>
    public async Task<bool> TryRecordPendingCircuitBreakerTripAsync(string? reason, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_options.RootDirectory);
            await AtomicFileWriter
                .WriteAsync(
                    _options.PendingTripPath,
                    string.IsNullOrWhiteSpace(reason) ? "Critical risk rule demanded a halt." : reason,
                    ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "A demanded circuit-breaker trip could not be durably recorded; the halt survives only in memory");
            return false;
        }
    }

    /// <summary>
    /// Opens the breaker at startup when a previous process recorded a trip it could not
    /// persist. The snapshot on disk is still the stale pre-trip one, so without this the
    /// halt would silently disappear across a restart.
    /// </summary>
    private void ApplyPendingTripMarker()
    {
        string reason;
        try
        {
            if (!File.Exists(_options.PendingTripPath))
            {
                return;
            }

            reason = File.ReadAllText(_options.PendingTripPath).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The marker exists but is unreadable: its very presence is the halt signal.
            _logger.LogError(exception, "Pending circuit-breaker trip marker could not be read; halting anyway");
            reason = string.Empty;
        }

        if (_circuitBreaker.IsOpen)
        {
            return;
        }

        _circuitBreaker = new ExecutionCircuitBreakerState(
            IsOpen: true,
            Reason: string.IsNullOrWhiteSpace(reason)
                ? "A circuit-breaker trip from a previous process was never durably committed."
                : reason,
            ChangedBy: "risk-engine/pending-trip-recovery",
            ChangedAt: DateTimeOffset.UtcNow);
        _logger.LogCritical(
            "Execution circuit breaker opened at startup: a previous process demanded a halt that never reached the snapshot");
    }

    /// <summary>
    /// Clears the pending-trip marker once an explicit breaker decision has been durably
    /// written — the snapshot is authoritative again, in either direction.
    /// </summary>
    private bool ClearPendingTripMarker()
    {
        try
        {
            if (File.Exists(_options.PendingTripPath))
            {
                File.Delete(_options.PendingTripPath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Pending circuit-breaker trip marker could not be cleared");
            return false;
        }
    }

    private void EnterFailClosedStateIfRequired(string reason)
    {
        if (!_options.FailClosedOnMissingOrCorruptSnapshot)
        {
            return;
        }

        _circuitBreaker = new ExecutionCircuitBreakerState(
            IsOpen: true,
            Reason: reason,
            ChangedBy: "system",
            ChangedAt: DateTimeOffset.UtcNow);
        _defaultMaxPositionSize = null;
        _symbolPositionLimits = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        _manualOverrides = new Dictionary<string, ExecutionManualOverride>(StringComparer.OrdinalIgnoreCase);
        _version = 0;
    }

    private async Task<(T Result, ExecutionControlSnapshot Snapshot, bool Persisted)> MutateAndPersistAsync<T>(
        Func<T> mutation,
        Func<T, bool>? shouldPersist,
        CancellationToken ct)
    {
        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ExecutionControlSnapshot previous;
            ExecutionControlSnapshot next;
            T result;
            lock (_lock)
            {
                PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
                previous = BuildSnapshotLocked();
                result = mutation();
                if (shouldPersist is not null && !shouldPersist(result))
                {
                    return (result, previous, false);
                }

                _version = checked(_version + 1);
                PurgeExpiredOverridesLocked(DateTimeOffset.UtcNow);
                next = BuildSnapshotLocked();
            }

            try
            {
                await PersistSnapshotAsync(next, ct).ConfigureAwait(false);
            }
            catch
            {
                lock (_lock)
                {
                    ApplySnapshotLocked(previous);
                }

                throw;
            }

            return (result, next, true);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void ApplySnapshotLocked(ExecutionControlSnapshot snapshot)
    {
        _circuitBreaker = snapshot.CircuitBreaker;
        _defaultMaxPositionSize = snapshot.DefaultMaxPositionSize;
        _symbolPositionLimits = new Dictionary<string, decimal>(
            snapshot.SymbolPositionLimits,
            StringComparer.OrdinalIgnoreCase);
        _manualOverrides = snapshot.ManualOverrides.ToDictionary(
            static entry => entry.OverrideId,
            StringComparer.OrdinalIgnoreCase);
        _version = snapshot.Version;
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
            AsOf: DateTimeOffset.UtcNow,
            Version: _version);
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
        ManualOverrideTarget target)
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

        if (!OverrideMatchesTarget(overrideEntry, target))
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

    private static bool OverrideMatchesTarget(ExecutionManualOverride overrideEntry, ManualOverrideTarget target) =>
        MatchesOptionalTarget(overrideEntry.Symbol, target.Symbol) &&
        MatchesOptionalTarget(overrideEntry.StrategyId, target.StrategyId) &&
        MatchesOptionalTarget(overrideEntry.RunId, target.RunId);

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

    private readonly record struct ManualOverrideTarget(
        string? Symbol,
        string? StrategyId,
        string? RunId)
    {
        public static ManualOverrideTarget ForOrder(OrderRequest request, string? runId) =>
            new(request.Symbol, request.StrategyId, runId);
    }
}
