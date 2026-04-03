using System;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Detects and tracks whether the application is running in fixture/offline mode.
/// Provides a centralized check so all UI pages can show a hard visual distinction
/// when viewing sample data instead of live backend data.
/// </summary>
/// <remarks>
/// Fixture mode is activated by:
/// - Command-line flag: --fixture
/// - Environment variable: MDC_FIXTURE_MODE=1
/// - Backend unreachable (auto-detected, triggers offline mode banner)
/// </remarks>
public sealed class FixtureModeDetector
{
    private static readonly Lazy<FixtureModeDetector> _instance = new(() => new());
    public static FixtureModeDetector Instance => _instance.Value;

    private volatile bool _isFixtureMode;
    private volatile bool _isOfflineMode;
    private volatile bool _backendReachable = true;

    private FixtureModeDetector()
    {
        // Check environment variable and command-line flags at startup
        var envVar = Environment.GetEnvironmentVariable("MDC_FIXTURE_MODE");
        _isFixtureMode = string.Equals(envVar, "1", StringComparison.Ordinal)
            || string.Equals(envVar, "true", StringComparison.OrdinalIgnoreCase);

        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--fixture", StringComparison.OrdinalIgnoreCase))
            {
                _isFixtureMode = true;
                break;
            }
        }
    }

    /// <summary>
    /// Gets whether the application is explicitly running in fixture mode.
    /// </summary>
    public bool IsFixtureMode => _isFixtureMode;

    /// <summary>
    /// Gets whether the application is in offline mode (backend unreachable).
    /// </summary>
    public bool IsOfflineMode => _isOfflineMode;

    /// <summary>
    /// Gets whether the backend is currently reachable.
    /// </summary>
    public bool IsBackendReachable => _backendReachable;

    /// <summary>
    /// Gets whether any non-live mode is active (fixture or offline).
    /// </summary>
    public bool IsNonLiveMode => _isFixtureMode || _isOfflineMode;

    /// <summary>
    /// Gets a human-readable mode label for display in the UI banner.
    /// </summary>
    public string ModeLabel => (_isFixtureMode, _isOfflineMode) switch
    {
        (true, _) => "FIXTURE MODE — Showing sample data, not connected to live backend",
        (_, true) => "OFFLINE — Backend unreachable, displaying cached/stale data",
        _ => string.Empty
    };

    /// <summary>
    /// Gets the currently active fixture scenario.
    /// Only meaningful when <see cref="IsFixtureMode"/> is true.
    /// </summary>
    public FixtureScenario ActiveScenario => FixtureDataService.Instance.ActiveScenario;

    /// <summary>
    /// Gets a short human-readable label for the active fixture scenario,
    /// suitable for display in the fixture banner (e.g. "Connected (healthy)").
    /// </summary>
    public string ScenarioLabel => FixtureDataService.GetScenarioLabel(FixtureDataService.Instance.ActiveScenario);

    /// <summary>
    /// Advances the fixture data to the next scenario in the cycle and raises
    /// <see cref="ModeChanged"/> so the UI banner refreshes.
    /// Has no effect when <see cref="IsFixtureMode"/> is false.
    /// </summary>
    /// <returns>The new active scenario after cycling.</returns>
    public FixtureScenario CycleScenario()
    {
        if (!_isFixtureMode)
        {
            return FixtureDataService.Instance.ActiveScenario;
        }

        var next = FixtureDataService.Instance.CycleToNextScenario();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        return next;
    }

    /// <summary>
    /// Gets the banner background color suggestion.
    /// Fixture mode: amber/orange, Offline: red.
    /// </summary>
    public string BannerColor => _isFixtureMode ? "#FFB300" : "#F44336";

    /// <summary>
    /// Sets explicit fixture mode state.
    /// </summary>
    public void SetFixtureMode(bool enabled)
    {
        _isFixtureMode = enabled;
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates backend reachability state.
    /// Automatically triggers offline mode when backend is unreachable.
    /// </summary>
    public void UpdateBackendReachability(bool isReachable)
    {
        var wasOffline = _isOfflineMode;
        _backendReachable = isReachable;
        _isOfflineMode = !isReachable;

        if (wasOffline != _isOfflineMode)
        {
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Event raised when fixture/offline mode changes.
    /// UI pages should subscribe to this to update their visual state.
    /// </summary>
    public event EventHandler? ModeChanged;
}
