namespace Meridian.Execution.Allocation;

/// <summary>
/// Distributes block-trade quantities across multiple destination sleeves, entities,
/// or vehicles according to an <see cref="AllocationRule"/>.
/// </summary>
public interface IAllocationEngine
{
    /// <summary>
    /// Allocates <paramref name="totalQuantity"/> shares/contracts of <paramref name="symbol"/>
    /// executed at <paramref name="fillPrice"/> across the slices defined in
    /// <paramref name="rule"/>.
    /// </summary>
    /// <param name="symbol">Instrument symbol of the block trade.</param>
    /// <param name="totalQuantity">
    ///     Unsigned quantity of the block (always positive; direction is implied by context).
    /// </param>
    /// <param name="fillPrice">Execution price of the block trade.</param>
    /// <param name="rule">The allocation rule that defines slice weights.</param>
    /// <param name="allocatedAt">Timestamp of the block fill.</param>
    /// <returns>
    ///     An <see cref="AllocationResult"/> containing the per-slice distribution.
    /// </returns>
    AllocationResult Allocate(
        string symbol,
        long totalQuantity,
        decimal fillPrice,
        AllocationRule rule,
        DateTimeOffset allocatedAt);
}
