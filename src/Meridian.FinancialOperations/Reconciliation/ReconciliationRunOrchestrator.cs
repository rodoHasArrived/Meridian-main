using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class ReconciliationRunOrchestrator(
    IReconciliationIngestionScheduler scheduler,
    IReadOnlyList<IReconciliationSourceAdapter> adapters,
    ReconciliationMatchingEngine matchingEngine)
{
    private readonly Dictionary<string, ReconciliationRun> _runByIdempotencyKey = new(StringComparer.OrdinalIgnoreCase);

    public async Task<(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence)> RunDailyAsync(
        DateOnly businessDate,
        DateTimeOffset runTimestampUtc,
        string baseCurrency,
        bool rerun,
        MatchingTolerances tolerances,
        CancellationToken ct)
    {
        var key = $"recon:{businessDate:yyyy-MM-dd}";
        var priorExists = _runByIdempotencyKey.TryGetValue(key, out var prior);
        var profile = tolerances.ToStatementToleranceProfile();
        if (priorExists && !rerun)
        {
            var cached = matchingEngine.Run(prior!, profile);
            return (prior! with
            {
                ToleranceProfileId = profile.ProfileId,
                ToleranceProfileVersion = profile.Version
            }, cached.matches, cached.breaks, cached.evidence);
        }

        var snapshotVersion = priorExists ? prior!.SnapshotVersion + 1 : 1;
        var request = new ReconciliationIngestionRequest(businessDate, runTimestampUtc, baseCurrency, snapshotVersion);
        var snapshots = await scheduler.CaptureAsync(adapters, request, ct).ConfigureAwait(false);
        var run = new ReconciliationRun(Guid.NewGuid(), businessDate, runTimestampUtc, rerun, snapshotVersion, key, snapshots)
        {
            ToleranceProfileId = profile.ProfileId,
            ToleranceProfileVersion = profile.Version
        };
        _runByIdempotencyKey[key] = run;

        var result = matchingEngine.Run(run, profile);
        return (run, result.matches, result.breaks, result.evidence);
    }

    public async Task<(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence)> RunDailyAsync(
        DateOnly businessDate,
        DateTimeOffset runTimestampUtc,
        string baseCurrency,
        bool rerun,
        StatementToleranceProfile toleranceProfile,
        CancellationToken ct)
    {
        var key = $"recon:{businessDate:yyyy-MM-dd}";
        var priorExists = _runByIdempotencyKey.TryGetValue(key, out var prior);
        if (priorExists && !rerun)
        {
            var cached = matchingEngine.Run(prior!, toleranceProfile);
            return (prior! with
            {
                ToleranceProfileId = toleranceProfile.ProfileId,
                ToleranceProfileVersion = toleranceProfile.Version
            }, cached.matches, cached.breaks, cached.evidence);
        }

        var snapshotVersion = priorExists ? prior!.SnapshotVersion + 1 : 1;
        var request = new ReconciliationIngestionRequest(businessDate, runTimestampUtc, baseCurrency, snapshotVersion);
        var snapshots = await scheduler.CaptureAsync(adapters, request, ct).ConfigureAwait(false);
        var run = new ReconciliationRun(Guid.NewGuid(), businessDate, runTimestampUtc, rerun, snapshotVersion, key, snapshots)
        {
            ToleranceProfileId = toleranceProfile.ProfileId,
            ToleranceProfileVersion = toleranceProfile.Version
        };
        _runByIdempotencyKey[key] = run;

        var result = matchingEngine.Run(run, toleranceProfile);
        return (run, result.matches, result.breaks, result.evidence);
    }
}
