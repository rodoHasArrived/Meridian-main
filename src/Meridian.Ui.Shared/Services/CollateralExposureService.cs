using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed class CollateralExposureService
{
    private static readonly IReadOnlyList<HaircutRuleDto> HaircutRules =
    [
        new("cash", 0.00m, 100m),
        new("treasury", 0.02m, 98m),
        new("equity", 0.15m, 85m),
        new("credit", 0.20m, 80m)
    ];

    public ExposureSnapshotDto BuildSnapshot()
    {
        var asOf = DateTimeOffset.UtcNow;
        var counterparties = new[]
        {
            BuildCounterparty("CP-ALPHA", 12_400_000m, 19_800_000m, 15_100_000m, 8_700_000m, 4_200_000m, 2_100_000m),
            BuildCounterparty("CP-BETA", 6_500_000m, 11_900_000m, 6_900_000m, 3_900_000m, 2_400_000m, 1_100_000m)
        };

        var breaches = BuildThresholdBreaches(counterparties, asOf);
        var calls = BuildCollateralCalls(counterparties, asOf);
        var trend = Enumerable.Range(0, 12)
            .Select(i =>
            {
                var t = asOf.AddMinutes(-5 * (11 - i));
                var net = counterparties.Sum(c => c.NetExposure) * (0.95m + (i * 0.005m));
                var coverage = counterparties.Sum(c => c.HaircutAdjustedCollateral) / Math.Max(1m, net);
                return new ExposureTrendPointDto(t, decimal.Round(net, 2), decimal.Round(coverage, 4));
            })
            .ToArray();

        return new ExposureSnapshotDto(asOf, "micro-batch:5m", counterparties, breaches, calls, trend);
    }

    private static CounterpartyExposureDto BuildCounterparty(string name, decimal net, decimal gross, decimal collateral, decimal repo, decimal swap, decimal margin)
    {
        var marginReq = new MarginRequirementDto(gross * 0.10m, net * 0.05m, gross * 0.10m + net * 0.05m);
        var adjusted = ApplyHaircuts(collateral);
        return new CounterpartyExposureDto(
            name,
            net,
            gross,
            collateral,
            adjusted,
            marginReq,
            adjusted >= marginReq.TotalRequired,
            [
                new ProductExposureDto("repo", repo, repo * 1.45m),
                new ProductExposureDto("swap", swap, swap * 1.55m),
                new ProductExposureDto("margin", margin, margin * 1.35m)
            ]);
    }

    private static decimal ApplyHaircuts(decimal collateralBalance)
    {
        var buckets = new Dictionary<string, decimal>
        {
            ["cash"] = collateralBalance * 0.35m,
            ["treasury"] = collateralBalance * 0.30m,
            ["equity"] = collateralBalance * 0.20m,
            ["credit"] = collateralBalance * 0.15m
        };

        return decimal.Round(HaircutRules.Sum(rule => buckets[rule.AssetClass] * (rule.AdvanceRatePercent / 100m)), 2);
    }

    private static IReadOnlyList<ThresholdBreachDto> BuildThresholdBreaches(IReadOnlyList<CounterpartyExposureDto> counterparties, DateTimeOffset asOf)
    {
        const decimal earlyWarn = 12_000_000m;
        const decimal hardLimit = 18_000_000m;

        return counterparties
            .SelectMany(cp => new[]
            {
                cp.GrossExposure >= hardLimit
                    ? new ThresholdBreachDto(cp.Counterparty, "hard", cp.GrossExposure, hardLimit, asOf, "Hard breach: reduce exposure and post collateral.")
                    : null,
                cp.GrossExposure >= earlyWarn
                    ? new ThresholdBreachDto(cp.Counterparty, "warning", cp.GrossExposure, earlyWarn, asOf, "Early warning: escalate collateral monitoring.")
                    : null
            })
            .Where(x => x is not null)
            .Cast<ThresholdBreachDto>()
            .ToArray();
    }

    private static IReadOnlyList<CollateralCallDto> BuildCollateralCalls(IReadOnlyList<CounterpartyExposureDto> counterparties, DateTimeOffset asOf)
        => counterparties
            .Where(cp => !cp.CollateralSufficient)
            .Select(cp => new CollateralCallDto(
                $"CALL-{cp.Counterparty}-{asOf:yyyyMMddHHmm}",
                cp.Counterparty,
                decimal.Round(cp.MarginRequirement.TotalRequired - cp.HaircutAdjustedCollateral, 2),
                0m,
                asOf.AddHours(2),
                "pending",
                "Haircut-adjusted collateral below required margin."))
            .ToArray();
}
