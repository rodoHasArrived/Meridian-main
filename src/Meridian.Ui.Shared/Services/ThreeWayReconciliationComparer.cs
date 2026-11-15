using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>Compares independent retained operands; coverage and comparability are not zero balances.</summary>
public static class ThreeWayReconciliationComparer
{
    public static IReadOnlyList<ThreeWayReconciliationCheckDto> Compare(
        IReadOnlyList<ThreeWaySourceSnapshotDto> sources, DateTimeOffset cutoffUtc,
        TimeSpan maximumAge, TimeSpan maximumDrift)
    {
        if (maximumAge <= TimeSpan.Zero || maximumDrift < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        var checks = new List<ThreeWayReconciliationCheckDto>();
        var normalized = new List<ThreeWaySourceSnapshotDto>();
        foreach (var name in new[] { "ledger", "oms", "broker" })
        {
            var candidates = sources.Where(source => source.Source == name).ToArray();
            var source = candidates.Length == 1 ? candidates[0]
                : new ThreeWaySourceSnapshotDto(name, cutoffUtc, "unavailable", null, [], "Unknown", ["Exactly one independent source snapshot is required."]);
            var issues = source.CoverageFailures.ToList();
            if (source.AsOfUtc > cutoffUtc || cutoffUtc - source.AsOfUtc > maximumAge)
                issues.Add("Source timestamp is after the cutoff or older than the declared maximum age.");
            if (source.Positions.Any(position => position.SecurityId == Guid.Empty || position.Quantity < 0m || position.CostBasis < 0m) ||
                source.Positions.GroupBy(position => position.SecurityId).Any(group => group.Count() != 1))
                issues.Add("Positions require unique canonical security identities and supported long quantities/basis.");
            source = source with { CoverageFailures = issues };
            normalized.Add(source);
            checks.Add(new($"coverage:{name}", "coverage", null, null, name, name,
                null, null, 0m, issues.Count == 0 ? "Matched" : "Blocked",
                issues.Count == 0 ? "Source scope and coverage are available." : string.Join(" ", issues)));
        }
        foreach (var (leftIndex, rightIndex) in new[] { (0, 1), (0, 2), (1, 2) })
        {
            var left = normalized[leftIndex];
            var right = normalized[rightIndex];
            var complete = left.CoverageFailures.Count == 0 && right.CoverageFailures.Count == 0;
            if ((left.AsOfUtc - right.AsOfUtc).Duration() > maximumDrift)
            {
                checks.Add(new($"timing:{left.Source}:{right.Source}", "timing", null, null,
                    left.Source, right.Source, null, null, 0m, "Blocked", "Source timestamps exceed the declared comparison drift."));
                complete = false;
            }
            Add("cash", null, null, complete ? left.Cash : null, complete ? right.Cash : null, 0.01m);
            var ids = left.Positions.Concat(right.Positions).Where(position => position.SecurityId != Guid.Empty)
                .Select(position => position.SecurityId).Distinct().Order().ToArray();
            foreach (var id in ids)
            {
                var leftPosition = left.Positions.FirstOrDefault(position => position.SecurityId == id);
                var rightPosition = right.Positions.FirstOrDefault(position => position.SecurityId == id);
                var symbol = leftPosition?.Symbol ?? rightPosition?.Symbol;
                Add("quantity", id, symbol, complete ? leftPosition?.Quantity ?? 0m : null,
                    complete ? rightPosition?.Quantity ?? 0m : null, 0m);
                var comparable = complete &&
                    left.CostBasisConvention == ThreeWayReconciliationOptions.SupportedBasisConvention &&
                    right.CostBasisConvention == left.CostBasisConvention;
                Add("cost-basis", id, symbol, comparable ? leftPosition is null ? 0m : leftPosition.CostBasis : null,
                    comparable ? rightPosition is null ? 0m : rightPosition.CostBasis : null, 0.01m);
            }

            void Add(string measure, Guid? securityId, string? symbol, decimal? leftAmount, decimal? rightAmount, decimal tolerance)
            {
                var status = !leftAmount.HasValue || !rightAmount.HasValue ? "Blocked"
                    : Math.Abs(leftAmount.Value - rightAmount.Value) <= tolerance ? "Matched" : "Break";
                checks.Add(new($"{measure}:{securityId?.ToString("N") ?? "cash"}:{left.Source}:{right.Source}",
                    measure, securityId, symbol, left.Source, right.Source, leftAmount, rightAmount, tolerance, status,
                    status == "Blocked" ? "Required source operands or comparable cost conventions are unavailable."
                        : status == "Matched" ? "Retained operands agree within tolerance."
                        : measure == "quantity" ? "Quantity differs; inspect missing/duplicate fills and unapplied corporate actions."
                        : measure == "cash" ? "Cash differs; inspect settlement timing, fills, and independently evidenced fees."
                        : "Cost basis differs; inspect retained acquisition and relief evidence."));
            }
        }
        return checks;
    }
}
