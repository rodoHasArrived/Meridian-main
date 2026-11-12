using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    public EntityConsolidationViewModel EntityConsolidation { get; }

    private void ApplyEntityConsolidationBooks(IReadOnlyList<LedgerBookDto> books)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => EntityConsolidation.SetBooks(books));
        else EntityConsolidation.SetBooks(books);
    }

    public async Task<bool> OpenConsolidationDraftsAsync(CancellationToken ct = default)
    {
        var preview = EntityConsolidation.Preview;
        if (preview is null || preview.Drafts.Count == 0 || _activeFundProfile?.FundProfileId != preview.FundProfileId)
            return false;
        var workspace = await _configurationService.GetWorkspaceAsync(preview.FundProfileId, preview.Request.EliminationBookId, ct);
        if (_activeFundProfile?.FundProfileId != preview.FundProfileId || EntityConsolidation.Preview?.Request != preview.Request)
            return false;
        _configuration = workspace;
        ApplyConfiguration(_configuration);
        await LoadManualJournalPeriodAsync(ct);
        await LoadManualJournalWorkbenchAsync(ct);
        var firstDraft = preview.Drafts[0].JournalEntryId.ToString("D");
        SelectedManualJournalDraftRow = ManualJournalDraftRows.FirstOrDefault(row => row.Key == firstDraft);
        return SelectedManualJournalDraftRow is not null;
    }
}
