namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Represents the current state of the market.
/// </summary>
public enum MarketState : byte
{
    /// <summary>
    /// Market is in normal trading hours.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Market is closed.
    /// </summary>
    Closed = 1,

    /// <summary>
    /// Trading is halted for this security.
    /// </summary>
    Halted = 2,

    /// <summary>
    /// Market state is unknown.
    /// </summary>
    Unknown = 3,

    /// <summary>
    /// Pre-market trading session.
    /// </summary>
    PreMarket = 4,

    /// <summary>
    /// After-hours trading session.
    /// </summary>
    AfterHours = 5,

    /// <summary>
    /// Market is in auction phase.
    /// </summary>
    Auction = 6
}
