namespace Meridian.Execution.Allocation;

/// <summary>
/// Defines how a block trade (or any pooled fill) should be distributed across
/// multiple sleeves, entities, or vehicles within a <c>FundLedgerBook</c>.
/// </summary>
/// <param name="RuleId">Unique identifier for this rule.</param>
/// <param name="Name">Human-readable description of the rule.</param>
/// <param name="SliceWeights">
///     Mapping of destination ID (sleeve ID, entity ID, etc.) to its relative weight.
///     Weights need not sum to 1.0 — they are normalized internally.
/// </param>
public sealed record AllocationRule(
    Guid RuleId,
    string Name,
    IReadOnlyDictionary<string, decimal> SliceWeights)
{
    /// <summary>Creates a new rule with an auto-generated ID.</summary>
    public static AllocationRule Create(string name, IReadOnlyDictionary<string, decimal> weights)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
            throw new ArgumentException("At least one slice weight is required.", nameof(weights));
        if (weights.Values.Any(w => w < 0m))
            throw new ArgumentException("All slice weights must be non-negative.", nameof(weights));
        if (weights.Values.Sum() == 0m)
            throw new ArgumentException("Slice weights must not all be zero.", nameof(weights));

        return new AllocationRule(Guid.NewGuid(), name, weights);
    }

    /// <summary>
    /// Creates an equal-weight rule that distributes evenly across all
    /// <paramref name="destinationIds"/>.
    /// </summary>
    public static AllocationRule EqualWeight(string name, IEnumerable<string> destinationIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var ids = destinationIds?.ToList()
            ?? throw new ArgumentNullException(nameof(destinationIds));
        if (ids.Count == 0)
            throw new ArgumentException("At least one destination is required.", nameof(destinationIds));

        var weights = ids.ToDictionary(id => id, _ => 1m);
        return Create(name, weights);
    }

    /// <summary>
    /// Returns the normalized weights (each weight divided by the total sum).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> NormalizedWeights()
    {
        var total = SliceWeights.Values.Sum();
        return SliceWeights.ToDictionary(
            kvp => kvp.Key,
            kvp => total == 0m ? 0m : kvp.Value / total);
    }
}
