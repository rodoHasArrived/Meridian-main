namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Indicates whether an option contract is a call or a put.
/// </summary>
public enum OptionRight : byte
{
    /// <summary>
    /// Call option — the right to buy the underlying at the strike price.
    /// </summary>
    Call = 0,

    /// <summary>
    /// Put option — the right to sell the underlying at the strike price.
    /// </summary>
    Put = 1
}
