using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Desktop view model for the posted-journal ledger — the fund's book of record, scoped by
/// ledger period. Mirrors the browser workstation's posted-ledger panel over the same shared API
/// seam, so the two operator lanes cannot disagree about the book (W8-WPF-PARITY-001).
/// <para>
/// Deliberately holds no state of its own beyond what it fetched: the accounting lane's
/// desktop-local JSON stores are not a source of ledger truth, and this surface must never
/// become one.
/// </para>
/// </summary>
public sealed class PostedLedgerViewModel : BindableBase, IDisposable
{
    private readonly ILedgerReportsApiClient? _client;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private int _loadRevision;
    private bool _isRefreshing;
    private Guid? _selectedPeriodId;
    private string _selectedPeriodLabel = "No period selected";
    private string _statusText = "Waiting for the posted journal.";
    private string _periodsErrorText = string.Empty;
    private string _trialBalanceErrorText = string.Empty;
    private string _pnlErrorText = string.Empty;
    private string _periodNotice = string.Empty;
    private string _signoffText = string.Empty;
    private string _balanceSummaryText = "Trial balance not loaded.";
    private bool _isOutOfBalance;

    public PostedLedgerViewModel(ILedgerReportsApiClient? client = null)
    {
        _client = client;
        RefreshCommand = new AsyncRelayCommand(
            () => _isDisposed ? Task.CompletedTask : RefreshAsync(_cts.Token),
            () => !IsRefreshing);
        SelectPeriodCommand = new AsyncRelayCommand<PostedLedgerPeriodRow>(
            row => row is null || _isDisposed
                ? Task.CompletedTask
                : SelectPeriodAsync(row.PeriodId, _cts.Token));
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<PostedLedgerPeriodRow> SelectPeriodCommand { get; }

    public ObservableCollection<PostedLedgerPeriodRow> Periods { get; } = [];

    public ObservableCollection<PostedLedgerTrialBalanceRow> TrialBalance { get; } = [];

    public ObservableCollection<PostedLedgerMetricRow> PnlMetrics { get; } = [];

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Guid? SelectedPeriodId
    {
        get => _selectedPeriodId;
        private set => SetProperty(ref _selectedPeriodId, value);
    }

    public string SelectedPeriodLabel
    {
        get => _selectedPeriodLabel;
        private set => SetProperty(ref _selectedPeriodLabel, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SignoffText
    {
        get => _signoffText;
        private set => SetProperty(ref _signoffText, value);
    }

    public string BalanceSummaryText
    {
        get => _balanceSummaryText;
        private set => SetProperty(ref _balanceSummaryText, value);
    }

    /// <summary>True when the posted trial balance does not tie — a blocker, not a warning.</summary>
    public bool IsOutOfBalance
    {
        get => _isOutOfBalance;
        private set => SetProperty(ref _isOutOfBalance, value);
    }

    /// <summary>
    /// Explains an expected empty state (an open period with no closed-period summary). Distinct
    /// from the error properties so a period that simply has not closed never reads as an outage.
    /// </summary>
    public string PeriodNotice
    {
        get => _periodNotice;
        private set
        {
            if (SetProperty(ref _periodNotice, value))
            {
                OnPropertyChanged(nameof(HasPeriodNotice));
            }
        }
    }

    public bool HasPeriodNotice => !string.IsNullOrEmpty(PeriodNotice);

    public string PeriodsErrorText
    {
        get => _periodsErrorText;
        private set
        {
            if (SetProperty(ref _periodsErrorText, value))
            {
                OnPropertyChanged(nameof(HasPeriodsError));
            }
        }
    }

    public bool HasPeriodsError => !string.IsNullOrEmpty(PeriodsErrorText);

    public string TrialBalanceErrorText
    {
        get => _trialBalanceErrorText;
        private set
        {
            if (SetProperty(ref _trialBalanceErrorText, value))
            {
                OnPropertyChanged(nameof(HasTrialBalanceError));
            }
        }
    }

    public bool HasTrialBalanceError => !string.IsNullOrEmpty(TrialBalanceErrorText);

    public string PnlErrorText
    {
        get => _pnlErrorText;
        private set
        {
            if (SetProperty(ref _pnlErrorText, value))
            {
                OnPropertyChanged(nameof(HasPnlError));
            }
        }
    }

    public bool HasPnlError => !string.IsNullOrEmpty(PnlErrorText);

    /// <summary>Loads once, when the page is first shown.</summary>
    public void Activate()
    {
        if (_isDisposed || _hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _ = RefreshAsync(_cts.Token);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; nothing to cancel.
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var revision = ++_loadRevision;
        IsRefreshing = true;
        StatusText = "Loading the posted journal.";
        PeriodsErrorText = string.Empty;

        try
        {
            if (_client is null)
            {
                PeriodsErrorText = "The ledger reporting client is not available in this session.";
                StatusText = "Posted journal unavailable.";
                return;
            }

            var response = await _client.GetPeriodsAsync(ct).ConfigureAwait(true);
            if (revision != _loadRevision)
            {
                return;
            }

            if (!response.Success || response.Data is null)
            {
                Periods.Clear();
                PeriodsErrorText = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "Ledger periods could not be loaded."
                    : response.ErrorMessage;
                StatusText = "Posted journal unavailable.";
                return;
            }

            ApplyPeriods(response.Data);

            var defaultPeriodId = PostedLedgerProjection.ResolveDefaultPeriodId(response.Data);
            if (defaultPeriodId is null)
            {
                TrialBalance.Clear();
                PnlMetrics.Clear();
                StatusText = "No ledger periods exist yet. Create a ledger book and period to start the governed book.";
                return;
            }

            await SelectPeriodAsync(defaultPeriodId.Value, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Page navigated away; nothing to report.
        }
        catch (Exception ex)
        {
            if (revision == _loadRevision)
            {
                PeriodsErrorText = ex.Message;
                StatusText = "Posted journal unavailable.";
            }
        }
        finally
        {
            if (revision == _loadRevision)
            {
                IsRefreshing = false;
            }
        }
    }

    public async Task SelectPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        SelectedPeriodId = periodId;
        SelectedPeriodLabel = Periods.FirstOrDefault(row => row.PeriodId == periodId)?.Label ?? "Selected period";
        foreach (var row in Periods)
        {
            row.IsSelected = row.PeriodId == periodId;
        }

        PeriodNotice = string.Empty;
        TrialBalanceErrorText = string.Empty;
        PnlErrorText = string.Empty;

        if (_client is null)
        {
            TrialBalanceErrorText = "The ledger reporting client is not available in this session.";
            return;
        }

        try
        {
            var trialBalanceTask = _client.GetTrialBalanceAsync(periodId, ct);
            var pnlTask = _client.GetPnlSummaryAsync(periodId, ct);
            await Task.WhenAll(trialBalanceTask, pnlTask).ConfigureAwait(true);

            ApplyTrialBalance(await trialBalanceTask.ConfigureAwait(true));
            ApplyPnl(await pnlTask.ConfigureAwait(true));
            StatusText = $"Posted journal for {SelectedPeriodLabel}.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Page navigated away; nothing to report.
        }
        catch (Exception ex)
        {
            TrialBalance.Clear();
            PnlMetrics.Clear();
            TrialBalanceErrorText = ex.Message;
        }
    }

    private void ApplyPeriods(IReadOnlyList<LedgerPeriodDto> periods)
    {
        Periods.Clear();
        foreach (var period in PostedLedgerProjection.SortPeriodsDescending(periods))
        {
            Periods.Add(new PostedLedgerPeriodRow(
                period.PeriodId,
                PostedLedgerProjection.DescribePeriod(period),
                PostedLedgerProjection.DescribePeriodStatus(period.Status),
                $"{period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}"));
        }
    }

    private void ApplyTrialBalance(ApiResponse<List<LedgerPeriodTrialBalanceLineDto>> response)
    {
        TrialBalance.Clear();

        if (!response.Success || response.Data is null)
        {
            if (PostedLedgerProjection.IsMissingSummary(response.StatusCode))
            {
                PeriodNotice = PostedLedgerProjection.MissingSummaryNotice;
            }
            else
            {
                TrialBalanceErrorText = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "The posted trial balance could not be loaded."
                    : response.ErrorMessage;
            }

            BalanceSummaryText = "Trial balance not loaded.";
            IsOutOfBalance = false;
            return;
        }

        foreach (var line in response.Data)
        {
            TrialBalance.Add(new PostedLedgerTrialBalanceRow(
                line.AccountName,
                line.AccountType,
                line.Symbol,
                line.Balance.ToString("C", CultureInfo.CurrentCulture),
                line.EntryCount));
        }

        IsOutOfBalance = PostedLedgerProjection.IsOutOfBalance(response.Data);
        var variance = PostedLedgerProjection.SumBalances(response.Data);
        BalanceSummaryText = IsOutOfBalance
            ? $"{TrialBalance.Count} accounts · out by {Math.Abs(variance).ToString("C", CultureInfo.CurrentCulture)}"
            : $"{TrialBalance.Count} accounts · in balance";
    }

    private void ApplyPnl(ApiResponse<LedgerPeriodPnlSummaryDto> response)
    {
        PnlMetrics.Clear();

        if (!response.Success || response.Data is null)
        {
            if (PostedLedgerProjection.IsMissingSummary(response.StatusCode))
            {
                PeriodNotice = PostedLedgerProjection.MissingSummaryNotice;
            }
            else
            {
                PnlErrorText = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "The posted P&L summary could not be loaded."
                    : response.ErrorMessage;
            }

            SignoffText = string.Empty;
            return;
        }

        var pnl = response.Data;
        PnlMetrics.Add(new PostedLedgerMetricRow("Total revenue", pnl.TotalRevenue.ToString("C", CultureInfo.CurrentCulture)));
        PnlMetrics.Add(new PostedLedgerMetricRow("Total expenses", pnl.TotalExpenses.ToString("C", CultureInfo.CurrentCulture)));
        PnlMetrics.Add(new PostedLedgerMetricRow("Net income", pnl.NetIncome.ToString("C", CultureInfo.CurrentCulture)));
        PnlMetrics.Add(new PostedLedgerMetricRow(
            "Period-on-period variance",
            pnl.PeriodOnPeriodVariance is { } variance
                ? variance.ToString("C", CultureInfo.CurrentCulture)
                : "No prior period"));
        PnlMetrics.Add(new PostedLedgerMetricRow(
            "Open breaks",
            pnl.OpenBreakCount.ToString(CultureInfo.CurrentCulture)));

        SignoffText = PostedLedgerProjection.DescribeSignoffStatus(pnl.SignoffStatus);
    }
}

/// <summary>A selectable ledger period in the desktop posted-journal surface.</summary>
public sealed class PostedLedgerPeriodRow : BindableBase
{
    private bool _isSelected;

    public PostedLedgerPeriodRow(Guid periodId, string label, string statusLabel, string rangeLabel)
    {
        PeriodId = periodId;
        Label = label;
        StatusLabel = statusLabel;
        RangeLabel = rangeLabel;
    }

    public Guid PeriodId { get; }

    public string Label { get; }

    public string StatusLabel { get; }

    public string RangeLabel { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record PostedLedgerTrialBalanceRow(
    string AccountName,
    string AccountType,
    string? Symbol,
    string BalanceLabel,
    int EntryCount);

public sealed record PostedLedgerMetricRow(string Label, string Value);
