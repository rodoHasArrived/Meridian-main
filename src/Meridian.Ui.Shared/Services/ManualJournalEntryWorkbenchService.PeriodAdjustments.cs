using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private async Task<string?> ValidatePriorPeriodAdjustmentAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        var authority = draft.PriorPeriodAdjustmentSource;
        if (authority is null)
            return draft.TreasuryContext?.IdempotencyKey?.StartsWith("prior-period|", StringComparison.Ordinal) == true
                ? "Prior-period correction requires server-retained source authority." : null;
        var expectedPredecessor = authority.Correction?.JournalEntryId ?? authority.SourceJournalEntryId;
        var isRebook = authority.Correction is { ReverseSides: false };
        if ((isRebook ? draft.RebookedFromJournalEntryId : draft.ReversalOfJournalEntryId) != expectedPredecessor
            || (isRebook ? draft.ReversalOfJournalEntryId : draft.RebookedFromJournalEntryId) is not null
            || (authority.Correction is { } correction &&
                (correction.JournalEntryId == Guid.Empty || string.IsNullOrWhiteSpace(correction.Reason)))
            || authority.TargetPeriodId.ToString("D") != draft.PeriodId
            || authority.AccountingDate != draft.AccountingDate
            || authority.AccountingDate != draft.TreasuryContext?.EffectiveDate
            || string.IsNullOrWhiteSpace(authority.Reason) || string.IsNullOrWhiteSpace(authority.IntentId)
            || !SamePostingLines(authority.ExpectedLines.Select(line => line with { Description = null, EvidenceLink = null }).ToArray(),
                draft.Lines.Select(line => line with { Description = null, EvidenceLink = null }).ToArray()))
            return "Prior-period correction source, date, scope or inverse lines changed after preparation.";
        if (_journalStore is null || draft.LedgerBookId is not { } bookId)
            return "Authoritative ledger state is required for a prior-period correction.";
        var source = await _journalStore.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: bookId, JournalEntryId: authority.SourceJournalEntryId), ct).ConfigureAwait(false);
        if (source.Count != 1 || source[0].PeriodId != authority.SourcePeriodId
            || (source[0].Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(source[0].Entry.Timestamp.UtcDateTime)) != authority.OriginalEffectiveDate)
            return "The exact retained source journal is unavailable in the selected ledger book.";
        var sourcePeriod = await _journalStore.GetPeriodAsync(authority.SourcePeriodId, ct).ConfigureAwait(false);
        var target = await _journalStore.GetPeriodAsync(authority.TargetPeriodId, ct).ConfigureAwait(false);
        if (sourcePeriod is null || sourcePeriod.Status != "HardClosed" || sourcePeriod.LedgerBookId != bookId
            || target is null || target.LedgerBookId != bookId || target.Status != "Open"
            || draft.AccountingDate < target.StartDate || draft.AccountingDate > target.EndDate
            || target.StartDate <= sourcePeriod.EndDate)
            return "The source is no longer hard-closed or the selected later period is no longer open.";
        if (authority.Correction is { } priorCorrection)
        {
            var predecessors = await _journalStore.QueryAsync(new LedgerJournalEntryQuery(
                LedgerBookId: bookId, JournalEntryId: priorCorrection.JournalEntryId), ct).ConfigureAwait(false);
            if (predecessors.Count != 1 || predecessors[0].Entry.JournalEntryId != priorCorrection.JournalEntryId
                || predecessors[0].PeriodId != authority.TargetPeriodId)
                return "The exact posted prior-period adjustment being corrected is unavailable in the selected open period.";
            var retained = predecessors[0].Entry;
            var tags = retained.Metadata.Tags;
            if (tags?.GetValueOrDefault("prior-period.sourceJournalEntryId") != authority.SourceJournalEntryId.ToString("D")
                || tags.GetValueOrDefault("prior-period.sourcePeriodId") != authority.SourcePeriodId.ToString("D")
                || tags.GetValueOrDefault("prior-period.originalEffectiveDate") != authority.OriginalEffectiveDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                || tags.GetValueOrDefault("prior-period.intentId") != authority.IntentId)
                return "The posted correction predecessor does not retain the original closed-period source lineage.";
            var book = await _journalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
            if (book is null || !retained.IsBalanced || retained.Lines.Any(line => line.Currency is null
                || !line.Currency.IsFunctionalCurrency
                || !string.Equals(line.Currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)))
                return "A prior-period adjustment correction requires its balanced, native-currency posted predecessor.";
            var inverseLines = priorCorrection.ReverseSides ? draft.Lines
                : draft.Lines.Where(line => line.LineId.StartsWith("rebook-inverse-", StringComparison.Ordinal)).ToArray();
            if (inverseLines.Count != retained.Lines.Count)
                return "A prior-period adjustment correction must retain the complete inverse of its posted predecessor.";
            var configuration = await _configurationService.GetWorkspaceAsync(draft.FundProfileId, bookId,
                ct, draft.TenantId, draft.CompanyId).ConfigureAwait(false);
            var chart = BuildChartByPath(configuration.ChartOfAccounts);
            var unmatched = retained.Lines.ToList();
            foreach (var inverse in inverseLines)
            {
                if (!chart.TryGetValue(inverse.AccountPath, out var account) || account.IsArchived
                    || !TryMapLedgerAccountType(account.AccountType, out var accountType)
                    || !string.Equals(inverse.Currency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    return "The original native-currency account mapping is unavailable for the correction inverse.";
                var identity = new LedgerAccount(account.AccountName, accountType,
                    NormalizeOptional(inverse.LedgerAccountSymbol), NormalizeOptional(inverse.LedgerAccountFinancialAccountId));
                var debit = inverse.Side == AccountingTemplateLineSideDto.Debit ? inverse.Amount : 0m;
                var credit = inverse.Side == AccountingTemplateLineSideDto.Credit ? inverse.Amount : 0m;
                var index = unmatched.FindIndex(line => line.Account == identity && line.Debit == credit && line.Credit == debit
                    && SamePostingDimensions(inverse.Dimensions, LedgerDimensionMapper.ToDto(line.Dimensions)));
                if (index < 0)
                    return "The correction inverse no longer reproduces the posted predecessor; restore its original account mapping and economics.";
                unmatched.RemoveAt(index);
            }
        }
        return null;
    }

    private async Task EnsureMaterializedPriorPeriodInverseAsync(
        ManualJournalEntryDraftDto draft, IReadOnlyList<LedgerEntry> lines, CancellationToken ct)
    {
        if (draft.PriorPeriodAdjustmentSource is not { } authority)
            return;
        var correction = authority.Correction;
        var predecessorId = correction?.JournalEntryId ?? authority.SourceJournalEntryId;
        var predecessorPeriod = correction is null ? authority.SourcePeriodId : authority.TargetPeriodId;
        if (_journalStore is null || draft.LedgerBookId is not { } bookId)
            throw new InvalidOperationException("Authoritative correction predecessor is required before posting.");
        var predecessors = await _journalStore.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: bookId, JournalEntryId: predecessorId), ct).ConfigureAwait(false);
        if (predecessors.Count != 1 || predecessors[0].Entry.JournalEntryId != predecessorId
            || predecessors[0].PeriodId != predecessorPeriod)
            throw new InvalidOperationException("The exact posted correction predecessor is unavailable before posting.");
        var inverseIds = draft.Lines.Where(line => correction is null || correction.ReverseSides || line.LineId.StartsWith("rebook-inverse-", StringComparison.Ordinal))
            .Select(line => CreateDeterministicGuid("manual-je-line", draft.JournalEntryId.ToString("D"), line.LineId, line.Side.ToString())).ToHashSet();
        var inverse = lines.Where(line => inverseIds.Contains(line.EntryId)).ToArray();
        var unmatched = predecessors[0].Entry.Lines.ToList();
        if (inverse.Length != unmatched.Count)
            throw new InvalidOperationException("The materialized correction must retain every predecessor inverse leg.");
        foreach (var candidate in inverse)
        {
            var index = unmatched.FindIndex(source => source.Account == candidate.Account
                && source.Debit == candidate.Credit && source.Credit == candidate.Debit
                && source.Currency is { } sourceCurrency && candidate.Currency is { } currency
                && sourceCurrency.TransactionCurrency == currency.TransactionCurrency
                && sourceCurrency.FunctionalCurrency == currency.FunctionalCurrency
                && sourceCurrency.TransactionDebit == currency.TransactionCredit
                && sourceCurrency.TransactionCredit == currency.TransactionDebit
                && sourceCurrency.FxRateToFunctional == currency.FxRateToFunctional
                && SamePostingDimensions(LedgerDimensionMapper.ToDto(source.Dimensions), LedgerDimensionMapper.ToDto(candidate.Dimensions)));
            if (index < 0)
                throw new InvalidOperationException("The materialized correction inverse differs from its posted predecessor; restore the original account mapping and economics.");
            unmatched.RemoveAt(index);
        }
    }
}
