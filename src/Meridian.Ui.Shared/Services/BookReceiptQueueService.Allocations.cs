using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class BookReceiptQueueService
{
    private static BookReceiptDto[] ValidateReceipts(IReadOnlyList<BookReceiptDto> raw, DateOnly asOf,
        List<string> issues, out Dictionary<string, string> aliases)
    {
        var knownAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        aliases = knownAliases;
        var candidates = raw.Where(row => row.EffectiveDate <= asOf).ToArray();
        var contradictoryKinds = candidates.GroupBy(row => (row.SourceSystem.Trim(), row.SourceReference.Trim()))
            .Where(group => group.Any(static row => row.ReversesReceiptId is null) && group.Any(static row => row.ReversesReceiptId is not null))
            .Select(static group => group.Key).ToHashSet();
        var result = new List<BookReceiptDto>();
        AddGroups(candidates.Where(static row => row.ReversesReceiptId is null), ConflictingIds(candidates));
        // Reimports can name different aliases of the same original payment. Resolve those
        // aliases before comparing reversal payloads, otherwise genuine reversals disappear.
        var normalized = candidates.Select(row => row.ReversesReceiptId is { } reversalId
            ? row with { ReversesReceiptId = knownAliases.GetValueOrDefault(reversalId) ?? reversalId } : row).ToArray();
        AddGroups(normalized.Where(static row => row.ReversesReceiptId is not null), ConflictingIds(normalized));
        return result.OrderBy(static row => row.ReversesReceiptId is not null).ThenBy(static row => row.EffectiveDate)
            .ThenBy(static row => row.ReceiptId, StringComparer.Ordinal).ToArray();

        static HashSet<string> ConflictingIds(IEnumerable<BookReceiptDto> rows)
            => rows.GroupBy(static row => row.ReceiptId, StringComparer.Ordinal)
                .Where(group => group.Select(ReceiptContent).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(static group => group.Key).ToHashSet(StringComparer.Ordinal);

        void AddGroups(IEnumerable<BookReceiptDto> population, IReadOnlySet<string> rejectedIds)
        {
            foreach (var group in population.GroupBy(row => (row.SourceSystem.Trim(), row.SourceReference.Trim())))
            {
                var rows = group.ToArray();
                if (contradictoryKinds.Contains(group.Key) || rows.Any(row => rejectedIds.Contains(row.ReceiptId)) ||
                    rows.Select(ReceiptContent).Distinct(StringComparer.Ordinal).Count() != 1 ||
                    rows.Any(row => string.IsNullOrWhiteSpace(row.ReceiptId) || string.IsNullOrWhiteSpace(row.SourceSystem) ||
                        string.IsNullOrWhiteSpace(row.SourceReference) || row.SecurityId == Guid.Empty || string.IsNullOrWhiteSpace(row.Currency) ||
                        row.Amount == 0m || (row.Amount < 0m && row.ReversesReceiptId is null) || row.EvidenceLinks.Count == 0))
                {
                    issues.Add($"Payment evidence '{group.Key}' is conflicting or incomplete and was not counted.");
                    continue;
                }
                var canonical = rows.OrderBy(static row => row.ReceiptId, StringComparer.Ordinal).First() with
                {
                    EvidenceLinks = rows.SelectMany(static row => row.EvidenceLinks).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                };
                result.Add(canonical);
                foreach (var alias in rows)
                    knownAliases[alias.ReceiptId] = canonical.ReceiptId;
            }
        }
    }

    private static string ReceiptContent(BookReceiptDto row)
        => JsonSerializer.Serialize(row with
        {
            ReceiptId = string.Empty,
            EvidenceLinks = [],
            // FlowType is only an inference hint; exact retained legs already identify the obligation.
            FlowType = row.Allocations.Count > 0 ? null : row.FlowType,
            Allocations = row.Allocations.OrderBy(static leg => leg.ObligationKey, StringComparer.Ordinal).ToArray()
        }, BookReceiptJsonContext.Default.BookReceiptDto);

    private static Dictionary<string, Dictionary<string, decimal>> Allocate(
        IReadOnlyList<BookReceiptDto> receipts, IReadOnlyDictionary<string, BookReceiptExpectationDto> expectations, List<string> issues)
    {
        var result = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);
        var receiptsById = receipts.ToDictionary(static row => row.ReceiptId, StringComparer.Ordinal);
        foreach (var receipt in receipts)
        {
            var amount = Math.Abs(receipt.Amount);
            Dictionary<string, decimal> legs;
            if (receipt.ReversesReceiptId is { } originalId)
            {
                if (!receiptsById.TryGetValue(originalId, out var original) || original.ReversesReceiptId is not null ||
                    original.SecurityId != receipt.SecurityId || original.Currency != receipt.Currency ||
                    original.EffectiveDate > receipt.EffectiveDate || !result.TryGetValue(originalId, out var originalLegs))
                {
                    issues.Add($"Reversal '{receipt.ReceiptId}' has no exact retained original payment allocation.");
                    continue;
                }
                var available = originalLegs.ToDictionary(static pair => pair.Key, pair => pair.Value +
                    receipts.Where(row => row.ReversesReceiptId == originalId && result.ContainsKey(row.ReceiptId))
                        .Sum(row => result[row.ReceiptId].GetValueOrDefault(pair.Key)), StringComparer.Ordinal);
                if (amount > available.Values.Sum())
                {
                    issues.Add($"Reversal '{receipt.ReceiptId}' exceeds the remaining original payment allocation.");
                    continue;
                }
                legs = receipt.Allocations.Count > 0
                    ? receipt.Allocations.GroupBy(static row => row.ObligationKey, StringComparer.Ordinal)
                        .ToDictionary(static group => group.Key, static group => group.Sum(row => row.Amount), StringComparer.Ordinal)
                    : Proportion(amount, available, receipt.Currency);
                if (receipt.Allocations.Any(static row => row.Amount <= 0m) || legs.Values.Sum() != amount ||
                    legs.Any(pair => pair.Value <= 0m || pair.Value > available.GetValueOrDefault(pair.Key)))
                {
                    issues.Add($"Reversal '{receipt.ReceiptId}' has invalid linked allocation amounts.");
                    continue;
                }
                result[receipt.ReceiptId] = legs.ToDictionary(static pair => pair.Key, static pair => -pair.Value, StringComparer.Ordinal);
                continue;
            }
            legs = receipt.Allocations.GroupBy(static row => row.ObligationKey, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Sum(row => row.Amount), StringComparer.Ordinal);
            if (legs.Count == 0)
            {
                var matching = MatchingObligations(receipt, expectations.Values);
                var eligible = matching.Select(row => new ReconciliationMatchKernel.SplitCandidate(row.ObligationKey,
                        Math.Max(0m, row.ExpectedAmount - result.Values.Sum(existing => existing.GetValueOrDefault(row.ObligationKey)))))
                    .Where(static row => row.Amount > 0m).ToArray();
                if (matching.Length == 1)
                    legs[matching[0].ObligationKey] = amount;
                else if (eligible.Length <= 4 && eligible.Sum(static row => row.Amount) == amount &&
                    ReconciliationMatchKernel.TryFindSplit(amount, eligible, 0m, 4, out var split, out _))
                    legs = split.ToDictionary(static row => row.Id, static row => row.Amount, StringComparer.Ordinal);
                else
                {
                    issues.Add($"Payment '{receipt.ReceiptId}' has no unambiguous obligation allocation.");
                    continue;
                }
            }
            if (receipt.Allocations.Any(static row => row.Amount <= 0m) || legs.Values.Any(static value => value <= 0m) ||
                legs.Values.Sum() > amount || legs.Any(pair => !expectations.TryGetValue(pair.Key, out var expected) ||
                    expected.SecurityId != receipt.SecurityId || expected.Currency != receipt.Currency))
            {
                issues.Add($"Payment '{receipt.ReceiptId}' has invalid or excessive allocations and was not counted.");
                continue;
            }
            if (legs.Values.Sum() < amount)
                issues.Add($"Payment '{receipt.ReceiptId}' has an unallocated residual of {(amount - legs.Values.Sum()).ToString(CultureInfo.InvariantCulture)} {receipt.Currency}.");
            result[receipt.ReceiptId] = legs;
        }
        return result;
    }

    private static BookReceiptExpectationDto[] MatchingObligations(BookReceiptDto receipt, IEnumerable<BookReceiptExpectationDto> expectations)
    {
        var candidates = expectations.Where(row => row.SecurityId == receipt.SecurityId && row.Currency == receipt.Currency &&
            row.DueDate == receipt.EffectiveDate).ToArray();
        if (receipt.FlowType is not null)
        {
            if (IsIncome(receipt.FlowType))
            {
                candidates = candidates.Where(static row => IsIncome(row.FlowType)).ToArray();
                if (candidates.Select(static row => row.FlowType).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                    return [];
            }
            else
                candidates = candidates.Where(row => string.Equals(row.FlowType, receipt.FlowType, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        return candidates.OrderBy(static row => row.ObligationKey, StringComparer.Ordinal).ToArray();

        static bool IsIncome(string kind) => string.Equals(kind, "Coupon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "Interest", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> Proportion(decimal amount, IReadOnlyDictionary<string, decimal> available, string currency)
    {
        var remaining = amount;
        var total = available.Values.Sum();
        var nonzero = available.Where(static pair => pair.Value > 0m).OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var index = 0; index < nonzero.Length; index++)
        {
            var leg = nonzero[index];
            var tailCapacity = nonzero.Skip(index + 1).Sum(static item => item.Value);
            var proposed = decimal.Round(amount * leg.Value / total, CurrencyMinorUnits.GetPrecision(currency.ToUpperInvariant()), MidpointRounding.ToEven);
            var value = Math.Min(leg.Value, Math.Min(remaining, Math.Max(remaining - tailCapacity, proposed)));
            if (value > 0m)
                result[leg.Key] = value;
            remaining -= value;
        }
        return result;
    }

    private static BookReceiptPostingDto[] ValidatePostings(
        IReadOnlyList<BookReceiptPostingDto> raw,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> allocations,
        IReadOnlyList<BookReceiptDto> receipts,
        IReadOnlyDictionary<string, BookReceiptExpectationDto> expectations,
        DateOnly asOf, List<string> issues)
    {
        var result = new List<BookReceiptPostingDto>();
        var invalidIds = raw.GroupBy(static row => row.PostingId, StringComparer.Ordinal)
            .Where(group => group.Select(PostingContent).Distinct(StringComparer.Ordinal).Count() != 1)
            .Select(static group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var group in raw.Where(row => row.EffectiveDate <= asOf)
                     .GroupBy(static row => (row.JournalEntryId, row.ReceiptId, row.ObligationKey)))
        {
            var rows = group.ToArray();
            var posting = rows[0];
            if (rows.Any(row => invalidIds.Contains(row.PostingId)) || rows.Select(PostingContent).Distinct(StringComparer.Ordinal).Count() != 1 ||
                string.IsNullOrWhiteSpace(posting.PostingId) || string.IsNullOrWhiteSpace(posting.JournalEntryId) ||
                posting.Amount <= 0m || posting.EvidenceLinks.Count == 0 ||
                !expectations.TryGetValue(posting.ObligationKey, out var expected) || expected.Currency != posting.Currency ||
                !allocations.TryGetValue(posting.ReceiptId, out var legs) || legs.GetValueOrDefault(posting.ObligationKey) < posting.Amount ||
                receipts.All(receipt => receipt.ReceiptId != posting.ReceiptId || receipt.ReversesReceiptId is not null))
            {
                issues.Add($"Posting '{posting.PostingId}' lacks unique posted journal evidence for its exact receipt allocation.");
                continue;
            }
            result.Add(posting);
        }
        foreach (var group in result.Where(static row => !row.IsReversed).GroupBy(static row => (row.ReceiptId, row.ObligationKey)).ToArray())
        {
            if (group.Sum(static row => row.Amount) <= allocations[group.Key.ReceiptId].GetValueOrDefault(group.Key.ObligationKey))
                continue;
            issues.Add($"Posted journals exceed receipt allocation '{group.Key}'.");
            var ids = group.Select(static row => row.PostingId).ToHashSet(StringComparer.Ordinal);
            result.RemoveAll(row => ids.Contains(row.PostingId));
        }
        foreach (var group in result.GroupBy(static row => (row.JournalEntryId, row.Currency)).ToArray())
        {
            var capacities = group.Select(static row => row.JournalCashAmount).Distinct().ToArray();
            var shared = group.Select(static row => row.ReceiptId).Distinct(StringComparer.Ordinal).Count() > 1;
            if (!shared && capacities.All(static value => value is null))
                continue;
            if (capacities.Length == 1 && capacities[0] is > 0m &&
                group.Where(static row => !row.IsReversed).Sum(static row => row.Amount) <= capacities[0])
                continue;
            issues.Add($"Journal '{group.Key}' has missing, conflicting, or exceeded cash-line allocation capacity.");
            result.RemoveAll(row => row.JournalEntryId == group.Key.JournalEntryId && row.Currency == group.Key.Currency);
        }
        return result.OrderBy(static row => row.PostingId, StringComparer.Ordinal).ToArray();
    }

    private static string PostingContent(BookReceiptPostingDto posting)
        => JsonSerializer.Serialize(posting with { PostingId = string.Empty, EvidenceLinks = [] }, BookReceiptJsonContext.Default.BookReceiptPostingDto);
}
