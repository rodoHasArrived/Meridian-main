namespace Meridian.Ledger;

/// <summary>
/// Input for daily portfolio valuation and fair-value adjustment projection.
/// </summary>
public sealed record DailyPortfolioPricingInput
{
    public DailyPortfolioPricingInput(
        DailyPortfolioPricingPolicy policy,
        string periodId,
        DateTimeOffset asOf,
        string baseCurrency,
        IReadOnlyList<DailyPortfolioPriceMark> marks)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(marks);
        if (string.IsNullOrWhiteSpace(periodId))
            throw new ArgumentException("Period identifier must not be null or whitespace.", nameof(periodId));
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("Base currency must not be null or whitespace.", nameof(baseCurrency));
        if (marks.Count == 0)
            throw new ArgumentException("At least one daily portfolio price mark is required.", nameof(marks));

        Policy = policy;
        PeriodId = periodId.Trim();
        AsOf = asOf;
        BaseCurrency = baseCurrency.Trim().ToUpperInvariant();
        Marks = marks;
    }

    public DailyPortfolioPricingPolicy Policy { get; }

    public string PeriodId { get; }

    public DateTimeOffset AsOf { get; }

    public string BaseCurrency { get; }

    public IReadOnlyList<DailyPortfolioPriceMark> Marks { get; }
}
