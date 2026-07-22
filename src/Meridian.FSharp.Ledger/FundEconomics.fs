namespace Meridian.FSharp.Ledger

open System

/// Pure, deterministic fund-economics accrual math kept in F# alongside the ledger kernels.
///
/// These are the fee/expense accruals a fund accountant needs that the C# NAV/waterfall/clawback
/// calculators in Meridian.Ledger intentionally do NOT cover: a day-weighted management fee, a
/// straight-line expense accrual, and a performance fee with a hurdle, high-water mark, and
/// crystallization. Callers convert the returned amounts into governed ledger postings; nothing
/// here touches I/O. Currency amounts round to 2 decimals, banker's rounding, matching the C#
/// LedgerCurrencyRounding used by the sibling kernels.
[<RequireQualifiedAccess>]
module FundEconomics =

    [<Literal>]
    let private CurrencyDecimals = 2

    let private roundCurrency (value: decimal) : decimal =
        Math.Round(value, CurrencyDecimals, MidpointRounding.ToEven)

    /// Outcome of a performance-fee accrual against the high-water mark.
    type PerformanceFeeAccrual =
        { /// Appreciation above the prior high-water mark, floored at zero.
          GrossProfit: decimal
          /// Profit remaining after the hurdle is satisfied — the base the fee is charged on.
          FeeBase: decimal
          /// Performance fee accrued this period.
          PerformanceFee: decimal
          /// Fee that crystallizes (is locked in) this period; zero when not crystallizing.
          CrystallizedFee: decimal
          /// High-water mark carried into the next period after crystallization.
          NewHighWaterMark: decimal }

    /// <summary>
    /// Day-weighted management fee: net assets × annual rate × accrual days / year-basis days.
    /// </summary>
    let managementFeeAccrual
        (netAssets: decimal)
        (annualRate: decimal)
        (accrualDays: int)
        (yearBasisDays: int)
        : decimal =
        if netAssets < 0m then invalidArg (nameof netAssets) "Net assets cannot be negative."
        if annualRate < 0m then invalidArg (nameof annualRate) "Management fee rate cannot be negative."
        if accrualDays < 0 then invalidArg (nameof accrualDays) "Accrual days cannot be negative."
        if yearBasisDays <= 0 then invalidArg (nameof yearBasisDays) "Year-basis days must be positive."

        roundCurrency (netAssets * annualRate * decimal accrualDays / decimal yearBasisDays)

    /// <summary>
    /// Straight-line expense accrual: annual amount × accrual days / year-basis days.
    /// </summary>
    let expenseAccrual
        (annualAmount: decimal)
        (accrualDays: int)
        (yearBasisDays: int)
        : decimal =
        if annualAmount < 0m then invalidArg (nameof annualAmount) "Annual expense amount cannot be negative."
        if accrualDays < 0 then invalidArg (nameof accrualDays) "Accrual days cannot be negative."
        if yearBasisDays <= 0 then invalidArg (nameof yearBasisDays) "Year-basis days must be positive."

        roundCurrency (annualAmount * decimal accrualDays / decimal yearBasisDays)

    /// <summary>
    /// Performance fee against a high-water mark, net of a currency hurdle. Fee is charged only on
    /// appreciation above both the prior high-water mark and the hurdle. When crystallizing, the fee
    /// locks in and the high-water mark advances to the post-fee net asset value; otherwise the fee
    /// accrues but the high-water mark is left unchanged until a later crystallization.
    /// </summary>
    let performanceFeeAccrual
        (endingNavBeforeFee: decimal)
        (priorHighWaterMark: decimal)
        (hurdleAmount: decimal)
        (performanceFeeRate: decimal)
        (crystallize: bool)
        : PerformanceFeeAccrual =
        if priorHighWaterMark < 0m then invalidArg (nameof priorHighWaterMark) "High-water mark cannot be negative."
        if hurdleAmount < 0m then invalidArg (nameof hurdleAmount) "Hurdle amount cannot be negative."
        if performanceFeeRate < 0m || performanceFeeRate >= 1m then
            invalidArg (nameof performanceFeeRate) "Performance fee rate must be in [0, 1)."

        let grossProfit = max 0m (endingNavBeforeFee - priorHighWaterMark)
        let feeBase = max 0m (grossProfit - hurdleAmount)
        let fee = roundCurrency (feeBase * performanceFeeRate)
        let crystallizedFee = if crystallize then fee else 0m
        let newHighWaterMark =
            if crystallize then max priorHighWaterMark (endingNavBeforeFee - crystallizedFee)
            else priorHighWaterMark

        { GrossProfit = grossProfit
          FeeBase = feeBase
          PerformanceFee = fee
          CrystallizedFee = crystallizedFee
          NewHighWaterMark = newHighWaterMark }
