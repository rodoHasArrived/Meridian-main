namespace Meridian.Execution;

/// <summary>
/// Owns the OMS report intake and retained accounting-handoff replay.
/// Construction and read-only recovery inspection do not start background work.
/// </summary>
public interface IOrderManagementLifecycle
{
    /// <summary>
    /// Starts report intake once and enables order operations. Repeated starts are idempotent;
    /// a stopped instance cannot be restarted. The token cancels startup, not the service lifetime.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes admission, drains admitted operations and accepted fills, and stops background work.
    /// Repeated calls share one shutdown. Cancelling the token cancels the wait, not the drain;
    /// disposal still awaits it before dependencies can be released.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
