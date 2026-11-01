using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Coordinates daily source capture, normalization, matching, and reconciliation evidence
/// production.
/// </summary>
public sealed class ReconciliationRunOrchestrator(
    IReconciliationIngestionScheduler scheduler,
    IReadOnlyList<IReconciliationSourceAdapter> adapters,
    ReconciliationMatchingEngine matchingEngine)
{
    // Guarded by _gate. The gate also serializes check → capture → record, so two concurrent
    // first runs for the same key cannot both capture and both claim snapshot version 1.
    private readonly Dictionary<string, ReconciliationRun> _runByIdempotencyKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence)> RunDailyAsync(
        DateOnly businessDate,
        DateTimeOffset runTimestampUtc,
        string baseCurrency,
        bool rerun,
        MatchingTolerances tolerances,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tolerances);
        return RunDailyCoreAsync(businessDate, runTimestampUtc, baseCurrency, rerun, tolerances.ToStatementToleranceProfile(), ct);
    }

    public Task<(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence)> RunDailyAsync(
        DateOnly businessDate,
        DateTimeOffset runTimestampUtc,
        string baseCurrency,
        bool rerun,
        StatementToleranceProfile toleranceProfile,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(toleranceProfile);
        return RunDailyCoreAsync(businessDate, runTimestampUtc, baseCurrency, rerun, toleranceProfile, ct);
    }

    /// <summary>
    /// Idempotency key for a daily run. The base currency is part of the key: a USD and a EUR
    /// run for the same business date capture different reporting views and must not share a
    /// cached result.
    /// </summary>
    internal static string BuildIdempotencyKey(DateOnly businessDate, string baseCurrency) =>
        $"recon:{businessDate:yyyy-MM-dd}:{baseCurrency.Trim().ToUpperInvariant()}";

    private async Task<(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence)> RunDailyCoreAsync(
        DateOnly businessDate,
        DateTimeOffset runTimestampUtc,
        string baseCurrency,
        bool rerun,
        StatementToleranceProfile profile,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        var key = BuildIdempotencyKey(businessDate, baseCurrency);

        ReconciliationRun run;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var priorExists = _runByIdempotencyKey.TryGetValue(key, out var prior);
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
            run = new ReconciliationRun(Guid.NewGuid(), businessDate, runTimestampUtc, rerun, snapshotVersion, key, snapshots)
            {
                ToleranceProfileId = profile.ProfileId,
                ToleranceProfileVersion = profile.Version
            };
            _runByIdempotencyKey[key] = run;
        }
        finally
        {
            _gate.Release();
        }

        var result = matchingEngine.Run(run, profile);
        return (run, result.matches, result.breaks, result.evidence);
    }
}
