using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Services;

public sealed partial class BookReceiptQueueService
{
    private static Dictionary<string, List<BookReceiptExpectationVersionDto>> BuildHistories(
        IReadOnlyList<BookReceiptRunDto> prior, IReadOnlyList<BookReceiptExpectationDto> incoming, DateOnly asOf, List<string> issues)
    {
        var result = new Dictionary<string, List<BookReceiptExpectationVersionDto>>(StringComparer.Ordinal);
        foreach (var item in prior.SelectMany(static run => run.Items))
        {
            if (!result.TryGetValue(item.ObligationKey, out var versions))
                result[item.ObligationKey] = versions = [];
            foreach (var version in item.ExpectationHistory)
            {
                // Retain each observation's accepted ordering, including restoration of an older version.
                versions.RemoveAll(existing => existing.VersionId == version.VersionId);
                versions.Add(version);
            }
        }
        foreach (var group in incoming.GroupBy(static row => row.ObligationKey, StringComparer.Ordinal))
        {
            var distinct = group.DistinctBy(ExpectationHash).ToArray();
            if (distinct.Length != 1)
            {
                issues.Add($"Obligation '{group.Key}' has conflicting accepted expectations.");
                continue;
            }
            var row = distinct[0];
            if (string.IsNullOrWhiteSpace(row.ObligationKey) || row.SecurityId == Guid.Empty || row.PositionId == Guid.Empty ||
                string.IsNullOrWhiteSpace(row.Currency) || row.Quantity <= 0m || row.ExpectedAmount <= 0m ||
                row.TermsVersionId == Guid.Empty || row.ProjectionRunId == Guid.Empty || row.ProjectedCashFlowId == Guid.Empty ||
                string.IsNullOrWhiteSpace(row.TermsHash) || row.RetainedEvidence.Count == 0 ||
                row.RetainedEvidence.Any(evidence => !RetainedEvidenceIdentityValidator.IsComplete(evidence) || evidence.EffectiveDate > asOf ||
                    DateOnly.FromDateTime(evidence.ReviewedAtUtc.UtcDateTime) > asOf || DateOnly.FromDateTime(evidence.RetainedAtUtc.UtcDateTime) > asOf))
            {
                issues.Add($"Obligation '{row.ObligationKey}' lacks an authoritative holding or accepted projection evidence.");
                continue;
            }
            if (!result.TryGetValue(row.ObligationKey, out var versions))
                result[row.ObligationKey] = versions = [];
            var hash = ExpectationHash(row);
            if (versions.All(existing => existing.VersionId != hash))
                versions.Add(new BookReceiptExpectationVersionDto(hash, asOf, row));
            else if (versions[^1].VersionId != hash)
            {
                var restored = versions.Single(existing => existing.VersionId == hash);
                versions.Remove(restored);
                versions.Add(restored);
            }
        }
        return result;
    }

    private static BookReceiptSourceSnapshotDto Canonicalize(BookReceiptSourceSnapshotDto snapshot)
        => snapshot with
        {
            Expectations = snapshot.Expectations.Select(row => row with
            {
                EvidenceLinks = row.EvidenceLinks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                RetainedEvidence = row.RetainedEvidence.OrderBy(static evidence => evidence.EvidenceId, StringComparer.Ordinal)
                    .ThenBy(static evidence => evidence.EvidenceVersion).ThenBy(static evidence => evidence.ContentHashSha256, StringComparer.Ordinal).ToArray()
            }).OrderBy(static row => row.ObligationKey, StringComparer.Ordinal).ThenBy(ExpectationHash, StringComparer.Ordinal).ToArray(),
            Receipts = snapshot.Receipts.Select(row => row with
            {
                EvidenceLinks = row.EvidenceLinks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                Allocations = row.Allocations.OrderBy(static leg => leg.ObligationKey, StringComparer.Ordinal).ThenBy(static leg => leg.Amount).ToArray()
            }).OrderBy(static row => row.ReceiptId, StringComparer.Ordinal).ThenBy(ReceiptContent, StringComparer.Ordinal).ToArray(),
            Postings = snapshot.Postings.Select(row => row with
            {
                EvidenceLinks = row.EvidenceLinks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            }).OrderBy(static row => row.PostingId, StringComparer.Ordinal).ThenBy(PostingContent, StringComparer.Ordinal).ToArray(),
            Coverage = snapshot.Coverage.OrderBy(static row => row.Area, StringComparer.Ordinal).ThenBy(static row => row.Reason, StringComparer.Ordinal).ToArray()
        };

    private static string ExpectationHash(BookReceiptExpectationDto row)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(row, BookReceiptJsonContext.Default.BookReceiptExpectationDto));

    private static BookReceiptDto[] MergeReceipts(IReadOnlyList<BookReceiptDto> previous, IReadOnlyList<BookReceiptDto> current)
        => previous.Concat(current).DistinctBy(row => JsonSerializer.Serialize(row, BookReceiptJsonContext.Default.BookReceiptDto))
            .OrderBy(static row => row.EffectiveDate).ThenBy(static row => row.ReceiptId, StringComparer.Ordinal)
            .ThenBy(row => JsonSerializer.Serialize(row, BookReceiptJsonContext.Default.BookReceiptDto), StringComparer.Ordinal).ToArray();

    private static BookReceiptDto[] PinRetainedReceiptAllocations(
        IReadOnlyList<BookReceiptRunDto> prior, IReadOnlyList<BookReceiptDto> raw, List<string> issues)
    {
        var retained = prior.SelectMany(static run => run.Items).SelectMany(static item => item.Receipts)
            .Where(static receipt => receipt.Allocations.Count > 0)
            .GroupBy(static receipt => (receipt.SourceSystem.Trim(), receipt.SourceReference.Trim()))
            .ToDictionary(static group => group.Key, group => group
                .Select(static receipt => receipt.Allocations.OrderBy(static leg => leg.ObligationKey, StringComparer.Ordinal).ThenBy(static leg => leg.Amount).ToArray())
                .DistinctBy(AllocationIdentity).ToArray());
        var result = new List<BookReceiptDto>();
        foreach (var receipt in raw)
        {
            if (!retained.TryGetValue((receipt.SourceSystem.Trim(), receipt.SourceReference.Trim()), out var versions))
            {
                result.Add(receipt);
                continue;
            }
            if (versions.Length != 1)
            {
                issues.Add($"Payment '{receipt.ReceiptId}' has conflicting retained allocation decisions.");
                continue;
            }
            // Fill unspecified legs only. Canonical payment validation compares new explicit
            // evidence against the pinned old observation and refuses a contradictory reallocation.
            result.Add(receipt.Allocations.Count == 0 ? receipt with { Allocations = versions[0] } : receipt);
        }
        return result.ToArray();
    }

    private static string AllocationIdentity(IReadOnlyList<BookReceiptAllocationDto> legs)
        => string.Join("|", legs.Select(leg => $"{Uri.EscapeDataString(leg.ObligationKey)}:{leg.Amount.ToString(CultureInfo.InvariantCulture)}"));

    private static BookReceiptPostingDto[] PinRetainedPostingAllocations(
        IReadOnlyList<BookReceiptRunDto> prior, IReadOnlyList<BookReceiptPostingDto> raw,
        IReadOnlyDictionary<string, string> aliases, List<string> issues)
    {
        string CanonicalReceipt(string id) => aliases.GetValueOrDefault(id) ?? id;
        var retained = new Dictionary<(string Journal, string Receipt), BookReceiptPostingDto[]>();
        var byId = new Dictionary<string, BookReceiptPostingDto>(StringComparer.Ordinal);
        foreach (var run in prior)
        {
            var observed = run.Items.SelectMany(static item => item.Postings)
                .Select(row => row with { ReceiptId = CanonicalReceipt(row.ReceiptId) }).DistinctBy(static row => row.PostingId).ToArray();
            foreach (var group in observed.GroupBy(static row => (row.JournalEntryId, row.ReceiptId)))
                retained[group.Key] = group.OrderBy(static row => row.ObligationKey, StringComparer.Ordinal)
                    .ThenBy(static row => row.PostingId, StringComparer.Ordinal).ToArray();
            foreach (var posting in observed)
                byId[posting.PostingId] = posting;
        }
        var result = new List<BookReceiptPostingDto>();
        var rejectedJournals = new HashSet<string>(StringComparer.Ordinal);
        var normalized = raw.Select(row => row with { ReceiptId = CanonicalReceipt(row.ReceiptId) }).ToArray();
        foreach (var duplicate in normalized.GroupBy(static row => row.PostingId, StringComparer.Ordinal)
                     .Where(group => group.Select(PostingContent).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            issues.Add($"Posting '{duplicate.Key}' has conflicting duplicate source observations.");
            foreach (var posting in duplicate)
                rejectedJournals.Add(posting.JournalEntryId);
        }
        foreach (var group in normalized.GroupBy(static row => (row.JournalEntryId, row.ReceiptId)))
        {
            if (rejectedJournals.Contains(group.Key.JournalEntryId))
                continue;
            var current = group.OrderBy(static row => row.ObligationKey, StringComparer.Ordinal)
                .ThenBy(static row => row.PostingId, StringComparer.Ordinal).ToArray();
            if (current.Any(row => byId.TryGetValue(row.PostingId, out var old) &&
                    (row.JournalEntryId != old.JournalEntryId || row.ReceiptId != old.ReceiptId)))
            {
                issues.Add($"Posting identity for journal '{group.Key.JournalEntryId}' changed its retained journal or receipt.");
                rejectedJournals.Add(group.Key.JournalEntryId);
                foreach (var row in current)
                    if (byId.TryGetValue(row.PostingId, out var old))
                        rejectedJournals.Add(old.JournalEntryId);
                continue;
            }
            if (!retained.TryGetValue(group.Key, out var previous))
            {
                result.AddRange(current);
                continue;
            }
            var immutable = current.Select(static row => (row.Currency, row.EffectiveDate, row.JournalCashAmount)).Distinct().ToArray();
            var oldImmutable = previous.Select(static row => (row.Currency, row.EffectiveDate, row.JournalCashAmount)).Distinct().ToArray();
            var sameAllocation = AllocationIdentity(current.Select(static row => new BookReceiptAllocationDto(row.ObligationKey, row.Amount)).Distinct().ToArray())
                == AllocationIdentity(previous.Select(static row => new BookReceiptAllocationDto(row.ObligationKey, row.Amount)).Distinct().ToArray());
            if (immutable.Length != 1 || oldImmutable.Length != 1 || immutable[0] != oldImmutable[0] ||
                current.Any(static row => row.Amount <= 0m) ||
                current.DistinctBy(PostingContent).Sum(static row => row.Amount) != previous.DistinctBy(PostingContent).Sum(static row => row.Amount) ||
                (!sameAllocation && (!current.All(static row => row.IsInferredAllocation) || !previous.All(static row => row.IsInferredAllocation))))
            {
                issues.Add($"Journal '{group.Key.JournalEntryId}' conflicts with its retained posted allocation or immutable cash evidence.");
                rejectedJournals.Add(group.Key.JournalEntryId);
                continue;
            }
            if (!sameAllocation)
                issues.Add($"Inferred posting allocations for journal '{group.Key.JournalEntryId}' changed with expectations; the previously recognized allocation is retained for review.");
            var reversalStates = current.Select(static row => row.IsReversed).Distinct().ToArray();
            if (reversalStates.Length != 1)
            {
                issues.Add($"Journal '{group.Key.JournalEntryId}' has conflicting reversal evidence.");
                rejectedJournals.Add(group.Key.JournalEntryId);
                continue;
            }
            var wasReversed = previous.Any(static row => row.IsReversed);
            if (wasReversed && !reversalStates[0])
                issues.Add($"Journal '{group.Key.JournalEntryId}' no longer supplies its retained reversal evidence.");
            var links = previous.SelectMany(static row => row.EvidenceLinks).Concat(current.SelectMany(static row => row.EvidenceLinks))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            result.AddRange((sameAllocation ? current : previous).Select(row => row with
            {
                IsReversed = wasReversed || reversalStates[0],
                IsInferredAllocation = row.IsInferredAllocation && previous.All(static old => old.IsInferredAllocation),
                EvidenceLinks = sameAllocation
                    ? row.EvidenceLinks.Concat(previous.Where(old => old.ObligationKey == row.ObligationKey && old.Amount == row.Amount)
                            .SelectMany(static old => old.EvidenceLinks))
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                    : links
            }));
        }
        return result.Where(row => !rejectedJournals.Contains(row.JournalEntryId)).ToArray();
    }
}
