using Meridian.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation;

public interface IReconciliationSourceAdapter
{
    ReconciliationSourceType SourceType { get; }

    Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct);
}

public interface IReconciliationIngestionScheduler
{
    Task<IReadOnlyList<DataSourceSnapshot>> CaptureAsync(
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        ReconciliationIngestionRequest request,
        CancellationToken ct);
}

public sealed record ReconciliationIngestionRequest(
    DateOnly BusinessDate,
    DateTimeOffset RunTimestampUtc,
    string BaseCurrency,
    int SnapshotVersion);

public interface IInstrumentMappingService
{
    string ResolveCanonicalId(string? cusip, string? isin, string? ticker, string? internalId);
}

public interface IFxRateProvider
{
    decimal Convert(decimal amount, string fromCurrency, string toCurrency, DateTimeOffset atUtc);
}

public interface IAccountingCalendar
{
    DateOnly ResolvePeriod(DateTimeOffset postedAtUtc);
}

public sealed class ReconciliationNormalizationService(
    IInstrumentMappingService instrumentMapping,
    IFxRateProvider fxRateProvider,
    IAccountingCalendar accountingCalendar)
{
    public NormalizedPosition NormalizePosition(NormalizedPosition input, string baseCurrency, DateTimeOffset runTimestampUtc)
    {
        var canonical = instrumentMapping.ResolveCanonicalId(input.Cusip, input.Isin, input.Ticker, input.InternalSecurityId);
        var fx = fxRateProvider.Convert(1m, input.Currency, baseCurrency, runTimestampUtc);
        return input with
        {
            InstrumentCanonicalId = canonical,
            MarketValue = decimal.Round(input.MarketValue * fx, 6),
            Currency = baseCurrency,
            AsOfUtc = runTimestampUtc
        };
    }

    public NormalizedCashEntry NormalizeCashEntry(NormalizedCashEntry input, string baseCurrency, DateTimeOffset runTimestampUtc)
    {
        var amountBase = fxRateProvider.Convert(input.Amount, input.Currency, baseCurrency, runTimestampUtc);
        return input with
        {
            AmountBase = decimal.Round(amountBase, 6),
            BaseCurrency = baseCurrency,
            PostedAtUtc = runTimestampUtc,
            AccountingPeriod = accountingCalendar.ResolvePeriod(runTimestampUtc)
        };
    }
}

public sealed record MatchingTolerances(decimal Quantity, decimal Price, decimal MarketValue, decimal CashAmount, TimeSpan TimingWindow);

public sealed class ReconciliationMatchingEngine
{
    public (IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence) Run(
        ReconciliationRun run,
        MatchingTolerances tolerances)
    {
        var evidence = new List<MatchEvidence>();
        var matches = new List<MatchGroup>();
        var breaks = new List<BreakRecord>();

        var allPositions = run.SourceSnapshots.SelectMany(s => s.Positions).ToArray();
        var groupedByInstrument = allPositions.GroupBy(static p => p.InstrumentCanonicalId);
        foreach (var instrumentGroup in groupedByInstrument)
        {
            var exactGroups = instrumentGroup.GroupBy(static p => (p.Quantity, p.Price, p.MarketValue)).ToArray();
            foreach (var exactGroup in exactGroups.Where(static g => g.Count() > 1))
            {
                var exactEvidence = new MatchEvidence(Guid.NewGuid().ToString("N"), "exact-position-v1", "Exact", "Exact position tuple match.", 0m, new Dictionary<string, string>());
                evidence.Add(exactEvidence);
                matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), BreakClassification.Matched, exactEvidence.RuleId, exactGroup.Select(p => p.PositionId).ToArray(), Array.Empty<string>(), [exactEvidence.EvidenceId]));
            }

            if (exactGroups.Length > 1 || exactGroups[0].Count() == 1)
            {
                var breakEvidence = new MatchEvidence(Guid.NewGuid().ToString("N"), "true-break-position-v1", "Exact", "Position has no exact cross-source match.", null, new Dictionary<string, string>());
                evidence.Add(breakEvidence);
                breaks.Add(new BreakRecord(Guid.NewGuid().ToString("N"), BreakClassification.TrueBreak, breakEvidence.RuleId, "No exact matching position candidate found.", run.SourceSnapshots.Select(s => s.SnapshotId).ToArray(), [breakEvidence.EvidenceId]));
            }
        }

        var allCash = run.SourceSnapshots.SelectMany(s => s.CashEntries).ToArray();
        foreach (var candidate in allCash)
        {
            var near = allCash.Where(other => other.CashEntryId != candidate.CashEntryId && Math.Abs(other.AmountBase - candidate.AmountBase) <= tolerances.CashAmount).ToArray();
            if (near.Length > 0)
            {
                var timeDelta = near.Min(other => Math.Abs((other.PostedAtUtc - candidate.PostedAtUtc).TotalMinutes));
                var classification = timeDelta <= tolerances.TimingWindow.TotalMinutes ? BreakClassification.MatchedWithinTolerance : BreakClassification.PotentialBreak;
                var ruleId = classification == BreakClassification.MatchedWithinTolerance ? "tolerance-cash-v1" : "timing-window-v1";
                var ev = new MatchEvidence(Guid.NewGuid().ToString("N"), ruleId, "Tolerance", "Cash amount/timing tolerance evaluation.", (decimal?)timeDelta, new Dictionary<string, string>());
                evidence.Add(ev);
                matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), classification, ev.RuleId, Array.Empty<string>(), near.Append(candidate).Select(c => c.CashEntryId).Distinct().ToArray(), [ev.EvidenceId]));
            }
            else
            {
                var fuzzyHit = allCash.FirstOrDefault(other => other.CashEntryId != candidate.CashEntryId &&
                    (!string.IsNullOrWhiteSpace(candidate.CounterpartyReference) && string.Equals(candidate.CounterpartyReference, other.CounterpartyReference, StringComparison.OrdinalIgnoreCase) ||
                     !string.IsNullOrWhiteSpace(candidate.SettlementId) && string.Equals(candidate.SettlementId, other.SettlementId, StringComparison.OrdinalIgnoreCase) ||
                     !string.IsNullOrWhiteSpace(candidate.Comment) && !string.IsNullOrWhiteSpace(other.Comment) && other.Comment.Contains(candidate.Comment, StringComparison.OrdinalIgnoreCase)));

                if (fuzzyHit is not null)
                {
                    var ev = new MatchEvidence(Guid.NewGuid().ToString("N"), "fuzzy-reference-v1", "Fuzzy", "Reference-level fuzzy hit.", null, new Dictionary<string, string>());
                    evidence.Add(ev);
                    matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), BreakClassification.PotentialBreak, ev.RuleId, Array.Empty<string>(), [candidate.CashEntryId, fuzzyHit.CashEntryId], [ev.EvidenceId]));
                }
                else
                {
                    var ev = new MatchEvidence(Guid.NewGuid().ToString("N"), "true-break-v1", "Fuzzy", "No exact/tolerance/fuzzy evidence available.", null, new Dictionary<string, string>());
                    evidence.Add(ev);
                    breaks.Add(new BreakRecord(Guid.NewGuid().ToString("N"), BreakClassification.TrueBreak, ev.RuleId, "No matching candidate found.", run.SourceSnapshots.Select(s => s.SnapshotId).ToArray(), [ev.EvidenceId]));
                }
            }
        }

        return (matches, breaks, evidence);
    }
}

public sealed class DefaultReconciliationIngestionScheduler : IReconciliationIngestionScheduler
{
    public async Task<IReadOnlyList<DataSourceSnapshot>> CaptureAsync(
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        ReconciliationIngestionRequest request,
        CancellationToken ct)
    {
        var snapshots = new List<DataSourceSnapshot>();
        foreach (var adapter in adapters.OrderBy(static a => a.SourceType))
        {
            snapshots.Add(await adapter.CaptureSnapshotAsync(request, ct).ConfigureAwait(false));
        }

        return snapshots;
    }
}

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
        if (priorExists && !rerun)
        {
            var cached = matchingEngine.Run(prior!, tolerances);
            return (prior!, cached.matches, cached.breaks, cached.evidence);
        }

        var snapshotVersion = priorExists ? prior!.SnapshotVersion + 1 : 1;
        var request = new ReconciliationIngestionRequest(businessDate, runTimestampUtc, baseCurrency, snapshotVersion);
        var snapshots = await scheduler.CaptureAsync(adapters, request, ct).ConfigureAwait(false);
        var run = new ReconciliationRun(Guid.NewGuid(), businessDate, runTimestampUtc, rerun, snapshotVersion, key, snapshots);
        _runByIdempotencyKey[key] = run;

        var result = matchingEngine.Run(run, tolerances);
        return (run, result.matches, result.breaks, result.evidence);
    }
}
