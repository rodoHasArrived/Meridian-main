namespace Meridian.Execution.Models;

/// <summary>Lifecycle states an order can be in.</summary>
public enum OrderStatus
{
    /// <summary>Order has been accepted by the gateway and is pending routing.</summary>
    Accepted,

    /// <summary>Order has been sent to the venue and is resting in the book.</summary>
    Working,

    /// <summary>Order has been completely filled.</summary>
    Filled,

    /// <summary>Order has been partially filled; remainder is still working.</summary>
    PartiallyFilled,

    /// <summary>Order was cancelled before being filled.</summary>
    Cancelled,

    /// <summary>Order was rejected by the broker or risk engine.</summary>
    Rejected
}
