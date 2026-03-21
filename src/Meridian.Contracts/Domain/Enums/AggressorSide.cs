namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Indicates which side of the market initiated the trade.
/// </summary>
public enum AggressorSide : byte
{
    /// <summary>
    /// The aggressor side could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The buyer initiated the trade (lifted the offer).
    /// </summary>
    Buy = 1,

    /// <summary>
    /// The seller initiated the trade (hit the bid).
    /// </summary>
    Sell = 2
}
