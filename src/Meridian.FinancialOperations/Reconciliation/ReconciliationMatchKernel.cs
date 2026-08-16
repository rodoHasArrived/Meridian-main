using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Deterministic matching primitives shared by the canonical reconciliation floor and, by design,
/// the W9-INGEST-009 statement/ledger sided matcher: stable best-first assignment over scored
/// candidate pairs, bounded same-sign split (one-to-many / many-to-one) discovery, and
/// content-derived identifiers so identical inputs reproduce identical match artifacts across
/// re-runs. Nothing here allocates randomness or reads clocks — determinism is the contract.
/// </summary>
public static class ReconciliationMatchKernel
{
    /// <summary>
    /// Upper bound on the candidate set a split search will consider. Callers pre-rank candidates;
    /// anything beyond this bound is deterministically ignored rather than exploding the search.
    /// </summary>
    public const int MaxSplitSearchCandidates = 24;

    /// <summary>
    /// Selects a non-overlapping assignment from candidate pairs that MUST already be ordered
    /// best-first with a total, deterministic order (score, then domain tie-breakers, then member
    /// ids). Walks the ranking once and takes each pair whose member keys are all unconsumed. Member
    /// keys must be unique across both populations — prefix keys per side when raw ids can collide.
    /// </summary>
    public static IReadOnlyList<TPair> SelectDeterministicAssignment<TPair>(
        IEnumerable<TPair> orderedAdmissiblePairs,
        Func<TPair, IReadOnlyList<string>> memberKeys)
    {
        ArgumentNullException.ThrowIfNull(orderedAdmissiblePairs);
        ArgumentNullException.ThrowIfNull(memberKeys);

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<TPair>();
        foreach (var pair in orderedAdmissiblePairs)
        {
            var keys = memberKeys(pair);
            var free = true;
            foreach (var key in keys)
            {
                if (consumed.Contains(key))
                {
                    free = false;
                    break;
                }
            }

            if (!free)
            {
                continue;
            }

            foreach (var key in keys)
            {
                consumed.Add(key);
            }

            selected.Add(pair);
        }

        return selected;
    }

    /// <summary>One candidate leg offered to <see cref="TryFindSplit"/>.</summary>
    public sealed record SplitCandidate(string Id, decimal Amount);

    /// <summary>
    /// Searches for the best bounded subset of same-sign candidate legs whose sum lands within
    /// <paramref name="tolerance"/> of <paramref name="targetAmount"/>. "Best" is total and
    /// deterministic: smallest absolute residual, then fewest legs, then the lexicographically
    /// smallest sorted leg-id sequence. A split needs at least two legs — single-leg matches belong
    /// to the pair stages. Candidates whose sign differs from the target are ignored (netting
    /// mixed-sign groups is casework, not floor matching), and only the
    /// <see cref="MaxSplitSearchCandidates"/> largest candidates by magnitude participate.
    /// </summary>
    public static bool TryFindSplit(
        decimal targetAmount,
        IReadOnlyList<SplitCandidate> candidates,
        decimal tolerance,
        int maxLegs,
        out IReadOnlyList<SplitCandidate> legs,
        out decimal residual) =>
        TryFindSplit(targetAmount, candidates, tolerance, maxLegs, accept: null, out legs, out residual);

    /// <summary>
    /// <see cref="TryFindSplit(decimal, IReadOnlyList{SplitCandidate}, decimal, int, out IReadOnlyList{SplitCandidate}, out decimal)"/>
    /// with a caller-supplied acceptance validator: only subsets the validator approves compete for
    /// "best", so a subset that wins on the summed amount but fails a secondary constraint (for
    /// example aggregate market value on a position split) does not shadow a fully valid subset.
    /// </summary>
    public static bool TryFindSplit(
        decimal targetAmount,
        IReadOnlyList<SplitCandidate> candidates,
        decimal tolerance,
        int maxLegs,
        Func<IReadOnlyList<SplitCandidate>, bool>? accept,
        out IReadOnlyList<SplitCandidate> legs,
        out decimal residual)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        legs = [];
        residual = 0m;
        if (targetAmount == 0m || maxLegs < 2)
        {
            return false;
        }

        tolerance = Math.Abs(tolerance);
        var sign = Math.Sign(targetAmount);
        var pool = candidates
            .Where(candidate => candidate is not null && Math.Sign(candidate.Amount) == sign)
            .OrderByDescending(static candidate => Math.Abs(candidate.Amount))
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .Take(MaxSplitSearchCandidates)
            .ToArray();
        if (pool.Length < 2)
        {
            return false;
        }

        var targetAbs = Math.Abs(targetAmount);
        // Suffix sums bounded by the remaining leg budget let the search prove early that a branch
        // can no longer reach the tolerance band around the target.
        var amountsAbs = pool.Select(static candidate => Math.Abs(candidate.Amount)).ToArray();

        var bestFound = false;
        var bestResidualAbs = 0m;
        var bestLegs = new List<int>();
        var current = new List<int>(maxLegs);

        void Search(int index, decimal sumAbs)
        {
            if (current.Count >= 2)
            {
                var residualAbs = Math.Abs(targetAbs - sumAbs);
                if (residualAbs <= tolerance
                    && (!bestFound
                        || residualAbs < bestResidualAbs
                        || (residualAbs == bestResidualAbs && current.Count < bestLegs.Count)
                        || (residualAbs == bestResidualAbs && current.Count == bestLegs.Count && CompareLegIds(current, bestLegs) < 0))
                    && (accept is null || accept(current.Select(i => pool[i]).ToArray())))
                {
                    bestFound = true;
                    bestResidualAbs = residualAbs;
                    bestLegs = [.. current];
                }
            }

            if (index >= pool.Length || current.Count >= maxLegs)
            {
                return;
            }

            // Same-sign magnitudes only accumulate, so overshooting the upper band prunes the branch.
            if (sumAbs > targetAbs + tolerance)
            {
                return;
            }

            // Even taking the largest remaining candidates up to the leg budget cannot reach the
            // lower band — nothing below this node can qualify.
            var legBudget = maxLegs - current.Count;
            var reachable = sumAbs;
            for (int i = index, taken = 0; i < pool.Length && taken < legBudget; i++, taken++)
            {
                reachable += amountsAbs[i];
            }

            if (reachable < targetAbs - tolerance)
            {
                return;
            }

            current.Add(index);
            Search(index + 1, sumAbs + amountsAbs[index]);
            current.RemoveAt(current.Count - 1);
            Search(index + 1, sumAbs);
        }

        Search(0, 0m);
        if (!bestFound)
        {
            return false;
        }

        var selected = bestLegs.Select(index => pool[index]).ToArray();
        legs = selected;
        residual = targetAmount - selected.Sum(static leg => leg.Amount);
        return true;

        int CompareLegIds(List<int> left, List<int> right)
        {
            // The tie-break contract compares the SORTED id sequences: indices arrive in
            // magnitude-sorted pool order, which is not lexicographic id order.
            var leftIds = left.Select(i => pool[i].Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
            var rightIds = right.Select(i => pool[i].Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
            var count = Math.Min(leftIds.Length, rightIds.Length);
            for (var i = 0; i < count; i++)
            {
                var compared = string.CompareOrdinal(leftIds[i], rightIds[i]);
                if (compared != 0)
                {
                    return compared;
                }
            }

            return leftIds.Length.CompareTo(rightIds.Length);
        }
    }

    /// <summary>
    /// Derives a stable identifier from match content (idempotency seed, rule, member ids…): the
    /// same inputs always produce the same id, so re-evaluating a cached run re-produces identical
    /// match-group, break, and evidence identifiers instead of minting fresh GUIDs per call.
    /// </summary>
    public static string CreateDeterministicId(string prefix, IReadOnlyList<string> parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(parts);
        var payload = string.Join('\u001f', parts);
        return $"{prefix}-{Sha256Digest.ComputeUtf8(payload)[..24]}";
    }
}
