using Meridian.Application.Monitoring;

namespace Meridian.Application.Composition;

/// <summary>
/// Static bridge that receives Polly circuit breaker state-change notifications
/// and forwards them to the <see cref="CircuitBreakerStatusService"/> singleton.
/// </summary>
/// <remarks>
/// This indirection is required because Polly callback delegates are captured
/// as closures during DI service registration (before the container is built),
/// while <see cref="CircuitBreakerStatusService"/> is only resolvable after
/// the container is built.  <see cref="Initialize"/> is called once by
/// <see cref="HostStartup.InitializeHttpClientFactory"/> after the root
/// service provider is constructed.
/// </remarks>
internal static class CircuitBreakerCallbackRouter
{
    private static volatile CircuitBreakerStatusService? _service;

    /// <summary>
    /// Wires the router to forward state-change notifications to <paramref name="service"/>.
    /// Call this once after the DI container is built.
    /// </summary>
    public static void Initialize(CircuitBreakerStatusService service)
    {
        _service = service;
    }

    /// <summary>
    /// Forwards a circuit breaker state transition to the service.
    /// Safe to call before <see cref="Initialize"/>; notifications are silently dropped.
    /// </summary>
    public static void Notify(string name, CircuitBreakerState state, string? lastError)
    {
        _service?.RecordStateTransition(name, state, lastError);
    }
}
