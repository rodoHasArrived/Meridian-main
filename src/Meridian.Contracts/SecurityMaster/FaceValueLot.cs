using Meridian.Contracts.FixedIncome;

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

    /// <summary>
    /// The lot's amortized cost basis as of <paramref name="asOf"/> under the requested
    /// <paramref name="method"/>. <see cref="BondAmortizationMethod.ConstantYield"/> applies the
    /// effective-interest method (US GAAP ASC 310-20 for most premium amortization);
    /// <see cref="BondAmortizationMethod.NoAmortization"/> holds the book flat;
    /// <see cref="BondAmortizationMethod.AuctionRate"/> recognises the premium/discount to par
    /// immediately; the remaining methods fall back to day-count-weighted straight-line
    /// (<see cref="AmortizedBasisAsOf(DayCountConvention, DateOnly, DateOnly)"/>), the historical
    /// immaterial-difference accommodation.
    /// </summary>
    /// <param name="annualCouponRatePercent">Annual coupon rate in percent-of-par terms (4.25 = 4.25%), matching the Security Master's <c>couponRate</c> convention; 0 for zero-coupon accretion.</param>
    /// <param name="paymentsPerYear">Coupon payments per year (2 for semi-annual, the fixed-income default).</param>
    public decimal AmortizedBasisAsOf(
        BondAmortizationMethod method,
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear = 2)
        => method switch
        {
            BondAmortizationMethod.ConstantYield =>
                ConstantYieldAmortizedBasisAsOf(convention, maturity, asOf, annualCouponRatePercent, paymentsPerYear),
            BondAmortizationMethod.NoAmortization => CostBasis,
            BondAmortizationMethod.AuctionRate => asOf >= AcquiredDate ? OriginalFace : CostBasis,
            _ => AmortizedBasisAsOf(convention, maturity, asOf),
        };

    /// <summary>
    /// Effective-interest (constant-yield) amortized basis: the yield to maturity implied by the
    /// acquisition price is solved once, then the book value rolls forward period by period —
    /// interest income accrues as a constant proportion of carrying value
    /// (<c>basis × (1 + i)</c> less the period coupon), so a premium amortizes slowly at first and
    /// faster near maturity, and a discount accretes in reverse — the ASC 310-20 profile the
    /// straight-line method only approximates. The partial current period interpolates linearly
    /// between period boundaries. Periods are level (no odd-first-period day-count adjustment);
    /// the day-count convention scales elapsed time into period space.
    /// </summary>
    public decimal ConstantYieldAmortizedBasisAsOf(
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentsPerYear);
        ArgumentOutOfRangeException.ThrowIfNegative(annualCouponRatePercent);

        if (maturity <= AcquiredDate || PremiumDiscount == 0m)
            return CostBasis;
        if (asOf <= AcquiredDate)
            return CostBasis;
        if (asOf >= maturity)
            return OriginalFace;

        var lifeYears = DayCountConventions.Fraction(convention, AcquiredDate, maturity);
        if (lifeYears <= 0m)
            return CostBasis;

        var totalPeriods = (int)Math.Round(lifeYears * paymentsPerYear, MidpointRounding.AwayFromZero);
        if (totalPeriods < 1)
            totalPeriods = 1;

        var pricePerUnit = PricePercentOfPar / ParBasis;
        var couponPerPeriod = annualCouponRatePercent / 100m / paymentsPerYear;
        var yieldPerPeriod = SolveYieldPerPeriod(pricePerUnit, couponPerPeriod, totalPeriods);

        // Elapsed holding scaled into period space, capped at the final period boundary.
        var elapsedYears = DayCountConventions.Fraction(convention, AcquiredDate, asOf);
        var elapsedPeriods = elapsedYears / lifeYears * totalPeriods;
        if (elapsedPeriods >= totalPeriods)
            return OriginalFace;

        var wholePeriods = (int)decimal.Truncate(elapsedPeriods);
        var partialPeriod = elapsedPeriods - wholePeriods;

        var basis = pricePerUnit;
        for (var period = 0; period < wholePeriods; period++)
        {
            basis = (basis * (1m + yieldPerPeriod)) - couponPerPeriod;
        }

        if (partialPeriod > 0m)
        {
            var nextBasis = (basis * (1m + yieldPerPeriod)) - couponPerPeriod;
            basis += (nextBasis - basis) * partialPeriod;
        }

        return basis * OriginalFace;
    }

    /// <summary>
    /// Solves the per-period yield that discounts the level coupon stream plus par redemption to
    /// the acquisition price, by bisection — the pricing function is strictly decreasing in yield,
    /// so the bracket converges unconditionally. Precision is far below a cent on realistic faces.
    /// </summary>
    private static decimal SolveYieldPerPeriod(decimal pricePerUnit, decimal couponPerPeriod, int totalPeriods)
    {
        var low = -0.5m;
        var high = 5m;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var mid = (low + high) / 2m;
            if (PricePerUnitAtYield(couponPerPeriod, totalPeriods, mid) > pricePerUnit)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2m;
    }

    private static decimal PricePerUnitAtYield(decimal couponPerPeriod, int totalPeriods, decimal yieldPerPeriod)
    {
        if (yieldPerPeriod == 0m)
            return (couponPerPeriod * totalPeriods) + 1m;

        var discount = 1m;
        var price = 0m;
        for (var period = 1; period <= totalPeriods; period++)
        {
            discount /= 1m + yieldPerPeriod;
            price += couponPerPeriod * discount;
        }

        return price + discount;
    }
}
