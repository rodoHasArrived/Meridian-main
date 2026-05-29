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

public sealed record MatchingTolerances(decimal Quantity, decimal Price, decimal MarketValue, decimal CashAmount, TimeSpan TimingWindow)
{
    public StatementToleranceProfile ToStatementToleranceProfile() => new(
        StatementToleranceProfile.DefaultProfileId,
        StatementToleranceProfile.DefaultProfileVersion,
        [new CashToleranceRule("cash-amount-window-v1", CashAmount, null, TimingWindow)],
        [new PositionToleranceRule("position-quantity-price-value-v1", Quantity, MarketValue, Price)],
        [new TransactionToleranceRule("transaction-amount-price-settlement-v1", CashAmount, TimingWindow, Price)]);
}

public sealed class ReconciliationMatchingEngine
{
    public (IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence) Run(
        ReconciliationRun run,
        MatchingTolerances tolerances) => Run(run, tolerances.ToStatementToleranceProfile());

    public (IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence) Run(
        ReconciliationRun run,
        StatementToleranceProfile toleranceProfile)
    {
        var effectiveRun = run with
        {
            ToleranceProfileId = toleranceProfile.ProfileId,
            ToleranceProfileVersion = toleranceProfile.Version
        };
        var evidence = new List<MatchEvidence>();
        var matches = new List<MatchGroup>();
        var breaks = new List<BreakRecord>();

        var allPositions = effectiveRun.SourceSnapshots.SelectMany(s => s.Positions).ToArray();
        var groupedByInstrument = allPositions.GroupBy(static p => p.InstrumentCanonicalId);
        foreach (var instrumentGroup in groupedByInstrument)
        {
            var positions = instrumentGroup.ToArray();
            var resolvedPositionIds = new HashSet<string>(StringComparer.Ordinal);
            var exactGroups = positions.GroupBy(static p => (p.Quantity, p.Price, p.MarketValue)).ToArray();
            foreach (var exactGroup in exactGroups.Where(static g => g.Count() > 1))
            {
                var exactPositions = exactGroup.ToArray();
                var exactEvidence = new MatchEvidence(Guid.NewGuid().ToString("N"), "exact-position-v1", "Exact", "Exact position tuple match.", 0m, new Dictionary<string, string>());
                evidence.Add(exactEvidence);
                matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), BreakClassification.Matched, exactEvidence.RuleId, exactPositions.Select(p => p.PositionId).ToArray(), Array.Empty<string>(), [exactEvidence.EvidenceId])
                {
                    ToleranceProfileId = toleranceProfile.ProfileId,
                    ToleranceProfileVersion = toleranceProfile.Version
                });

                foreach (var position in exactPositions)
                {
                    resolvedPositionIds.Add(position.PositionId);
                }
            }

            var toleranceCandidates = positions
                .Where(position => !resolvedPositionIds.Contains(position.PositionId))
                .ToArray();
            foreach (var toleranceMatch in FindPositionToleranceMatches(toleranceCandidates, toleranceProfile))
            {
                var toleranceEvidence = new MatchEvidence(
                    Guid.NewGuid().ToString("N"),
                    toleranceMatch.Rule.RuleId,
                    "Tolerance",
                    $"Position tolerance rule {toleranceMatch.Rule.RuleId} allowed quantity/price/market value deltas.",
                    toleranceMatch.MaxDelta,
                    new Dictionary<string, string>
                    {
                        ["toleranceProfileId"] = toleranceProfile.ProfileId,
                        ["toleranceProfileVersion"] = toleranceProfile.Version.ToString(),
                        ["toleranceRuleId"] = toleranceMatch.Rule.RuleId
                    });
                evidence.Add(toleranceEvidence);
                matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), BreakClassification.MatchedWithinTolerance, toleranceEvidence.RuleId, toleranceMatch.Positions.Select(p => p.PositionId).ToArray(), Array.Empty<string>(), [toleranceEvidence.EvidenceId])
                {
                    ToleranceProfileId = toleranceProfile.ProfileId,
                    ToleranceProfileVersion = toleranceProfile.Version,
                    ToleranceRuleId = toleranceMatch.Rule.RuleId
                });

                foreach (var position in toleranceMatch.Positions)
                {
                    resolvedPositionIds.Add(position.PositionId);
                }
            }

            var unresolvedPositions = positions
                .Where(position => !resolvedPositionIds.Contains(position.PositionId))
                .ToArray();
            if (unresolvedPositions.Length > 0)
            {
                var breakEvidence = new MatchEvidence(
                    Guid.NewGuid().ToString("N"),
                    "true-break-position-v1",
                    "Exact",
                    "Position has no exact cross-source match.",
                    null,
                    new Dictionary<string, string>
                    {
                        ["unresolvedPositionIds"] = string.Join(",", unresolvedPositions.Select(static position => position.PositionId))
                    });
                evidence.Add(breakEvidence);
                breaks.Add(new BreakRecord(Guid.NewGuid().ToString("N"), BreakClassification.TrueBreak, breakEvidence.RuleId, "No exact matching position candidate found.", effectiveRun.SourceSnapshots.Select(s => s.SnapshotId).ToArray(), [breakEvidence.EvidenceId]));
            }
        }

        var allCash = effectiveRun.SourceSnapshots.SelectMany(s => s.CashEntries).ToArray();
        foreach (var candidate in allCash)
        {
            var near = allCash
                .Select(other =>
                {
                    var rule = FindCashToleranceRule(candidate, other, toleranceProfile, out var allowed);
                    return new { Cash = other, Rule = rule, Allowed = allowed };
                })
                .Where(item => item.Cash.CashEntryId != candidate.CashEntryId && item.Rule is not null)
                .ToArray();
            if (near.Length > 0)
            {
                var first = near[0];
                var rule = first.Rule!;
                var timeDelta = Math.Abs((first.Cash.PostedAtUtc - candidate.PostedAtUtc).TotalMinutes);
                var ev = new MatchEvidence(
                    Guid.NewGuid().ToString("N"),
                    rule.RuleId,
                    "Tolerance",
                    $"Cash tolerance rule {rule.RuleId} allowed amount/settlement-date delta.",
                    Math.Abs(first.Cash.AmountBase - candidate.AmountBase),
                    new Dictionary<string, string>
                    {
                        ["allowedTolerance"] = first.Allowed.ToString(),
                        ["settlementDateDeltaMinutes"] = timeDelta.ToString(),
                        ["toleranceProfileId"] = toleranceProfile.ProfileId,
                        ["toleranceProfileVersion"] = toleranceProfile.Version.ToString(),
                        ["toleranceRuleId"] = rule.RuleId
                    });
                evidence.Add(ev);
                matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), BreakClassification.MatchedWithinTolerance, ev.RuleId, Array.Empty<string>(), near.Select(item => item.Cash).Append(candidate).Select(c => c.CashEntryId).Distinct().ToArray(), [ev.EvidenceId])
                {
                    ToleranceProfileId = toleranceProfile.ProfileId,
                    ToleranceProfileVersion = toleranceProfile.Version,
                    ToleranceRuleId = rule.RuleId
                });
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
                    matches.Add(new MatchGroup(Guid.NewGuid().ToString("N"), BreakClassification.PotentialBreak, ev.RuleId, Array.Empty<string>(), [candidate.CashEntryId, fuzzyHit.CashEntryId], [ev.EvidenceId])
                    {
                        ToleranceProfileId = toleranceProfile.ProfileId,
                        ToleranceProfileVersion = toleranceProfile.Version
                    });
                }
                else
                {
                    var ev = new MatchEvidence(Guid.NewGuid().ToString("N"), "true-break-v1", "Fuzzy", "No exact/tolerance/fuzzy evidence available.", null, new Dictionary<string, string>());
                    evidence.Add(ev);
                    breaks.Add(new BreakRecord(Guid.NewGuid().ToString("N"), BreakClassification.TrueBreak, ev.RuleId, "No matching candidate found.", effectiveRun.SourceSnapshots.Select(s => s.SnapshotId).ToArray(), [ev.EvidenceId]));
                }
            }
        }

        return (matches, breaks, evidence);
    }
    private static CashToleranceRule? FindCashToleranceRule(NormalizedCashEntry candidate, NormalizedCashEntry other, StatementToleranceProfile profile, out decimal allowedTolerance)
    {
        foreach (var rule in profile.CashRules)
        {
            if (rule.Allows(candidate.AmountBase, other.AmountBase, other.PostedAtUtc - candidate.PostedAtUtc, out allowedTolerance))
            {
                return rule;
            }
        }

        allowedTolerance = 0m;
        return null;
    }

    private static IReadOnlyList<PositionToleranceMatch> FindPositionToleranceMatches(
        IReadOnlyList<NormalizedPosition> positions,
        StatementToleranceProfile profile)
    {
        var matches = new List<PositionToleranceMatch>();
        var resolvedPositionIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < positions.Count; i++)
        {
            var left = positions[i];
            if (resolvedPositionIds.Contains(left.PositionId))
            {
                continue;
            }

            for (var j = i + 1; j < positions.Count; j++)
            {
                var right = positions[j];
                if (resolvedPositionIds.Contains(right.PositionId))
                {
                    continue;
                }

                foreach (var candidateRule in profile.PositionRules)
                {
                    if (!candidateRule.Allows(left.Quantity, right.Quantity, left.MarketValue, right.MarketValue, left.Price, right.Price))
                    {
                        continue;
                    }

                    resolvedPositionIds.Add(left.PositionId);
                    resolvedPositionIds.Add(right.PositionId);
                    matches.Add(new PositionToleranceMatch(
                        [left, right],
                        candidateRule,
                        Math.Max(Math.Abs(left.Quantity - right.Quantity), Math.Max(Math.Abs(left.Price - right.Price), Math.Abs(left.MarketValue - right.MarketValue)))));
                    break;
                }

                if (resolvedPositionIds.Contains(left.PositionId))
                {
                    break;
                }
            }
        }

        return matches;
    }

    private sealed record PositionToleranceMatch(IReadOnlyList<NormalizedPosition> Positions, PositionToleranceRule Rule, decimal MaxDelta);
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
