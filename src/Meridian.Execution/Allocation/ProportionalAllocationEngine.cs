namespace Meridian.Execution.Allocation;

/// <summary>
/// Proportional allocation engine that distributes a block trade across destination
/// slices in proportion to their normalized weights from an <see cref="AllocationRule"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine uses a largest-remainder algorithm to ensure that integer lot quantities
/// always sum to the total block size, even after rounding.
/// </para>
/// <para>
/// Any rounding residual (at most +1 or −1 share) is assigned to the slice with the
/// largest fractional remainder.
/// </para>
/// </remarks>
public sealed class ProportionalAllocationEngine : IAllocationEngine
{
    /// <inheritdoc/>
    public AllocationResult Allocate(
        string symbol,
        long totalQuantity,
        decimal fillPrice,
        AllocationRule rule,
        DateTimeOffset allocatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(rule);
        if (totalQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalQuantity), "Must be positive.");

        var normalized = rule.NormalizedWeights();
        var slices = ComputeSlices(totalQuantity, fillPrice, normalized);

        return new AllocationResult(
            RuleId: rule.RuleId,
            Symbol: symbol,
            TotalQuantity: totalQuantity,
            FillPrice: fillPrice,
            Slices: slices,
            AllocatedAt: allocatedAt);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<AllocationSlice> ComputeSlices(
        long totalQuantity,
        decimal fillPrice,
        IReadOnlyDictionary<string, decimal> weights)
    {
        // Step 1: compute raw (fractional) quantities
        var rawQuantities = weights
            .Select(kvp => (kvp.Key, Weight: kvp.Value, Raw: (double)(totalQuantity * kvp.Value)))
            .ToList();

        // Step 2: floor each quantity
        var floors = rawQuantities
            .Select(x => (x.Key, x.Weight, Floor: (long)Math.Floor(x.Raw), Frac: x.Raw - Math.Floor(x.Raw)))
            .ToList();

        var allocated = floors.Sum(x => x.Floor);
        var remaining = totalQuantity - allocated;

        // Step 3: distribute residuals by largest remainder
        var sorted = floors
            .OrderByDescending(x => x.Frac)
            .ToList();

        var allocations = new Dictionary<string, long>(floors.Count);
        foreach (var (key, _, floor, _) in floors)
            allocations[key] = floor;

        for (var i = 0; i < remaining; i++)
            allocations[sorted[i].Key]++;

        // Step 4: build result slices
        return weights.Keys
            .Select(id => new AllocationSlice(
                DestinationId: id,
                AllocatedQuantity: allocations[id],
                AllocatedWeight: weights[id],
                ProRataValue: allocations[id] * fillPrice))
            .ToList();
    }
}
