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

    private readonly List<DateTimeOffset> _admitted = [];

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

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Not used: this rule reserves capacity, which must happen inside
    /// <see cref="EvaluateAndReserveAsync"/> so the check and the consumption stay atomic.
    /// </summary>
    public Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Evaluate(reserve: false).Finding);
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
            _admitted.RemoveAll(timestamp => timestamp < cutoff);

            var count = _admitted.Count;
            if (count >= maxOrdersPerMinute)
            {
                _logger.LogWarning(
                    "Order rate throttle: {Count} orders in last minute reaches limit {Limit}",
                    count,
                    maxOrdersPerMinute);

                return new RiskRuleReservationResult(
                    new RiskFinding(
                        Code: RateExceededCode,
                        Message: string.Create(
                            CultureInfo.InvariantCulture,
                            $"Order rate limit: {count} orders/min reaches the {maxOrdersPerMinute} order ceiling."),
                        ObservedValue: count,
                        LimitValue: maxOrdersPerMinute),
                    Reservation: null);
            }

            if (!reserve)
            {
                return new RiskRuleReservationResult(Finding: null, Reservation: null);
            }

            _admitted.Add(now);
            return new RiskRuleReservationResult(
                Finding: null,
                Reservation: new SlotReservation(this, now));
        }
    }

    private void Release(DateTimeOffset stamp)
    {
        lock (_sync)
        {
            var index = _admitted.LastIndexOf(stamp);
            if (index >= 0)
            {
                _admitted.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Re-stamps a committed slot to the time the order was actually routed. The window measures
    /// routed orders, so a slot reserved at evaluation time but acknowledged seconds later must
    /// expire relative to the acknowledgement — otherwise, at a one-per-minute ceiling, an order
    /// reserved at t=0 and accepted at t=50 frees its slot at t=60, only ten seconds after it
    /// actually routed.
    /// </summary>
    private void Restamp(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_sync)
        {
            var index = _admitted.LastIndexOf(from);
            if (index >= 0)
            {
                _admitted[index] = to;
            }
        }
    }

    /// <summary>
    /// One consumed slot in the rate window. Settling is idempotent, and the slot keeps blocking
    /// capacity while it is pending, so an in-flight submission cannot be double-spent.
    /// </summary>
    private sealed class SlotReservation(OrderRateThrottle owner, DateTimeOffset stamp) : IRiskReservation
    {
        private int _settled;

        public void Commit()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                owner.Restamp(stamp, owner._timeProvider.GetUtcNow());
            }
        }

        public void Rollback()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                owner.Release(stamp);
            }
        }
    }
}
