using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Inputs for one portfolio cash ladder computation. Positions carry the
/// instrument-level asset-operations projections produced by
/// <see cref="AssetObligationProjectionService"/> (or published projections);
/// cash balances and capital activity are joined from Ledger-side providers.
/// </summary>
public sealed record PortfolioCashLadderInputs(
    DateOnly AsOfDate,
    int HorizonDays,
    string BaseCurrency,
    IReadOnlyList<PortfolioCashLadderPositionDto> Positions,
    IReadOnlyList<PortfolioCashBalanceDto> CashBalances,
    IReadOnlyList<PortfolioCapitalActivityDto> CapitalActivity,
    decimal MinimumCashThreshold,
    int BucketDays = 7)
{
    /// <summary>
    /// Provenance notices about how <see cref="Positions"/> were sourced (e.g. a
    /// warning that they are unit-quantity placeholders because no holdings source
    /// was wired). Surfaced in the ladder's warnings so overstated liquidity is
    /// visible rather than silent.
    /// </summary>
    public IReadOnlyList<string> PositionSourceNotices { get; init; } = [];
}

/// <summary>
/// Aggregates per-instrument projected cash flows across every held position,
/// joins opening cash and dated capital activity, and produces the
/// portfolio-wide dated cash ladder with an optional stress scenario applied.
/// Every instrument contribution stays traceable to the projection run and
/// Security Master terms version that produced it, and every scenario states
/// what it models versus what it assumes.
/// </summary>
public static class PortfolioCashLadderEngine
{
    public const string EngineVersion = "portfolio-cash-ladder-v1";

    public const string BaseScenarioId = "base";
    public const string RatesUpScenarioId = "rates-up-100bp";
    public const string RatesDownScenarioId = "rates-down-100bp";
    public const string EarlyCallScenarioId = "early-call";
    public const string RedemptionWaveScenarioId = "redemption-wave-20";
    public const string FxAdverseScenarioId = "fx-adverse-10";

    private const decimal RateShiftPerHundredBps = 0.01m;
    private const decimal RedemptionWaveFraction = 0.20m;
    private const int RedemptionWaveNoticeDays = 30;
    private const decimal FxAdverseShockFraction = 0.10m;

    private const string CouponLane = "Coupons & interest";
    private const string PrincipalLane = "Maturities & principal";
    private const string DividendLane = "Dividends & distributions";
    private const string FeeLane = "Fees";
    private const string CapitalLane = "Capital activity";
    private const string ExpenseLane = "Expense accruals";
    private const string OtherLane = "Other";

    public static IReadOnlyList<PortfolioCashScenarioDto> ScenarioCatalog { get; } =
    [
        new(
            BaseScenarioId,
            "Base projection",
            "Contractual flows from retained Security Master terms with no stress applied.",
            ["Instrument flows exactly as projected from retained terms.", "Capital activity exactly as scheduled."],
            ["Non-base-currency amounts are aggregated 1:1 into the base currency (no FX conversion)."]),
        new(
            RatesUpScenarioId,
            "Rates +100bp",
            "Parallel 100bp upward shift applied to retained floating-rate flows only.",
            ["Floating-rate coupon and interest flows reprice by scaling to the shifted rate."],
            [
                "Fixed-coupon flows are contractual and remain unchanged.",
                "Flows are treated as floating only when the retained terms or flow payload identify a floating rate.",
                "No reinvestment, prepayment-speed, or valuation effects are modeled."
            ]),
        new(
            RatesDownScenarioId,
            "Rates -100bp",
            "Parallel 100bp downward shift applied to retained floating-rate flows only.",
            ["Floating-rate coupon and interest flows reprice by scaling to the shifted rate (floored at 0%)."],
            [
                "Fixed-coupon flows are contractual and remain unchanged.",
                "Flows are treated as floating only when the retained terms or flow payload identify a floating rate.",
                "No reinvestment, prepayment-speed, or valuation effects are modeled."
            ]),
        new(
            EarlyCallScenarioId,
            "Early call",
            "Every security with a retained call date inside the horizon is assumed called on that date.",
            [
                "Coupons due after the call date are removed.",
                "Principal otherwise returned after the call date is pulled forward to the call date."
            ],
            [
                "100% exercise at the first retained call date, at par, with no call premium.",
                "Securities without a retained call date are unchanged."
            ]),
        new(
            RedemptionWaveScenarioId,
            "Redemption wave 20%",
            "Investor redemption outflow of 20% of opening cash, settling after a 30-day notice period.",
            ["A single capital outflow is added at as-of + 30 days sized from opening cash."],
            [
                "Redemption is proxied on opening cash, not NAV; NAV-based sizing is not modeled.",
                "Gating, side pockets, and staggered settlement are not modeled."
            ]),
        new(
            FxAdverseScenarioId,
            "FX adverse 10%",
            "Worst-case parallel 10% FX move applied to every non-base-currency flow.",
            ["Non-base-currency inflows are reduced 10%; non-base-currency outflows are increased 10%."],
            [
                "A single adverse parallel move is applied; correlations and hedges are not modeled.",
                "Base-currency flows are unchanged."
            ])
    ];

    public static PortfolioCashLadderDto Build(PortfolioCashLadderInputs inputs, string? scenarioId = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var horizonDays = Math.Max(1, inputs.HorizonDays);
        var bucketDays = Math.Max(1, inputs.BucketDays);
        var windowStart = inputs.AsOfDate;
        var windowEnd = inputs.AsOfDate.AddDays(horizonDays);
        var warnings = new List<string>();
        var assumptions = new List<string>();

        var scenario = ResolveScenario(scenarioId, warnings);
        warnings.AddRange(inputs.PositionSourceNotices);
        var openingCash = inputs.CashBalances.Sum(static balance => balance.Amount);

        var contributions = new List<PortfolioCashLadderContributionDto>();
        var securitiesWithFlows = 0;
        foreach (var position in inputs.Positions)
        {
            var rows = BuildPositionContributions(position, windowStart, windowEnd);
            if (rows.Count > 0)
            {
                securitiesWithFlows++;
            }

            contributions.AddRange(rows);
        }

        contributions.AddRange(BuildCapitalContributions(inputs.CapitalActivity, windowStart, windowEnd, warnings));

        contributions = ApplyScenario(scenario.ScenarioId, contributions, inputs, openingCash, windowStart, windowEnd, warnings);

        var foreignCurrencies = contributions
            .Where(row => !string.Equals(row.Currency, inputs.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(static row => row.Currency)
            .Where(static currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static currency => currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (foreignCurrencies.Length > 0)
        {
            warnings.Add(
                $"Flows in {string.Join(", ", foreignCurrencies)} are aggregated 1:1 into {inputs.BaseCurrency}; no FX conversion is applied.");
        }

        var foreignCashCurrencies = inputs.CashBalances
            .Where(balance => !string.Equals(balance.Currency, inputs.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(static balance => balance.Currency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (foreignCashCurrencies.Length > 0)
        {
            warnings.Add(
                $"Cash balances in {string.Join(", ", foreignCashCurrencies)} are aggregated 1:1 into {inputs.BaseCurrency}.");
        }

        if (inputs.CashBalances.Count == 0)
        {
            warnings.Add("No cash balances were provided; the cumulative line starts from zero.");
        }

        if (inputs.CapitalActivity.Count == 0)
        {
            assumptions.Add("No investor capital schedule rows were provided; capital activity is absent from the ladder.");
        }

        assumptions.Add(
            "Instrument flows reflect the retained terms basis scaled by held quantity; positions default to one unit of the terms basis when no holdings quantity is supplied.");
        assumptions.Add(
            "Each instrument contribution is traceable to its projection run and Security Master terms version.");

        var buckets = BuildBuckets(
            contributions,
            windowStart,
            windowEnd,
            bucketDays,
            openingCash,
            inputs.MinimumCashThreshold);

        var securitiesWithoutFlows = inputs.Positions.Count - securitiesWithFlows;
        if (securitiesWithoutFlows > 0)
        {
            warnings.Add(
                $"{securitiesWithoutFlows} of {inputs.Positions.Count} evaluated positions contributed no projected flows inside the horizon (no retained schedule, or flows outside the window).");
        }

        return new PortfolioCashLadderDto(
            EngineVersion,
            inputs.AsOfDate,
            horizonDays,
            inputs.BaseCurrency,
            scenario.ScenarioId,
            RoundCash(openingCash),
            inputs.MinimumCashThreshold,
            buckets,
            contributions
                .OrderBy(static row => row.DueDate)
                .ThenBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.FlowType, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ScenarioCatalog,
            assumptions,
            warnings,
            DateTimeOffset.UtcNow,
            inputs.Positions.Count,
            securitiesWithFlows);
    }

    private static PortfolioCashScenarioDto ResolveScenario(string? scenarioId, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return ScenarioCatalog[0];
        }

        var match = ScenarioCatalog.FirstOrDefault(scenario =>
            string.Equals(scenario.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            warnings.Add($"Unknown scenario '{scenarioId}'; the base projection was used instead.");
            return ScenarioCatalog[0];
        }

        return match;
    }

    private static List<PortfolioCashLadderContributionDto> BuildPositionContributions(
        PortfolioCashLadderPositionDto position,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var operations = position.Operations;
        // Preserve the signed held quantity: a negative (short) position inverts the
        // cash-flow direction, and an explicit zero contributes nothing. The DTO default
        // of 1 already covers an omitted quantity, so no coercion happens here.
        var quantity = position.Quantity;
        if (quantity == 0m)
        {
            return [];
        }

        var latestRun = SelectProjectionRun(operations);
        var flows = LatestRunFlows(operations);
        var latestTerms = operations.TermsHistory
            .OrderByDescending(static row => row.EffectiveDate)
            .ThenByDescending(static row => row.RecordedAt)
            .FirstOrDefault();

        var rows = new List<PortfolioCashLadderContributionDto>();
        foreach (var flow in flows)
        {
            if (flow.DueDate < windowStart || flow.DueDate >= windowEnd)
            {
                continue;
            }

            var lane = ResolveInstrumentLane(flow.FlowType);
            var direction = ResolveInstrumentDirection(flow.FlowType);
            var amount = RoundCash(flow.Amount * quantity * direction);
            rows.Add(new PortfolioCashLadderContributionDto(
                operations.Subject.SecurityId,
                operations.Subject.DisplayName,
                operations.Subject.AssetClass,
                flow.ProjectionRunId,
                latestRun?.EngineVersion,
                latestTerms?.TermsVersionId,
                latestTerms?.TermsHash,
                flow.SourceDomain ?? "AssetOperations",
                flow.SourceEntityId,
                flow.FlowType,
                lane,
                flow.DueDate,
                amount,
                flow.Currency,
                amount,
                ScenarioAdjustment: null));
        }

        return rows;
    }

    private static IEnumerable<PortfolioCashLadderContributionDto> BuildCapitalContributions(
        IReadOnlyList<PortfolioCapitalActivityDto> capitalActivity,
        DateOnly windowStart,
        DateOnly windowEnd,
        List<string> warnings)
    {
        foreach (var activity in capitalActivity)
        {
            if (activity.DueDate < windowStart || activity.DueDate >= windowEnd)
            {
                continue;
            }

            var direction = ResolveCapitalDirection(activity.ActivityKind);
            if (direction is null)
            {
                warnings.Add(
                    $"Capital activity kind '{activity.ActivityKind}' ({activity.Summary}) is not recognized and was excluded from the ladder.");
                continue;
            }

            var amount = RoundCash(Math.Abs(activity.Amount) * direction.Value);
            yield return new PortfolioCashLadderContributionDto(
                Guid.Empty,
                activity.Summary,
                "CapitalActivity",
                ProjectionRunId: null,
                ProjectionEngineVersion: null,
                TermsVersionId: null,
                TermsHash: null,
                activity.SourceDomain,
                activity.SourceEntityId,
                activity.ActivityKind,
                ResolveCapitalLane(activity.ActivityKind),
                activity.DueDate,
                amount,
                activity.Currency,
                amount,
                ScenarioAdjustment: null);
        }
    }

    private static List<PortfolioCashLadderContributionDto> ApplyScenario(
        string scenarioId,
        List<PortfolioCashLadderContributionDto> contributions,
        PortfolioCashLadderInputs inputs,
        decimal openingCash,
        DateOnly windowStart,
        DateOnly windowEnd,
        List<string> warnings)
        => scenarioId switch
        {
            RatesUpScenarioId => ApplyRateShift(contributions, inputs, RateShiftPerHundredBps),
            RatesDownScenarioId => ApplyRateShift(contributions, inputs, -RateShiftPerHundredBps),
            EarlyCallScenarioId => ApplyEarlyCall(contributions, inputs, windowStart, windowEnd),
            RedemptionWaveScenarioId => ApplyRedemptionWave(contributions, inputs, openingCash, windowStart, windowEnd, warnings),
            FxAdverseScenarioId => ApplyFxAdverse(contributions, inputs),
            _ => contributions
        };

    private static List<PortfolioCashLadderContributionDto> ApplyRateShift(
        List<PortfolioCashLadderContributionDto> contributions,
        PortfolioCashLadderInputs inputs,
        decimal rateShift)
    {
        var floatingSecurityIds = inputs.Positions
            .Where(static position => LatestTermsIndicateFloatingRate(position.Operations))
            .Select(static position => position.Operations.Subject.SecurityId)
            .ToHashSet();
        var flowRates = BuildFlowRateIndex(inputs.Positions);

        var result = new List<PortfolioCashLadderContributionDto>(contributions.Count);
        foreach (var row in contributions)
        {
            var isRateLane = row.SourceLane == CouponLane;
            if (!isRateLane || !floatingSecurityIds.Contains(row.SecurityId))
            {
                result.Add(row);
                continue;
            }

            if (!flowRates.TryGetValue((row.SecurityId, row.DueDate, row.FlowType), out var annualRate) ||
                annualRate <= 0m)
            {
                result.Add(row);
                continue;
            }

            var shiftedRate = Math.Max(0m, annualRate + rateShift);
            var adjusted = RoundCash(row.BaseAmount * (shiftedRate / annualRate));
            result.Add(row with
            {
                Amount = adjusted,
                ScenarioAdjustment =
                    $"Floating-rate flow repriced from {annualRate:P2} to {shiftedRate:P2} (parallel {rateShift * 100:+0.00;-0.00}% shift)."
            });
        }

        return result;
    }

    private static List<PortfolioCashLadderContributionDto> ApplyEarlyCall(
        List<PortfolioCashLadderContributionDto> contributions,
        PortfolioCashLadderInputs inputs,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var callDateBySecurity = new Dictionary<Guid, DateOnly>();
        foreach (var position in inputs.Positions)
        {
            var callDate = ReadLatestTermsDate(position.Operations, "callDate", "mandatoryPutDate", "preRefundDate");
            if (callDate is { } date && date >= windowStart && date < windowEnd)
            {
                callDateBySecurity[position.Operations.Subject.SecurityId] = date;
            }
        }

        if (callDateBySecurity.Count == 0)
        {
            return contributions;
        }

        // Drop the in-window coupon/principal rows dated after the call; those obligations
        // no longer occur once the security is redeemed on the call date.
        var result = contributions
            .Where(row => !(callDateBySecurity.TryGetValue(row.SecurityId, out var callDate) && row.DueDate > callDate))
            .ToList();

        // Principal returned on the call date is the outstanding principal — every principal-lane
        // flow due strictly after the call date, taken from the full projection rather than only the
        // in-window rows, so a security whose maturity is beyond the horizon still returns principal
        // at the call date. Quantity keeps its sign so short positions net correctly. Multiple
        // positions (e.g. account-level lots) for the same security are accumulated, not overwritten.
        var pulledPrincipalBySecurity = new Dictionary<Guid, decimal>();
        var currencyBySecurity = new Dictionary<Guid, string>();
        var representativeOperations = new Dictionary<Guid, AssetOperationsDetailDto>();
        foreach (var position in inputs.Positions)
        {
            var securityId = position.Operations.Subject.SecurityId;
            if (!callDateBySecurity.TryGetValue(securityId, out var callDate) || position.Quantity == 0m)
            {
                continue;
            }

            representativeOperations.TryAdd(securityId, position.Operations);
            foreach (var flow in LatestRunFlows(position.Operations))
            {
                if (flow.DueDate <= callDate || ResolveInstrumentLane(flow.FlowType) != PrincipalLane)
                {
                    continue;
                }

                currencyBySecurity[securityId] = flow.Currency;
                pulledPrincipalBySecurity[securityId] =
                    pulledPrincipalBySecurity.GetValueOrDefault(securityId)
                    + RoundCash(flow.Amount * position.Quantity * ResolveInstrumentDirection(flow.FlowType));
            }
        }

        foreach (var (securityId, pulledPrincipal) in pulledPrincipalBySecurity.OrderBy(static pair => pair.Key))
        {
            if (pulledPrincipal == 0m || !representativeOperations.TryGetValue(securityId, out var operations))
            {
                continue;
            }

            var amount = RoundCash(pulledPrincipal);
            var currency = currencyBySecurity.GetValueOrDefault(securityId, inputs.BaseCurrency);
            result.Add(BuildCallRedemptionRow(operations, callDateBySecurity[securityId], amount, currency));
        }

        return result;
    }

    private static PortfolioCashLadderContributionDto BuildCallRedemptionRow(
        AssetOperationsDetailDto operations,
        DateOnly callDate,
        decimal amount,
        string currency)
    {
        var latestRun = SelectProjectionRun(operations);
        var latestTerms = operations.TermsHistory
            .OrderByDescending(static row => row.EffectiveDate)
            .ThenByDescending(static row => row.RecordedAt)
            .FirstOrDefault();
        return new PortfolioCashLadderContributionDto(
            operations.Subject.SecurityId,
            operations.Subject.DisplayName,
            operations.Subject.AssetClass,
            latestRun?.ProjectionRunId,
            latestRun?.EngineVersion,
            latestTerms?.TermsVersionId,
            latestTerms?.TermsHash,
            latestRun?.SourceDomain ?? "AssetOperations",
            latestRun?.SourceEntityId,
            "CallRedemption",
            PrincipalLane,
            callDate,
            amount,
            currency,
            amount,
            $"Assumed called on retained call date {callDate:yyyy-MM-dd}: principal otherwise due later is returned at par and subsequent coupons are removed.");
    }

    private static IReadOnlyList<AssetProjectedCashFlowDto> LatestRunFlows(AssetOperationsDetailDto operations)
    {
        var run = SelectProjectionRun(operations);
        return run is null
            ? operations.ProjectedCashFlows
            : operations.ProjectedCashFlows
                .Where(flow => flow.ProjectionRunId == run.ProjectionRunId)
                .ToArray();
    }

    /// <summary>
    /// Chooses the projection run whose flows drive the ladder: the latest completed run, so a
    /// failed, cancelled, or still-running run recorded after a good one cannot erase a security's
    /// prior completed schedule. Falls back to the latest run of any status only when no completed
    /// run exists (e.g. sources that do not stamp a status).
    /// </summary>
    private static AssetCashFlowProjectionRunDto? SelectProjectionRun(AssetOperationsDetailDto operations)
        => operations.CashFlowProjectionRuns
               .Where(static run => string.Equals(run.Status, "Completed", StringComparison.OrdinalIgnoreCase))
               .MaxBy(static run => run.GeneratedAt)
           ?? operations.CashFlowProjectionRuns.MaxBy(static run => run.GeneratedAt);

    private static List<PortfolioCashLadderContributionDto> ApplyRedemptionWave(
        List<PortfolioCashLadderContributionDto> contributions,
        PortfolioCashLadderInputs inputs,
        decimal openingCash,
        DateOnly windowStart,
        DateOnly windowEnd,
        List<string> warnings)
    {
        if (openingCash <= 0m)
        {
            return contributions;
        }

        // Settlement is after a 30-day notice period. If the requested horizon is shorter than the
        // notice, the redemption settles beyond the ladder and must not be clamped into the last
        // in-window day — doing so would fabricate a liquidity breach the scenario never implies.
        var settlementDate = windowStart.AddDays(RedemptionWaveNoticeDays);
        if (settlementDate >= windowEnd)
        {
            warnings.Add(
                $"Redemption wave settles after the {RedemptionWaveNoticeDays}-day notice period, beyond the {(windowEnd.DayNumber - windowStart.DayNumber)}-day horizon; no outflow is shown in this view.");
            return contributions;
        }

        var amount = RoundCash(-openingCash * RedemptionWaveFraction);
        var result = new List<PortfolioCashLadderContributionDto>(contributions)
        {
            new(
                Guid.Empty,
                "Assumed investor redemption wave",
                "CapitalActivity",
                ProjectionRunId: null,
                ProjectionEngineVersion: null,
                TermsVersionId: null,
                TermsHash: null,
                "Scenario",
                RedemptionWaveScenarioId,
                "Redemption",
                CapitalLane,
                settlementDate,
                amount,
                inputs.BaseCurrency,
                amount,
                $"Scenario-generated outflow: {RedemptionWaveFraction:P0} of opening cash after a {RedemptionWaveNoticeDays}-day notice period (opening-cash proxy, not NAV-based).")
        };
        return result;
    }

    private static List<PortfolioCashLadderContributionDto> ApplyFxAdverse(
        List<PortfolioCashLadderContributionDto> contributions,
        PortfolioCashLadderInputs inputs)
    {
        var result = new List<PortfolioCashLadderContributionDto>(contributions.Count);
        foreach (var row in contributions)
        {
            if (string.Equals(row.Currency, inputs.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(row);
                continue;
            }

            var factor = row.BaseAmount >= 0m ? 1m - FxAdverseShockFraction : 1m + FxAdverseShockFraction;
            var adjusted = RoundCash(row.BaseAmount * factor);
            result.Add(row with
            {
                Amount = adjusted,
                ScenarioAdjustment =
                    $"Adverse 10% FX move applied to {row.Currency} flow ({(row.BaseAmount >= 0m ? "inflow reduced" : "outflow increased")})."
            });
        }

        return result;
    }

    private static IReadOnlyList<PortfolioCashLadderBucketDto> BuildBuckets(
        IReadOnlyList<PortfolioCashLadderContributionDto> contributions,
        DateOnly windowStart,
        DateOnly windowEnd,
        int bucketDays,
        decimal openingCash,
        decimal minimumCashThreshold)
    {
        var buckets = new List<PortfolioCashLadderBucketDto>();
        var cumulative = openingCash;
        for (var bucketStart = windowStart; bucketStart < windowEnd; bucketStart = bucketStart.AddDays(bucketDays))
        {
            var bucketEndExclusive = bucketStart.AddDays(bucketDays);
            if (bucketEndExclusive > windowEnd)
            {
                bucketEndExclusive = windowEnd;
            }

            var bucketEnd = bucketEndExclusive.AddDays(-1);
            var rows = contributions
                .Where(row => row.DueDate >= bucketStart && row.DueDate < bucketEndExclusive)
                .ToArray();
            var inflows = RoundCash(rows.Where(static row => row.Amount > 0m).Sum(static row => row.Amount));
            var outflows = RoundCash(rows.Where(static row => row.Amount < 0m).Sum(static row => row.Amount));
            var net = RoundCash(inflows + outflows);

            // Track the running balance by due date within the bucket so a large intra-bucket
            // outflow that is later offset by an inflow still registers a breach; netting the whole
            // bucket first would hide the trough (e.g. a day-1 redemption covered by a day-6 coupon).
            var intraBucketMinimum = cumulative;
            var runningBalance = cumulative;
            foreach (var row in rows.OrderBy(static row => row.DueDate))
            {
                runningBalance = RoundCash(runningBalance + row.Amount);
                if (runningBalance < intraBucketMinimum)
                {
                    intraBucketMinimum = runningBalance;
                }
            }

            cumulative = RoundCash(cumulative + net);

            var sources = rows
                .GroupBy(static row => row.SourceLane, StringComparer.Ordinal)
                .Select(static group => new PortfolioCashLadderSourceSliceDto(
                    group.Key,
                    RoundCash(group.Sum(static row => row.Amount)),
                    group.Count()))
                .OrderBy(static slice => slice.Source, StringComparer.Ordinal)
                .ToArray();

            buckets.Add(new PortfolioCashLadderBucketDto(
                bucketStart,
                bucketEnd,
                FormatBucketLabel(bucketStart, bucketEnd),
                inflows,
                outflows,
                net,
                cumulative,
                intraBucketMinimum < minimumCashThreshold,
                sources));
        }

        return buckets;
    }

    private static string FormatBucketLabel(DateOnly bucketStart, DateOnly bucketEnd)
        => bucketStart == bucketEnd
            ? bucketStart.ToString("MMM d", CultureInfo.InvariantCulture)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{bucketStart:MMM d} – {bucketEnd:MMM d}");

    private static string ResolveInstrumentLane(string flowType)
    {
        if (flowType.Contains("Coupon", StringComparison.OrdinalIgnoreCase) ||
            flowType.Contains("Interest", StringComparison.OrdinalIgnoreCase))
        {
            return CouponLane;
        }

        if (flowType.Contains("Principal", StringComparison.OrdinalIgnoreCase) ||
            flowType.Contains("Maturity", StringComparison.OrdinalIgnoreCase) ||
            flowType.Contains("Redemption", StringComparison.OrdinalIgnoreCase))
        {
            return PrincipalLane;
        }

        if (flowType.Contains("Dividend", StringComparison.OrdinalIgnoreCase) ||
            flowType.Contains("Distribution", StringComparison.OrdinalIgnoreCase))
        {
            return DividendLane;
        }

        if (flowType.Contains("Fee", StringComparison.OrdinalIgnoreCase))
        {
            return FeeLane;
        }

        if (flowType.Contains("Expense", StringComparison.OrdinalIgnoreCase))
        {
            return ExpenseLane;
        }

        return OtherLane;
    }

    private static int ResolveInstrumentDirection(string flowType)
        => flowType.Contains("Expense", StringComparison.OrdinalIgnoreCase) ||
           flowType.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase)
            ? -1
            : 1;

    private static int? ResolveCapitalDirection(string activityKind)
    {
        if (activityKind.Contains("Contribution", StringComparison.OrdinalIgnoreCase) ||
            activityKind.Contains("Subscription", StringComparison.OrdinalIgnoreCase) ||
            activityKind.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (activityKind.Contains("Redemption", StringComparison.OrdinalIgnoreCase) ||
            activityKind.Contains("Withdrawal", StringComparison.OrdinalIgnoreCase) ||
            activityKind.Contains("Distribution", StringComparison.OrdinalIgnoreCase) ||
            activityKind.Contains("Expense", StringComparison.OrdinalIgnoreCase) ||
            activityKind.Contains("Fee", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return null;
    }

    private static string ResolveCapitalLane(string activityKind)
        => activityKind.Contains("Expense", StringComparison.OrdinalIgnoreCase) ||
           activityKind.Contains("Fee", StringComparison.OrdinalIgnoreCase)
            ? ExpenseLane
            : CapitalLane;

    private static bool LatestTermsIndicateFloatingRate(AssetOperationsDetailDto operations)
    {
        var payload = LatestTermsPayload(operations);
        if (payload is not { } terms)
        {
            return false;
        }

        foreach (var propertyName in (string[])["rateTypeKind", "rateType", "couponKind"])
        {
            if (TryReadString(terms, propertyName, out var value) &&
                value.Contains("Float", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return TryReadString(terms, "floatingRateIndex", out var index) && !string.IsNullOrWhiteSpace(index);
    }

    private static Dictionary<(Guid SecurityId, DateOnly DueDate, string FlowType), decimal> BuildFlowRateIndex(
        IReadOnlyList<PortfolioCashLadderPositionDto> positions)
    {
        // Build the rate index from the same latest-run flows the contributions were derived from,
        // so a stale projection run cannot overwrite the current run's annual rate and cause the
        // rate scenarios to reprice a current cash flow against an old rate.
        var index = new Dictionary<(Guid, DateOnly, string), decimal>();
        foreach (var position in positions)
        {
            foreach (var flow in LatestRunFlows(position.Operations))
            {
                if (flow.AnnualRate is { } rate && rate > 0m)
                {
                    index[(flow.SecurityId, flow.DueDate, flow.FlowType)] = rate;
                }
            }
        }

        return index;
    }

    private static DateOnly? ReadLatestTermsDate(AssetOperationsDetailDto operations, params string[] propertyNames)
    {
        var payload = LatestTermsPayload(operations);
        if (payload is not { } terms)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (!TryReadString(terms, propertyName, out var raw))
            {
                continue;
            }

            if (DateOnly.TryParse(raw, out var date))
            {
                return date;
            }

            if (DateTimeOffset.TryParse(raw, out var timestamp))
            {
                return DateOnly.FromDateTime(timestamp.UtcDateTime.Date);
            }
        }

        return null;
    }

    private static JsonElement? LatestTermsPayload(AssetOperationsDetailDto operations)
    {
        var latestTerms = operations.TermsHistory
            .OrderByDescending(static row => row.EffectiveDate)
            .ThenByDescending(static row => row.RecordedAt)
            .FirstOrDefault();
        return latestTerms?.ExtensionPayload is { } payload && payload.ValueKind == JsonValueKind.Object
            ? payload
            : null;
    }

    private static bool TryReadString(JsonElement payload, string propertyName, out string value)
    {
        foreach (var candidate in EnumerateTermSources(payload))
        {
            foreach (var property in candidate.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return true;
                    }
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static IEnumerable<JsonElement> EnumerateTermSources(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        yield return payload;
        // Nested term objects must be matched case-insensitively: retained Security Master terms
        // from AssetObligationProjectionService use "AssetSpecificTerms"/"CommonTerms" casing, which a
        // case-sensitive TryGetProperty would miss, hiding call dates and floating-rate indicators.
        if (TryGetPropertyIgnoreCase(payload, "assetSpecificTerms", out var assetSpecificTerms) &&
            assetSpecificTerms.ValueKind == JsonValueKind.Object)
        {
            yield return assetSpecificTerms;

            if (TryGetPropertyIgnoreCase(assetSpecificTerms, "profileFields", out var profileFields) &&
                profileFields.ValueKind == JsonValueKind.Object)
            {
                yield return profileFields;
            }
        }

        if (TryGetPropertyIgnoreCase(payload, "commonTerms", out var commonTerms) &&
            commonTerms.ValueKind == JsonValueKind.Object)
        {
            yield return commonTerms;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
}
