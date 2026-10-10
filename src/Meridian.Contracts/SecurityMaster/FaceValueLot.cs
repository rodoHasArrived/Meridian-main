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
    /// between calendar coupon boundaries using the current period's day-count fraction.
    /// Supports one to 1200 monthly, quarterly, semiannual or annual coupon dates after acquisition,
    /// including end-of-month schedules.
    /// <para>
    /// <b>Regular schedules</b> (acquisition on a coupon date of the calendar schedule running from
    /// acquisition to maturity) roll whole periods exactly as before.
    /// </para>
    /// <para>
    /// <b>Odd (short) first period</b> — acquisition settling inside a coupon period, the usual
    /// secondary-market case. The schedule is generated backward from <paramref name="maturity"/> at
    /// the regular frequency: maturity is assumed to be a regular coupon date, a maturity on the last
    /// day of its month implies an end-of-month schedule, and otherwise the maturity day is kept and
    /// clipped only in short months. The first period is a stub from acquisition to the next coupon
    /// date with fraction <c>f = DCF(acquisition, nextCoupon) / DCF(previousCoupon, nextCoupon)</c>
    /// under <paramref name="convention"/>; the full regular coupon is paid at the next coupon date.
    /// </para>
    /// <para>
    /// Accrued-interest assumption: <see cref="PricePercentOfPar"/> is a <i>clean</i> price, so the
    /// purchased accrued interest <c>AI = coupon × (1 − f)</c> is not part of the cost basis — it is a
    /// separate receivable repaid by the first coupon. The yield therefore solves the street-convention
    /// dirty price <c>clean + AI = Σ CF<sub>k</sub> / (1 + y)<sup>f + k</sup></c> (k = 0 … n − 1, par
    /// with the last coupon), and the ex-coupon basis at the first coupon date is
    /// <c>(clean + AI)(1 + y)<sup>f</sup> − coupon</c>, which equals the present value of the remaining
    /// flows at the solved yield. Inside the stub the basis interpolates linearly by day count between
    /// the clean cost and that value. A retained yield must reconcile to the clean price through this
    /// same pricing function.
    /// </para>
    /// <para>
    /// Odd <i>last</i> periods are not modelled: without a contractual first-coupon or issue-date
    /// anchor, maturity is treated as a regular coupon date, so no short final period can arise.
    /// </para>
    /// </summary>
    public decimal ConstantYieldAmortizedBasisAsOf(
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear = 2,
        decimal? retainedAnnualEffectiveYield = null)
        => ConstantYieldAmortizedBasisCore(convention, maturity, asOf, annualCouponRatePercent, paymentsPerYear,
            retainedAnnualEffectiveYield, allowOddFirstPeriod: true);

    // Canonical lot amortization model v2 (OpenLotAmortization.ModelVersion) admits regular calendar
    // schedules only; that admission rule is part of the governed calculation version, so odd first
    // periods stay refused here while the regular arithmetic is shared bit for bit with the public kernel.
    internal decimal RegularScheduleConstantYieldAmortizedBasisAsOf(
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear,
        decimal? retainedAnnualEffectiveYield)
        => ConstantYieldAmortizedBasisCore(convention, maturity, asOf, annualCouponRatePercent, paymentsPerYear,
            retainedAnnualEffectiveYield, allowOddFirstPeriod: false);

    private decimal ConstantYieldAmortizedBasisCore(
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear,
        decimal? retainedAnnualEffectiveYield,
        bool allowOddFirstPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentsPerYear);
        ArgumentOutOfRangeException.ThrowIfNegative(annualCouponRatePercent);

        if (retainedAnnualEffectiveYield is <= -1m
            || (retainedAnnualEffectiveYield is not null && maturity <= AcquiredDate))
            throw new ArgumentException("Retained effective yield or maturity is invalid.");
        if (maturity <= AcquiredDate)
            return CostBasis;

        var couponDates = RegularCouponDates(maturity, paymentsPerYear, throwWhenIrregular: !allowOddFirstPeriod);
        if (couponDates is null)
            return OddFirstPeriodConstantYieldBasis(convention, maturity, asOf, annualCouponRatePercent,
                paymentsPerYear, retainedAnnualEffectiveYield);
        var totalPeriods = couponDates.Length - 1;

        // Canonical acquisitions retain annual yield as a decimal (0.05 = 5%). Verify that
        // it prices these level contractual cash flows before using it, including at maturity.
        if (retainedAnnualEffectiveYield is { } retainedYield)
        {
            if (Math.Abs(PricePerUnitAtYield(annualCouponRatePercent / 100m / paymentsPerYear,
                    totalPeriods, retainedYield / paymentsPerYear) - PricePercentOfPar / ParBasis) > 0.0000000001m)
                throw new ArgumentException("Retained effective yield does not reconcile to acquisition price and level contractual periods.");
        }

        if (PremiumDiscount == 0m)
            return CostBasis;
        if (asOf <= AcquiredDate)
            return CostBasis;
        if (asOf >= maturity)
            return OriginalFace;

        var pricePerUnit = PricePercentOfPar / ParBasis;
        var couponPerPeriod = annualCouponRatePercent / 100m / paymentsPerYear;
        var yieldPerPeriod = retainedAnnualEffectiveYield is { } annualYield
            ? annualYield / paymentsPerYear
            : SolveYieldPerPeriod(pricePerUnit, couponPerPeriod, totalPeriods);

        var wholePeriods = 0;
        while (couponDates[wholePeriods + 1] <= asOf)
            wholePeriods++;
        var periodStart = couponDates[wholePeriods];
        var periodEnd = couponDates[wholePeriods + 1];
        var periodFraction = DayCountConventions.Fraction(convention, periodStart, periodEnd);
        if (periodFraction <= 0m)
            throw new ArgumentException("Constant-yield amortization requires a positive coupon-period day-count fraction.");
        var partialPeriod = DayCountConventions.Fraction(convention, periodStart, asOf) / periodFraction;

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

    // Frozen v1 arithmetic for retained, unversioned canonical instructions only.
    // New calculations use calendar coupon boundaries in the public kernel above.
    internal decimal LegacyConstantYieldAmortizedBasisAsOf(
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear = 2,
        decimal? retainedAnnualEffectiveYield = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paymentsPerYear);
        ArgumentOutOfRangeException.ThrowIfNegative(annualCouponRatePercent);

        // Canonical acquisitions retain annual yield as a decimal (0.05 = 5%). Verify that
        // it prices these level contractual cash flows before using it, including at maturity.
        if (retainedAnnualEffectiveYield is { } retainedYield)
        {
            if (retainedYield <= -1m || maturity <= AcquiredDate)
                throw new ArgumentException("Retained effective yield or maturity is invalid.");
            var periods = DayCountConventions.Fraction(convention, AcquiredDate, maturity) * paymentsPerYear;
            if (periods < 1m || periods > 1200m || periods != decimal.Truncate(periods)
                || Math.Abs(PricePerUnitAtYield(annualCouponRatePercent / 100m / paymentsPerYear,
                    (int)periods, retainedYield / paymentsPerYear) - PricePercentOfPar / ParBasis) > 0.0000000001m)
                throw new ArgumentException("Retained effective yield does not reconcile to acquisition price and level contractual periods.");
        }

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
        var yieldPerPeriod = retainedAnnualEffectiveYield is { } annualYield
            ? annualYield / paymentsPerYear
            : SolveYieldPerPeriod(pricePerUnit, couponPerPeriod, totalPeriods);

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

    // Returns null instead of throwing for a schedule that is not regular from acquisition when
    // throwWhenIrregular is false; the caller then prices it with the odd-first-period model.
    private DateOnly[]? RegularCouponDates(DateOnly maturity, int paymentsPerYear, bool throwWhenIrregular)
    {
        if (paymentsPerYear is not (1 or 2 or 4 or 12))
            throw new ArgumentException("Constant-yield amortization requires an annual, semiannual, quarterly or monthly coupon frequency.");
        var monthsPerPeriod = 12 / paymentsPerYear;
        var months = (maturity.Year - AcquiredDate.Year) * 12 + maturity.Month - AcquiredDate.Month;
        if (months < monthsPerPeriod || months % monthsPerPeriod != 0 || months / monthsPerPeriod > 1200)
        {
            if (!throwWhenIrregular)
                return null;
            throw new ArgumentException("Constant-yield amortization requires one to 1200 level calendar coupon periods.");
        }

        // Endpoints at month end retain EOM through February and leap years. Otherwise
        // retain the contractual day, clipping only short months. Anchor each boundary
        // independently so a clipped February never shifts later coupon dates.
        var endOfMonth = AcquiredDate.Day == DateTime.DaysInMonth(AcquiredDate.Year, AcquiredDate.Month)
            && maturity.Day == DateTime.DaysInMonth(maturity.Year, maturity.Month);
        var dayOfMonth = Math.Max(AcquiredDate.Day, maturity.Day);
        var dates = new DateOnly[months / monthsPerPeriod + 1];
        for (var period = 0; period < dates.Length; period++)
        {
            var month = AcquiredDate.AddMonths(period * monthsPerPeriod);
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            dates[period] = new DateOnly(month.Year, month.Month, endOfMonth ? lastDay : Math.Min(dayOfMonth, lastDay));
        }
        if (dates[0] != AcquiredDate || dates[^1] != maturity)
        {
            if (!throwWhenIrregular)
                return null;
            throw new ArgumentException("Constant-yield amortization requires regular calendar coupon dates; odd periods are unsupported by this calculation version.");
        }
        return dates;
    }

    /// <summary>
    /// Constant-yield basis for an acquisition inside a coupon period (short first period). See
    /// <see cref="ConstantYieldAmortizedBasisAsOf"/> for the schedule, stub-fraction and
    /// accrued-interest conventions.
    /// </summary>
    private decimal OddFirstPeriodConstantYieldBasis(
        DayCountConvention convention,
        DateOnly maturity,
        DateOnly asOf,
        decimal annualCouponRatePercent,
        int paymentsPerYear,
        decimal? retainedAnnualEffectiveYield)
    {
        // couponDates[0] is the quasi-coupon date on or before acquisition; [1..n] are paid after it.
        var couponDates = BackwardCouponDates(maturity, paymentsPerYear);
        var totalCoupons = couponDates.Length - 1;
        var firstCoupon = couponDates[1];
        var firstPeriodFraction = DayCountConventions.Fraction(convention, couponDates[0], firstCoupon);
        if (firstPeriodFraction <= 0m)
            throw new ArgumentException("Constant-yield amortization requires a positive coupon-period day-count fraction.");
        var stubToCoupon = DayCountConventions.Fraction(convention, AcquiredDate, firstCoupon);
        var stubFraction = Math.Min(1m, stubToCoupon / firstPeriodFraction);

        var pricePerUnit = PricePercentOfPar / ParBasis;
        var couponPerPeriod = annualCouponRatePercent / 100m / paymentsPerYear;
        // Purchased accrued interest: the clean acquisition price excludes it and the full first
        // coupon repays it, so it never enters the carrying basis.
        var accruedPerUnit = couponPerPeriod * (1m - stubFraction);

        // Canonical acquisitions retain annual yield as a decimal (0.05 = 5%). It must price the
        // odd-first-period schedule to the clean acquisition price before it is used.
        if (retainedAnnualEffectiveYield is { } retainedYield
            && Math.Abs(CleanPricePerUnitAtYield(couponPerPeriod, totalCoupons, stubFraction, retainedYield / paymentsPerYear)
                - pricePerUnit) > 0.0000000001m)
            throw new ArgumentException("Retained effective yield does not reconcile to acquisition price and the odd-first-period contractual schedule.");

        if (PremiumDiscount == 0m)
            return CostBasis;
        if (asOf <= AcquiredDate)
            return CostBasis;
        if (asOf >= maturity)
            return OriginalFace;

        var yieldPerPeriod = retainedAnnualEffectiveYield is { } annualYield
            ? annualYield / paymentsPerYear
            : SolveOddFirstPeriodYield(pricePerUnit, couponPerPeriod, totalCoupons, stubFraction);

        // Ex-coupon carrying value at the first coupon date: the dirty acquisition amount grown at
        // the constant yield over the stub, less the full coupon received.
        var afterFirstCoupon = ((pricePerUnit + accruedPerUnit) * DecimalPow(1m + yieldPerPeriod, stubFraction))
            - couponPerPeriod;

        if (asOf < firstCoupon)
        {
            var stubWeight = stubToCoupon > 0m
                ? DayCountConventions.Fraction(convention, AcquiredDate, asOf) / stubToCoupon
                : 0m;
            return (pricePerUnit + ((afterFirstCoupon - pricePerUnit) * stubWeight)) * OriginalFace;
        }

        var wholePeriods = 1;
        while (couponDates[wholePeriods + 1] <= asOf)
            wholePeriods++;
        var periodStart = couponDates[wholePeriods];
        var periodEnd = couponDates[wholePeriods + 1];
        var periodFraction = DayCountConventions.Fraction(convention, periodStart, periodEnd);
        if (periodFraction <= 0m)
            throw new ArgumentException("Constant-yield amortization requires a positive coupon-period day-count fraction.");
        var partialPeriod = DayCountConventions.Fraction(convention, periodStart, asOf) / periodFraction;

        var basis = afterFirstCoupon;
        for (var period = 1; period < wholePeriods; period++)
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

    // Regular coupon dates generated backward from maturity (market convention), each anchored
    // independently so a clipped February never shifts other dates. Element 0 is the last coupon
    // date on or before acquisition: the quasi-coupon start of the stub period.
    private DateOnly[] BackwardCouponDates(DateOnly maturity, int paymentsPerYear)
    {
        var monthsPerPeriod = 12 / paymentsPerYear;
        var endOfMonth = maturity.Day == DateTime.DaysInMonth(maturity.Year, maturity.Month);
        var dates = new List<DateOnly> { maturity };
        while (dates[^1] > AcquiredDate)
        {
            if (dates.Count > 1200)
                throw new ArgumentException("Constant-yield amortization requires one to 1200 calendar coupon periods.");
            var month = maturity.AddMonths(-dates.Count * monthsPerPeriod);
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            dates.Add(new DateOnly(month.Year, month.Month, endOfMonth ? lastDay : Math.Min(maturity.Day, lastDay)));
        }

        dates.Reverse();
        return dates.ToArray();
    }

    /// <summary>
    /// Clean price per unit of face with a short first period of fraction <paramref name="stubFraction"/>:
    /// the dirty value <c>Σ c / (1 + y)^(f + k) + 1 / (1 + y)^(f + n − 1)</c> — equal to
    /// <c>(1 + y)^(1 − f)</c> times the regular n-period price — less accrued interest <c>c × (1 − f)</c>.
    /// </summary>
    private static decimal CleanPricePerUnitAtYield(decimal couponPerPeriod, int totalCoupons, decimal stubFraction, decimal yieldPerPeriod)
        => (DecimalPow(1m + yieldPerPeriod, 1m - stubFraction) * PricePerUnitAtYield(couponPerPeriod, totalCoupons, yieldPerPeriod))
            - (couponPerPeriod * (1m - stubFraction));

    private static decimal SolveOddFirstPeriodYield(decimal pricePerUnit, decimal couponPerPeriod, int totalCoupons, decimal stubFraction)
    {
        var low = -0.5m;
        var high = 5m;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var mid = (low + high) / 2m;
            if (CleanPriceExceeds(pricePerUnit, couponPerPeriod, totalCoupons, stubFraction, mid))
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

    // Price falls strictly as yield rises, so a probe whose price overflows decimal (a long
    // schedule discounted at a deeply negative yield) prices above any finite target: the root
    // lies at a higher yield. Treating the overflow that way keeps the fixed bracket safe for
    // every schedule within the coupon limit instead of throwing mid-bisection.
    private static bool CleanPriceExceeds(decimal pricePerUnit, decimal couponPerPeriod, int totalCoupons, decimal stubFraction, decimal yieldPerPeriod)
    {
        try
        {
            return CleanPricePerUnitAtYield(couponPerPeriod, totalCoupons, stubFraction, yieldPerPeriod) > pricePerUnit;
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    // Deterministic decimal power for a positive base and an exponent in [0, 1]. Pure decimal
    // series rather than double Math.Pow, so replayed amortization is platform-independent.
    private static decimal DecimalPow(decimal value, decimal exponent)
    {
        if (exponent == 0m || value == 1m)
            return 1m;
        if (exponent == 1m)
            return value;
        return DecimalExp(exponent * DecimalLn(value));
    }

    private static decimal DecimalLn(decimal value)
    {
        if (value <= 0m)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Logarithm requires a positive value.");
        // ln(x) = 2 · atanh((x − 1) / (x + 1)), convergent for every positive x.
        var z = (value - 1m) / (value + 1m);
        var zSquared = z * z;
        var power = z;
        var sum = 0m;
        for (var k = 0; k < 4000; k++)
        {
            var term = power / ((2 * k) + 1);
            if (term == 0m)
                break;
            sum += term;
            power *= zSquared;
        }

        return 2m * sum;
    }

    private static decimal DecimalExp(decimal value)
    {
        if (value < 0m)
            return 1m / DecimalExp(-value);
        var term = 1m;
        var sum = 1m;
        for (var k = 1; k < 1000; k++)
        {
            term = term * value / k;
            if (term == 0m)
                break;
            sum += term;
        }

        return sum;
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
