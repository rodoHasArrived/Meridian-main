namespace Meridian.Ledger;

/// <summary>
/// How a daily valuation run treats a mark whose price is older than the policy allows.
/// </summary>
public enum StalePriceHandling
{
    /// <summary>Include the mark unchanged; staleness is tolerated without annotation.</summary>
    Allow,

    /// <summary>Include the mark but flag it as stale so downstream reviewers can see the exception.</summary>
    Flag,

    /// <summary>Exclude the mark from the fair-value draft; the position is surfaced for operator attention.</summary>
    Block,
}

/// <summary>
/// Freshness policy for daily marks. When enabled, a price whose observation date is more than
/// <see cref="MaxAgeDays"/> days before the valuation date is considered stale and treated per
/// <see cref="Handling"/>. Disabled by default so existing valuation runs are unaffected.
/// </summary>
/// <param name="Enabled">When false, no staleness assessment is performed and every mark is fresh.</param>
/// <param name="MaxAgeDays">Maximum permitted age, in days, of a price relative to the valuation date.</param>
/// <param name="Handling">Action taken when a mark is stale.</param>
public sealed record StalePricePolicy(bool Enabled, int MaxAgeDays, StalePriceHandling Handling)
{
    /// <summary>No staleness enforcement; every mark is treated as fresh.</summary>
    public static StalePricePolicy Disabled { get; } = new(false, 0, StalePriceHandling.Allow);

    /// <summary>Convenience factory for an enabled policy.</summary>
    public static StalePricePolicy Of(int maxAgeDays, StalePriceHandling handling) =>
        new(true, maxAgeDays, handling);

    /// <summary>Validates the age bound.</summary>
    public StalePricePolicy EnsureValid()
    {
        if (Enabled && MaxAgeDays < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxAgeDays), MaxAgeDays, "Maximum price age days cannot be negative.");
        return this;
    }

    /// <summary>
    /// Assesses a price observed on <paramref name="priceAsOf"/> against a <paramref name="valuationDate"/>.
    /// A future observation always blocks because it was not observable at valuation time.
    /// </summary>
    public StalePriceAssessment Assess(DateOnly priceAsOf, DateOnly valuationDate)
    {
        var ageDays = valuationDate.DayNumber - priceAsOf.DayNumber;
        if (ageDays < 0)
            return new StalePriceAssessment(true, ageDays, StalePriceHandling.Block);
        if (!Enabled)
            return StalePriceAssessment.Fresh;
        if (ageDays <= MaxAgeDays)
            return new StalePriceAssessment(false, Math.Max(0, ageDays), StalePriceHandling.Allow);

        return new StalePriceAssessment(true, ageDays, Handling);
    }
}

/// <summary>Result of assessing a mark's freshness against a <see cref="StalePricePolicy"/>.</summary>
/// <param name="IsStale">True when the price exceeded the permitted age.</param>
/// <param name="AgeDays">Signed age of the price in days; negative means a future observation.</param>
/// <param name="Handling">The action to take: <see cref="StalePriceHandling.Allow"/> when fresh.</param>
public sealed record StalePriceAssessment(bool IsStale, int AgeDays, StalePriceHandling Handling)
{
    /// <summary>A fresh (non-stale) assessment.</summary>
    public static StalePriceAssessment Fresh { get; } = new(false, 0, StalePriceHandling.Allow);
}
