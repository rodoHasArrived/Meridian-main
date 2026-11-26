using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    /// <summary>
    /// Opens a counterpart journal from the intercompany queue: the Fund Ledger reloads for the book's fund
    /// profile on its Journal tab and selects that entry. The fund profile comes from the shared book list.
    /// </summary>
    internal void OpenCounterpartJournal(Guid ledgerBookId, Guid journalEntryId, string? fundProfileId)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            StatusText = $"Journal {journalEntryId:D} is in book {ledgerBookId:D}. Load the book list on the Counterpart proposals tab to open it.";
            return;
        }

        _navigationService.NavigateTo("FundLedger", new FundOperationsNavigationContext(
            Tab: FundOperationsTab.Journal,
            FundProfileId: fundProfileId,
            LedgerBookId: ledgerBookId,
            JournalEntryId: journalEntryId));
    }

    /// <summary>Selects the journal a deep link asked for, or says plainly that this fund's view does not hold it.</summary>
    private void FocusRequestedJournalEntry(FundOperationsNavigationContext? context)
    {
        if (context?.JournalEntryId is not { } journalEntryId)
            return;

        var line = VisibleJournal.FirstOrDefault(item => item.JournalEntryId == journalEntryId)
                   ?? Journal.FirstOrDefault(item => item.JournalEntryId == journalEntryId);
        if (line is not null)
        {
            SelectedJournalEntry = line;
            StatusText = $"Journal {journalEntryId:D} is selected on the Journal tab.";
            return;
        }

        StatusText = $"Journal {journalEntryId:D} is not in this fund's loaded ledger view. It may be outside the current scope or not yet posted.";
    }
}
