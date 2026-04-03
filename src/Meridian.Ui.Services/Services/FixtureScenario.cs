namespace Meridian.Ui.Services.Services;

/// <summary>
/// Named fixture scenarios for UI development and debugging.
/// Each scenario corresponds to a distinct system state that can be activated
/// at runtime without restarting the application, enabling rapid iteration
/// over all visual states in a single session.
/// </summary>
public enum FixtureScenario
{
    /// <summary>
    /// System fully connected with healthy metrics — the happy-path state.
    /// Providers are active, throughput is nominal, no errors.
    /// </summary>
    Connected,

    /// <summary>
    /// System disconnected from all providers.
    /// Useful for testing empty-state / reconnection UI paths.
    /// </summary>
    Disconnected,

    /// <summary>
    /// System partially connected with degraded metrics.
    /// Some providers are unavailable or rate-limited; throughput is reduced.
    /// </summary>
    Degraded,

    /// <summary>
    /// System in error state — provider errors, high drop rate, low throughput.
    /// Useful for testing error banners, alerts, and recovery UI flows.
    /// </summary>
    Error,

    /// <summary>
    /// System is initializing or loading — connections pending, data not yet available.
    /// Useful for testing skeleton/loading states and progress indicators.
    /// </summary>
    Loading,
}
