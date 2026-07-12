using System.Collections.Concurrent;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Risk.Rules;

/// <summary>
/// Throttles order submission rate to prevent runaway algorithms.
/// </summary>
public sealed class OrderRateThrottle : IRiskRule
{
    private readonly ConcurrentQueue<DateTimeOffset> _recentOrders = new();

    // Serializes the purge → count → enqueue sequence. Each ConcurrentQueue operation is
    // individually thread-safe, but without this lock concurrent callers can all observe a
    // count below the limit and then all enqueue, letting an order burst exceed the cap.
    private readonly Lock _sync = new();
    private readonly int _maxOrdersPerMinute;
    private readonly ILogger<OrderRateThrottle> _logger;

    public OrderRateThrottle(int maxOrdersPerMinute, ILogger<OrderRateThrottle> logger)
    {
        _maxOrdersPerMinute = maxOrdersPerMinute;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "OrderRateThrottle";

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddMinutes(-1);

        lock (_sync)
        {
            // Purge old entries
            while (_recentOrders.TryPeek(out var oldest) && oldest < cutoff)
            {
                _recentOrders.TryDequeue(out _);
            }

            var count = _recentOrders.Count;
            if (count >= _maxOrdersPerMinute)
            {
                _logger.LogWarning("Order rate throttle: {Count} orders in last minute exceeds limit {Limit}",
                    count, _maxOrdersPerMinute);

                return Task.FromResult(RiskValidationResult.Rejected(
                    $"Order rate limit: {count} orders/min exceeds {_maxOrdersPerMinute} limit"));
            }

            _recentOrders.Enqueue(now);
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
