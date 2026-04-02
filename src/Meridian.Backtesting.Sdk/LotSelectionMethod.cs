namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Controls which open lots are selected first when a sell or cover order is processed.
/// The chosen method affects both realised P&amp;L timing and tax treatment.
/// </summary>
public enum LotSelectionMethod
{
    /// <summary>
    /// First In, First Out — oldest lots are closed first.
    /// This is the default and matches legacy behaviour.
    /// </summary>
    Fifo,

    /// <summary>
    /// Last In, First Out — most recently opened lots are closed first.
    /// Can defer gains by closing lower-cost lots later.
    /// </summary>
    Lifo,

    /// <summary>
    /// Highest Cost In, First Out — the lot with the highest entry price is
    /// closed first, minimising short-term capital gains (tax-loss harvesting).
    /// </summary>
    Hifo,

    /// <summary>
    /// Specific Identification — the caller supplies a <see cref="OpenLot.LotId"/>
    /// via <see cref="FillEvent.TargetLotId"/> to nominate the exact lot to close.
    /// Falls back to <see cref="Fifo"/> when no matching lot is found.
    /// </summary>
    SpecificId,
}
