using System;

namespace Meridian.Contracts.Services;

/// <summary>
/// Contract for probing internet connectivity and broadcasting state changes.
/// </summary>
public interface IConnectivityProbeService
{
    /// <summary>
    /// Gets whether the system is currently online.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Raised when connectivity state changes (true = online, false = offline).
    /// </summary>
    event EventHandler<bool>? ConnectivityChanged;
}
