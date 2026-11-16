namespace Meridian.FSharp.Ledger

open System

/// Whether an input to a return rests on activity the ledger has posted.
type ReturnInputBasis =
    /// Posted to the ledger (or, for an external feed, recorded as final by that source).
    | Posted = 0
    /// Approved or projected but not posted; any figure resting on it is pro-forma.
    | ProForma = 1

/// Disposition of a return figure. Only <c>Reported</c> may be shown as the return.
type ReturnFigureStatus =
    | Reported = 0
    /// Rests on unposted activity; must be labeled pro-forma.
    | ProForma = 1
    /// Inputs are incomplete or the convention cannot produce one defensible number.
    | ReviewRequired = 2

/// The calculation convention a figure was produced under. Every figure names one.
type ReturnConvention =
    /// Sub-periods break at every external flow, each valued at the flow date; geometrically linked.
    | TimeWeighted = 0
    /// Time-weighted, but at least one sub-period lacked a valuation at a flow date and used a
    /// day-weighted Modified Dietz approximation for that sub-period. Labeled as an approximation.
    | LinkedModifiedDietz = 1
    /// Single-period day-weighted Modified Dietz.
    | ModifiedDietz = 2
    /// Money-weighted: the internal rate of return of the dated cash-flow series (XIRR, Actual/365).
    | MoneyWeighted = 3

/// End-of-day value of the measured portfolio, fund, class, or investor capital account.
/// The value INCLUDES any external flow dated the same day (end-of-day flow convention).
type ReturnValuationPoint =
    { AsOf: DateOnly
      Value: decimal
      Currency: string
      Basis: ReturnInputBasis }

/// External capital flow into (+) or out of (-) the measured scope. Income, fees, and trading are
/// NOT external flows: they are part of the return. Flows are timed at end of day on <c>Date</c>.
type ReturnExternalFlow =
    { Date: DateOnly
      Amount: decimal
      Currency: string
      Basis: ReturnInputBasis }

/// A labeled return figure. <c>Value</c> is null whenever no single number may be reported.
type ReturnFigure =
    { Convention: ReturnConvention
      Status: ReturnFigureStatus
      Currency: string
      PeriodStart: DateOnly
      PeriodEnd: DateOnly
      /// Return over the period as a fraction (0.023 = 2.3%), not annualized.
      Value: Nullable<decimal>
      /// Annualized rate; populated only for the money-weighted convention.
      AnnualizedValue: Nullable<decimal>
      /// Number of linked sub-periods (time-weighted conventions); 1 otherwise.
      SubPeriodCount: int
      /// Every rate that zeroes the money-weighted NPV within the scanned range.
      CandidateRates: decimal[]
      /// Why the figure carries its status or convention; empty only for a clean exact figure.
      Reasons: string[] }

/// Pure, deterministic return kernels shared by the brokerage, ledger, fund, share-class, and
/// investor lanes (W10-PERF-001). Nothing here performs I/O or reads a clock; callers supply
/// valuation points and external flows already filtered to the measured scope and period.
///
/// Conventions, stated once so every caller inherits them:
/// - Valuations and flows are end-of-day. A flow dated on the first valuation date is already in
///   the opening value and is ignored; flows outside (first, last] are ignored.
/// - Time-weighted sub-period return: (V_end − F) / V_start − 1, exact when all of the sub-period's
///   flows fall on its end date. Otherwise the sub-period uses Modified Dietz with weight
///   (end − flowDate) / (end − start) and the figure is labeled LinkedModifiedDietz.
/// - Money-weighted: XIRR on Actual/365 from the investor's side (opening value and contributions
///   paid in, distributions and closing value received). Every root in [-99%, +10,000%] is found;
///   none or more than one root makes the figure review-required rather than picking one.
/// - Inputs in a currency other than the declared one make the figure review-required. Amounts in
///   different currencies are never summed; translate first with <c>translateValuations</c> /
///   <c>translateFlows</c>, where a missing rate leaves the original currency and so blocks.
/// - Figures round to 10 decimal places, midpoint away from zero.
[<RequireQualifiedAccess>]
module ReturnMeasurement =

    [<Literal>]
    let private ReturnDecimals = 10

    [<Literal>]
    let private DaysPerYear = 365.0

    let private roundReturn (value: decimal) =
        Math.Round(value, ReturnDecimals, MidpointRounding.AwayFromZero)

    let private days (a: DateOnly) (b: DateOnly) = b.DayNumber - a.DayNumber

    let private sameCurrency (declared: string) (candidate: string) =
        String.Equals(declared, candidate, StringComparison.OrdinalIgnoreCase)

    let private figure convention status currency periodStart periodEnd value annualized subPeriods candidates reasons =
        { Convention = convention
          Status = status
          Currency = currency
          PeriodStart = periodStart
          PeriodEnd = periodEnd
          Value = value
          AnnualizedValue = annualized
          SubPeriodCount = subPeriods
          CandidateRates = candidates
          Reasons = reasons }

    /// Shared input validation. Returns ordered points, in-window flows, and blocking reasons.
    let private prepare
        (currency: string)
        (points: ReturnValuationPoint seq)
        (flows: ReturnExternalFlow seq)
        =
        if String.IsNullOrWhiteSpace currency then
            invalidArg (nameof currency) "A declared currency is required."

        let points = points |> Seq.sortBy (fun p -> p.AsOf) |> Seq.toArray
        let blocking = ResizeArray<string>()

        if points.Length < 2 then
            blocking.Add "At least two valuation points are required to measure a return."

        points
        |> Array.countBy (fun p -> p.AsOf)
        |> Array.filter (fun (_, n) -> n > 1)
        |> Array.iter (fun (d, _) -> blocking.Add(sprintf "More than one valuation point is dated %s." (d.ToString "yyyy-MM-dd")))

        let first, last =
            if points.Length = 0 then DateOnly.MinValue, DateOnly.MinValue
            else points[0].AsOf, points[points.Length - 1].AsOf

        let flows =
            flows
            |> Seq.filter (fun f -> f.Date > first && f.Date <= last)
            |> Seq.sortBy (fun f -> f.Date)
            |> Seq.toArray

        for p in points do
            if not (sameCurrency currency p.Currency) then
                blocking.Add(
                    sprintf "Valuation on %s is in %s, not the declared %s; translate at the as-of rate or report per currency."
                        (p.AsOf.ToString "yyyy-MM-dd") p.Currency currency)

        for f in flows do
            if not (sameCurrency currency f.Currency) then
                blocking.Add(
                    sprintf "External flow on %s is in %s, not the declared %s; translate at the flow's as-of rate or report per currency."
                        (f.Date.ToString "yyyy-MM-dd") f.Currency currency)

        let proForma =
            points |> Array.exists (fun p -> p.Basis = ReturnInputBasis.ProForma)
            || flows |> Array.exists (fun f -> f.Basis = ReturnInputBasis.ProForma)

        points, flows, blocking, proForma, first, last

    /// Modified Dietz over one interval; None when the weighted capital base is not positive.
    let private modifiedDietz (startValue: decimal) (endValue: decimal) (startDate: DateOnly) (endDate: DateOnly) (flows: ReturnExternalFlow[]) =
        let length = decimal (days startDate endDate)
        let net = flows |> Array.sumBy (fun f -> f.Amount)
        let weighted = flows |> Array.sumBy (fun f -> f.Amount * decimal (days f.Date endDate) / length)
        let basis = startValue + weighted
        if basis <= 0m then None else Some((endValue - startValue - net) / basis)

    /// <summary>
    /// Time-weighted return. Breaks a sub-period at every valuation point and links geometrically.
    /// Exact when every external flow date carries a valuation point; otherwise each affected
    /// sub-period falls back to day-weighted Modified Dietz and the figure is labeled
    /// <c>LinkedModifiedDietz</c> with the approximated dates named.
    /// </summary>
    let timeWeighted
        (currency: string)
        (points: ReturnValuationPoint seq)
        (flows: ReturnExternalFlow seq)
        : ReturnFigure =
        let points, flows, blocking, proForma, first, last = prepare currency points flows
        let subPeriods = max 0 (points.Length - 1)

        let blocked convention (reasons: string seq) =
            figure convention ReturnFigureStatus.ReviewRequired currency first last (Nullable()) (Nullable()) subPeriods [||] (Seq.toArray reasons)

        if blocking.Count > 0 then
            blocked ReturnConvention.TimeWeighted blocking
        else
            let mutable growth = 1m
            let approximated = ResizeArray<string>()
            let reasons = ResizeArray<string>()

            for i in 1 .. points.Length - 1 do
                let a, b = points[i - 1], points[i]
                let inPeriod = flows |> Array.filter (fun f -> f.Date > a.AsOf && f.Date <= b.AsOf)
                let exact = inPeriod |> Array.forall (fun f -> f.Date = b.AsOf)

                if exact then
                    if a.Value <= 0m then
                        reasons.Add(sprintf "Opening value on %s is not positive; the sub-period return is undefined." (a.AsOf.ToString "yyyy-MM-dd"))
                    else
                        let net = inPeriod |> Array.sumBy (fun f -> f.Amount)
                        growth <- growth * ((b.Value - net) / a.Value)
                else
                    for f in inPeriod do
                        if f.Date <> b.AsOf then approximated.Add(f.Date.ToString "yyyy-MM-dd")

                    match modifiedDietz a.Value b.Value a.AsOf b.AsOf inPeriod with
                    | Some r -> growth <- growth * (1m + r)
                    | None ->
                        reasons.Add(
                            sprintf "Weighted capital base between %s and %s is not positive; the sub-period return is undefined."
                                (a.AsOf.ToString "yyyy-MM-dd") (b.AsOf.ToString "yyyy-MM-dd"))

            let convention =
                if approximated.Count = 0 then ReturnConvention.TimeWeighted else ReturnConvention.LinkedModifiedDietz

            if reasons.Count > 0 then
                blocked convention reasons
            else
                if approximated.Count > 0 then
                    reasons.Add(
                        sprintf "Approximation: no valuation on external-flow date(s) %s; those sub-periods use day-weighted Modified Dietz."
                            (String.Join(", ", approximated |> Seq.distinct)))

                let status =
                    if proForma then
                        reasons.Add "Pro-forma: rests on activity the ledger has not posted."
                        ReturnFigureStatus.ProForma
                    else
                        ReturnFigureStatus.Reported

                figure convention status currency first last (Nullable(roundReturn (growth - 1m))) (Nullable()) subPeriods [||] (reasons.ToArray())

    /// <summary>
    /// Single-period day-weighted Modified Dietz between the first and last valuation points,
    /// ignoring intermediate valuations. Always labeled as the ModifiedDietz convention.
    /// </summary>
    let modifiedDietzReturn
        (currency: string)
        (points: ReturnValuationPoint seq)
        (flows: ReturnExternalFlow seq)
        : ReturnFigure =
        let points, flows, blocking, proForma, first, last = prepare currency points flows

        let blocked (reasons: string seq) =
            figure ReturnConvention.ModifiedDietz ReturnFigureStatus.ReviewRequired currency first last (Nullable()) (Nullable()) 1 [||] (Seq.toArray reasons)

        if blocking.Count > 0 then
            blocked blocking
        else
            let a, b = points[0], points[points.Length - 1]
            match modifiedDietz a.Value b.Value a.AsOf b.AsOf flows with
            | None -> blocked [ "Weighted capital base is not positive; Modified Dietz is undefined for this period." ]
            | Some r ->
                let reasons = ResizeArray<string>()
                let status =
                    if proForma then
                        reasons.Add "Pro-forma: rests on activity the ledger has not posted."
                        ReturnFigureStatus.ProForma
                    else
                        ReturnFigureStatus.Reported
                figure ReturnConvention.ModifiedDietz status currency first last (Nullable(roundReturn r)) (Nullable()) 1 [||] (reasons.ToArray())

    // --- Money-weighted ----------------------------------------------------------------------

    [<Literal>]
    let private MinRate = -0.99

    [<Literal>]
    let private MaxRate = 100.0

    let private npv (series: (float * float)[]) (rate: float) =
        series |> Array.sumBy (fun (years, amount) -> amount / Math.Pow(1.0 + rate, years))

    let private bisect (f: float -> float) (lo: float) (hi: float) =
        let mutable lo, hi = lo, hi
        let mutable fLo = f lo
        for _ in 1 .. 200 do
            let mid = (lo + hi) / 2.0
            let fMid = f mid
            if fLo * fMid <= 0.0 then hi <- mid
            else
                lo <- mid
                fLo <- fMid
        (lo + hi) / 2.0

    /// Every sign change of NPV across a dense grid on (MinRate, MaxRate], refined by bisection.
    let private findRoots (series: (float * float)[]) =
        // Dense near zero where real fund returns live, geometric beyond.
        let grid =
            seq {
                yield! seq { for i in 0 .. 1990 -> MinRate + float i * 0.001 }   // -0.99 .. 1.0
                let mutable r = 1.0
                while r < MaxRate do
                    r <- r * 1.01
                    yield min r MaxRate
            }
            |> Seq.distinct
            |> Seq.toArray

        let f = npv series
        let roots = ResizeArray<float>()
        let mutable prevRate = grid[0]
        let mutable prevValue = f prevRate
        if prevValue = 0.0 then roots.Add prevRate

        for i in 1 .. grid.Length - 1 do
            let rate = grid[i]
            let value = f rate
            if value = 0.0 then roots.Add rate
            elif prevValue <> 0.0 && Math.Sign value <> Math.Sign prevValue then
                roots.Add(bisect f prevRate rate)
            prevRate <- rate
            prevValue <- value

        roots.ToArray()

    /// <summary>
    /// Money-weighted return (XIRR, Actual/365) over the period. The investor-side series is the
    /// opening value paid in, each contribution paid in, each distribution received, and the
    /// closing value received. When the NPV has no root, or more than one root, in the scanned
    /// range the figure is review-required and the candidate rates are returned rather than one
    /// being chosen.
    /// </summary>
    let moneyWeighted
        (currency: string)
        (points: ReturnValuationPoint seq)
        (flows: ReturnExternalFlow seq)
        : ReturnFigure =
        let points, flows, blocking, proForma, first, last = prepare currency points flows

        let result status value annualized candidates (reasons: string seq) =
            figure ReturnConvention.MoneyWeighted status currency first last value annualized 1 candidates (Seq.toArray reasons)

        if blocking.Count > 0 then
            result ReturnFigureStatus.ReviewRequired (Nullable()) (Nullable()) [||] blocking
        elif days first last = 0 then
            result ReturnFigureStatus.ReviewRequired (Nullable()) (Nullable()) [||] [ "The period has zero length." ]
        else
            let opening, closing = points[0], points[points.Length - 1]
            let years (d: DateOnly) = float (days first d) / DaysPerYear

            let series =
                [| yield 0.0, -(float opening.Value)
                   for f in flows -> years f.Date, -(float f.Amount)
                   yield years closing.AsOf, float closing.Value |]

            // Adjacent grid brackets can converge on the same root; collapse roots within 1e-7.
            let roots =
                findRoots series
                |> Array.fold (fun (acc: float list) r ->
                    match acc with
                    | prev :: _ when abs (r - prev) < 1e-7 -> acc
                    | _ -> r :: acc) []
                |> List.rev
                |> List.map (fun r -> roundReturn (decimal r))
                |> List.toArray
            let periodYears = years closing.AsOf
            let toPeriod (rate: decimal) = roundReturn (decimal (Math.Pow(1.0 + float rate, periodYears) - 1.0))

            match roots with
            | [| rate |] ->
                let reasons = ResizeArray<string>()
                let signChanges =
                    series |> Array.map snd |> Array.filter (fun v -> v <> 0.0)
                    |> Array.pairwise |> Array.filter (fun (a, b) -> Math.Sign a <> Math.Sign b) |> Array.length
                if signChanges > 1 then
                    reasons.Add(
                        sprintf "Non-conventional cash-flow series (%d sign changes); one root found in [%.0f%%, %.0f%%]."
                            signChanges (MinRate * 100.0) (MaxRate * 100.0))
                let status =
                    if proForma then
                        reasons.Add "Pro-forma: rests on activity the ledger has not posted."
                        ReturnFigureStatus.ProForma
                    else
                        ReturnFigureStatus.Reported
                result status (Nullable(toPeriod rate)) (Nullable rate) [| rate |] reasons
            | [||] ->
                result ReturnFigureStatus.ReviewRequired (Nullable()) (Nullable()) [||]
                    [ sprintf "The money-weighted return did not converge: no rate in [%.0f%%, %.0f%%] zeroes the cash-flow series." (MinRate * 100.0) (MaxRate * 100.0) ]
            | many ->
                result ReturnFigureStatus.ReviewRequired (Nullable()) (Nullable()) many
                    [ sprintf "The cash-flow series admits %d money-weighted rates; no single rate is reported." many.Length ]

    // --- Currency translation ----------------------------------------------------------------

    /// <summary>
    /// Translates valuations into <paramref name="presentationCurrency"/> at each point's as-of
    /// rate. <paramref name="rate"/> returns units of presentation currency per unit of the source
    /// currency on a date, or null when no rate exists. An untranslatable point keeps its original
    /// currency, so any return measured from the output is review-required rather than wrong.
    /// </summary>
    let translateValuations
        (presentationCurrency: string)
        (rate: Func<string, DateOnly, Nullable<decimal>>)
        (points: ReturnValuationPoint seq)
        : ReturnValuationPoint[] =
        points
        |> Seq.map (fun p ->
            if sameCurrency presentationCurrency p.Currency then p
            else
                let r = rate.Invoke(p.Currency, p.AsOf)
                if r.HasValue then { p with Value = p.Value * r.Value; Currency = presentationCurrency } else p)
        |> Seq.toArray

    /// <summary>Translates external flows at each flow's as-of rate; see <c>translateValuations</c>.</summary>
    let translateFlows
        (presentationCurrency: string)
        (rate: Func<string, DateOnly, Nullable<decimal>>)
        (flows: ReturnExternalFlow seq)
        : ReturnExternalFlow[] =
        flows
        |> Seq.map (fun f ->
            if sameCurrency presentationCurrency f.Currency then f
            else
                let r = rate.Invoke(f.Currency, f.Date)
                if r.HasValue then { f with Amount = f.Amount * r.Value; Currency = presentationCurrency } else f)
        |> Seq.toArray
