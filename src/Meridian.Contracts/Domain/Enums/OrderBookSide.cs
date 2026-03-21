namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Represents which side of the order book a level belongs to.
/// </summary>
public enum OrderBookSide : byte
{
    /// <summary>
    /// Bid side (buy orders).
    /// </summary>
    Bid = 0,

    /// <summary>
    /// Ask side (sell orders).
    /// </summary>
    Ask = 1
}
