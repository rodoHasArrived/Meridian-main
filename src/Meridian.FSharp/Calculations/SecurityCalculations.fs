namespace Meridian.FSharp.Calculations

open System
open Meridian.FSharp.Domain

/// Day-count fraction calculations per ISDA and market conventions.
/// Reference: Clearwater Security Types and Calculation Reference Guide (May/June 2026), Section 2.2.
[<RequireQualifiedAccess>]
module DayCount =

    /// Actual/360: actual calendar days divided by 360.
    /// Common for money-market instruments (CP, CD, repo, T-bills).
    let actual360 (startDate: DateOnly) (endDate: DateOnly) : decimal =
        let days = (endDate.ToDateTime(TimeOnly.MinValue) - startDate.ToDateTime(TimeOnly.MinValue)).TotalDays
        decimal days / 360m

    /// Actual/365 Fixed: actual calendar days divided by 365.
    /// Common for UK gilts, Australian bonds, and some money-market instruments.
    let actual365 (startDate: DateOnly) (endDate: DateOnly) : decimal =
        let days = (endDate.ToDateTime(TimeOnly.MinValue) - startDate.ToDateTime(TimeOnly.MinValue)).TotalDays
        decimal days / 365m

    /// 30/360 (Bond Basis / US): months treated as 30 days, year as 360.
    /// Default for most US corporate and municipal bonds.
    let thirty360 (startDate: DateOnly) (endDate: DateOnly) : decimal =
        let d1 = min startDate.Day 30
        let d2 = if startDate.Day >= 30 then min endDate.Day 30 else endDate.Day
        let months = (endDate.Year - startDate.Year) * 12 + (endDate.Month - startDate.Month)
        let days = d2 - d1
        decimal (months * 30 + days) / 360m

    /// Actual/Actual (ISDA): actual days in each calendar year, weighted by year length.
    /// Default for US Treasury notes and bonds.
    let actualActualIsda (startDate: DateOnly) (endDate: DateOnly) : decimal =
        if startDate >= endDate then 0m
        else
            let start = startDate.ToDateTime(TimeOnly.MinValue)
            let finish = endDate.ToDateTime(TimeOnly.MinValue)
            if start.Year = finish.Year then
                let daysInYear = if DateTime.IsLeapYear start.Year then 366.0 else 365.0
                decimal (finish - start).Days / decimal daysInYear
            else
                let daysInFirstYear = if DateTime.IsLeapYear start.Year then 366.0 else 365.0
                let daysInLastYear = if DateTime.IsLeapYear finish.Year then 366.0 else 365.0
                let firstFrac = double (DateTime(start.Year + 1, 1, 1) - start).Days / daysInFirstYear
                let lastFrac  = double (finish - DateTime(finish.Year, 1, 1)).Days / daysInLastYear
                let fullYears = finish.Year - start.Year - 1
                decimal firstFrac + decimal fullYears + decimal lastFrac

    /// Dispatch to the appropriate day-count fraction for the given convention.
    let fractionFor (convention: DayCountConvention) (startDate: DateOnly) (endDate: DateOnly) : decimal =
        match convention with
        | Actual360          -> actual360 startDate endDate
        | Actual365          -> actual365 startDate endDate
        | Thirty360          -> thirty360 startDate endDate
        | Business252        -> actual365 startDate endDate // simplified; full Biz/252 requires trading calendar
        | OtherDayCount _    -> actual365 startDate endDate // safe fallback


/// Core Clearwater-aligned security calculations.
/// Formulas reference the Clearwater Security Types and Calculation Reference Guide (May/June 2026),
/// Sections 2.2, 2.3, and Appendix B.
[<RequireQualifiedAccess>]
module SecurityCalculations =

    // -----------------------------------------------------------------------
    // 2.2  Interest and accrual
    // -----------------------------------------------------------------------

    /// Period interest cash flow:
    ///   Interest_i = Outstanding_principal_i × Coupon_rate_i × Day-count_fraction_i
    let interestCashFlow
        (outstandingPrincipal : decimal)
        (couponRate           : decimal)
        (dayCountFraction     : decimal) : decimal =
        outstandingPrincipal * couponRate * dayCountFraction

    /// Accrued interest from the last coupon date to the evaluation date:
    ///   Accrued_interest = Principal × Coupon_rate × Accrued_day-count_fraction
    let accruedInterest
        (principal       : decimal)
        (couponRate      : decimal)
        (lastCouponDate  : DateOnly)
        (evaluationDate  : DateOnly)
        (convention      : DayCountConvention) : decimal =
        if evaluationDate <= lastCouponDate then 0m
        else
            let fraction = DayCount.fractionFor convention lastCouponDate evaluationDate
            principal * couponRate * fraction

    // -----------------------------------------------------------------------
    // Market value
    // -----------------------------------------------------------------------

    /// Bond / fixed-income market value for a price quoted per 100:
    ///   Local_market_value = Par × Factor × Price / 100
    /// Factor = 1.0 for non-factored bonds; < 1.0 for amortising/prepaying pools.
    let bondMarketValue (par : decimal) (factor : decimal) (quotedPrice : decimal) : decimal =
        par * factor * quotedPrice / 100m

    /// Equity or fund market value:
    ///   Market_value = Shares_or_units × Market_price_or_NAV
    let equityMarketValue (sharesOrUnits : decimal) (marketPriceOrNav : decimal) : decimal =
        sharesOrUnits * marketPriceOrNav

    // -----------------------------------------------------------------------
    // Dirty / clean price
    // -----------------------------------------------------------------------

    /// Dirty price = Clean price + Accrued interest (full price including interest).
    let dirtyPrice (cleanPrice : decimal) (accruedInterest : decimal) : decimal =
        cleanPrice + accruedInterest

    /// Clean price = Dirty price − Accrued interest (quoted flat price).
    let cleanPrice (dirtyPrice : decimal) (accruedInterest : decimal) : decimal =
        dirtyPrice - accruedInterest

    // -----------------------------------------------------------------------
    // 2.3  Amortization methodologies
    // -----------------------------------------------------------------------

    /// Straight-line daily amortization/accretion amount:
    ///   Daily_amount = (Target_book_price − Starting_book_price) / Amortization_days
    /// Positive result → accretion (discount); negative result → amortization (premium).
    let straightLineDailyAmount
        (targetBookPrice   : decimal)
        (startingBookPrice : decimal)
        (amortizationDays  : int) : decimal =
        if amortizationDays <= 0 then 0m
        else (targetBookPrice - startingBookPrice) / decimal amortizationDays

    /// Constant-yield (effective-interest) income for one period:
    ///   Effective_interest_income = Beginning_amortized_cost × Periodic_yield
    let constantYieldIncome
        (beginningAmortizedCost : decimal)
        (periodicYield          : decimal) : decimal =
        beginningAmortizedCost * periodicYield

    /// Premium amortization or discount accretion for the period:
    ///   Amortization_accretion = Effective_interest_income − Contractual_interest_recognized
    /// Positive result → accretion (discount); negative result → amortization (premium).
    let amortizationAccretion
        (effectiveInterestIncome       : decimal)
        (contractualInterestRecognized : decimal) : decimal =
        effectiveInterestIncome - contractualInterestRecognized

    // -----------------------------------------------------------------------
    // Structured / prepaying securities
    // -----------------------------------------------------------------------

    /// Weighted Average Life:
    ///   WAL = Σ(Principal_payment_i × Time_to_payment_i) / Σ(Principal_payment_i)
    /// principalPayments: list of (amount, timeToPaymentYears) tuples.
    let weightedAverageLife (principalPayments : (decimal * decimal) list) : decimal =
        let totalPrincipal = principalPayments |> List.sumBy fst
        if totalPrincipal = 0m then 0m
        else
            let weightedSum = principalPayments |> List.sumBy (fun (p, t) -> p * t)
            weightedSum / totalPrincipal

    // -----------------------------------------------------------------------
    // Inflation-linked securities
    // -----------------------------------------------------------------------

    /// Inflation-adjusted principal (e.g. TIPS):
    ///   Adjusted_principal = Base_units × Inflation_factor
    /// The Clearwater example: 1,000 units × 1.2 factor = 1,200 adjusted principal.
    let inflationAdjustedPrincipal (baseUnits : decimal) (inflationFactor : decimal) : decimal =
        baseUnits * inflationFactor

    /// Inflation-linked market value:
    ///   Market_value = Base_units × Inflation_factor × Quoted_price / 100
    let inflationLinkedMarketValue
        (baseUnits      : decimal)
        (inflationFactor : decimal)
        (quotedPrice    : decimal) : decimal =
        baseUnits * inflationFactor * quotedPrice / 100m

    // -----------------------------------------------------------------------
    // Purchased Credit Impaired (PCI) lots
    // -----------------------------------------------------------------------

    /// PCI daily amortization/accretion:
    ///   Daily_amount = [Beginning_BV × Periodic_yield / Days_in_period] − Daily_interest_income
    /// Step 1: Periodic total income = Beginning BV × periodic yield.
    /// Step 2: Daily total income    = Periodic total income / days in period.
    /// Step 3: Daily amortization    = Daily total income − daily interest income already recognized.
    let pciDailyAmortization
        (beginningBookValue  : decimal)
        (periodicYield       : decimal)
        (daysInPeriod        : int)
        (dailyInterestIncome : decimal) : decimal =
        if daysInPeriod <= 0 then 0m
        else
            let periodicTotalIncome = beginningBookValue * periodicYield
            let dailyTotalIncome    = periodicTotalIncome / decimal daysInPeriod
            dailyTotalIncome - dailyInterestIncome

    // -----------------------------------------------------------------------
    // Foreign currency
    // -----------------------------------------------------------------------

    /// FX remeasurement:
    ///   Functional_currency_amount = Local_currency_amount × Daily_FX_rate
    /// Amortization is calculated locally and then remeasured to functional currency.
    let fxRemeasurement (localAmount : decimal) (fxRate : decimal) : decimal =
        localAmount * fxRate

    // -----------------------------------------------------------------------
    // Short-term discount instruments
    // -----------------------------------------------------------------------

    /// Straight-line daily accretion for CP, CDs, and agency discount notes:
    ///   Daily_accretion = (Face_value − Purchase_price) / Settlement_to_maturity_days
    /// Example: purchased at 98, face 100, 90 days → 2 / 90 ≈ 0.0222 per day.
    let shortTermDailyAccretion
        (faceValue                  : decimal)
        (purchasePrice              : decimal)
        (settlementToMaturityDays   : int) : decimal =
        if settlementToMaturityDays <= 0 then 0m
        else (faceValue - purchasePrice) / decimal settlementToMaturityDays

    // -----------------------------------------------------------------------
    // Repo interest
    // -----------------------------------------------------------------------

    /// Repo / reverse-repo interest for a period:
    ///   Repo_interest = Principal × Repo_rate × Day-count_fraction
    let repoInterest
        (principal        : decimal)
        (repoRate         : decimal)
        (dayCountFraction : decimal) : decimal =
        principal * repoRate * dayCountFraction

    // -----------------------------------------------------------------------
    // Callable / putable option in-the-money tests (Section 4.5)
    // -----------------------------------------------------------------------

    /// Returns true when a callable bond is in-the-money:
    ///   Purchase_price > Next_call_price → amortize to next call date and price.
    let isCallInTheMoney (purchasePrice : decimal) (nextCallPrice : decimal) : bool =
        purchasePrice > nextCallPrice

    /// Returns true when a putable bond is in-the-money:
    ///   Purchase_price ≤ Next_put_price → amortize to next put date and price.
    let isPutInTheMoney (purchasePrice : decimal) (nextPutPrice : decimal) : bool =
        purchasePrice <= nextPutPrice

    // -----------------------------------------------------------------------
    // Yield solvers
    // -----------------------------------------------------------------------

    /// Newton-Raphson yield solver.
    /// Finds the yield Y such that: Price = Σ [ CF_i / (1 + Y/f)^(f × t_i) ]
    /// cashFlows: list of (timeToPaymentYears, cashFlowAmount).
    /// Returns None if the price is non-positive, there are no cash flows, or convergence fails.
    let private newtonRaphsonYield
        (price              : decimal)
        (cashFlows          : (decimal * decimal) list)
        (compoundingFreq    : int)
        (maxIterations      : int) : decimal option =
        if cashFlows.IsEmpty || price <= 0m || maxIterations <= 0 then None
        else
            let f = double compoundingFreq
            let pv (y : double) =
                cashFlows
                |> List.sumBy (fun (t, cf) ->
                    double cf / Math.Pow(1.0 + y / f, f * double t))

            let mutable y         = 0.05
            let mutable converged = false
            let mutable iter      = 0
            let target            = double price
            let tolerance         = 1e-10

            while not converged && iter < maxIterations do
                let pvY     = pv y
                let pvYUp   = pv (y + 1e-7)
                let deriv   = (pvYUp - pvY) / 1e-7
                if abs deriv < 1e-15 then
                    iter <- maxIterations
                else
                    let yNew = y - (pvY - target) / deriv
                    if abs (yNew - y) < tolerance then converged <- true
                    y    <- yNew
                    iter <- iter + 1

            if converged then Some (decimal y) else None

    /// Purchase/amortization yield — internal rate of return solving:
    ///   Purchase_dirty_price = Σ [ CF_i / (1 + Y/f)^(f × t_i) ]
    /// cashFlows: list of (timeToPaymentYears, cashFlowAmount).
    /// compoundingFrequency: 1 = annual, 2 = semi-annual, 4 = quarterly.
    let purchaseYield
        (purchaseDirtyPrice  : decimal)
        (cashFlows           : (decimal * decimal) list)
        (compoundingFrequency : int) : decimal option =
        newtonRaphsonYield purchaseDirtyPrice cashFlows compoundingFrequency 200

    /// Book yield — daily solve from end-of-day book price and current projected cash flows:
    ///   Current_book_price = PV of current projected cash flows
    /// Clearwater uses an iterative Newton-Raphson solver internally.
    let bookYield
        (currentBookPrice    : decimal)
        (cashFlows           : (decimal * decimal) list)
        (compoundingFrequency : int) : decimal option =
        newtonRaphsonYield currentBookPrice cashFlows compoundingFrequency 200
