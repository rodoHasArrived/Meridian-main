using CommunityToolkit.Mvvm.Input;

namespace Meridian.Wpf.ViewModels;

public sealed partial class PostedLedgerViewModel
{
    private readonly Func<string, byte[], CancellationToken, Task<bool>> _saveJournalCsv;
    private bool _isExportingJournal;
    private CancellationTokenSource? _journalExportCts;
    private string _journalExportStatus = string.Empty;
    public IAsyncRelayCommand ExportJournalCsvCommand { get; }
    public string JournalExportStatus { get => _journalExportStatus; private set => SetProperty(ref _journalExportStatus, value); }
    private bool CanExportJournalCsv => !_isDisposed && !_isExportingJournal && _client is not null && SelectedBookId.HasValue &&
        SelectedPeriodId.HasValue && Periods.Any(row => row.PeriodId == SelectedPeriodId);
    private void ResetJournalCsvExport()
    {
        _journalExportCts?.Cancel();
        _journalExportCts = null;
        _isExportingJournal = false;
        JournalExportStatus = string.Empty;
        ExportJournalCsvCommand?.NotifyCanExecuteChanged();
    }

    public async Task ExportJournalCsvAsync()
    {
        if (!CanExportJournalCsv || _client is null || SelectedPeriodId is not { } periodId) return;
        var periodRevision = _periodRevision;
        var bookRevision = _bookRevision;
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = requestCancellation.Token;
        _journalExportCts = requestCancellation;
        _isExportingJournal = true;
        JournalExportStatus = "Downloading complete journals for the selected period…";
        ExportJournalCsvCommand.NotifyCanExecuteChanged();
        try
        {
            var response = await _client.ExportJournalCsvAsync(periodId, ct);
            if (_isDisposed || ct.IsCancellationRequested || periodRevision != _periodRevision || bookRevision != _bookRevision) return;
            if (!response.Success || response.Data is null)
            {
                JournalExportStatus = response.ErrorMessage ?? "Journal CSV export is unavailable.";
                return;
            }
            var saved = await _saveJournalCsv($"journal-{periodId:D}.csv", response.Data, ct);
            if (!_isDisposed && !ct.IsCancellationRequested && periodRevision == _periodRevision && bookRevision == _bookRevision)
                JournalExportStatus = saved ? "Complete period journal saved. Table filters do not limit this file." : "Journal CSV save cancelled.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_isDisposed && periodRevision == _periodRevision && bookRevision == _bookRevision)
                JournalExportStatus = $"Journal CSV export failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_journalExportCts, requestCancellation))
            {
                _journalExportCts = null;
                _isExportingJournal = false;
                ExportJournalCsvCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private static async Task<bool> SaveJournalCsvAsync(string filename, byte[] bytes, CancellationToken ct)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = filename, Filter = "CSV files|*.csv", Title = "Save complete period journal" };
        if (dialog.ShowDialog() != true) return false;
        await File.WriteAllBytesAsync(dialog.FileName, bytes, ct);
        return true;
    }
}
