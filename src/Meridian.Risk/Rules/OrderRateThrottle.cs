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

        // Purge old entries
        while (_recentOrders.TryPeek(out var oldest) && oldest < cutoff)
        {
            _recentOrders.TryDequeue(out _);
        }

        if (_recentOrders.Count >= _maxOrdersPerMinute)
        {
            _logger.LogWarning("Order rate throttle: {Count} orders in last minute exceeds limit {Limit}",
                _recentOrders.Count, _maxOrdersPerMinute);

            return Task.FromResult(RiskValidationResult.Rejected(
                $"Order rate limit: {_recentOrders.Count} orders/min exceeds {_maxOrdersPerMinute} limit"));
        }

        _recentOrders.Enqueue(now);
        return Task.FromResult(RiskValidationResult.Approved());
    }
}
