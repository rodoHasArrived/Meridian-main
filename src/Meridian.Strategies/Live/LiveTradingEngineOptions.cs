namespace Meridian.Strategies.Live;

/// <summary>Host-tunable options for the live trading engine.</summary>
public sealed class LiveTradingEngineOptions
{
    public const string SectionKey = "Execution:LiveTradingEngine";

    /// <summary>
    /// Master switch for the engine. When disabled, promoted runs are recorded but never
    /// activated (the pre-engine behaviour).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Safety switch for real-money runs. Paper runs always may activate; live runs
    /// additionally require this flag and <c>BrokerageConfiguration.LiveExecutionEnabled</c>.
    /// Default false keeps the platform paper-first.
    /// </summary>
    public bool AllowLiveRuns { get; set; }

    /// <summary>
    /// Fallback trading universe for runs whose parameter set does not carry a
    /// <c>symbols</c>/<c>symbol</c> entry.
    /// </summary>
    public string[] DefaultSymbols { get; set; } = [];

    /// <summary>Capacity of each session's pending fill-report queue.</summary>
    public int FillReportQueueCapacity { get; set; } = 1024;
}
