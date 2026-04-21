namespace Meridian.Backtesting.Sdk;

/// <summary>Progress notification emitted by <c>BacktestEngine</c> during replay.</summary>
public sealed record BacktestProgressEvent(
    double ProgressFraction,        // 0.0 – 1.0
    DateOnly CurrentDate,
    decimal PortfolioValue,
    long EventsProcessed,
    string? Message = null,
    /// <summary>
    /// Rolling performance metrics available after at least 60 trading days have elapsed.
    /// <c>null</c> before that threshold or on the final completion event.
    /// </summary>
    IntermediateMetrics? LiveMetrics = null);
