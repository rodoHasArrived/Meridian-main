namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Kind of depth integrity issue detected.
/// </summary>
public enum DepthIntegrityKind : byte
{
    /// <summary>
    /// No integrity issue detected.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// Unknown integrity issue.
    /// </summary>
    Unknown = 1,

    /// <summary>
    /// Sequence gap detected.
    /// </summary>
    Gap = 2,

    /// <summary>
    /// Out-of-order message received.
    /// </summary>
    OutOfOrder = 3,

    /// <summary>
    /// Invalid position in order book.
    /// </summary>
    InvalidPosition = 4,

    /// <summary>
    /// Stale data detected.
    /// </summary>
    Stale = 5
}
