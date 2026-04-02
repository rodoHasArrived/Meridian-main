namespace Meridian.Execution.TaxLotAccounting;

/// <summary>
/// The cost-basis identification method used when relieving (closing) tax lots.
/// The chosen method affects the timing and magnitude of realized gains or losses,
/// which has material tax implications.
/// </summary>
public enum TaxLotAccountingMethod
{
    /// <summary>
    /// First In, First Out — the oldest lots are relieved first.
    /// Most common default for equity positions.
    /// </summary>
    Fifo,

    /// <summary>
    /// Last In, First Out — the most recently opened lots are relieved first.
    /// May produce lower short-term gains in a rising market.
    /// </summary>
    Lifo,

    /// <summary>
    /// Highest In, First Out — lots with the highest cost basis are relieved first.
    /// Minimizes current-period realized gains (maximizes tax deferral in rising markets).
    /// </summary>
    Hifo,

    /// <summary>
    /// Specific lot identification — the caller designates exact lot IDs to relieve.
    /// Offers the most control but requires explicit lot selection at trade time.
    /// </summary>
    SpecificId,
}
