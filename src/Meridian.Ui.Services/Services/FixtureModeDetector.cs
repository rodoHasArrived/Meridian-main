using System;

namespace Meridian.Ui.Services.Services;

public enum FixtureModeKind
{
    Live,
    Fixture,
    Offline
}

/// <summary>
/// Detects and tracks whether the application is running in fixture/offline mode.
/// Provides a centralized check so UI pages can distinguish intentional demo data
/// from backend-unreachable offline state.
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
    private volatile string? _serverDataProvenanceToken;

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
    /// Non-real data provenance reported by the connected backend (for example
    /// <c>seeded</c> when the host is serving a seeded demo workspace), or <c>null</c>
    /// when the backend serves real data or has not reported yet.
    /// </summary>
    public string? ServerDataProvenanceToken => _serverDataProvenanceToken;

    /// <summary>
    /// Gets whether any non-live mode is active (fixture, offline, or a backend that
    /// reports simulated/seeded/sample data).
    /// </summary>
    public bool IsNonLiveMode => _isFixtureMode || _isOfflineMode || _serverDataProvenanceToken is not null;

    /// <summary>
    /// Gets the normalized non-live mode kind for UI tone selection.
    /// </summary>
    public FixtureModeKind ModeKind => (_isFixtureMode, _isOfflineMode) switch
    {
        (true, _) => FixtureModeKind.Fixture,
        (_, true) => FixtureModeKind.Offline,
        _ => FixtureModeKind.Live
    };

    /// <summary>
    /// Gets a human-readable mode label for display in the UI banner.
    /// </summary>
    public string ModeLabel => (_isFixtureMode, _isOfflineMode) switch
    {
        (true, _) => "Demo data mode - sample workflow data; live services are not connected.",
        (_, true) => "Offline - local backend is unreachable; showing cached or fallback data.",
        _ when _serverDataProvenanceToken is not null =>
            $"{_serverDataProvenanceToken.ToUpperInvariant()} data - the connected workspace is serving simulated/seeded records, not live provider-backed data.",
        _ => string.Empty
    };

    /// <summary>
    /// Gets the banner background color suggestion.
    /// Fixture mode: blue informational, Offline: red.
    /// </summary>
    public string BannerColor => _isFixtureMode ? "#2563EB" : "#F44336";

    /// <summary>
    /// Sets explicit fixture mode state.
    /// </summary>
    public void SetFixtureMode(bool enabled)
    {
        _isFixtureMode = enabled;
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records the data provenance the connected backend reports for the records it
    /// serves. Pass a non-real provenance token (<c>simulated</c>, <c>seeded</c>,
    /// <c>sample</c>) to surface the persistent shell banner, or <c>null</c> when the
    /// backend serves real data. W9-TRUTH-001: a workstation attached to a seeded demo
    /// workspace must keep labeling it.
    /// </summary>
    public void ReportServerDataProvenance(string? nonRealProvenanceToken)
    {
        var normalized = string.IsNullOrWhiteSpace(nonRealProvenanceToken)
            ? null
            : nonRealProvenanceToken.Trim().ToLowerInvariant();
        if (string.Equals(normalized, "real", StringComparison.Ordinal))
        {
            normalized = null;
        }

        if (!string.Equals(_serverDataProvenanceToken, normalized, StringComparison.Ordinal))
        {
            _serverDataProvenanceToken = normalized;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
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
    /// Gets the currently active fixture scenario label for display in the UI.
    /// </summary>
    public string ScenarioLabel => FixtureDataService.GetScenarioLabel(FixtureDataService.Instance.ActiveScenario);

    /// <summary>
    /// Gets the currently active fixture scenario.
    /// </summary>
    public FixtureScenario ActiveScenario => FixtureDataService.Instance.ActiveScenario;

    /// <summary>
    /// Cycles to the next scenario in the <see cref="FixtureScenario"/> sequence.
    /// No-op when fixture mode is off.
    /// </summary>
    /// <returns>The scenario that is now active after the cycle.</returns>
    public FixtureScenario CycleScenario()
    {
        if (!_isFixtureMode)
            return FixtureDataService.Instance.ActiveScenario;

        var values = Enum.GetValues<FixtureScenario>();
        var current = FixtureDataService.Instance.ActiveScenario;
        var nextIndex = ((int)current + 1) % values.Length;
        var next = values[nextIndex];
        FixtureDataService.Instance.SetScenario(next);
        ModeChanged?.Invoke(this, EventArgs.Empty);
        return next;
    }

    /// <summary>
    /// Event raised when fixture/offline mode changes.
    /// UI pages should subscribe to this to update their visual state.
    /// </summary>
    public event EventHandler? ModeChanged;
}
