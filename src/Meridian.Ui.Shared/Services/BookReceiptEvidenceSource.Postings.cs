using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class BookReceiptEvidenceSource
{
    private static void ProjectPostings(IReadOnlyList<StatementRunMatchGroupRecord> groups,
        IReadOnlyDictionary<string, BookReceiptDto> byEvidence, IReadOnlyList<LedgerJournalEntryRecord> ledger,
        string baseCurrency, Guid fundAccount, string externalAccount, DateOnly asOfDate, IReadOnlyList<BookReceiptExpectationDto> expectations,
        List<BookReceiptPostingDto> postings, List<BookReceiptCoverageDto> coverage)
    {
        var reversed = ReversedJournals(ledger, asOfDate);
        foreach (var group in groups)
        {
            var linked = group.StatementEvidenceReferences.Where(byEvidence.ContainsKey).Select(e => byEvidence[e])
                .DistinctBy(r => r.ReceiptId).ToArray();
            if (linked.Length == 0)
                continue;
            var ids = group.InternalEvidenceReferences.Where(r => r.StartsWith("internal:journal:", StringComparison.Ordinal))
                .Select(r => r["internal:journal:".Length..]).Where(r => Guid.TryParse(r, out _)).Select(Guid.Parse).ToHashSet();
            var records = ledger.Where(r => ids.Contains(r.Entry.JournalEntryId)).ToArray();
            if (linked.Length != group.StatementEvidenceReferences.Count || records.Length != ids.Count || ids.Count == 0
                || (linked.Length > 1 && records.Length > 1)
                || linked.Select(r => r.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            {
                coverage.Add(new("posting-allocation", false, "A retained journal match lacks unambiguous payment membership."));
                continue;
            }
            var currency = linked[0].Currency;
            var cash = records.Where(r => Tag(r.Entry.Metadata, LedgerJournalReversal.ReversalOfTag) is null)
                .Select(r => (Record: r, Amount: r.Entry.Lines.Where(IsCashLine)
                    .Where(l => Equal(l.Account.FinancialAccountId ?? r.Entry.Metadata.FinancialAccountId, externalAccount)
                        || Equal(l.Account.FinancialAccountId ?? r.Entry.Metadata.FinancialAccountId, fundAccount.ToString("D")))
                    .Where(l => Equal(Currency(l, baseCurrency), currency)).Sum(CashAmount)))
                .Where(r => r.Amount > 0m).ToArray();
            if (cash.Sum(r => r.Amount) != linked.Sum(r => r.Amount) || linked.Any(r => r.Amount <= 0))
            {
                coverage.Add(new("posting-allocation", false, "Matched journal cash does not equal the provider receipts."));
                continue;
            }
            var candidate = new List<BookReceiptPostingDto>();
            foreach (var receipt in linked)
            {
                var allocations = ReceiptAllocations(receipt, expectations);
                if (allocations.Count == 0)
                {
                    coverage.Add(new("posting-allocation", false,
                        "Current projections omit the journal allocation; retained queue history must establish its linkage."));
                    // This is an observation of actual journal cash, not a new obligation. Retained
                    // recognized allocations alone may resolve its empty key and observe a reversal.
                    foreach (var (record, amount) in cash)
                        candidate.Add(Posting(record, receipt, string.Empty, linked.Length > 1 ? receipt.Amount : amount,
                            amount, true, reversed, group.MatchGroupId));
                    continue;
                }
                if (allocations.Count > 1 && cash.Length > 1)
                {
                    coverage.Add(new("posting-allocation", false, "Multiple journals and obligations require explicit allocation evidence."));
                    candidate.Clear();
                    break;
                }
                foreach (var (record, amount) in cash)
                    foreach (var allocation in allocations)
                        candidate.Add(Posting(record, receipt, allocation.ObligationKey,
                            allocations.Count > 1 ? allocation.Amount : linked.Length > 1 ? receipt.Amount : amount,
                            amount, receipt.Allocations.Count == 0, reversed, group.MatchGroupId));
            }
            postings.AddRange(candidate);
        }
    }

    private static HashSet<Guid> ReversedJournals(IReadOnlyList<LedgerJournalEntryRecord> ledger, DateOnly asOfDate)
    {
        var reversed = ledger.Select(r => Tag(r.Entry.Metadata, LedgerJournalReversal.ReversalOfTag))
            .Where(s => Guid.TryParse(s, out _)).Select(s => Guid.Parse(s!)).ToHashSet();
        var byId = ledger.ToDictionary(r => r.Entry.JournalEntryId);
        // ReadLedgerAsync already restricts posted records to this book and daily cutoff.
        // Governed adjustments can retain their predecessor without the older reversal.of tag;
        // neither a predecessor link nor a reason label alone establishes a cash reversal.
        foreach (var candidate in ledger)
        {
            if (candidate.PostingKind != LedgerPostingKindDto.Adjustment
                || candidate.SourceJournalEntryId is not { } originalId || originalId == candidate.Entry.JournalEntryId
                || candidate.AdjustmentApproval is not { Status: LedgerAdjustmentApprovalStatusDto.Approved } approval
                || approval.ApprovedAt == default || DateOnly.FromDateTime(approval.ApprovedAt.UtcDateTime) > asOfDate
                || !byId.TryGetValue(originalId, out var original) || candidate.AccountingBasis != original.AccountingBasis)
                continue;
            if (IntercompanyJournalCorrectionEvidence.IsExactInverse(candidate.Entry, original.Entry))
                reversed.Add(originalId);
        }
        return reversed;
    }

    private static BookReceiptPostingDto Posting(LedgerJournalEntryRecord record, BookReceiptDto receipt,
        string key, decimal amount, decimal capacity, bool inferred, HashSet<Guid> reversed, string matchGroup)
        => new($"journal:{record.Entry.JournalEntryId:D}:{receipt.ReceiptId}:{(key.Length == 0 ? "unallocated" : key)}",
            receipt.ReceiptId, key, amount, receipt.Currency, record.Entry.JournalEntryId.ToString("D"),
            EffectiveDate(record), reversed.Contains(record.Entry.JournalEntryId),
            [UiApiRoutes.LedgerPeriodJournalEntries.Replace("{periodId:guid}", record.PeriodId.ToString("D"), StringComparison.Ordinal),
                $"reconciliation-match:{matchGroup}"], capacity, inferred);

    private static IReadOnlyList<BookReceiptAllocationDto> ReceiptAllocations(BookReceiptDto receipt,
        IReadOnlyList<BookReceiptExpectationDto> expectations)
    {
        if (receipt.Allocations.Count > 0)
            return receipt.Allocations;
        var eligible = expectations.Where(e => e.SecurityId == receipt.SecurityId && Equal(e.Currency, receipt.Currency)
            && e.DueDate == receipt.EffectiveDate).ToArray();
        if (Equal(receipt.FlowType, "Interest") || Equal(receipt.FlowType, "Coupon"))
        {
            eligible = eligible.Where(e => Equal(e.FlowType, "Interest") || Equal(e.FlowType, "Coupon")).ToArray();
            if (eligible.Select(e => e.FlowType).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                return [];
        }
        else
            eligible = eligible.Where(e => Equal(e.FlowType, receipt.FlowType)).ToArray();
        if (eligible.Length == 1)
            return [new(eligible[0].ObligationKey, receipt.Amount)];
        var candidates = eligible.Select(e => new ReconciliationMatchKernel.SplitCandidate(e.ObligationKey, e.ExpectedAmount)).ToArray();
        return candidates.Length > 1 && candidates.Sum(c => c.Amount) == receipt.Amount
            && ReconciliationMatchKernel.TryFindSplit(receipt.Amount, candidates, 0m, 4, out var split, out _)
            ? split.Select(s => new BookReceiptAllocationDto(s.Id, s.Amount)).ToArray() : [];
    }

    private static IReadOnlyList<BookReceiptAllocationDto> ExplicitAllocations(CanonicalStatementRow source,
        decimal amount, Guid security, string currency, IReadOnlyList<BookReceiptExpectationDto> expectations)
    {
        var split = source.ReceiptAllocations;
        if (!string.IsNullOrWhiteSpace(split))
        {
            using var document = JsonDocument.Parse(split);
            var allocations = new List<BookReceiptAllocationDto>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var key = item.TryGetProperty("obligationKey", out var keyValue) ? keyValue.GetString() : null;
                var projection = item.TryGetProperty("projectedCashFlowId", out var projectionValue)
                    && Guid.TryParse(projectionValue.GetString(), out var id) ? id : Guid.Empty;
                var resolved = ExplicitKey(key, projection, security, currency, expectations);
                if (resolved is null || !item.TryGetProperty("amount", out var value)
                    || !value.TryGetDecimal(out var allocated) || allocated <= 0m)
                    throw new InvalidDataException("Retained receipt allocation identity or amount is invalid.");
                allocations.Add(new(resolved, allocated));
            }
            if (allocations.Count == 0 || allocations.Sum(a => a.Amount) != Math.Abs(amount)
                || allocations.Select(a => a.ObligationKey).Distinct(StringComparer.Ordinal).Count() != allocations.Count)
                throw new InvalidDataException("Retained receipt allocations do not conserve the provider payment.");
            return allocations.OrderBy(a => a.ObligationKey, StringComparer.Ordinal).ToArray();
        }
        var resolvedKey = ExplicitKey(source.ObligationKey, source.ProjectedCashFlowId ?? Guid.Empty,
            security, currency, expectations);
        return resolvedKey is null ? [] : [new(resolvedKey, Math.Abs(amount))];
    }

    private static string? ExplicitKey(string? key, Guid projection, Guid security, string currency,
        IReadOnlyList<BookReceiptExpectationDto> expectations)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            key = key.Trim();
            var current = expectations.Where(e => e.ObligationKey == key).ToArray();
            if (current.Length > 1 || current.Any(e => e.SecurityId != security || !Equal(e.Currency, currency)))
                throw new InvalidDataException("Receipt allocation conflicts with the obligation's security or currency.");
            // Omission from today's projection never deletes a canonical provider allocation.
            // The queue validates this key against retained history; unknown keys remain invalid.
            return key;
        }
        var matches = expectations.Where(e => projection != Guid.Empty && e.ProjectedCashFlowId == projection
            && e.SecurityId == security && Equal(e.Currency, currency)).ToArray();
        if (projection == Guid.Empty)
            return null;
        return matches.Length == 1 ? matches[0].ObligationKey : BookReceiptProjectionIdentity.For(projection);
    }

    private static bool IsCashLine(LedgerEntry line) => line.Account.AccountType == LedgerAccountType.Asset
        && (line.Account.Name == "Cash" || (line.Account.Name.StartsWith("Cash (", StringComparison.Ordinal)
            && line.Account.Name.EndsWith(")", StringComparison.Ordinal)));
    private static string Currency(LedgerEntry line, string baseCurrency)
        => line.Currency?.TransactionCurrency ?? line.Account.Symbol ?? baseCurrency;
    private static decimal CashAmount(LedgerEntry line)
        => line.Currency is { } currency ? currency.TransactionDebit - currency.TransactionCredit : line.Debit - line.Credit;
}
