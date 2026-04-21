namespace Meridian.Execution.Allocation;

/// <summary>
/// The allocation assigned to a single destination slice (sleeve, entity, or vehicle)
/// after a block trade has been distributed.
/// </summary>
/// <param name="DestinationId">
///     Identifier of the destination (e.g., sleeve ID, entity ID).
/// </param>
/// <param name="AllocatedQuantity">Number of shares/contracts allocated to this slice.</param>
/// <param name="AllocatedWeight">Normalized weight used for this slice (0–1).</param>
/// <param name="ProRataValue">
///     Dollar value of the allocation (allocated quantity × fill price).
/// </param>
public sealed record AllocationSlice(
    string DestinationId,
    long AllocatedQuantity,
    decimal AllocatedWeight,
    decimal ProRataValue);

/// <summary>
/// The full result of distributing a block trade across destinations using an
/// <see cref="AllocationRule"/>.
/// </summary>
/// <param name="RuleId">The rule that produced this allocation.</param>
/// <param name="Symbol">Instrument symbol.</param>
/// <param name="TotalQuantity">Total quantity of the block trade being allocated.</param>
/// <param name="FillPrice">Execution price of the block fill.</param>
/// <param name="Slices">Per-destination allocation slices.</param>
/// <param name="AllocatedAt">Timestamp of the allocation calculation.</param>
public sealed record AllocationResult(
    Guid RuleId,
    string Symbol,
    long TotalQuantity,
    decimal FillPrice,
    IReadOnlyList<AllocationSlice> Slices,
    DateTimeOffset AllocatedAt)
{
    /// <summary>
    /// Validates that all slice quantities sum to the block total.
    /// Rounding residuals of up to ±1 share are acceptable.
    /// </summary>
    public bool IsBalanced =>
        Math.Abs(Slices.Sum(s => s.AllocatedQuantity) - TotalQuantity) <= 1;

    /// <summary>Total gross value of the block trade.</summary>
    public decimal TotalGrossValue => TotalQuantity * FillPrice;
}
