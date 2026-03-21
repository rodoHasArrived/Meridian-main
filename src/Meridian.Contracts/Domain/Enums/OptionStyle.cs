namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Exercise style of an option contract.
/// </summary>
public enum OptionStyle : byte
{
    /// <summary>
    /// American-style option — can be exercised at any time before expiration.
    /// Most US equity options are American-style.
    /// </summary>
    American = 0,

    /// <summary>
    /// European-style option — can only be exercised at expiration.
    /// Most index options (SPX, NDX, RUT) are European-style.
    /// </summary>
    European = 1
}
