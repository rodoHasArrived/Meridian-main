using System.Collections.Concurrent;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Tracks per-provider health status and short-lived failure backoff for the
/// <see cref="CompositeHistoricalDataProvider"/>. Extracted so the composite provider does not
/// own health/backoff bookkeeping directly (single-responsibility collaborator).
/// </summary>
internal sealed class ProviderHealthTracker
{
    private readonly ConcurrentDictionary<string, ProviderHealthStatus> _healthStatus = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _providerFailures = new();
    private readonly TimeSpan _failureBackoffDuration;

    public ProviderHealthTracker(IEnumerable<string> providerNames, TimeSpan failureBackoffDuration)
    {
        _failureBackoffDuration = failureBackoffDuration;
        foreach (var name in providerNames)
        {
            _healthStatus[name] = new ProviderHealthStatus(name, true, "Not checked");
        }
    }

    /// <summary>Current health status of all tracked providers.</summary>
    public IReadOnlyDictionary<string, ProviderHealthStatus> Health => _healthStatus;

    /// <summary>
    /// Whether the provider is currently within its post-failure backoff window and should be skipped.
    /// </summary>
    public bool IsInBackoffPeriod(string providerName)
        => _providerFailures.TryGetValue(providerName, out var failedAt)
           && DateTimeOffset.UtcNow - failedAt < _failureBackoffDuration;

    /// <summary>Record a provider failure, starting its backoff window and marking it unhealthy.</summary>
    public void RecordFailure(string providerName, string message)
    {
        _providerFailures[providerName] = DateTimeOffset.UtcNow;
        UpdateHealth(providerName, isAvailable: false, message);
    }

    /// <summary>Clear a provider's failure state after a successful call.</summary>
    public void ClearFailure(string providerName)
        => _providerFailures.TryRemove(providerName, out _);

    /// <summary>Update the recorded health status for a provider.</summary>
    public void UpdateHealth(string providerName, bool isAvailable, string? message = null, TimeSpan? responseTime = null)
    {
        _healthStatus[providerName] = new ProviderHealthStatus(
            providerName,
            isAvailable,
            message,
            DateTimeOffset.UtcNow,
            responseTime);
    }
}
