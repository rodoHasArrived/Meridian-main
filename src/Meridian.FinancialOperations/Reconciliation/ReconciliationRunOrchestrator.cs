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
    // Both dictionaries are guarded by _sync, held only for lookups and writes. Capture is
    // serialized per idempotency key (check → capture → record), so two concurrent first runs
    // for one key cannot both claim snapshot version 1, while runs for other dates/currencies
    // and cached lookups never wait behind a slow capture.
    private readonly object _sync = new();
    private readonly Dictionary<string, ReconciliationRun> _runByIdempotencyKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _keyGates = new(StringComparer.OrdinalIgnoreCase);

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

        if (!rerun && TryGetRun(key, out var cachedRun))
            return ServeCached(cachedRun, profile);

        SemaphoreSlim gate;
        lock (_sync)
        {
            if (!_keyGates.TryGetValue(key, out gate!))
                _keyGates[key] = gate = new SemaphoreSlim(1, 1);
        }

        ReconciliationRun run;
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check under the key gate: a concurrent first run may have just recorded it.
            var priorExists = TryGetRun(key, out var prior);
            if (priorExists && !rerun)
                return ServeCached(prior!, profile);

            var snapshotVersion = priorExists ? prior!.SnapshotVersion + 1 : 1;
            var request = new ReconciliationIngestionRequest(businessDate, runTimestampUtc, baseCurrency, snapshotVersion);
            var snapshots = await scheduler.CaptureAsync(adapters, request, ct).ConfigureAwait(false);
            run = new ReconciliationRun(Guid.NewGuid(), businessDate, runTimestampUtc, rerun, snapshotVersion, key, snapshots)
            {
                ToleranceProfileId = profile.ProfileId,
                ToleranceProfileVersion = profile.Version
            };
            lock (_sync)
                _runByIdempotencyKey[key] = run;
        }
        finally
        {
            gate.Release();
        }

        var result = matchingEngine.Run(run, profile);
        return (run, result.matches, result.breaks, result.evidence);
    }

    private bool TryGetRun(string key, out ReconciliationRun run)
    {
        lock (_sync)
            return _runByIdempotencyKey.TryGetValue(key, out run!);
    }

    private (ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence) ServeCached(
        ReconciliationRun prior,
        StatementToleranceProfile profile)
    {
        var cached = matchingEngine.Run(prior, profile);
        return (prior with
        {
            ToleranceProfileId = profile.ProfileId,
            ToleranceProfileVersion = profile.Version
        }, cached.matches, cached.breaks, cached.evidence);
    }
}
