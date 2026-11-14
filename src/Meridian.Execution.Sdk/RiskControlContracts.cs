namespace Meridian.Execution.Sdk;

/// <summary>Durable circuit-breaker authority used by pre-trade validation.</summary>
public interface IRiskCircuitBreakerControl
{
    bool IsCircuitBreakerOpen { get; }
    Task SetCircuitBreakerAsync(bool isOpen, string? reason, string? changedBy,
        string? correlationId = null, CancellationToken ct = default);
    Task<bool> TryRecordPendingCircuitBreakerTripAsync(string? reason, CancellationToken ct = default);
}

/// <summary>Retains the actual outcome of an automated circuit-breaker cancellation sweep.</summary>
public interface IRiskControlAuditWriter
{
    Task RecordCircuitBreakerSweepAsync(string outcome, string? actor, string? message,
        string? reason, CancellationToken ct = default);
}
