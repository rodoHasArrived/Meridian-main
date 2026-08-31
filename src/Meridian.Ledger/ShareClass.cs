namespace Meridian.Ledger;

/// <summary>How a share class equalizes performance fees across investors who subscribe at
/// different times relative to the class high-water mark.</summary>
public enum EqualizationMethod
{
    /// <summary>No equalization; every unit bears the same NAV with no per-subscription adjustment.</summary>
    None = 0,

    /// <summary>Single-NAV equalisation via equalisation credits and contingent redemptions.</summary>
    Equalisation = 1,
}

/// <summary>Whether a unit transaction issues (subscription) or cancels (redemption) units.</summary>
public enum UnitTransactionType
{
    Subscription = 0,
    Redemption = 1,
}

/// <summary>
/// A fund share/unit class with the terms needed to unitize NAV: base currency, fee rates, the
/// inception NAV per unit, and the equalization method.
/// </summary>
public sealed record ShareClass
{
    public ShareClass(
        string shareClassId,
        string fundProfileId,
        string name,
        string currency,
        decimal managementFeeRateAnnual,
        decimal performanceFeeRate,
        decimal inceptionNavPerUnit,
        DateOnly inceptionDate,
        EqualizationMethod equalizationMethod = EqualizationMethod.None)
    {
        if (string.IsNullOrWhiteSpace(shareClassId))
            throw new ArgumentException("Share class identifier must not be null or whitespace.", nameof(shareClassId));
        if (string.IsNullOrWhiteSpace(fundProfileId))
            throw new ArgumentException("Fund profile identifier must not be null or whitespace.", nameof(fundProfileId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Share class name must not be null or whitespace.", nameof(name));
        if (managementFeeRateAnnual < 0m || managementFeeRateAnnual > 1m)
            throw new ArgumentOutOfRangeException(nameof(managementFeeRateAnnual), managementFeeRateAnnual, "Management fee rate must be between 0 and 1.");
        if (performanceFeeRate < 0m || performanceFeeRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(performanceFeeRate), performanceFeeRate, "Performance fee rate must be in [0, 1).");
        if (inceptionNavPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(inceptionNavPerUnit), inceptionNavPerUnit, "Inception NAV per unit must be positive.");

        ShareClassId = shareClassId.Trim();
        FundProfileId = fundProfileId.Trim();
        Name = name.Trim();
        Currency = InvestorCommitment.NormalizeCurrency(currency, nameof(currency));
        ManagementFeeRateAnnual = managementFeeRateAnnual;
        PerformanceFeeRate = performanceFeeRate;
        InceptionNavPerUnit = inceptionNavPerUnit;
        InceptionDate = inceptionDate;
        EqualizationMethod = equalizationMethod;
    }

    public string ShareClassId { get; }

    public string FundProfileId { get; }

    public string Name { get; }

    public string Currency { get; }

    public decimal ManagementFeeRateAnnual { get; }

    public decimal PerformanceFeeRate { get; }

    public decimal InceptionNavPerUnit { get; }

    public DateOnly InceptionDate { get; }

    public EqualizationMethod EqualizationMethod { get; }
}

/// <summary>
/// One dated subscription or redemption in a share class. Subscriptions are specified by cash
/// <see cref="Amount"/> at the dealing <see cref="NavPerUnit"/>; redemptions are specified by
/// <see cref="Units"/>.
/// </summary>
public sealed record UnitTransaction
{
    public UnitTransaction(
        string transactionId,
        string shareClassId,
        string investorId,
        DateOnly dealingDate,
        UnitTransactionType type,
        decimal navPerUnit,
        decimal amount = 0m,
        decimal units = 0m)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new ArgumentException("Transaction identifier must not be null or whitespace.", nameof(transactionId));
        if (string.IsNullOrWhiteSpace(shareClassId))
            throw new ArgumentException("Share class identifier must not be null or whitespace.", nameof(shareClassId));
        if (string.IsNullOrWhiteSpace(investorId))
            throw new ArgumentException("Investor identifier must not be null or whitespace.", nameof(investorId));
        if (navPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(navPerUnit), navPerUnit, "Dealing NAV per unit must be positive.");
        if (type == UnitTransactionType.Subscription && amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Subscription amount must be positive.");
        if (type == UnitTransactionType.Redemption && units <= 0m)
            throw new ArgumentOutOfRangeException(nameof(units), units, "Redemption units must be positive.");

        TransactionId = transactionId.Trim();
        ShareClassId = shareClassId.Trim();
        InvestorId = investorId.Trim();
        DealingDate = dealingDate;
        Type = type;
        NavPerUnit = navPerUnit;
        Amount = amount;
        Units = units;
    }

    public string TransactionId { get; }

    public string ShareClassId { get; }

    public string InvestorId { get; }

    public DateOnly DealingDate { get; }

    public UnitTransactionType Type { get; }

    public decimal NavPerUnit { get; }

    public decimal Amount { get; }

    public decimal Units { get; }
}
