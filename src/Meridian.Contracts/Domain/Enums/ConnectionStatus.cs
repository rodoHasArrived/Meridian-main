namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Connection status for data providers.
/// </summary>
public enum ConnectionStatus : byte
{
    /// <summary>
    /// Not connected.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// Connection in progress.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// Successfully connected.
    /// </summary>
    Connected = 2,

    /// <summary>
    /// Reconnection in progress.
    /// </summary>
    Reconnecting = 3,

    /// <summary>
    /// Connection failed or in error state.
    /// </summary>
    Faulted = 4
}
