using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed record LegacyMatchingTolerance(decimal Quantity, decimal Price, decimal MarketValue, decimal CashAmount, TimeSpan TimingWindow);

public sealed record LegacyReconciliationRuleContext(LegacyMatchingTolerance Tolerance, IReadOnlyDictionary<string, string> RuleIdsByStage);

public interface ILegacyReconciliationRunRepository
{
    Task<bool> ExistsAsync(DateOnly accountingPeriod, string snapshotVersion, CancellationToken cancellationToken);
    Task SaveAsync(ReconciliationRun run, IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, CancellationToken cancellationToken);
}

public sealed class LegacyReconciliationNormalizer(
    ILegacyInstrumentMappingService instrumentMappingService,
    ILegacyFxConversionService fxConversionService,
    ILegacyAccountingPeriodService accountingPeriodService)
{
    public DataSourceSnapshot Normalize(LegacyReconciliationSourcePayload payload, string baseCurrency)
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

public sealed class LegacyReconciliationMatchingEngine
{
    public (IReadOnlyList<MatchGroup> Matches, IReadOnlyList<BreakRecord> Breaks) Execute(
        ReconciliationRun run,
        IReadOnlyList<DataSourceSnapshot> snapshots,
        LegacyReconciliationRuleContext rules)
    {
        var allPositions = snapshots
            .SelectMany(s => s.Positions.Select(p => new { Snapshot = s, Position = p }))
            .ToArray();
        var allCash = snapshots
            .SelectMany(s => s.CashEntries.Select(c => new { Snapshot = s, Cash = c }))
            .ToArray();
        var groups = new List<MatchGroup>();
        var breaks = new List<BreakRecord>();

        var expectedSourceCount = snapshots.Select(s => s.SourceId).Distinct(StringComparer.Ordinal).Count();

        foreach (var positionSet in allPositions.GroupBy(p => p.Position.InstrumentKey))
        {
            var stage = "Exact";
            var outcome = ReconciliationOutcome.PotentialBreak;
            var evidence = new Dictionary<string, string>();

            var sourceCount = positionSet.Select(p => p.Snapshot.SourceId).Distinct(StringComparer.Ordinal).Count();

            if (sourceCount == expectedSourceCount &&
                positionSet.Select(p => p.Position.Quantity).Distinct().Count() == 1 &&
                positionSet.Select(p => p.Position.MarketValue).Distinct().Count() == 1)
            {
                outcome = ReconciliationOutcome.Matched;
                evidence["exact.position"] = "Quantity and MV identical across all expected sources.";
            }
            else if (sourceCount == expectedSourceCount && WithinTolerance(positionSet.Select(p => p.Position), rules.Tolerance, out var toleranceEvidence))
            {
                stage = "Tolerance";
                outcome = ReconciliationOutcome.MatchedWithinTolerance;
                evidence["tolerance.position"] = toleranceEvidence;
            }
            else
            {
                stage = "Fuzzy";
                if (sourceCount < expectedSourceCount)
                {
                    evidence["source.coverage"] = $"Observed {sourceCount} of {expectedSourceCount} expected sources.";
                }

                var fuzzy = allCash.Where(c => c.Cash.CounterpartyReference?.Contains(positionSet.Key, StringComparison.OrdinalIgnoreCase) == true
                                            || c.Cash.SettlementId?.Contains(positionSet.Key, StringComparison.OrdinalIgnoreCase) == true
                                            || c.Cash.Comments?.Contains(positionSet.Key, StringComparison.OrdinalIgnoreCase) == true).Take(3).ToArray();
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
                positionSet.Select(p => p.Position).ToArray(),
                Array.Empty<NormalizedCashEntry>(),
                run.RunTimestamp);
            groups.Add(matchGroup);

            if (outcome is ReconciliationOutcome.PotentialBreak or ReconciliationOutcome.TrueBreak)
            {
                breaks.Add(new BreakRecord(Guid.NewGuid(), matchGroup.MatchGroupId, outcome, "POSITION_BREAK", $"{stage} stage break for {positionSet.Key}", matchGroup.RuleIds, evidence, run.RunTimestamp));
            }
        }


        foreach (var cashSet in allCash.GroupBy(c => (c.Cash.AccountId, c.Cash.AccountingPeriod)))
        {
            var stage = "CashTolerance";
            var evidence = new Dictionary<string, string>();
            var sourceCount = cashSet.Select(c => c.Snapshot.SourceId).Distinct(StringComparer.Ordinal).Count();
            var hasExpectedCoverage = sourceCount == expectedSourceCount;
            var amounts = cashSet.Select(c => c.Cash.BaseAmount).ToArray();
            var amountSpread = amounts.Max() - amounts.Min();
            var minBookedAt = cashSet.Min(c => c.Cash.BookingTimestamp);
            var maxBookedAt = cashSet.Max(c => c.Cash.BookingTimestamp);
            var timingSpread = maxBookedAt - minBookedAt;

            var withinTolerance = hasExpectedCoverage
                && Math.Abs(amountSpread) <= rules.Tolerance.CashAmount
                && timingSpread <= rules.Tolerance.TimingWindow;

            var outcome = withinTolerance
                ? ReconciliationOutcome.MatchedWithinTolerance
                : ReconciliationOutcome.PotentialBreak;

            evidence["cash.tolerance"] = $"Amount spread {amountSpread}, timing spread {timingSpread}.";
            if (!hasExpectedCoverage)
            {
                evidence["source.coverage"] = $"Observed {sourceCount} of {expectedSourceCount} expected sources.";
            }

            var group = new MatchGroup(
                Guid.NewGuid(),
                outcome,
                stage,
                ResolveRuleIds(rules, stage),
                evidence,
                Array.Empty<NormalizedPosition>(),
                cashSet.Select(c => c.Cash).ToArray(),
                run.RunTimestamp);
            groups.Add(group);

            if (outcome is ReconciliationOutcome.PotentialBreak or ReconciliationOutcome.TrueBreak)
            {
                breaks.Add(new BreakRecord(Guid.NewGuid(), group.MatchGroupId, outcome, "CASH_BREAK", $"{stage} stage break for account {cashSet.Key.AccountId}", group.RuleIds, evidence, run.RunTimestamp));
            }
        }
        return (groups, breaks);
    }

    private static bool WithinTolerance(IEnumerable<NormalizedPosition> positions, LegacyMatchingTolerance tolerance, out string evidence)
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

    private static IReadOnlyList<string> ResolveRuleIds(LegacyReconciliationRuleContext rules, string stage)
        => rules.RuleIdsByStage.TryGetValue(stage, out var id) ? [id] : ["UNSPECIFIED_RULE"];
}

public sealed class LegacyDailyReconciliationOrchestrator(
    ILegacyReconciliationSourceIngestionScheduler scheduler,
    LegacyReconciliationNormalizer normalizer,
    LegacyReconciliationMatchingEngine matchingEngine,
    ILegacyReconciliationRunRepository repository)
{
    public async Task<ReconciliationRun> RunAsync(
        DateOnly accountingPeriod,
        string snapshotVersion,
        bool forceRerun,
        string trigger,
        string baseCurrency,
        LegacyReconciliationPollingSchedule schedule,
        LegacyReconciliationRuleContext rules,
        IReadOnlyList<ILegacyReconciliationSourceAdapter> adapters,
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
