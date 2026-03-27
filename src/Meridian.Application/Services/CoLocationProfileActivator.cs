using System.Net;
using Meridian.Application.Logging;
using Meridian.Core.Performance;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Activates exchange colocation profile for ultra-low-latency trading:
/// - GC to sustained low-latency mode
/// - Nagle algorithm disabled at socket and HTTP layers
/// - Optional thread affinity to CPU core 0 (exchange colocation data center pattern)
/// </summary>
public interface ICoLocationProfileActivator
{
    /// <summary>
    /// Gets whether the CoLocation profile is currently active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Activates the colocation profile.
    /// </summary>
    void Activate();
}

/// <summary>
/// Implements exchange colocation profile activation.
/// </summary>
public sealed class CoLocationProfileActivator : ICoLocationProfileActivator
{
    private readonly ILogger _logger = LoggingSetup.ForContext<CoLocationProfileActivator>();
    private bool _isActive;

    public bool IsActive => _isActive;

    public void Activate()
    {
        if (_isActive)
        {
            _logger.Warning("CoLocation profile already active; skipping re-activation");
            return;
        }

        try
        {
            // Set GC to sustained low-latency mode to reduce GC pause times
            // This avoids full GC pauses during market hours
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
            _logger.Information("GC latency mode set to SustainedLowLatency");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to set GC latency mode");
        }

        try
        {
            // Disable Nagle's algorithm at the HTTP layer to reduce latency
            // Nagle combines small packets which adds latency; colocation should send immediately
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.Expect100Continue = false;
            _logger.Information("HTTP/TCP Nagle algorithm disabled");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to disable Nagle algorithm");
        }

        try
        {
            // Attempt to pin current thread to core 0
            // In colocation data centers, keeping market data threads on one core reduces cache misses
            if (ThreadingUtilities.TrySetThreadAffinity(0))
            {
                _logger.Information("Current thread pinned to CPU core 0");
            }
            else
            {
                _logger.Debug("Could not pin current thread to CPU core 0 (may require elevated privileges)");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to pin thread to core 0");
        }

        _isActive = true;
        _logger.Information("CoLocation profile activated");
    }
}
