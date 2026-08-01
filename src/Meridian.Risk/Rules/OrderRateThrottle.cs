using System.Globalization;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk.Rules;

/// <summary>
/// Throttles order submission rate to prevent runaway algorithms.
/// <para>
/// Capacity is consumed by <em>reservation</em> during evaluation rather than by enqueueing on the
/// pass path. That keeps the purge → count → reserve sequence atomic under one lock, exactly as the
/// original check-and-enqueue was, while letting the caller release the slot if the order is
/// ultimately blocked by another rule or fails before reaching the venue. Counting an order that
/// never routed would throttle later orders for no reason.
/// </para>
/// </summary>
public sealed class OrderRateThrottle : IReservingRiskRule
{
    /// <summary>Code emitted when the ceiling is reached.</summary>
    public const string RateExceededCode = "ORDER_RATE_EXCEEDED";

    // Slots for orders that actually routed. Only these expire out of the rolling window.
    private readonly List<DateTimeOffset> _committed = [];

    // Slots held by in-flight submissions. These deliberately do NOT expire: a submission whose
    // acknowledgement takes longer than the window must keep blocking capacity, or the slot would
    // be purged while the order is still live and a second order could take it.
    private readonly HashSet<long> _pending = [];
    private long _nextReservationId;

    // Serializes purge → count → reserve. Each step is cheap; the lock exists so concurrent
    // callers cannot all observe room below the cap and all consume it.
    private readonly Lock _sync = new();
    private readonly Func<int> _maxOrdersPerMinute;
    private readonly ILogger<OrderRateThrottle> _logger;
    private readonly TimeProvider _timeProvider;

    public OrderRateThrottle(int maxOrdersPerMinute, ILogger<OrderRateThrottle> logger)
        : this(() => maxOrdersPerMinute, logger)
    {
    }

    /// <summary>
    /// Creates a throttle whose ceiling is read per evaluation, so operator-tuned (hot-reloaded)
    /// limits take effect without rebuilding the rule.
    /// </summary>
    public OrderRateThrottle(Func<int> maxOrdersPerMinute, ILogger<OrderRateThrottle> logger)
        : this(maxOrdersPerMinute, logger, TimeProvider.System)
    {
    }

    public OrderRateThrottle(
        Func<int> maxOrdersPerMinute,
        ILogger<OrderRateThrottle> logger,
        TimeProvider timeProvider)
    {
        _maxOrdersPerMinute = maxOrdersPerMinute ?? throw new ArgumentNullException(nameof(maxOrdersPerMinute));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public string RuleName => "OrderRateThrottle";

    /// <summary>
    /// Slots currently held: routed orders still inside the window, plus every in-flight
    /// submission holding a pending reservation.
    /// <para>
    /// This is what the rule will actually compare against the ceiling on the next evaluation, so a
    /// status surface reading it can never disagree with the gate. Reconstructing the same number
    /// from audit history cannot match it — a reservation is taken before any audit record exists
    /// and released without one — so a projection would show capacity during the window between
    /// submission and acknowledgement, exactly when the throttle is most likely to be blocking.
    /// </para>
    /// </summary>
    public int CurrentUsage
    {
        get
        {
            var cutoff = _timeProvider.GetUtcNow().AddMinutes(-1);
            lock (_sync)
            {
                _committed.RemoveAll(timestamp => timestamp < cutoff);
                return _committed.Count + _pending.Count;
            }
        }
    }

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Not used: this rule reserves capacity, which must happen inside
    /// <see cref="EvaluateAndReserveAsync"/> so the check and the consumption stay atomic.
    /// </summary>
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Evaluate(reserve: false).Result);
    }

    /// <inheritdoc />
    public Task<RiskRuleReservationResult> EvaluateAndReserveAsync(
        OrderRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Evaluate(reserve: true));
    }

    private RiskRuleReservationResult Evaluate(bool reserve)
    {
        var now = _timeProvider.GetUtcNow();
        var cutoff = now.AddMinutes(-1);
        var maxOrdersPerMinute = _maxOrdersPerMinute();

        lock (_sync)
        {
            _committed.RemoveAll(timestamp => timestamp < cutoff);

            // Capacity is committed-in-window plus everything still in flight.
            var count = _committed.Count + _pending.Count;
            if (count >= maxOrdersPerMinute)
            {
                _logger.LogWarning(
                    "Order rate throttle: {Count} orders in last minute reaches limit {Limit}",
                    count,
                    maxOrdersPerMinute);

                return new RiskRuleReservationResult(
                    RiskValidationResult.Rejected(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Order rate limit: {count} orders/min reaches the {maxOrdersPerMinute} order ceiling.")) with
                    {
                        Code = RateExceededCode,
                        ObservedValue = count,
                        LimitValue = maxOrdersPerMinute,
                    },
                    Reservation: null);
            }

            if (!reserve)
            {
                return new RiskRuleReservationResult(RiskValidationResult.Approved(), Reservation: null);
            }

            var id = ++_nextReservationId;
            _pending.Add(id);
            return new RiskRuleReservationResult(
                RiskValidationResult.Approved(),
                Reservation: new SlotReservation(this, id));
        }
    }

    /// <summary>Releases a pending slot: the order never routed.</summary>
    private void Release(long id)
    {
        lock (_sync)
        {
            _pending.Remove(id);
        }
    }

    /// <summary>
    /// Converts a pending slot into a committed one stamped at the routing time.
    /// <para>
    /// The window measures routed orders, so the slot expires relative to acknowledgement rather
    /// than to reservation: at a one-per-minute ceiling, an order reserved at t=0 and accepted at
    /// t=50 must hold its slot until t=110, not t=60.
    /// </para>
    /// </summary>
    private void Promote(long id, DateTimeOffset routedAt)
    {
        lock (_sync)
        {
            if (_pending.Remove(id))
            {
                _committed.Add(routedAt);
            }
        }
    }

    /// <summary>
    /// One consumed slot in the rate window. Settling is idempotent, and the slot keeps blocking
    /// capacity while it is pending — including past the window length — so an in-flight submission
    /// cannot be double-spent.
    /// </summary>
    private sealed class SlotReservation(OrderRateThrottle owner, long id) : IRiskReservation
    {
        private int _settled;

        public void Commit()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                owner.Promote(id, owner._timeProvider.GetUtcNow());
            }
        }

        public void Rollback()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                owner.Release(id);
            }
        }
    }
}
