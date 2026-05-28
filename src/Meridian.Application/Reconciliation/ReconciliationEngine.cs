using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation.Legacy;

public sealed record MatchingTolerance(decimal Quantity, decimal Price, decimal MarketValue, decimal CashAmount, TimeSpan TimingWindow);

public sealed record ReconciliationRuleContext(MatchingTolerance Tolerance, IReadOnlyDictionary<string, string> RuleIdsByStage);

public interface IReconciliationRunRepository
{
    Task<bool> ExistsAsync(DateOnly accountingPeriod, string snapshotVersion, CancellationToken cancellationToken);
    Task SaveAsync(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, CancellationToken cancellationToken);
}

public sealed class ReconciliationNormalizer(
    IInstrumentMappingService instrumentMappingService,
    IFxConversionService fxConversionService,
    IAccountingPeriodService accountingPeriodService)
{
    public DataSourceSnapshot Normalize(ReconciliationSourcePayload payload, string baseCurrency)
    {
        var positions = payload.RawPositions.Select(raw =>
        {
            var aligned = accountingPeriodService.AlignTimestamp(raw.AsOfTimestamp, payload.SourceId);
            var fxRate = fxConversionService.GetFxRate(raw.Currency, baseCurrency, aligned);
            return new NormalizedPosition(
                raw.PositionId,
                instrumentMappingService.ResolveInstrumentKey(raw.Cusip, raw.Isin, raw.Ticker, raw.InternalSecurityId),
                raw.Cusip,
                raw.Isin,
                raw.Ticker,
                raw.InternalSecurityId,
                raw.Quantity,
                raw.Price,
                raw.MarketValue * fxRate,
                raw.Currency,
                baseCurrency,
                fxRate,
                aligned);
        }).ToArray();

        var cashEntries = payload.RawCashEntries.Select(raw =>
        {
            var aligned = accountingPeriodService.AlignTimestamp(raw.BookingTimestamp, payload.SourceId);
            var fxRate = fxConversionService.GetFxRate(raw.Currency, baseCurrency, aligned);
            return new NormalizedCashEntry(
                raw.CashEntryId,
                raw.AccountId,
                raw.CounterpartyReference,
                raw.SettlementId,
                raw.Comments,
                raw.Amount,
                raw.Currency,
                baseCurrency,
                fxRate,
                raw.Amount * fxRate,
                aligned,
                accountingPeriodService.ResolvePeriod(aligned, payload.SourceId));
        }).ToArray();

        return new DataSourceSnapshot(payload.SourceId, payload.SourceType, payload.SourceId, payload.CapturedAt, payload.Version, positions, cashEntries);
    }
}

public sealed class ReconciliationMatchingEngine
{
    public (IReadOnlyList<MatchGroup> Matches, IReadOnlyList<BreakRecord> Breaks) Execute(
        ReconciliationRun run,
        IReadOnlyList<DataSourceSnapshot> snapshots,
        ReconciliationRuleContext rules)
    {
        var allPositions = snapshots.SelectMany(s => s.Positions).ToArray();
        var allCash = snapshots.SelectMany(s => s.CashEntries).ToArray();
        var groups = new List<MatchGroup>();
        var breaks = new List<BreakRecord>();

        foreach (var positionSet in allPositions.GroupBy(p => p.InstrumentKey))
        {
            var stage = "Exact";
            var outcome = ReconciliationOutcome.PotentialBreak;
            var evidence = new Dictionary<string, string>();

            if (positionSet.Select(p => p.Quantity).Distinct().Count() == 1 &&
                positionSet.Select(p => p.MarketValue).Distinct().Count() == 1)
            {
                outcome = ReconciliationOutcome.Matched;
                evidence["exact.position"] = "Quantity and MV identical across sources.";
            }
            else if (WithinTolerance(positionSet, rules.Tolerance, out var toleranceEvidence))
            {
                stage = "Tolerance";
                outcome = ReconciliationOutcome.MatchedWithinTolerance;
                evidence["tolerance.position"] = toleranceEvidence;
            }
            else
            {
                stage = "Fuzzy";
                var fuzzy = allCash.Where(c => c.CounterpartyReference?.Contains(positionSet.Key, StringComparison.OrdinalIgnoreCase) == true
                                            || c.SettlementId?.Contains(positionSet.Key, StringComparison.OrdinalIgnoreCase) == true
                                            || c.Comments?.Contains(positionSet.Key, StringComparison.OrdinalIgnoreCase) == true).Take(3).ToArray();
                if (fuzzy.Length > 0)
                {
                    outcome = ReconciliationOutcome.PotentialBreak;
                    evidence["fuzzy.reference"] = $"Found {fuzzy.Length} linked cash references for manual review.";
                }
                else
                {
                    outcome = ReconciliationOutcome.TrueBreak;
                    evidence["fuzzy.reference"] = "No corroborating references found.";
                }
            }

            var matchGroup = new MatchGroup(
                Guid.NewGuid(),
                outcome,
                stage,
                ResolveRuleIds(rules, stage),
                evidence,
                positionSet.ToArray(),
                Array.Empty<NormalizedCashEntry>(),
                run.RunTimestamp);
            groups.Add(matchGroup);

            if (outcome is ReconciliationOutcome.PotentialBreak or ReconciliationOutcome.TrueBreak)
            {
                breaks.Add(new BreakRecord(Guid.NewGuid(), matchGroup.MatchGroupId, outcome, "POSITION_BREAK", $"{stage} stage break for {positionSet.Key}", matchGroup.RuleIds, evidence, run.RunTimestamp));
            }
        }

        return (groups, breaks);
    }

    private static bool WithinTolerance(IEnumerable<NormalizedPosition> positions, MatchingTolerance tolerance, out string evidence)
    {
        var list = positions.ToList();
        var qtySpread = list.Max(p => p.Quantity) - list.Min(p => p.Quantity);
        var mvSpread = list.Max(p => p.MarketValue) - list.Min(p => p.MarketValue);
        if (Math.Abs(qtySpread) <= tolerance.Quantity && Math.Abs(mvSpread) <= tolerance.MarketValue)
        {
            evidence = $"Quantity spread {qtySpread}, MV spread {mvSpread}.";
            return true;
        }

        evidence = $"Quantity spread {qtySpread}, MV spread {mvSpread} exceeded tolerance.";
        return false;
    }

    private static IReadOnlyList<string> ResolveRuleIds(ReconciliationRuleContext rules, string stage)
        => rules.RuleIdsByStage.TryGetValue(stage, out var id) ? [id] : ["UNSPECIFIED_RULE"];
}

public sealed class DailyReconciliationOrchestrator(
    IReconciliationSourceIngestionScheduler scheduler,
    ReconciliationNormalizer normalizer,
    ReconciliationMatchingEngine matchingEngine,
    IReconciliationRunRepository repository)
{
    public async Task<ReconciliationRun> RunAsync(
        DateOnly accountingPeriod,
        string snapshotVersion,
        bool forceRerun,
        string trigger,
        string baseCurrency,
        ReconciliationPollingSchedule schedule,
        ReconciliationRuleContext rules,
        IReadOnlyList<IReconciliationSourceAdapter> adapters,
        CancellationToken cancellationToken)
    {
        if (!forceRerun && await repository.ExistsAsync(accountingPeriod, snapshotVersion, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Reconciliation run already exists for {accountingPeriod} snapshot {snapshotVersion}.");
        }

        var payloads = await scheduler.IngestScheduledAsync(adapters, schedule, cancellationToken).ConfigureAwait(false);
        var snapshots = payloads.Select(p => normalizer.Normalize(p, baseCurrency)).ToArray();

        var run = new ReconciliationRun(Guid.NewGuid(), DateTimeOffset.UtcNow, accountingPeriod, snapshotVersion, forceRerun, trigger, snapshots);
        var (matches, breaks) = matchingEngine.Execute(run, snapshots, rules);
        await repository.SaveAsync(run, matches, breaks, cancellationToken).ConfigureAwait(false);
        return run;
    }
}
