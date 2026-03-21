namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Type of operation for a depth update.
/// </summary>
public enum DepthOperation : byte
{
    /// <summary>
    /// Insert a new level.
    /// </summary>
    Insert = 0,

    /// <summary>
    /// Update an existing level.
    /// </summary>
    Update = 1,

    /// <summary>
    /// Delete an existing level.
    /// </summary>
    Delete = 2
}
