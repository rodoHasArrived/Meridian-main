using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private async Task<ManualJournalEntryDraftDto> RetainIntercompanyFxForSaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        if (draft.EntryType == ManualJournalEntryTypeDto.Reversal && draft.ReversalOfJournalEntryId is not null)
            return draft with { IntercompanyFx = null }; // The append guard verifies the immutable original's exact inverse.
        var lines = draft.Lines.Where(x => x.Dimensions?.AccountingArrangementId is not null
            && x.TransactionCurrency is { } original && original.Currency != draft.Currency).ToArray();
        if (lines.Length == 0)
            return draft;
        try
        {
            var authority = _intercompanyFxAuthority ?? throw new InvalidOperationException("An approved book FX configuration is required.");
            if (draft.LedgerBookId is not { } bookId)
                throw new InvalidOperationException("A destination book is required for FX authority.");
            var context = await authority.ResolveAsync(bookId, draft.AccountingDate, ct).ConfigureAwait(false);
            if (context.Configuration.Policy.ReportingCurrency != draft.Currency)
                throw new InvalidOperationException("The FX configuration must translate into the draft functional currency.");
            ConsolidationFxAmountDto? retained = null;
            foreach (var line in lines)
            {
                var original = line.TransactionCurrency!;
                var amount = ConsolidationFxTranslator.ConvertAmount(original.Amount, original.Currency, context, draft.AccountingDate);
                if (amount.Amount != line.Amount || amount.Rate != original.FxRateToFunctional)
                    throw new InvalidOperationException("The functional amount and stated rate must match the approved transaction-date conversion.");
                retained ??= amount;
            }
            return draft with { IntercompanyFx = retained };
        }
        catch (InvalidOperationException error)
        {
            return draft with
            {
                ValidationIssues = [.. draft.ValidationIssues,
                Issue("manual-je.intercompany-fx", AccountingConfigurationValidationSeverityDto.Critical,
                    error.Message, "transactionCurrency", "Resolve the approved book FX configuration and original amounts before review.")]
            };
        }
    }

    private async Task ValidateCurrentIntercompanyDraftAsync(ManualJournalEntryDraftDto draft, CancellationToken ct)
    {
        if (_journalStore is null || draft.LedgerBookId is not { } bookId || IsConsolidationDraft(draft))
            return;
        var workspace = await _configurationService.GetWorkspaceAsync(draft.FundProfileId, bookId, ct,
            draft.TenantId, draft.CompanyId).ConfigureAwait(false);
        if (!draft.Lines.Any(line => line.Dimensions?.AccountingArrangementId is not null || workspace.ChartOfAccounts.Any(account =>
            account.Path == line.AccountPath && account.IntercompanyRole is not null)))
            return;
        if (draft.IntercompanyFx is { } retained)
        {
            var fx = _intercompanyFxAuthority ?? throw new InvalidOperationException("Intercompany FX authority is unavailable.");
            var current = await fx.ResolveAsync(bookId, draft.AccountingDate, ct).ConfigureAwait(false);
            if (current.Configuration.ContentHash != retained.ConfigurationHash || current.RateSet.RateSetId != retained.RateSetId
                || current.RateSet.Revision != retained.RateSetRevision || current.Configuration.Policy.Revision != retained.PolicyRevision)
                throw new InvalidOperationException("The reviewed intercompany FX authority changed. Preview and explicitly refresh the counterpart draft before renewed review.");
        }
        if (_intercompanyPostingAuthority is null || _journalStore is not ILedgerEntityControlJournalValidation validation)
            throw new InvalidOperationException("Authoritative lifecycle, legal agreement and accounting-profile review is unavailable.");
        var book = await _journalStore.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The intercompany draft book is unavailable.");
        if (!Guid.TryParse(draft.PeriodId, out var periodId))
            throw new InvalidOperationException("The intercompany draft requires a period.");
        var period = await _journalStore.GetPeriodAsync(periodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The intercompany period is unavailable.");
        var request = new JournalEntryLifecycleActionRequestDto(draft.JournalEntryId, draft.FundProfileId,
            JournalEntryLifecycleActionDto.Post, draft.PreparedBy, draft.Version, "Intercompany review authority validation.");
        var command = BuildManualPostingCommand(draft, request, bookId, periodId, period.Version,
            draft.EvidenceLinks, DateTimeOffset.UtcNow) with
        {
            ApprovalState = AccountingPostingApprovalStateDto.NotRequired,
            ApprovalId = null
        };
        // Preparation has no final posting approval yet. The validator owns a rollback-only
        // transaction; actual posting always retains and validates the approved metadata.
        var write = await BuildManualJournalEntryWriteAsync(draft, book, periodId, command, draft.EvidenceLinks,
            DateTimeOffset.UtcNow, ct, retainPostingApproval: false).ConfigureAwait(false);
        await validation.ValidateEntityPostingControlsAsync(write, ct).ConfigureAwait(false);
    }
}
