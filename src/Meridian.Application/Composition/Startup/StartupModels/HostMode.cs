namespace Meridian.Application.Composition.Startup.StartupModels;

/// <summary>
/// Discriminates the selected execution mode resolved at process startup.
/// </summary>
public enum HostMode : byte
{
    /// <summary>
    /// A one-shot CLI command (help, validate-config, symbols, backfill, etc.) that runs once and exits.
    /// </summary>
    Command,

    /// <summary>
    /// Desktop host: embedded HTTP UI server plus the data collector running side-by-side.
    /// </summary>
    Desktop,

    /// <summary>
    /// Headless streaming data collector (console / server / container).
    /// </summary>
    Collector,

    /// <summary>
    /// Historical data backfill operation running in headless mode.
    /// </summary>
    Backfill,
}
