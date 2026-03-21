namespace Meridian.Strategies.Models;

/// <summary>The lifecycle state of a registered strategy.</summary>
public enum StrategyStatus
{
    /// <summary>Strategy is registered but not yet started.</summary>
    Registered,

    /// <summary>Strategy is initialising or loading historical data for warm-up.</summary>
    WarmingUp,

    /// <summary>Strategy is running and actively processing market events.</summary>
    Running,

    /// <summary>Strategy has been paused; it is not processing events but retains its state.</summary>
    Paused,

    /// <summary>Strategy has been cleanly stopped and its state has been finalised.</summary>
    Stopped,

    /// <summary>Strategy encountered a fatal error and is no longer running.</summary>
    Faulted
}
