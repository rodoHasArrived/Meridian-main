using System.Globalization;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// The canonical reconciliation matching floor. Matches positions and cash entries captured from
/// multiple sources (prime / custodian / administrator / internal ledger) for one business date
/// with the semantics the W9-INGEST-009 statement/ledger sided matcher inherits via
/// <see cref="ReconciliationMatchKernel"/>:
/// <list type="bullet">
/// <item>populations are <b>sided</b> — a candidate pair must span two different source snapshots,
/// so a row never matches its own source and duplicated rows inside one feed surface as breaks;</item>
/// <item>tolerance candidates are <b>scored</b> and assigned best-first with stable tie-breakers
/// instead of first-encountered-wins;</item>
/// <item>unmatched residuals are probed for bounded <b>one-to-many / many-to-one split groups</b>
/// whose shape (anchor, legs, residual) is recorded in <see cref="MatchEvidence"/> attributes;</item>
/// <item>cash timing consults the <see cref="IAccountingCalendar"/>: the exact stage requires the
/// same accounting period and evidence carries business-day distance alongside wall-clock deltas;</item>
/// <item>currencies must agree before amounts are compared, so fail-closed FX normalization (a line
/// kept in its source currency because no rate was available) surfaces as a break instead of a
/// cross-currency mismatch;</item>
/// <item>match-group, break, and evidence identifiers are content-derived from the run's
/// idempotency key, snapshot version, rule, and member ids — re-evaluating the same run yields
/// identical artifacts, making re-runs idempotent.</item>
/// </list>
/// </summary>
public sealed class ReconciliationMatchingEngine
{
    private const string ExactPositionRuleId = "exact-position-v1";
    private const string PositionSplitRuleId = "position-split-v1";
    private const string PositionBreakRuleId = "true-break-position-v1";
    private const string ExactCashRuleId = "exact-cash-v1";
    private const string CashSplitRuleId = "cash-split-v1";
    private const string FuzzyReferenceRuleId = "fuzzy-reference-v1";
    private const string CashBreakRuleId = "true-break-v1";

    private const int MaxSplitLegs = 4;

    // Beyond this many unmatched entries in one account bucket, the comment-containment probe of
    // the fuzzy stage is skipped (reference-equality probes still run): comment containment is the
    // only quadratic-with-large-constants stage left and it exists for operator hints, not
    // correctness.
    private const int MaxFuzzyCommentPopulation = 256;

    private readonly IAccountingCalendar _calendar;

    public ReconciliationMatchingEngine(IAccountingCalendar? calendar = null) =>
        _calendar = calendar ?? BusinessDayAccountingCalendar.Default;

    public (IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence) Run(
        ReconciliationRun run,
        MatchingTolerances tolerances) => Run(run, tolerances.ToStatementToleranceProfile());

    public (IReadOnlyList<MatchGroup> matches, IReadOnlyList<BreakRecord> breaks, IReadOnlyList<MatchEvidence> evidence) Run(
        ReconciliationRun run,
        StatementToleranceProfile toleranceProfile)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(toleranceProfile);

        var effectiveRun = run with
        {
            ToleranceProfileId = toleranceProfile.ProfileId,
            ToleranceProfileVersion = toleranceProfile.Version
        };
        var seed = string.Join(
            ':',
            effectiveRun.IdempotencyKey,
            effectiveRun.SnapshotVersion.ToString(CultureInfo.InvariantCulture),
            toleranceProfile.ProfileId,
            toleranceProfile.Version.ToString(CultureInfo.InvariantCulture));
        var accumulator = new MatchAccumulator(seed, toleranceProfile);

        MatchPositions(effectiveRun, toleranceProfile, accumulator);
        MatchCash(effectiveRun, toleranceProfile, accumulator);

        return (accumulator.Matches, accumulator.Breaks, accumulator.Evidence);
    }

    // ── Positions ──────────────────────────────────────────────────────────────

    private void MatchPositions(ReconciliationRun run, StatementToleranceProfile profile, MatchAccumulator accumulator)
    {
        var sided = new List<SidedPosition>();
        for (var order = 0; order < run.SourceSnapshots.Count; order++)
        {
            var snapshot = run.SourceSnapshots[order];
            foreach (var position in snapshot.Positions)
            {
                sided.Add(new SidedPosition(position, snapshot.SnapshotId, snapshot.SourceType, order));
            }
        }

        foreach (var instrumentGroup in sided
            .GroupBy(static p => p.Position.InstrumentCanonicalId, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            var positions = instrumentGroup.ToArray();
            var consumed = new HashSet<string>(StringComparer.Ordinal);

            MatchExactPositionGroups(positions, consumed, accumulator);
            MatchScoredPositionPairs(positions, consumed, profile, accumulator);
            MatchPositionSplits(positions, consumed, profile, accumulator);
            EmitPositionBreaks(positions, consumed, accumulator);
        }
    }

    private static void MatchExactPositionGroups(
        SidedPosition[] positions,
        HashSet<string> consumed,
        MatchAccumulator accumulator)
    {
        var tupleGroups = positions
            .GroupBy(static p => (p.Position.Quantity, p.Position.Price, p.Position.MarketValue, Currency: NormalizeCurrency(p.Position.Currency)))
            .Where(static g => g.Count() > 1)
            .OrderBy(static g => g.Key.Quantity)
            .ThenBy(static g => g.Key.Price)
            .ThenBy(static g => g.Key.MarketValue)
            .ThenBy(static g => g.Key.Currency, StringComparer.Ordinal);

        foreach (var tupleGroup in tupleGroups)
        {
            // Sided floor: identical tuples inside a single source are duplicated data, not a
            // reconciliation match. Balance the group across snapshots — each matching round takes
            // exactly one row per contributing snapshot, so same-side duplicates in excess of the
            // other sides' rows stay unresolved and surface as breaks instead of being laundered
            // through a cross-source group.
            var queues = tupleGroup
                .GroupBy(static p => p.SnapshotId, StringComparer.Ordinal)
                .Select(static g => new Queue<SidedPosition>(g
                    .OrderBy(static p => p.Position.PositionId, StringComparer.Ordinal)))
                .OrderBy(static q => q.Peek().SourceOrder)
                .ToArray();
            if (queues.Length < 2)
            {
                continue;
            }

            while (queues.Count(static q => q.Count > 0) >= 2)
            {
                var round = queues
                    .Where(static q => q.Count > 0)
                    .Select(static q => q.Dequeue())
                    .ToArray();
                var members = round
                    .OrderBy(static p => p.Position.PositionId, StringComparer.Ordinal)
                    .ToArray();
                var memberIds = members.Select(static m => m.Position.PositionId).ToArray();
                var memberKeys = members.Select(static m => m.Key).ToArray();
                var evidence = accumulator.AddEvidence(
                    "Exact",
                    ExactPositionRuleId,
                    "Exact position tuple match.",
                    0m,
                    memberKeys,
                    new Dictionary<string, string>
                    {
                        ["matchShape"] = memberIds.Length == 2 ? "one-to-one" : "cross-source-group",
                        ["positionIds"] = string.Join(",", memberIds),
                        ["sources"] = string.Join(",", members.Select(static m => m.Source.ToString()).Distinct())
                    });
                accumulator.AddMatch(BreakClassification.Matched, "Exact", ExactPositionRuleId, null, memberIds, [], memberKeys, evidence);
                foreach (var member in members)
                {
                    consumed.Add(member.Key);
                }
            }
        }
    }

    private static void MatchScoredPositionPairs(
        SidedPosition[] positions,
        HashSet<string> consumed,
        StatementToleranceProfile profile,
        MatchAccumulator accumulator)
    {
        if (profile.PositionRules.Count == 0)
        {
            return;
        }

        var open = positions
            .Where(p => !consumed.Contains(p.Key))
            .OrderBy(static p => p.Position.PositionId, StringComparer.Ordinal)
            .ToArray();
        if (open.Length < 2)
        {
            return;
        }

        var candidates = new List<PositionPairCandidate>();
        for (var i = 0; i < open.Length; i++)
        {
            for (var j = i + 1; j < open.Length; j++)
            {
                var first = open[i];
                var second = open[j];
                if (string.Equals(first.SnapshotId, second.SnapshotId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(NormalizeCurrency(first.Position.Currency), NormalizeCurrency(second.Position.Currency), StringComparison.Ordinal))
                {
                    continue;
                }

                var (left, right) = first.SourceOrder <= second.SourceOrder ? (first, second) : (second, first);
                foreach (var rule in profile.PositionRules)
                {
                    if (!rule.Allows(
                        left.Position.Quantity,
                        right.Position.Quantity,
                        left.Position.MarketValue,
                        right.Position.MarketValue,
                        left.Position.Price,
                        right.Position.Price))
                    {
                        continue;
                    }

                    var quantityDelta = Math.Abs(left.Position.Quantity - right.Position.Quantity);
                    var priceDelta = Math.Abs(left.Position.Price - right.Position.Price);
                    var marketValueDelta = Math.Abs(left.Position.MarketValue - right.Position.MarketValue);
                    var score = (0.5m * NormalizedComponent(quantityDelta, rule.QuantityTolerance))
                        + (0.3m * NormalizedComponent(marketValueDelta, rule.MarketValueTolerance))
                        + (0.2m * NormalizedComponent(priceDelta, rule.PriceTolerance));
                    candidates.Add(new PositionPairCandidate(left, right, rule, score, quantityDelta, priceDelta, marketValueDelta));
                    break; // Profile order is rule precedence: the first admissible rule governs.
                }
            }
        }

        var ordered = candidates
            .OrderBy(static c => c.Score)
            .ThenBy(static c => c.MaxDelta)
            .ThenBy(static c => c.Left.Position.PositionId, StringComparer.Ordinal)
            .ThenBy(static c => c.Right.Position.PositionId, StringComparer.Ordinal);
        var assigned = ReconciliationMatchKernel.SelectDeterministicAssignment(
            ordered,
            static c => new[] { c.Left.Key, c.Right.Key });
        foreach (var pair in assigned)
        {
            var memberIds = new[] { pair.Left.Position.PositionId, pair.Right.Position.PositionId };
            var memberKeys = new[] { pair.Left.Key, pair.Right.Key };
            var evidence = accumulator.AddEvidence(
                "Tolerance",
                pair.Rule.RuleId,
                $"Position tolerance rule {pair.Rule.RuleId} allowed quantity/price/market value deltas.",
                pair.MaxDelta,
                memberKeys,
                new Dictionary<string, string>
                {
                    ["toleranceProfileId"] = accumulator.ProfileId,
                    ["toleranceProfileVersion"] = accumulator.ProfileVersionText,
                    ["toleranceRuleId"] = pair.Rule.RuleId,
                    ["matchShape"] = "one-to-one",
                    ["quantityDelta"] = Invariant(pair.QuantityDelta),
                    ["priceDelta"] = Invariant(pair.PriceDelta),
                    ["marketValueDelta"] = Invariant(pair.MarketValueDelta),
                    ["score"] = Invariant(pair.Score),
                    ["leftPositionId"] = pair.Left.Position.PositionId,
                    ["rightPositionId"] = pair.Right.Position.PositionId,
                    ["leftSource"] = pair.Left.Source.ToString(),
                    ["rightSource"] = pair.Right.Source.ToString()
                });
            accumulator.AddMatch(
                BreakClassification.MatchedWithinTolerance,
                "Tolerance",
                pair.Rule.RuleId,
                pair.Rule.RuleId,
                memberIds,
                [],
                memberKeys,
                evidence);
            consumed.Add(pair.Left.Key);
            consumed.Add(pair.Right.Key);
        }
    }

    private static void MatchPositionSplits(
        SidedPosition[] positions,
        HashSet<string> consumed,
        StatementToleranceProfile profile,
        MatchAccumulator accumulator)
    {
        if (profile.PositionRules.Count == 0)
        {
            return;
        }

        var snapshots = positions
            .GroupBy(static p => p.SnapshotId, StringComparer.Ordinal)
            .Select(static g => (SnapshotId: g.Key, g.First().SourceOrder, g.First().Source))
            .OrderBy(static s => s.SourceOrder)
            .ToArray();
        if (snapshots.Length < 2)
        {
            return;
        }

        foreach (var rule in profile.PositionRules)
        {
            foreach (var anchorSnapshot in snapshots)
            {
                foreach (var legSnapshot in snapshots)
                {
                    if (string.Equals(anchorSnapshot.SnapshotId, legSnapshot.SnapshotId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var anchors = positions
                        .Where(p => string.Equals(p.SnapshotId, anchorSnapshot.SnapshotId, StringComparison.Ordinal)
                            && !consumed.Contains(p.Key))
                        .OrderByDescending(static p => Math.Abs(p.Position.Quantity))
                        .ThenBy(static p => p.Position.PositionId, StringComparer.Ordinal)
                        .ToArray();
                    foreach (var anchor in anchors)
                    {
                        if (consumed.Contains(anchor.Key))
                        {
                            continue;
                        }

                        var anchorCurrency = NormalizeCurrency(anchor.Position.Currency);
                        var legPool = positions
                            .Where(p => string.Equals(p.SnapshotId, legSnapshot.SnapshotId, StringComparison.Ordinal)
                                && !consumed.Contains(p.Key)
                                && string.Equals(NormalizeCurrency(p.Position.Currency), anchorCurrency, StringComparison.Ordinal))
                            .ToArray();
                        if (legPool.Length < 2)
                        {
                            continue;
                        }

                        // Quantity discovers the split; the acceptance validator holds every
                        // candidate subset to the same rule's market-value and derived-price
                        // tolerances, so a subset that wins on quantity but fails value cannot
                        // shadow a fully valid subset for the same anchor.
                        var legById = legPool.ToDictionary(static p => p.Position.PositionId, StringComparer.Ordinal);
                        var found = ReconciliationMatchKernel.TryFindSplit(
                            anchor.Position.Quantity,
                            legPool.Select(static p => new ReconciliationMatchKernel.SplitCandidate(p.Position.PositionId, p.Position.Quantity)).ToArray(),
                            rule.QuantityTolerance,
                            MaxSplitLegs,
                            accept: candidateLegs =>
                            {
                                var candidatePositions = candidateLegs.Select(leg => legById[leg.Id]).ToArray();
                                var candidateQuantity = candidatePositions.Sum(static p => p.Position.Quantity);
                                var candidateMarketValue = candidatePositions.Sum(static p => p.Position.MarketValue);
                                if (Math.Abs(anchor.Position.MarketValue - candidateMarketValue) > Math.Abs(rule.MarketValueTolerance))
                                {
                                    return false;
                                }

                                return candidateQuantity == 0m
                                    || Math.Abs((candidateMarketValue / candidateQuantity) - anchor.Position.Price) <= Math.Abs(rule.PriceTolerance);
                            },
                            out var legs,
                            out var quantityResidual);
                        if (!found)
                        {
                            continue;
                        }

                        var legPositions = legs.Select(leg => legById[leg.Id]).ToArray();
                        var legMarketValue = legPositions.Sum(static p => p.Position.MarketValue);
                        var marketValueDelta = Math.Abs(anchor.Position.MarketValue - legMarketValue);
                        var legIds = legPositions.Select(static p => p.Position.PositionId).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
                        var memberIds = new[] { anchor.Position.PositionId }.Concat(legIds).ToArray();
                        var memberKeys = new[] { anchor.Key }.Concat(legPositions.Select(static p => p.Key)).ToArray();
                        var shape = anchor.SourceOrder <= legSnapshot.SourceOrder ? "one-to-many" : "many-to-one";
                        var evidence = accumulator.AddEvidence(
                            "Split",
                            PositionSplitRuleId,
                            $"Position split: {legIds.Length} {legSnapshot.Source} lots aggregate to one {anchor.Source} position within tolerance rule {rule.RuleId}.",
                            Math.Abs(quantityResidual),
                            memberKeys,
                            new Dictionary<string, string>
                            {
                                ["matchShape"] = shape,
                                ["anchorPositionId"] = anchor.Position.PositionId,
                                ["anchorSource"] = anchor.Source.ToString(),
                                ["legPositionIds"] = string.Join(",", legIds),
                                ["legSource"] = legSnapshot.Source.ToString(),
                                ["quantityResidual"] = Invariant(quantityResidual),
                                ["marketValueDelta"] = Invariant(marketValueDelta),
                                ["toleranceProfileId"] = accumulator.ProfileId,
                                ["toleranceProfileVersion"] = accumulator.ProfileVersionText,
                                ["toleranceRuleId"] = rule.RuleId
                            });
                        accumulator.AddMatch(
                            BreakClassification.MatchedWithinTolerance,
                            "Split",
                            PositionSplitRuleId,
                            rule.RuleId,
                            memberIds,
                            [],
                            memberKeys,
                            evidence);
                        consumed.Add(anchor.Key);
                        foreach (var legPosition in legPositions)
                        {
                            consumed.Add(legPosition.Key);
                        }
                    }
                }
            }
        }
    }

    private static void EmitPositionBreaks(
        SidedPosition[] positions,
        HashSet<string> consumed,
        MatchAccumulator accumulator)
    {
        var unresolved = positions
            .Where(p => !consumed.Contains(p.Key))
            .OrderBy(static p => p.Position.PositionId, StringComparer.Ordinal)
            .ToArray();
        if (unresolved.Length == 0)
        {
            return;
        }

        var memberIds = unresolved.Select(static p => p.Position.PositionId).ToArray();
        var memberKeys = unresolved.Select(static p => p.Key).ToArray();
        var snapshotIds = unresolved.Select(static p => p.SnapshotId).Distinct(StringComparer.Ordinal).ToArray();
        var evidence = accumulator.AddEvidence(
            "Break",
            PositionBreakRuleId,
            "Position has no cross-source match within the tolerance profile.",
            null,
            memberKeys,
            new Dictionary<string, string>
            {
                ["unresolvedPositionIds"] = string.Join(",", memberIds),
                ["instrumentCanonicalId"] = unresolved[0].Position.InstrumentCanonicalId
            });
        accumulator.AddBreak(
            "Break",
            PositionBreakRuleId,
            "No matching position candidate found.",
            snapshotIds,
            memberKeys,
            evidence);
    }

    // ── Cash ───────────────────────────────────────────────────────────────────

    private void MatchCash(ReconciliationRun run, StatementToleranceProfile profile, MatchAccumulator accumulator)
    {
        var sided = new List<SidedCash>();
        for (var order = 0; order < run.SourceSnapshots.Count; order++)
        {
            var snapshot = run.SourceSnapshots[order];
            foreach (var entry in snapshot.CashEntries)
            {
                sided.Add(new SidedCash(entry, snapshot.SnapshotId, snapshot.SourceType, order, _calendar.ResolvePeriod(entry.PostedAtUtc)));
            }
        }

        // Cash reconciles inside an account and a settlement currency: fail-closed FX keeps an
        // unconvertible line in its source currency, which lands it in a separate bucket and
        // surfaces it as a break rather than a cross-currency pseudo-match.
        foreach (var bucket in sided
            .GroupBy(static c => (
                Account: (c.Entry.AccountId ?? string.Empty).Trim().ToUpperInvariant(),
                Currency: NormalizeCurrency(c.Entry.BaseCurrency)))
            .OrderBy(static g => g.Key.Account, StringComparer.Ordinal)
            .ThenBy(static g => g.Key.Currency, StringComparer.Ordinal))
        {
            var entries = bucket
                .OrderBy(static c => c.Entry.AmountBase)
                .ThenBy(static c => c.Entry.CashEntryId, StringComparer.Ordinal)
                .ToArray();
            var consumed = new HashSet<string>(StringComparer.Ordinal);

            MatchExactCashPairs(entries, consumed, accumulator);
            MatchScoredCashPairs(entries, consumed, profile, accumulator);
            MatchCashSplits(entries, consumed, profile, accumulator);
            MatchFuzzyCashReferences(entries, consumed, accumulator);
            EmitCashBreaks(entries, consumed, accumulator);
        }
    }

    private void MatchExactCashPairs(SidedCash[] entries, HashSet<string> consumed, MatchAccumulator accumulator)
    {
        var candidates = new List<CashPairCandidate>();
        // Entries are amount-sorted, so equal-amount runs are contiguous.
        for (var i = 0; i < entries.Length; i++)
        {
            for (var j = i + 1; j < entries.Length && entries[j].Entry.AmountBase == entries[i].Entry.AmountBase; j++)
            {
                var (left, right) = OrderCashPair(entries[i], entries[j]);
                if (string.Equals(left.SnapshotId, right.SnapshotId, StringComparison.Ordinal) || left.Period != right.Period)
                {
                    continue;
                }

                candidates.Add(new CashPairCandidate(left, right, Rule: null, Score: 0m, AmountDelta: 0m, AllowedTolerance: 0m));
            }
        }

        // Equal amounts in the same period can repeat on both sides: strong reference agreement and
        // posting proximity outrank the ordinal-id tie-breakers so unrelated transactions do not
        // cross-pair when settlement ids or counterparty references identify the true counterparts.
        var ordered = candidates
            .OrderBy(static c => HasReferenceAgreement(c.Left.Entry, c.Right.Entry) ? 0 : 1)
            .ThenBy(static c => Math.Abs((c.Right.Entry.PostedAtUtc - c.Left.Entry.PostedAtUtc).Ticks))
            .ThenBy(static c => c.Left.Entry.CashEntryId, StringComparer.Ordinal)
            .ThenBy(static c => c.Right.Entry.CashEntryId, StringComparer.Ordinal);
        foreach (var pair in ReconciliationMatchKernel.SelectDeterministicAssignment(ordered, static c => new[] { c.Left.Key, c.Right.Key }))
        {
            var memberIds = new[] { pair.Left.Entry.CashEntryId, pair.Right.Entry.CashEntryId };
            var memberKeys = new[] { pair.Left.Key, pair.Right.Key };
            var postedDeltaMinutes = Math.Abs((pair.Right.Entry.PostedAtUtc - pair.Left.Entry.PostedAtUtc).TotalMinutes);
            var evidence = accumulator.AddEvidence(
                "Exact",
                ExactCashRuleId,
                "Cash amounts are equal within the same accounting period.",
                0m,
                memberKeys,
                new Dictionary<string, string>
                {
                    ["matchShape"] = "one-to-one",
                    ["accountingPeriod"] = pair.Left.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["businessDayDelta"] = "0",
                    ["postedDeltaMinutes"] = postedDeltaMinutes.ToString(CultureInfo.InvariantCulture),
                    ["leftSource"] = pair.Left.Source.ToString(),
                    ["rightSource"] = pair.Right.Source.ToString()
                });
            accumulator.AddMatch(BreakClassification.Matched, "Exact", ExactCashRuleId, null, [], memberIds, memberKeys, evidence);
            consumed.Add(pair.Left.Key);
            consumed.Add(pair.Right.Key);
        }
    }

    private void MatchScoredCashPairs(
        SidedCash[] entries,
        HashSet<string> consumed,
        StatementToleranceProfile profile,
        MatchAccumulator accumulator)
    {
        if (profile.CashRules.Count == 0)
        {
            return;
        }

        var open = entries.Where(c => !consumed.Contains(c.Key)).ToArray();
        if (open.Length < 2)
        {
            return;
        }

        // Amount window that bounds candidate generation: no rule can allow a pair whose base-amount
        // delta exceeds the largest absolute tolerance or the largest basis-point tolerance applied
        // to the largest amount in the bucket.
        var maxAbsolute = profile.CashRules.Max(static r => Math.Abs(r.AbsoluteCashTolerance));
        var maxBasisPoints = profile.CashRules.Max(static r => Math.Abs(r.BasisPointCashTolerance ?? 0m));
        var maxAmount = open.Max(static c => Math.Abs(c.Entry.AmountBase));
        var window = Math.Max(maxAbsolute, maxAmount * maxBasisPoints / 10_000m);

        var candidates = new List<CashPairCandidate>();
        for (var i = 0; i < open.Length; i++)
        {
            for (var j = i + 1; j < open.Length && open[j].Entry.AmountBase - open[i].Entry.AmountBase <= window; j++)
            {
                var (left, right) = OrderCashPair(open[i], open[j]);
                if (string.Equals(left.SnapshotId, right.SnapshotId, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var rule in profile.CashRules)
                {
                    if (!rule.Allows(left.Entry.AmountBase, right.Entry.AmountBase, right.Entry.PostedAtUtc - left.Entry.PostedAtUtc, out var allowed))
                    {
                        continue;
                    }

                    var amountDelta = Math.Abs(right.Entry.AmountBase - left.Entry.AmountBase);
                    var deltaMinutes = Math.Abs((right.Entry.PostedAtUtc - left.Entry.PostedAtUtc).TotalMinutes);
                    var windowMinutes = Math.Abs(rule.SettlementDateTolerance.TotalMinutes);
                    var amountComponent = allowed == 0m ? 0m : amountDelta / allowed;
                    var timeComponent = windowMinutes == 0d ? 0m : (decimal)(deltaMinutes / windowMinutes);
                    var referenceBonus = HasReferenceAgreement(left.Entry, right.Entry) ? 0.2m : 0m;
                    var score = Math.Max(0m, (0.7m * amountComponent) + (0.3m * timeComponent) - referenceBonus);
                    candidates.Add(new CashPairCandidate(left, right, rule, score, amountDelta, allowed));
                    break; // Profile order is rule precedence: the first admissible rule governs.
                }
            }
        }

        var ordered = candidates
            .OrderBy(static c => c.Score)
            .ThenBy(static c => c.AmountDelta)
            .ThenBy(c => Math.Abs((c.Right.Entry.PostedAtUtc - c.Left.Entry.PostedAtUtc).Ticks))
            .ThenBy(static c => c.Left.Entry.CashEntryId, StringComparer.Ordinal)
            .ThenBy(static c => c.Right.Entry.CashEntryId, StringComparer.Ordinal);
        foreach (var pair in ReconciliationMatchKernel.SelectDeterministicAssignment(ordered, static c => new[] { c.Left.Key, c.Right.Key }))
        {
            var rule = pair.Rule!;
            var memberIds = new[] { pair.Left.Entry.CashEntryId, pair.Right.Entry.CashEntryId };
            var memberKeys = new[] { pair.Left.Key, pair.Right.Key };
            var deltaMinutes = Math.Abs((pair.Right.Entry.PostedAtUtc - pair.Left.Entry.PostedAtUtc).TotalMinutes);
            var businessDayDelta = _calendar.CountBusinessDaysBetween(pair.Left.Period, pair.Right.Period);
            var evidence = accumulator.AddEvidence(
                "Tolerance",
                rule.RuleId,
                $"Cash tolerance rule {rule.RuleId} allowed amount/settlement-date delta.",
                pair.AmountDelta,
                memberKeys,
                new Dictionary<string, string>
                {
                    ["allowedTolerance"] = Invariant(pair.AllowedTolerance),
                    ["settlementDateDeltaMinutes"] = deltaMinutes.ToString(CultureInfo.InvariantCulture),
                    ["businessDayDelta"] = businessDayDelta.ToString(CultureInfo.InvariantCulture),
                    ["amountDeltaBase"] = Invariant(pair.AmountDelta),
                    ["score"] = Invariant(pair.Score),
                    ["matchShape"] = "one-to-one",
                    ["leftCashEntryId"] = pair.Left.Entry.CashEntryId,
                    ["rightCashEntryId"] = pair.Right.Entry.CashEntryId,
                    ["leftSource"] = pair.Left.Source.ToString(),
                    ["rightSource"] = pair.Right.Source.ToString(),
                    ["toleranceProfileId"] = accumulator.ProfileId,
                    ["toleranceProfileVersion"] = accumulator.ProfileVersionText,
                    ["toleranceRuleId"] = rule.RuleId
                });
            accumulator.AddMatch(
                BreakClassification.MatchedWithinTolerance,
                "Tolerance",
                rule.RuleId,
                rule.RuleId,
                [],
                memberIds,
                memberKeys,
                evidence);
            consumed.Add(pair.Left.Key);
            consumed.Add(pair.Right.Key);
        }
    }

    private void MatchCashSplits(
        SidedCash[] entries,
        HashSet<string> consumed,
        StatementToleranceProfile profile,
        MatchAccumulator accumulator)
    {
        if (profile.CashRules.Count == 0)
        {
            return;
        }

        var snapshots = entries
            .GroupBy(static c => c.SnapshotId, StringComparer.Ordinal)
            .Select(static g => (SnapshotId: g.Key, g.First().SourceOrder, g.First().Source))
            .OrderBy(static s => s.SourceOrder)
            .ToArray();
        if (snapshots.Length < 2)
        {
            return;
        }

        foreach (var rule in profile.CashRules)
        {
            var settlementWindow = rule.SettlementDateTolerance.Duration();
            foreach (var anchorSnapshot in snapshots)
            {
                foreach (var legSnapshot in snapshots)
                {
                    if (string.Equals(anchorSnapshot.SnapshotId, legSnapshot.SnapshotId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var anchors = entries
                        .Where(c => string.Equals(c.SnapshotId, anchorSnapshot.SnapshotId, StringComparison.Ordinal)
                            && !consumed.Contains(c.Key))
                        .OrderByDescending(static c => Math.Abs(c.Entry.AmountBase))
                        .ThenBy(static c => c.Entry.CashEntryId, StringComparer.Ordinal)
                        .ToArray();
                    foreach (var anchor in anchors)
                    {
                        if (consumed.Contains(anchor.Key))
                        {
                            continue;
                        }

                        var allowed = Math.Max(
                            Math.Abs(rule.AbsoluteCashTolerance),
                            rule.BasisPointCashTolerance is { } basisPoints
                                ? Math.Abs(anchor.Entry.AmountBase) * Math.Abs(basisPoints) / 10_000m
                                : 0m);
                        var legPool = entries
                            .Where(c => string.Equals(c.SnapshotId, legSnapshot.SnapshotId, StringComparison.Ordinal)
                                && !consumed.Contains(c.Key)
                                && (c.Entry.PostedAtUtc - anchor.Entry.PostedAtUtc).Duration() <= settlementWindow)
                            .ToArray();
                        if (legPool.Length < 2)
                        {
                            continue;
                        }

                        var legById = legPool.ToDictionary(static c => c.Entry.CashEntryId, StringComparer.Ordinal);
                        var found = ReconciliationMatchKernel.TryFindSplit(
                            anchor.Entry.AmountBase,
                            legPool.Select(static c => new ReconciliationMatchKernel.SplitCandidate(c.Entry.CashEntryId, c.Entry.AmountBase)).ToArray(),
                            allowed,
                            MaxSplitLegs,
                            out var legs,
                            out var residual);
                        if (!found)
                        {
                            continue;
                        }

                        var legEntries = legs.Select(leg => legById[leg.Id]).ToArray();
                        var legIds = legEntries.Select(static c => c.Entry.CashEntryId).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
                        var memberIds = new[] { anchor.Entry.CashEntryId }.Concat(legIds).ToArray();
                        var memberKeys = new[] { anchor.Key }.Concat(legEntries.Select(static c => c.Key)).ToArray();
                        var shape = anchor.SourceOrder <= legSnapshot.SourceOrder ? "one-to-many" : "many-to-one";
                        var businessDaySpread = legEntries
                            .Select(leg => Math.Abs(_calendar.CountBusinessDaysBetween(anchor.Period, leg.Period)))
                            .DefaultIfEmpty(0)
                            .Max();
                        var evidence = accumulator.AddEvidence(
                            "Split",
                            CashSplitRuleId,
                            $"Cash split: {legIds.Length} {legSnapshot.Source} entries sum to one {anchor.Source} entry within tolerance rule {rule.RuleId}.",
                            Math.Abs(residual),
                            memberKeys,
                            new Dictionary<string, string>
                            {
                                ["matchShape"] = shape,
                                ["anchorCashEntryId"] = anchor.Entry.CashEntryId,
                                ["anchorSource"] = anchor.Source.ToString(),
                                ["legCashEntryIds"] = string.Join(",", legIds),
                                ["legSource"] = legSnapshot.Source.ToString(),
                                ["amountResidualBase"] = Invariant(residual),
                                ["allowedTolerance"] = Invariant(allowed),
                                ["businessDaySpread"] = businessDaySpread.ToString(CultureInfo.InvariantCulture),
                                ["toleranceProfileId"] = accumulator.ProfileId,
                                ["toleranceProfileVersion"] = accumulator.ProfileVersionText,
                                ["toleranceRuleId"] = rule.RuleId
                            });
                        accumulator.AddMatch(
                            BreakClassification.MatchedWithinTolerance,
                            "Split",
                            CashSplitRuleId,
                            rule.RuleId,
                            [],
                            memberIds,
                            memberKeys,
                            evidence);
                        consumed.Add(anchor.Key);
                        foreach (var legEntry in legEntries)
                        {
                            consumed.Add(legEntry.Key);
                        }
                    }
                }
            }
        }
    }

    private static void MatchFuzzyCashReferences(SidedCash[] entries, HashSet<string> consumed, MatchAccumulator accumulator)
    {
        var open = entries
            .Where(c => !consumed.Contains(c.Key))
            .OrderBy(static c => c.Entry.CashEntryId, StringComparer.Ordinal)
            .ToArray();
        if (open.Length < 2)
        {
            return;
        }

        var probeComments = open.Length <= MaxFuzzyCommentPopulation;
        var candidates = new List<(SidedCash Left, SidedCash Right, string MatchedOn)>();
        for (var i = 0; i < open.Length; i++)
        {
            for (var j = i + 1; j < open.Length; j++)
            {
                var (left, right) = OrderCashPair(open[i], open[j]);
                if (string.Equals(left.SnapshotId, right.SnapshotId, StringComparison.Ordinal))
                {
                    continue;
                }

                var matchedOn = FuzzyReferenceAgreement(left.Entry, right.Entry, probeComments);
                if (matchedOn is null)
                {
                    continue;
                }

                candidates.Add((left, right, matchedOn));
            }
        }

        var ordered = candidates
            .OrderBy(static c => c.Left.Entry.CashEntryId, StringComparer.Ordinal)
            .ThenBy(static c => c.Right.Entry.CashEntryId, StringComparer.Ordinal);
        foreach (var pair in ReconciliationMatchKernel.SelectDeterministicAssignment(ordered, static c => new[] { c.Left.Key, c.Right.Key }))
        {
            var memberIds = new[] { pair.Left.Entry.CashEntryId, pair.Right.Entry.CashEntryId };
            var memberKeys = new[] { pair.Left.Key, pair.Right.Key };
            var evidence = accumulator.AddEvidence(
                "Fuzzy",
                FuzzyReferenceRuleId,
                "Reference-level fuzzy hit.",
                null,
                memberKeys,
                new Dictionary<string, string>
                {
                    ["matchShape"] = "one-to-one",
                    ["matchedOn"] = pair.MatchedOn,
                    ["leftCashEntryId"] = pair.Left.Entry.CashEntryId,
                    ["rightCashEntryId"] = pair.Right.Entry.CashEntryId
                });
            accumulator.AddMatch(BreakClassification.PotentialBreak, "Fuzzy", FuzzyReferenceRuleId, null, [], memberIds, memberKeys, evidence);
            consumed.Add(pair.Left.Key);
            consumed.Add(pair.Right.Key);
        }
    }

    private static void EmitCashBreaks(SidedCash[] entries, HashSet<string> consumed, MatchAccumulator accumulator)
    {
        foreach (var entry in entries
            .Where(c => !consumed.Contains(c.Key))
            .OrderBy(static c => c.Entry.CashEntryId, StringComparer.Ordinal))
        {
            var memberKeys = new[] { entry.Key };
            var evidence = accumulator.AddEvidence(
                "Break",
                CashBreakRuleId,
                "No exact/tolerance/fuzzy evidence available.",
                null,
                memberKeys,
                new Dictionary<string, string>
                {
                    ["cashEntryId"] = entry.Entry.CashEntryId,
                    ["accountId"] = entry.Entry.AccountId ?? string.Empty,
                    ["baseCurrency"] = entry.Entry.BaseCurrency ?? string.Empty,
                    ["amountBase"] = Invariant(entry.Entry.AmountBase),
                    ["accountingPeriod"] = entry.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["source"] = entry.Source.ToString()
                });
            accumulator.AddBreak(
                "Break",
                CashBreakRuleId,
                "No matching candidate found.",
                [entry.SnapshotId],
                memberKeys,
                evidence);
        }
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────

    private static (SidedCash Left, SidedCash Right) OrderCashPair(SidedCash first, SidedCash second)
    {
        if (first.SourceOrder != second.SourceOrder)
        {
            return first.SourceOrder < second.SourceOrder ? (first, second) : (second, first);
        }

        return string.CompareOrdinal(first.Entry.CashEntryId, second.Entry.CashEntryId) <= 0
            ? (first, second)
            : (second, first);
    }

    private static bool HasReferenceAgreement(NormalizedCashEntry left, NormalizedCashEntry right) =>
        (!string.IsNullOrWhiteSpace(left.CounterpartyReference)
            && string.Equals(left.CounterpartyReference, right.CounterpartyReference, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(left.SettlementId)
            && string.Equals(left.SettlementId, right.SettlementId, StringComparison.OrdinalIgnoreCase));

    private static string? FuzzyReferenceAgreement(NormalizedCashEntry left, NormalizedCashEntry right, bool probeComments)
    {
        if (!string.IsNullOrWhiteSpace(left.CounterpartyReference)
            && string.Equals(left.CounterpartyReference, right.CounterpartyReference, StringComparison.OrdinalIgnoreCase))
        {
            return "counterpartyReference";
        }

        if (!string.IsNullOrWhiteSpace(left.SettlementId)
            && string.Equals(left.SettlementId, right.SettlementId, StringComparison.OrdinalIgnoreCase))
        {
            return "settlementId";
        }

        if (probeComments
            && !string.IsNullOrWhiteSpace(left.Comment)
            && !string.IsNullOrWhiteSpace(right.Comment)
            && (right.Comment.Contains(left.Comment, StringComparison.OrdinalIgnoreCase)
                || left.Comment.Contains(right.Comment, StringComparison.OrdinalIgnoreCase)))
        {
            return "comment";
        }

        return null;
    }

    private static decimal NormalizedComponent(decimal delta, decimal tolerance)
    {
        var bound = Math.Abs(tolerance);
        return bound == 0m ? 0m : Math.Abs(delta) / bound;
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? string.Empty : currency.Trim().ToUpperInvariant();

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private readonly record struct SidedPosition(
        NormalizedPosition Position,
        string SnapshotId,
        ReconciliationSourceType Source,
        int SourceOrder)
    {
        /// <summary>
        /// Snapshot-qualified consumption key: independent sources may legally reuse source-local
        /// row ids, so bookkeeping must never treat two same-id rows from different snapshots as
        /// one row.
        /// </summary>
        public string Key => SnapshotId + "\u001f" + Position.PositionId;
    }

    private readonly record struct SidedCash(
        NormalizedCashEntry Entry,
        string SnapshotId,
        ReconciliationSourceType Source,
        int SourceOrder,
        DateOnly Period)
    {
        /// <summary>Snapshot-qualified consumption key; see <see cref="SidedPosition.Key"/>.</summary>
        public string Key => SnapshotId + "\u001f" + Entry.CashEntryId;
    }

    private sealed record PositionPairCandidate(
        SidedPosition Left,
        SidedPosition Right,
        PositionToleranceRule Rule,
        decimal Score,
        decimal QuantityDelta,
        decimal PriceDelta,
        decimal MarketValueDelta)
    {
        public decimal MaxDelta => Math.Max(QuantityDelta, Math.Max(PriceDelta, MarketValueDelta));
    }

    private sealed record CashPairCandidate(
        SidedCash Left,
        SidedCash Right,
        CashToleranceRule? Rule,
        decimal Score,
        decimal AmountDelta,
        decimal AllowedTolerance);

    /// <summary>
    /// Collects matches, breaks, and evidence with content-derived identifiers: every artifact id is
    /// a hash of the run's idempotency seed, stage, rule, and sorted member ids, so re-evaluating
    /// the same run produces byte-identical artifacts.
    /// </summary>
    private sealed class MatchAccumulator(string seed, StatementToleranceProfile profile)
    {
        public List<MatchGroup> Matches { get; } = [];

        public List<BreakRecord> Breaks { get; } = [];

        public List<MatchEvidence> Evidence { get; } = [];

        public string ProfileId { get; } = profile.ProfileId;

        public string ProfileVersionText { get; } = profile.Version.ToString(CultureInfo.InvariantCulture);

        private readonly StatementToleranceProfile _profile = profile;

        /// <summary>
        /// <paramref name="memberKeys"/> are the snapshot-qualified member keys used ONLY for id
        /// derivation: sources may legally reuse raw row ids, so two artifacts over same-id rows
        /// from different snapshots must still hash to distinct identifiers.
        /// </summary>
        public MatchEvidence AddEvidence(
            string stage,
            string ruleId,
            string narrative,
            decimal? numericDelta,
            IReadOnlyList<string> memberKeys,
            Dictionary<string, string> attributes)
        {
            var idParts = new List<string>(memberKeys.Count + 3) { seed, stage, ruleId };
            idParts.AddRange(memberKeys.OrderBy(static key => key, StringComparer.Ordinal));
            var evidence = new MatchEvidence(
                ReconciliationMatchKernel.CreateDeterministicId("ev", idParts),
                ruleId,
                stage,
                narrative,
                numericDelta,
                attributes);
            Evidence.Add(evidence);
            return evidence;
        }

        public void AddMatch(
            BreakClassification classification,
            string stage,
            string ruleId,
            string? toleranceRuleId,
            IReadOnlyList<string> positionIds,
            IReadOnlyList<string> cashEntryIds,
            IReadOnlyList<string> memberKeys,
            MatchEvidence evidence)
        {
            var idParts = new List<string>(memberKeys.Count + 3) { seed, stage, ruleId };
            idParts.AddRange(memberKeys.OrderBy(static key => key, StringComparer.Ordinal));
            Matches.Add(new MatchGroup(
                ReconciliationMatchKernel.CreateDeterministicId("mg", idParts),
                classification,
                ruleId,
                positionIds,
                cashEntryIds,
                [evidence.EvidenceId])
            {
                ToleranceProfileId = _profile.ProfileId,
                ToleranceProfileVersion = _profile.Version,
                ToleranceRuleId = toleranceRuleId
            });
        }

        public void AddBreak(
            string stage,
            string ruleId,
            string reason,
            IReadOnlyList<string> snapshotIds,
            IReadOnlyList<string> memberKeys,
            MatchEvidence evidence)
        {
            var idParts = new List<string>(memberKeys.Count + 3) { seed, stage, ruleId };
            idParts.AddRange(memberKeys.OrderBy(static key => key, StringComparer.Ordinal));
            Breaks.Add(new BreakRecord(
                ReconciliationMatchKernel.CreateDeterministicId("br", idParts),
                BreakClassification.TrueBreak,
                ruleId,
                reason,
                snapshotIds,
                [evidence.EvidenceId]));
        }
    }
}
