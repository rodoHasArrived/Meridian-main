namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical open lot for par-denominated (face-value) instruments. This aggregate makes explicit
/// the facts that were previously implicit caller conventions: the quote basis the acquisition
/// price is expressed in (<see cref="ParBasis"/> — no more silent price-per-100 assumption), the
/// pool factor the recorded face was booked at (<see cref="BookedFactor"/>), and the Security
/// Master identity the lot amortizes against. All derived economics (cost basis, premium/discount,
/// factor-restated face, day-count amortized basis) are owned here so every consumer computes them
/// identically.
/// </summary>
public sealed record FaceValueLot
{
    public FaceValueLot(
        string lotId,
        Guid securityId,
        DateOnly acquiredDate,
        decimal originalFace,
        decimal pricePercentOfPar,
        decimal bookedFactor = 1m,
        decimal parBasis = 100m)
    {
        if (string.IsNullOrWhiteSpace(lotId))
            throw new ArgumentException("Face-value lot identifier must not be null or whitespace.", nameof(lotId));
        if (securityId == Guid.Empty)
            throw new ArgumentException("Face-value lots must be linked to a Security Master identity.", nameof(securityId));
        if (originalFace <= 0m)
            throw new ArgumentOutOfRangeException(nameof(originalFace), originalFace, "Original face must be positive.");
        if (pricePercentOfPar < 0m)
            throw new ArgumentOutOfRangeException(nameof(pricePercentOfPar), pricePercentOfPar, "Acquisition price cannot be negative.");
        if (bookedFactor is <= 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(bookedFactor), bookedFactor, "Booked factor must be in (0, 1].");
        if (parBasis <= 0m)
            throw new ArgumentOutOfRangeException(nameof(parBasis), parBasis, "Par basis must be positive.");

        LotId = lotId.Trim();
        SecurityId = securityId;
        AcquiredDate = acquiredDate;
        OriginalFace = originalFace;
        PricePercentOfPar = pricePercentOfPar;
        BookedFactor = bookedFactor;
        ParBasis = parBasis;
    }

    public string LotId { get; }

    /// <summary>Security Master identity the lot's reference data (day count, factor, maturity) resolves from.</summary>
    public Guid SecurityId { get; }

    public DateOnly AcquiredDate { get; }

    /// <summary>Face amount at acquisition, in currency units of par.</summary>
    public decimal OriginalFace { get; }

    /// <summary>Acquisition price expressed per <see cref="ParBasis"/> of par (e.g. 102 per 100 = 2% premium).</summary>
    public decimal PricePercentOfPar { get; }

    /// <summary>Pool factor already reflected in <see cref="OriginalFace"/> when the lot was booked (1 = original face).</summary>
    public decimal BookedFactor { get; }

    /// <summary>
    /// The quote basis <see cref="PricePercentOfPar"/> is expressed against: 100 for the bond
    /// price-per-100 convention, 1 for prices quoted per unit of face. Making this explicit is what
    /// prevents a per-unit-priced lot from silently mis-amortizing through math that assumes 100.
    /// </summary>
    public decimal ParBasis { get; }

    /// <summary>Acquisition cost in currency units.</summary>
    public decimal CostBasis => OriginalFace * PricePercentOfPar / ParBasis;

    /// <summary>
    /// Signed premium (positive) or discount (negative) over par, in currency units — the amount
    /// that amortizes toward zero by maturity.
    /// </summary>
    public decimal PremiumDiscount => OriginalFace * (PricePercentOfPar - ParBasis) / ParBasis;

    /// <summary>
    /// Face outstanding under <paramref name="currentFactor"/>, restated from the factor the lot
    /// was booked at.
    /// </summary>
    public decimal CurrentFace(decimal currentFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentFactor);
        return OriginalFace * currentFactor / BookedFactor;
    }

    /// <summary>
    /// The lot's amortized cost basis as of <paramref name="asOf"/>: straight-line premium/discount
    /// amortization toward par, weighted by the day-count year fraction of elapsed holding over the
    /// lot's life to <paramref name="maturity"/> — the same method the cost-basis relief and ledger
    /// amortization engines apply, so a basis computed here always ties to what they post. Returns
    /// <see cref="CostBasis"/> unchanged when no life has elapsed or the lot has no amortizable life.
    /// </summary>
    public decimal AmortizedBasisAsOf(DayCountConvention convention, DateOnly maturity, DateOnly asOf)
    {
        if (maturity <= AcquiredDate || PremiumDiscount == 0m)
            return CostBasis;

        var amortizeTo = asOf < maturity ? asOf : maturity;
        var elapsed = DayCountConventions.Fraction(convention, AcquiredDate, amortizeTo);
        if (elapsed <= 0m)
            return CostBasis;

        var life = DayCountConventions.Fraction(convention, AcquiredDate, maturity);
        if (life <= 0m)
            return CostBasis;

        var weight = elapsed / life;
        if (weight > 1m)
            weight = 1m;

        return CostBasis - (PremiumDiscount * weight);
    }
}
