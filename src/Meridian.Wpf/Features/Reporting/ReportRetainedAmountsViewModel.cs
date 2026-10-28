using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Features.Reporting;

/// <summary>A retained generated amount, never a balance inferred from a label or current ledger.</summary>
public sealed record ReportRetainedAmountRow(ReportLedgerAmountBindingDto Binding)
{
    public string Label => Binding.Label;
    public string AmountLabel => $"{Binding.Amount.ToString("G29", CultureInfo.InvariantCulture)} {Binding.Currency}";
    public string SupportLabel => $"{Binding.JournalEntryIds.Count} retained journals · {Binding.LedgerEntryIds.Count} lines";
    public bool CanInspect => Binding.ProofAvailable is true;
}

/// <summary>Scope-bound report amount selection over the shared retained evidence drawer.</summary>
public sealed class ReportRetainedAmountsViewModel : BindableBase
{
    private readonly IReportRetainedAmountsApiClient? _client;
    private GovernedReportingRunDto? _run;
    private CancellationTokenSource? _read;
    private long _revision;
    private string _status = "Select a governed report run to inspect retained amounts.";
    private bool _loading;

    public ReportRetainedAmountsViewModel(IReportRetainedAmountsApiClient? client, ILedgerReportsApiClient? proofClient)
    {
        _client = client;
        ProofDrawer = new LedgerAmountProofDrawerViewModel(proofClient);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _run is not null && !IsLoading);
        InspectCommand = new AsyncRelayCommand<ReportRetainedAmountRow>(InspectAsync,
            row => row is not null && row.CanInspect && Rows.Contains(row) && !IsLoading);
    }

    public ObservableCollection<ReportRetainedAmountRow> Rows { get; } = [];
    public LedgerAmountProofDrawerViewModel ProofDrawer { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<ReportRetainedAmountRow> InspectCommand { get; }
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); RefreshCommand.NotifyCanExecuteChanged(); InspectCommand.NotifyCanExecuteChanged(); } }

    public void SetRun(GovernedReportingRunDto? run)
    {
        Invalidate();
        _run = run;
        StatusText = run is null ? "Select a governed report run to inspect retained amounts."
            : "Refresh retained amounts for the selected report revision.";
        RefreshCommand.NotifyCanExecuteChanged();
    }

    public void Close() { Invalidate(); _run = null; RefreshCommand.NotifyCanExecuteChanged(); }

    public void Suspend() { Invalidate(); StatusText = "Refresh retained amounts after returning to this report."; }

    public async Task RefreshAsync()
    {
        Invalidate();
        var revision = _revision;
        var run = _run;
        if (run is null || _client is null || !TryScope(run, out var scope)
            || run.Snapshot is null || !Sha256Digest.IsCanonical(run.Snapshot.SourceCheckpointHash))
        {
            StatusText = "Review required: retained report scope or its source checkpoint is unavailable.";
            return;
        }
        using var read = new CancellationTokenSource();
        _read = read;
        IsLoading = true;
        StatusText = "Loading retained trial-balance amounts…";
        try
        {
            var response = await _client.GetAmountsAsync(run.RunId, read.Token);
            if (revision != _revision || read.IsCancellationRequested) return;
            if (!response.Success || response.Data is not { } amounts)
                throw new InvalidOperationException("The authorized retained report catalogue is unavailable.");
            if (amounts.Any(item => item is null || item.Scope != scope || !Sha256Digest.IsCanonical(item.AmountId)
                || item.SubjectId != $"report:{run.RunId}:{item.AmountId}"
                || item.SourceSnapshotHash != run.Snapshot.SourceCheckpointHash || string.IsNullOrWhiteSpace(item.Label)
                || string.IsNullOrWhiteSpace(item.Currency) || item.Currency.Length != 3 || item.Currency.Any(ch => ch is < 'A' or > 'Z')
                || item.JournalEntryIds is null || item.LedgerEntryIds is null
                || item.JournalEntryIds.Count == 0 || item.LedgerEntryIds.Count == 0
                || item.JournalEntryIds.Any(id => id == Guid.Empty) || item.LedgerEntryIds.Any(id => id == Guid.Empty)
                || item.JournalEntryIds.Distinct().Count() != item.JournalEntryIds.Count
                || item.LedgerEntryIds.Distinct().Count() != item.LedgerEntryIds.Count)
                || amounts.Select(item => item.AmountId).Distinct(StringComparer.Ordinal).Count() != amounts.Length)
                throw new InvalidOperationException("Retained amount identity, scope or checkpoint does not match the selected report.");
            foreach (var amount in amounts) Rows.Add(new(amount));
            StatusText = amounts.Length == 0 ? "Review required: this report has no retained amount bindings; recertify legacy runs."
                : amounts.Any(amount => amount.ProofAvailable is not true)
                    ? "Retained balances loaded. Supporting proof requires ledger report access."
                    : "Select an amount to verify its complete retained journal support.";
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (Exception ex) { if (revision == _revision) StatusText = $"Report amount proof blocked: {ex.Message}"; }
        finally { if (revision == _revision) { _read = null; IsLoading = false; } }
    }

    private Task InspectAsync(ReportRetainedAmountRow? row)
    {
        if (row is null || !Rows.Contains(row) || !row.CanInspect || _run is null || !TryScope(_run, out var scope)
            || row.Binding.Scope != scope || row.Binding.SubjectId != $"report:{_run.RunId}:{row.Binding.AmountId}")
        {
            ProofDrawer.ShowBlocked("The amount no longer belongs to the selected retained report.");
            return Task.CompletedTask;
        }
        var binding = row.Binding;
        return ProofDrawer.OpenReportAsync(new(binding.SubjectId!, scope!, binding.Amount, binding.Currency,
            binding.Label, binding.JournalEntryIds, binding.LedgerEntryIds));
    }

    private void Invalidate()
    {
        _revision++;
        _read?.Cancel(); _read = null;
        ProofDrawer.Close(); Rows.Clear(); IsLoading = false;
    }

    private static bool TryScope(GovernedReportingRunDto run, out LedgerAmountScopeDto? scope)
    {
        scope = null;
        if (run.Scope is null || string.IsNullOrWhiteSpace(run.RunId) || string.IsNullOrWhiteSpace(run.Scope.TenantId)
            || string.IsNullOrWhiteSpace(run.Scope.CompanyId) || string.IsNullOrWhiteSpace(run.Scope.FundId)
            || !Guid.TryParse(run.Scope.BookId, out var book) || book == Guid.Empty
            || !Guid.TryParse(run.Scope.PeriodId, out var period) || period == Guid.Empty) return false;
        scope = new(run.Scope.TenantId, run.Scope.CompanyId, run.Scope.FundId, book, period);
        return true;
    }
}
