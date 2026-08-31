using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Collateral exposure snapshot composition for the workstation API surface: builds the
/// counterparty/product exposure, margin, collateral-call, threshold-breach, and trend view from
/// collateral input rows. When no rows have been ingested the snapshot is honestly empty —
/// fabricated demo rows are never substituted. Split out of the WorkstationEndpoints core partial
/// as a behavior-preserving relocation; the inline collateral route lambda and the shared
/// NormalizeOperatorInboxToken helper remain in core.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static ExposureSnapshotDto BuildCollateralExposureSnapshot(
        CollateralExposureService service,
        IReadOnlyList<CollateralInputRow> rows)
    {
        var snapshots = service.BuildSnapshots(rows);
        var breaches = service.EvaluateBreaches(snapshots);
        var breachDtos = breaches.Select(static breach => new ThresholdBreachDto(
            breach.Counterparty,
            breach.Severity.ToString(),
            breach.CoverageRatio,
            breach.Severity == ThresholdSeverity.HardBreach ? breach.HardBreachLevel : breach.EarlyWarningLevel,
            DateTimeOffset.UtcNow,
            breach.Message)).ToArray();

        var counterpartyDtos = snapshots.Select(static snapshot => new CounterpartyExposureDto(
            snapshot.Counterparty,
            snapshot.NetExposure,
            snapshot.GrossExposure,
            snapshot.CollateralBalance,
            snapshot.HaircutAdjustedCollateral,
            new MarginRequirementDto(0m, snapshot.RequiredCollateral, snapshot.RequiredCollateral),
            snapshot.HaircutAdjustedCollateral >= snapshot.RequiredCollateral,
            snapshot.ProductDecomposition.Select(static product => new ProductExposureDto(
                product.ProductType,
                product.NetExposure,
                product.GrossExposure)).ToArray())).ToArray();

        var calls = breaches
            .Where(static breach => breach.Severity == ThresholdSeverity.HardBreach)
            .Select(static breach => new CollateralCallDto(
                $"call-{NormalizeOperatorInboxToken(breach.Counterparty)}",
                breach.Counterparty,
                Math.Max(0m, breach.HardBreachLevel - breach.CoverageRatio),
                0m,
                DateTimeOffset.UtcNow.AddDays(1),
                "Open",
                breach.Message))
            .ToArray();

        var trend = Enumerable.Range(0, 12)
            .Select(index => new ExposureTrendPointDto(
                DateTimeOffset.UtcNow.AddHours(index - 11),
                snapshots.Sum(static snapshot => snapshot.NetExposure),
                snapshots.Count == 0 ? 0m : snapshots.Average(static snapshot => snapshot.CollateralCoverageRatio)))
            .ToArray();

        return new ExposureSnapshotDto(
            DateTimeOffset.UtcNow,
            rows.Count == 0 ? "micro-batch buffer (empty)" : "micro-batch buffer",
            counterpartyDtos,
            breachDtos,
            calls,
            trend);
    }
}
