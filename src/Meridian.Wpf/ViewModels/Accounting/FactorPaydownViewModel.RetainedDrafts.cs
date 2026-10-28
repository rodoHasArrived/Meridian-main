using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class FactorPaydownViewModel
{
    private ManualJournalEntryDraftDto? _selectedRetainedDraft;
    private bool _submittingRetainedDraft;
    public ObservableCollection<ManualJournalEntryDraftDto> RetainedDrafts { get; } = [];
    public IAsyncRelayCommand ReloadRetainedDraftsCommand { get; }
    public IAsyncRelayCommand SubmitRetainedDraftCommand { get; }
    public ManualJournalEntryDraftDto? SelectedRetainedDraft
    {
        get => _selectedRetainedDraft;
        set
        {
            if (value is not null && !RetainedDrafts.Contains(value)) return;
            if (!SetProperty(ref _selectedRetainedDraft, value)) return;
            RaisePropertyChanged(nameof(RetainedDraftStatusText));
            RaisePropertyChanged(nameof(RetainedDraftLines));
            NotifyCommands();
        }
    }
    public bool CanSelectRetainedDraft => !IsBusy;
    public bool CanSubmitRetainedDraft => !IsBusy && _client is not null && _selectedRetainedDraft is
        { Version: > 0, Status: ManualJournalEntryStatusDto.Draft, AssetAccountingDraft: not null };
    public IReadOnlyList<ManualJournalEntryLineDto> RetainedDraftLines => _selectedRetainedDraft?.Lines ?? [];
    public string RetainedDraftStatusText => _selectedRetainedDraft is { } draft
        ? $"Server draft {draft.JournalEntryId:D} · {draft.Status} · version {draft.Version} · {draft.AccountingBasis}. "
          + "May be submitted for review. Typed approval and atomic lot posting are unavailable; generic approval, posting and corrections are blocked."
        : "Reload and select a retained server factor-paydown draft. Existing desktop-local journal records remain in the journal workbench.";

    private void ClearRetainedDraftSelection()
    {
        SelectedRetainedDraft = null;
        RetainedDrafts.Clear();
    }

    private async Task ReloadRetainedDraftsAsync()
    {
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsBusy = true;
        try
        {
            await LoadRetainedDraftsCoreAsync(generation, _selectedRetainedDraft?.JournalEntryId, operation.Token);
            if (generation == _generation) StatusText = "Retained server drafts loaded. Select a draft to review its actual lines and submit for review.";
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Retained draft read cancelled."; }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            ClearRetainedDraftSelection();
            StatusText = $"Retained server drafts unavailable: {ex.Message}";
        }
        finally { if (generation == _generation) { _operation = null; IsBusy = false; } }
    }

    private async Task LoadRetainedDraftsCoreAsync(long generation, Guid? preferredDraftId, CancellationToken ct)
    {
        var fund = _fundProfileId ?? throw new InvalidOperationException("Select a fund.");
        var book = _bookId ?? throw new InvalidOperationException("Select a ledger book.");
        var workbench = await _client!.GetRetainedDraftsAsync(fund, book, ct);
        if (generation != _generation) return;
        if (workbench.FundProfileId != fund || workbench.LedgerBookId != book
            || workbench.Drafts.Any(draft => draft.FundProfileId != fund || draft.LedgerBookId != book))
            throw new InvalidOperationException("The retained draft response does not match the selected fund and book.");
        var drafts = workbench.Drafts.Where(draft => draft.AssetAccountingDraft?.Spine.FactorPaydown is not null).ToArray();
        if (drafts.Any(draft => draft.AssetAccountingDraft!.Spine.Scope.FundProfileId != fund
            || draft.AssetAccountingDraft.Spine.Scope.LedgerBookId != book))
            throw new InvalidOperationException("Retained asset authority does not match the selected fund and book.");
        ClearRetainedDraftSelection();
        RetainedDrafts.ReplaceWith(drafts);
        SelectedRetainedDraft = preferredDraftId is { } id ? drafts.FirstOrDefault(draft => draft.JournalEntryId == id) : null;
    }

    private async Task SubmitRetainedDraftAsync()
    {
        if (!CanSubmitRetainedDraft || _selectedRetainedDraft is not { } draft) return;
        var generation = _generation;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _submittingRetainedDraft = true;
        IsBusy = true;
        try
        {
            var submitted = await _client!.SubmitForReviewAsync(new(draft.JournalEntryId, draft.FundProfileId, "operator", draft.Version,
                Notes: "Factor-paydown draft submitted for review from the desktop workstation.",
                CorrelationId: $"factor-paydown-review:{draft.JournalEntryId:D}:v{draft.Version}", LedgerBookId: draft.LedgerBookId), operation.Token);
            if (generation != _generation) return;
            if (submitted.JournalEntryId != draft.JournalEntryId || submitted.FundProfileId != draft.FundProfileId
                || submitted.LedgerBookId != draft.LedgerBookId || submitted.Version < draft.Version
                || submitted.AssetAccountingDraft?.Spine.EventId != draft.AssetAccountingDraft!.Spine.EventId
                || submitted.AssetAccountingDraft?.Spine.Scope.FundProfileId != draft.FundProfileId
                || submitted.AssetAccountingDraft?.Spine.Scope.LedgerBookId != draft.LedgerBookId)
                throw new InvalidOperationException("The submitted draft does not match its retained server identity.");
            RetainedDrafts[RetainedDrafts.IndexOf(draft)] = submitted;
            SelectedRetainedDraft = submitted;
            StatusText = $"Server draft {submitted.JournalEntryId:D} returned {submitted.Status}. Review submission does not approve or post; typed approval and atomic lot posting remain unavailable.";
        }
        catch (OperationCanceledException) { if (generation == _generation) StatusText = "Review submission cancelled. Reload retained drafts before retrying."; }
        catch (Exception ex) { if (generation == _generation) StatusText = $"Review submission blocked: {ex.Message}. Reload retained drafts before retrying."; }
        finally { _submittingRetainedDraft = false; if (generation == _generation) { _operation = null; IsBusy = false; } }
    }
}
