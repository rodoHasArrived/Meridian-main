namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Controls which open lots are closed first when a sell or cover-short order is executed.
/// </summary>
public enum LotSelectionMethod
{
    /// <summary>First-in, first-out: the oldest lot is closed first. Default behavior.</summary>
    Fifo,

    /// <summary>Last-in, first-out: the most recently opened lot is closed first.</summary>
    Lifo,

    /// <summary>
    /// Highest-cost-in, first-out: the lot with the highest entry price is closed first.
    /// Useful for minimizing taxable gains.
    /// </summary>
    Hifo,

    /// <summary>
    /// Specific identification: the caller designates which lot to close by supplying
    /// <see cref="FillEvent.TargetLotId"/> on the fill event.
    /// Falls back to FIFO when <see cref="FillEvent.TargetLotId"/> is not set or not found.
    /// </summary>
    SpecificId,
}
