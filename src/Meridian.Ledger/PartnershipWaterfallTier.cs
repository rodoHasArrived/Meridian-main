namespace Meridian.Ledger;

/// <summary>
/// Ordered partnership waterfall tier with optional dollar cap and investor split rules.
/// </summary>
public sealed record PartnershipWaterfallTier
{
    public PartnershipWaterfallTier(
        string tierId,
        string description,
        IReadOnlyList<PartnershipWaterfallAllocationRule> allocationRules,
        decimal? capAmount = null)
    {
        if (string.IsNullOrWhiteSpace(tierId))
            throw new ArgumentException("Waterfall tier identifier must not be null or whitespace.", nameof(tierId));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Waterfall tier description must not be null or whitespace.", nameof(description));
        ArgumentNullException.ThrowIfNull(allocationRules);
        if (allocationRules.Count == 0)
            throw new ArgumentException("At least one waterfall allocation rule is required.", nameof(allocationRules));
        if (capAmount is <= 0m)
            throw new ArgumentOutOfRangeException(nameof(capAmount), capAmount, "Waterfall tier cap must be positive when supplied.");

        TierId = tierId.Trim();
        Description = description.Trim();
        AllocationRules = allocationRules;
        CapAmount = capAmount;
    }

    public string TierId { get; }

    public string Description { get; }

    public IReadOnlyList<PartnershipWaterfallAllocationRule> AllocationRules { get; }

    public decimal? CapAmount { get; }
}
