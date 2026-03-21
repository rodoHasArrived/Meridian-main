using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Wpf.Contracts;

/// <summary>
/// Interface for managing provider connections with auto-reconnection support.
/// Enables testability and dependency injection.
/// Phase 6C.2: Connection types (ConnectionState, ConnectionSettings, event args)
/// are now shared from Meridian.Ui.Services.Contracts.
/// </summary>
public interface IConnectionService : IDisposable
{
    /// <summary>Gets the current service URL being used for connections.</summary>
    string ServiceUrl { get; }

    /// <summary>Gets the current connection state.</summary>
    ConnectionState State { get; }

    /// <summary>Gets the current provider name.</summary>
    string CurrentProvider { get; }

    /// <summary>Gets the connection uptime.</summary>
    TimeSpan? Uptime { get; }

    /// <summary>Gets the last measured latency.</summary>
    double LastLatencyMs { get; }

    /// <summary>Gets the total reconnection count.</summary>
    int TotalReconnects { get; }

    /// <summary>Updates connection settings and reconfigures the API client if URL changed.</summary>
    void UpdateSettings(ConnectionSettings settings);

    /// <summary>Configures the service URL directly.</summary>
    void ConfigureServiceUrl(string serviceUrl, int timeoutSeconds = 30);

    /// <summary>Gets current connection settings.</summary>
    ConnectionSettings GetSettings();

    /// <summary>Starts the connection monitoring and auto-reconnection.</summary>
    void StartMonitoring();

    /// <summary>Stops connection monitoring.</summary>
    void StopMonitoring();

    /// <summary>Initiates a connection to the provider.</summary>
    Task<bool> ConnectAsync(string provider, CancellationToken ct = default);

    /// <summary>Disconnects from the provider.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Pauses auto-reconnection temporarily.</summary>
    void PauseAutoReconnect();

    /// <summary>Resumes auto-reconnection.</summary>
    void ResumeAutoReconnect();

    /// <summary>Event raised when connection state changes.</summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>Event raised when connection state changes (alias for ViewModel compatibility).</summary>
    event EventHandler<ConnectionStateEventArgs>? ConnectionStateChanged;

    /// <summary>Event raised when latency measurement is updated.</summary>
    event EventHandler<int>? LatencyUpdated;

    /// <summary>Event raised when a reconnection attempt is starting.</summary>
    event EventHandler<ReconnectEventArgs>? ReconnectAttempting;

    /// <summary>Event raised when reconnection succeeds.</summary>
    event EventHandler? ReconnectSucceeded;

    /// <summary>Event raised when all reconnection attempts fail.</summary>
    event EventHandler<ReconnectFailedEventArgs>? ReconnectFailed;

    /// <summary>Event raised when connection health is updated.</summary>
    event EventHandler<ConnectionHealthEventArgs>? ConnectionHealthUpdated;
}
