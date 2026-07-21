namespace Meridian.Ledger;

/// <summary>
/// A portfolio-scoped pricing rule that selects the price source, valuation method, and ASC 820
/// fair-value level to apply to a class of instruments in one portfolio, effective over a date window.
/// This is the per-portfolio complement to the fund-scoped <see cref="DailyPortfolioPricingPolicy"/>:
/// the policy states the fund's default valuation posture, while an ordered set of these rules lets
/// administrators override the source/method for specific instrument types within a portfolio.
/// </summary>
public sealed record PortfolioPricingRule
{
    public PortfolioPricingRule(
        string ruleId,
        string portfolioId,
        string priceSource,
        string valuationMethod,
        string approvedBy,
        DateTimeOffset approvedAtUtc,
        int priority = 100,
        string? instrumentType = null,
        FairValueLevel fairValueLevel = FairValueLevel.Unclassified,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            throw new ArgumentException("Pricing rule identifier must not be null or whitespace.", nameof(ruleId));
        if (string.IsNullOrWhiteSpace(portfolioId))
            throw new ArgumentException("Pricing rule portfolio identifier must not be null or whitespace.", nameof(portfolioId));
        if (string.IsNullOrWhiteSpace(priceSource))
            throw new ArgumentException("Pricing rule price source must not be null or whitespace.", nameof(priceSource));
        if (string.IsNullOrWhiteSpace(valuationMethod))
            throw new ArgumentException("Pricing rule valuation method must not be null or whitespace.", nameof(valuationMethod));
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("Pricing rule approver must not be null or whitespace.", nameof(approvedBy));
        if (effectiveTo is { } to && effectiveFrom is { } from && to < from)
            throw new ArgumentException("Pricing rule end date must not precede its start date.", nameof(effectiveTo));

        RuleId = ruleId.Trim();
        PortfolioId = portfolioId.Trim();
        PriceSource = priceSource.Trim();
        ValuationMethod = valuationMethod.Trim();
        ApprovedBy = approvedBy.Trim();
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
        Priority = priority;
        InstrumentType = string.IsNullOrWhiteSpace(instrumentType) ? null : instrumentType.Trim();
        FairValueLevel = fairValueLevel;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public string RuleId { get; }

    public string PortfolioId { get; }

    public string PriceSource { get; }

    public string ValuationMethod { get; }

    public string ApprovedBy { get; }

    public DateTimeOffset ApprovedAtUtc { get; }

    /// <summary>Evaluation precedence; lower values are resolved before higher ones.</summary>
    public int Priority { get; }

    /// <summary>Instrument type this rule applies to, or <see langword="null"/> to match any instrument.</summary>
    public string? InstrumentType { get; }

    public FairValueLevel FairValueLevel { get; }

    public DateOnly? EffectiveFrom { get; }

    public DateOnly? EffectiveTo { get; }

    public string? Description { get; }

    /// <summary>
    /// Returns <see langword="true"/> when this rule applies to the given instrument type on the given
    /// valuation date. A null <see cref="InstrumentType"/> matches any instrument; a null
    /// <paramref name="instrumentType"/> matches only instrument-agnostic rules. The caller is expected
    /// to pass an already-normalized value (see <see cref="PortfolioPricingRuleBook.Resolve"/>);
    /// comparison is case-insensitive.
    /// </summary>
    public bool Matches(string? instrumentType, DateOnly asOf)
    {
        if (EffectiveFrom is { } from && asOf < from)
            return false;
        if (EffectiveTo is { } to && asOf > to)
            return false;

        if (InstrumentType is null)
            return true;

        return instrumentType is not null
               && string.Equals(InstrumentType, instrumentType, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Mutable, thread-safe, ordered registry of <see cref="PortfolioPricingRule"/>s. Resolution picks the
/// highest-precedence rule (lowest <see cref="PortfolioPricingRule.Priority"/>, then most recently
/// approved) that matches a portfolio, instrument type, and valuation date.
/// </summary>
public sealed class PortfolioPricingRuleBook
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PortfolioPricingRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds or replaces a rule (keyed by <see cref="PortfolioPricingRule.RuleId"/>).</summary>
    public PortfolioPricingRule Add(PortfolioPricingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_gate)
        {
            _rules[rule.RuleId] = rule;
            return rule;
        }
    }

    /// <summary>Removes a rule by id and returns whether it existed.</summary>
    public bool Remove(string ruleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        lock (_gate)
        {
            return _rules.Remove(ruleId.Trim());
        }
    }

    /// <summary>All rules for a portfolio, ordered by precedence.</summary>
    public IReadOnlyList<PortfolioPricingRule> RulesFor(string portfolioId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portfolioId);
        var normalized = portfolioId.Trim();
        lock (_gate)
        {
            return _rules.Values
                .Where(rule => string.Equals(rule.PortfolioId, normalized, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static rule => rule.Priority)
                .ThenByDescending(static rule => rule.ApprovedAtUtc)
                .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// Resolves the effective pricing rule for a portfolio, instrument type, and valuation date, or
    /// <see langword="null"/> when no rule matches (the caller then falls back to the fund policy).
    /// </summary>
    public PortfolioPricingRule? Resolve(string portfolioId, string? instrumentType, DateOnly asOf)
    {
        // Normalize once here rather than per-rule inside the match loop.
        var normalizedInstrumentType = instrumentType?.Trim();
        return RulesFor(portfolioId).FirstOrDefault(rule => rule.Matches(normalizedInstrumentType, asOf));
    }

    /// <summary>All registered rules across all portfolios, ordered by portfolio then precedence.</summary>
    public IReadOnlyList<PortfolioPricingRule> Rules
    {
        get
        {
            lock (_gate)
            {
                return _rules.Values
                    .OrderBy(static rule => rule.PortfolioId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static rule => rule.Priority)
                    .ThenByDescending(static rule => rule.ApprovedAtUtc)
                    .ToArray();
            }
        }
    }
}
