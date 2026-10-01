using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.ViewModels;

public sealed partial class PostedLedgerViewModel
{
    private IReadOnlyList<LedgerJournalEntryDto> _journalEntries = [];
    private string _journalStatusText = "Select a ledger period to inspect posted amounts.";

    public LedgerAmountProofDrawerViewModel ProofDrawer { get; }

    public IAsyncRelayCommand<PostedLedgerAmountSelection> OpenAmountProofCommand { get; }

    public ObservableCollection<PostedLedgerJournalLineRow> JournalLines { get; } = [];

    public string JournalStatusText
    {
        get => _journalStatusText;
        private set => SetProperty(ref _journalStatusText, value);
    }

    private void ClearJournalProof()
    {
        ProofDrawer.Close();
        _journalEntries = [];
        JournalLines.Clear();
        JournalStatusText = "Select a posted debit or credit to review its retained support.";
    }

    private async Task LoadJournalAsync(Guid periodId, int revision, CancellationToken ct)
    {
        if (_client is null)
        {
            return;
        }

        JournalStatusText = "Loading posted journal amounts.";
        try
        {
            var response = await _client.GetJournalEntriesAsync(periodId, ct).ConfigureAwait(true);
            if (_isDisposed || revision != _periodRevision || ct.IsCancellationRequested)
            {
                return;
            }

            if (!response.Success || response.Data is null)
            {
                JournalStatusText = "Review required: " + (response.ErrorMessage ?? "Posted journal detail is unavailable.");
                return;
            }

            // A response from an older server may ignore scope. Never display its foreign lines
            // or manufacture a subject from an account name, symbol, or row position.
            _journalEntries = response.Data
                .Where(entry => entry.LedgerBookId == SelectedBookId && entry.PeriodId == periodId)
                .ToArray();
            ProjectJournal();
            if (_journalEntries.Count != response.Data.Count)
            {
                JournalStatusText = "Blocked: the journal response contained entries outside the selected book or period.";
                _journalEntries = [];
                JournalLines.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Navigation invalidates and closes the drawer before cancellation completes.
        }
        catch (Exception)
        {
            if (revision == _periodRevision && !_isDisposed)
            {
                JournalStatusText = "Review required: posted journal detail could not be loaded.";
            }
        }
    }

    private void ProjectJournal()
    {
        JournalLines.Clear();
        var fundId = SelectedBookRow?.FundProfileId ?? string.Empty;
        foreach (var journal in _journalEntries.Where(entry => entry.AccountingBasis == SelectedBasis))
        {
            foreach (var line in journal.Lines)
            {
                if (line.JournalEntryId != journal.JournalEntryId || line.EntryId == Guid.Empty)
                {
                    continue;
                }

                PostedLedgerAmountSelection? Selection(string side, decimal amount)
                    => amount == 0m || journal.LedgerBookId is not { } bookId ? null : new(
                        journal.JournalEntryId, line.EntryId, bookId, journal.PeriodId,
                        fundId, side, amount, BaseCurrency, line.AccountName);

                JournalLines.Add(new(
                    journal.JournalEntryId,
                    line.EntryId,
                    line.AccountName,
                    line.Symbol,
                    line.Description,
                    PostedLedgerProjection.FormatAmount(line.Debit, BaseCurrency),
                    PostedLedgerProjection.FormatAmount(line.Credit, BaseCurrency),
                    Selection("debit", line.Debit),
                    Selection("credit", line.Credit)));
            }
        }

        JournalStatusText = JournalLines.Count == 0
            ? "No posted journal amounts are available for this book, period, and basis."
            : "Select a posted debit or credit to review its retained support.";
    }

    private Task OpenAmountProofAsync(PostedLedgerAmountSelection? selection)
    {
        if (_isDisposed || selection is null)
        {
            return Task.CompletedTask;
        }

        // A stale button or an old command parameter cannot open evidence after scope changes.
        if (selection.LedgerBookId != SelectedBookId || selection.PeriodId != SelectedPeriodId ||
            !string.Equals(selection.FundProfileId, SelectedBookRow?.FundProfileId, StringComparison.Ordinal) ||
            !JournalLines.Any(row => ReferenceEquals(row.DebitProof, selection) || ReferenceEquals(row.CreditProof, selection)))
        {
            ProofDrawer.ShowBlocked("The selected amount no longer belongs to the visible ledger scope.");
            return Task.CompletedTask;
        }

        return ProofDrawer.OpenAsync(selection, _cts.Token);
    }
}

public sealed record PostedLedgerAmountSelection(
    Guid JournalEntryId,
    Guid EntryId,
    Guid LedgerBookId,
    Guid PeriodId,
    string FundProfileId,
    string Side,
    decimal Amount,
    string Currency,
    string AccountName)
{
    public string SubjectId => $"{JournalEntryId:D}:{EntryId:D}:{Side}";
}

public sealed record PostedLedgerJournalLineRow(
    Guid JournalEntryId,
    Guid EntryId,
    string AccountName,
    string? Symbol,
    string Description,
    string DebitLabel,
    string CreditLabel,
    PostedLedgerAmountSelection? DebitProof,
    PostedLedgerAmountSelection? CreditProof)
{
    public bool HasDebit => DebitProof is not null;
    public bool HasCredit => CreditProof is not null;
}
