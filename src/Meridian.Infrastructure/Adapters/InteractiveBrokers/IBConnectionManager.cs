namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// No-op connection manager stub for builds without the Interactive Brokers API.
/// </summary>
/// <remarks>
/// <para>
/// This stub enables the project to compile without requiring external IB API dependencies.
/// All operations are no-ops that return immediately with success indicators.
/// </para>
/// <para>
/// For production use with Interactive Brokers, compile with the IBAPI define to use
/// <see cref="EnhancedIBConnectionManager"/> which provides full TWS/Gateway integration.
/// </para>
/// </remarks>
/// <seealso cref="EnhancedIBConnectionManager"/>
public sealed class IBConnectionManager
{
    /// <summary>
    /// Gets a value indicating whether a connection is active.
    /// Always returns the last set value (no actual connection is established).
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Simulates a connection (no-op for stub implementation).
    /// </summary>
    public Task ConnectAsync()
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates a disconnection (no-op for stub implementation).
    /// </summary>
    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }
}
