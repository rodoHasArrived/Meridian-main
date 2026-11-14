using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private static bool IsOpeningBalanceDraft(ManualJournalEntryDraftDto draft)
        => draft.TreasuryContext?.IdempotencyKey?.StartsWith("portfolio-opening:", StringComparison.Ordinal) == true;

    private static bool IsOpeningCorrectionDraft(ManualJournalEntryDraftDto draft)
        => draft.TreasuryContext?.IdempotencyKey?.StartsWith("portfolio-opening-correction:", StringComparison.Ordinal) == true;

    internal static void EnsureOpeningBalanceSaveAuthority(ManualJournalEntryDraftDto incoming,
        ManualJournalEntryDraftDto? retained, bool trustedAutomatedIntake)
    {
        if (retained is null || !IsOpeningBalanceDraft(retained))
        {
            if (IsOpeningBalanceDraft(incoming) && !trustedAutomatedIntake)
                throw new InvalidOperationException("Opening balances require the retained broker-import intake.");
            if (IsOpeningBalanceDraft(incoming) && (incoming.ReversalOfJournalEntryId is not null
                || incoming.RebookedFromJournalEntryId is not null || incoming.Reversal is not null || incoming.Rebook is not null))
                throw new InvalidOperationException("An imported opening must be an originating journal without correction linkage.");
            if (retained is not null && IsOpeningCorrectionDraft(retained)
                && (incoming.LedgerBookId != retained.LedgerBookId || incoming.PeriodId != retained.PeriodId
                    || incoming.AccountingDate != retained.AccountingDate || incoming.Currency != retained.Currency
                    || incoming.AccountingBasis != retained.AccountingBasis))
                throw new InvalidOperationException("Opening correction book, period, date, currency and basis are immutable.");
            return;
        }
        // Source accounts and their chart paths are part of the reviewed economics. Replacing
        // a securities asset with an expense account is not a descriptive draft repair.
        if (incoming.LedgerBookId != retained.LedgerBookId || incoming.PeriodId != retained.PeriodId ||
            incoming.AccountingDate != retained.AccountingDate || incoming.Currency != retained.Currency ||
            incoming.EntryType != retained.EntryType || incoming.AccountingBasis != retained.AccountingBasis ||
            incoming.ReversalOfJournalEntryId != retained.ReversalOfJournalEntryId ||
            incoming.RebookedFromJournalEntryId != retained.RebookedFromJournalEntryId ||
            incoming.Reversal != retained.Reversal || incoming.Rebook != retained.Rebook ||
            incoming.TreasuryContext != retained.TreasuryContext ||
            !SamePostingLines(retained.Lines, incoming.Lines) ||
            retained.EvidenceLinks.Except(incoming.EvidenceLinks, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Opening source economics and reservation identity are immutable; reject and use a governed correction.");
    }

    private async Task<string?> ValidateOpeningCorrectionAsync(ManualJournalEntryDraftDto draft,
        IReadOnlyDictionary<string, ChartOfAccountsNodeDto> chart, CancellationToken ct,
        ManualJournalEntryDraftDto? prospectiveParent = null, bool allowHardClosedCloseLock = false)
    {
        if (!IsOpeningCorrectionDraft(draft))
        {
            if (draft.PriorPeriodAdjustmentSource is null
                && (draft.ReversalOfJournalEntryId ?? draft.RebookedFromJournalEntryId) is { } claimedSource)
            {
                var retainedSource = await _draftStore.GetAsync(draft.FundProfileId, claimedSource, ct,
                    draft.TenantId, draft.CompanyId).ConfigureAwait(false);
                if (retainedSource is not null && (IsOpeningBalanceDraft(retainedSource) || IsOpeningCorrectionDraft(retainedSource)))
                    return "A claimed opening source requires its canonical server-retained opening correction lifecycle authority.";
            }
            return null;
        }
        try
        {
            var predecessor = await ResolveOpeningCorrectionPredecessorAsync(draft, ct, prospectiveParent,
                allowHardClosedCloseLock).ConfigureAwait(false);
            var inverse = OpeningInverseLines(draft);
            var unmatched = predecessor.Entry.Lines.ToList();
            if (inverse.Count != unmatched.Count)
                return "Opening correction must retain every immutable predecessor inverse leg.";
            foreach (var line in inverse)
            {
                if (!chart.TryGetValue(line.AccountPath, out var account) || account.IsArchived
                    || !TryMapLedgerAccountType(account.AccountType, out var type)
                    || !string.Equals(line.Currency, draft.Currency, StringComparison.OrdinalIgnoreCase))
                    return "The original native-currency account mapping is unavailable for the opening correction inverse.";
                var identity = new LedgerAccount(account.AccountName, type,
                    NormalizeOptional(line.LedgerAccountSymbol), NormalizeOptional(line.LedgerAccountFinancialAccountId));
                var index = unmatched.FindIndex(source => source.Account == identity
                    && source.Debit == (line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m)
                    && source.Credit == (line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m)
                    && SamePostingDimensions(line.Dimensions, LedgerDimensionMapper.ToDto(source.Dimensions)));
                if (index < 0)
                    return "The opening correction inverse differs from the immutable predecessor; restore its original account mapping and economics.";
                unmatched.RemoveAt(index);
            }
            var replacement = draft.Lines.Except(inverse).ToArray();
            if (replacement.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : 0m)
                != replacement.Sum(line => line.Side == AccountingTemplateLineSideDto.Credit ? line.Amount : 0m))
                return "Opening rebook replacement lines must balance independently.";
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private async Task<LedgerJournalEntryRecord> ResolveOpeningCorrectionPredecessorAsync(
        ManualJournalEntryDraftDto draft, CancellationToken ct, ManualJournalEntryDraftDto? prospectiveParent = null,
        bool allowHardClosedCloseLock = false)
    {
        if (_journalStore is null || draft.LedgerBookId is not { } bookId
            || !Guid.TryParse(draft.PeriodId, out var periodId))
            throw new InvalidOperationException("Opening correction requires authoritative scoped ledger state.");
        var period = await _journalStore.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        var book = await _journalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        if (period is null || period.LedgerBookId != bookId
            || (period.Status != "Open" && !(allowHardClosedCloseLock && period.Status == "HardClosed"
                && draft.Status == ManualJournalEntryStatusDto.Posted)) || book is null
            || book.AccountingBasis != draft.AccountingBasis || book.BaseCurrency != draft.Currency
            || draft.TreasuryContext?.EffectiveDate != draft.AccountingDate)
            throw new InvalidOperationException("Opening correction requires its original open book period, native currency, basis and effective date.");
        var child = draft;
        var visited = new HashSet<Guid>();
        Guid? immediateId = null;
        ManualJournalEntryDraftDto? immediateParent = null;
        while (IsOpeningCorrectionDraft(child))
        {
            if (!visited.Add(child.JournalEntryId))
                throw new InvalidOperationException("Opening correction source lineage contains a cycle.");
            var reverse = child.ReversalOfJournalEntryId.HasValue;
            var parentId = child.ReversalOfJournalEntryId ?? child.RebookedFromJournalEntryId;
            if (parentId is null || parentId == Guid.Empty
                || (reverse && child.RebookedFromJournalEntryId is not null)
                || child.TreasuryContext?.IdempotencyKey != $"portfolio-opening-correction:{parentId:D}:{child.JournalEntryId:D}")
                throw new InvalidOperationException("Opening correction requires its canonical exact predecessor and unique correction identity.");
            immediateId ??= parentId;
            var parent = prospectiveParent?.JournalEntryId == parentId ? prospectiveParent
                : await _draftStore.GetAsync(draft.FundProfileId, parentId.Value, ct, draft.TenantId, draft.CompanyId).ConfigureAwait(false);
            prospectiveParent = null;
            if (parent is null || parent.LedgerBookId != bookId || parent.PeriodId != draft.PeriodId
                || parent.FundProfileId != draft.FundProfileId || parent.TenantId != draft.TenantId || parent.CompanyId != draft.CompanyId
                || parent.AccountingBasis != draft.AccountingBasis || parent.Currency != draft.Currency
                || parent.AccountingDate != draft.AccountingDate
                || (reverse ? parent.Reversal?.ReversalJournalEntryId : parent.Rebook?.RebookJournalEntryId) != child.JournalEntryId
                || (!IsOpeningBalanceDraft(parent) && !IsOpeningCorrectionDraft(parent)))
                throw new InvalidOperationException("Opening correction requires the server-retained parent-to-child lifecycle authority in the same book, period, date and basis.");
            immediateParent ??= parent;
            child = parent;
        }
        var parts = child.TreasuryContext?.IdempotencyKey?.Split(':');
        if (parts is not { Length: 3 } || !Guid.TryParse(parts[2], out var accountId)
            || child.TreasuryContext!.IdempotencyKey != $"portfolio-opening:{bookId:D}:{accountId:D}"
            || child.ReversalOfJournalEntryId is not null || child.RebookedFromJournalEntryId is not null)
            throw new InvalidOperationException("Opening correction lineage must terminate at the exact originating imported book/account source.");
        if (draft.Lines.Any(line => line.LedgerAccountFinancialAccountId != accountId.ToString("D")))
            throw new InvalidOperationException("Opening correction lines must retain the imported financial-account scope.");
        var roots = await _journalStore.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId,
            JournalEntryId: child.JournalEntryId), ct).ConfigureAwait(false);
        var predecessors = immediateId == child.JournalEntryId ? roots
            : await _journalStore.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId,
                JournalEntryId: immediateId), ct).ConfigureAwait(false);
        if (roots.Count != 1 || roots[0].PostingKind != LedgerPostingKindDto.Originating
            || roots[0].Entry.Metadata.IdempotencyKey != child.TreasuryContext.IdempotencyKey
            || roots[0].Entry.Lines.Any(line => line.Account.FinancialAccountId != accountId.ToString("D"))
            || predecessors.Count != 1 || roots[0].PeriodId != periodId || predecessors[0].PeriodId != periodId
            || predecessors[0].Entry.Metadata.IdempotencyKey != immediateParent?.TreasuryContext?.IdempotencyKey
            || predecessors[0].SourceJournalEntryId != (immediateParent?.ReversalOfJournalEntryId ?? immediateParent?.RebookedFromJournalEntryId)
            || roots[0].AccountingBasis != draft.AccountingBasis || predecessors[0].AccountingBasis != draft.AccountingBasis
            || roots[0].AggregateId != bookId || predecessors[0].AggregateId != bookId
            || (predecessors[0].Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(predecessors[0].Entry.Timestamp.UtcDateTime)) != draft.AccountingDate
            || roots[0].Entry.Lines.Concat(predecessors[0].Entry.Lines).Any(line => line.Currency is not { } currency
                || !currency.IsFunctionalCurrency || currency.FunctionalCurrency != book.BaseCurrency
                || currency.FxRateToFunctional != 1m || currency.TransactionDebit != line.Debit || currency.TransactionCredit != line.Credit))
            throw new InvalidOperationException("Opening correction requires its immutable originating root and native-currency posted predecessor in the same basis and period.");
        return predecessors[0];
    }

    private static IReadOnlyList<ManualJournalEntryLineDto> OpeningInverseLines(ManualJournalEntryDraftDto draft)
        => draft.ReversalOfJournalEntryId.HasValue ? draft.Lines
            : draft.Lines.Where(line => line.LineId.StartsWith("rebook-inverse-", StringComparison.Ordinal)).ToArray();

    private async Task EnsureMaterializedOpeningInverseAsync(ManualJournalEntryDraftDto draft,
        IReadOnlyList<LedgerEntry> lines, CancellationToken ct)
    {
        if (!IsOpeningCorrectionDraft(draft))
            return;
        var predecessor = await ResolveOpeningCorrectionPredecessorAsync(draft, ct).ConfigureAwait(false);
        var inverseIds = OpeningInverseLines(draft).Select(line => CreateDeterministicGuid("manual-je-line",
            draft.JournalEntryId.ToString("D"), line.LineId, line.Side.ToString())).ToHashSet();
        EnsureExactMaterializedCorrectionInverse(predecessor.Entry.Lines, lines.Where(line => inverseIds.Contains(line.EntryId)).ToArray());
    }
}
