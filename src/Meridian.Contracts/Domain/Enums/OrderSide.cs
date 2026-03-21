namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Represents the side (direction) of an order.
/// </summary>
public enum OrderSide : byte
{
    /// <summary>
    /// The order side could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A buy order (bid side).
    /// </summary>
    Buy = 1,

    /// <summary>
    /// A sell order (ask side).
    /// </summary>
    Sell = 2
}
