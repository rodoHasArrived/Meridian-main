namespace Meridian.Backtesting.Portfolio;

/// <summary>
/// The simulated account refused a fill before changing any portfolio state (short selling
/// or margin not permitted, or a fill canonical raw-price replay cannot represent). The
/// engine discards such a fill and continues the run. Any other failure while applying a
/// fill may leave the portfolio partly updated, so it is not caught and ends the run.
/// </summary>
public sealed class FillRejectedException : InvalidOperationException
{
    public FillRejectedException(string message)
        : base(message)
    {
    }
}
