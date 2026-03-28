using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.DirectLending;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Direct Lending workstation page.
/// Provides a loan portfolio browser with per-loan servicing details, accrual history,
/// and a manual accrual trigger for operators.
/// All HTTP communication goes through <see cref="ApiClientService"/> to keep the WPF
/// layer decoupled from the backend service layer.
/// </summary>
public sealed class DirectLendingViewModel : BindableBase
{
    private readonly ApiClientService _apiClient;
    private readonly List<LoanSummaryDto> _allLoans = new();

    // ── Collections ─────────────────────────────────────────────────────────

    public ObservableCollection<LoanSummaryDto> Loans { get; } = [];
    public ObservableCollection<DailyAccrualEntryDto> Accruals { get; } = [];
    public ObservableCollection<CashTransactionDto> CashTransactions { get; } = [];

    // ── Selected loan state ──────────────────────────────────────────────────

    private LoanSummaryDto? _selectedLoan;
    public LoanSummaryDto? SelectedLoan
    {
        get => _selectedLoan;
        set
        {
            if (SetProperty(ref _selectedLoan, value))
            {
                RaisePropertyChanged(nameof(HasSelectedLoan));
                NotifyCommandsChanged();
                _ = LoadLoanDetailAsync(value);
            }
        }
    }

    private LoanContractDetailDto? _selectedContract;
    public LoanContractDetailDto? SelectedContract
    {
        get => _selectedContract;
        private set => SetProperty(ref _selectedContract, value);
    }

    private LoanServicingStateDto? _selectedServicing;
    public LoanServicingStateDto? SelectedServicing
    {
        get => _selectedServicing;
        private set => SetProperty(ref _selectedServicing, value);
    }

    // ── Status & filter ──────────────────────────────────────────────────────

    private string _statusText = "Loading portfolio…";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    private string _selectedStatusFilter = "All";
    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    private bool _isDetailLoading;
    public bool IsDetailLoading
    {
        get => _isDetailLoading;
        private set => SetProperty(ref _isDetailLoading, value);
    }

    private Visibility _accrualResultVisibility = Visibility.Collapsed;
    public Visibility AccrualResultVisibility
    {
        get => _accrualResultVisibility;
        private set => SetProperty(ref _accrualResultVisibility, value);
    }

    private string _accrualResultText = string.Empty;
    public string AccrualResultText
    {
        get => _accrualResultText;
        private set => SetProperty(ref _accrualResultText, value);
    }

    // ── Computed ─────────────────────────────────────────────────────────────

    public bool HasSelectedLoan => SelectedLoan is not null;

    public IReadOnlyList<string> StatusFilters { get; } =
        ["All", "Draft", "Approved", "Active", "Suspended", "Matured", "Closed", "Defaulted"];

    // ── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PostAccrualCommand { get; }

    // ── Constructor ──────────────────────────────────────────────────────────

    public DirectLendingViewModel() : this(ApiClientService.Instance) { }

    internal DirectLendingViewModel(ApiClientService apiClient)
    {
        _apiClient = apiClient;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        PostAccrualCommand = new AsyncRelayCommand(() => PostAccrualAsync());
    }

    // ── Initialisation ───────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        StatusText = "Loading portfolio…";

        try
        {
            var portfolio = await _apiClient.GetAsync<LoanPortfolioSummaryDto>(
                "/api/loans/portfolio", ct).ConfigureAwait(false);

            _allLoans.Clear();
            if (portfolio?.Loans is not null)
            {
                _allLoans.AddRange(portfolio.Loans);
            }

            ApplyFilter();

            StatusText = portfolio is null
                ? "Service unavailable — backend not reachable."
                : $"{portfolio.TotalLoans} loan(s) — {portfolio.ActiveLoans} active, " +
                  $"${portfolio.TotalPrincipalOutstanding:N0} principal outstanding";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled.";
        }
        catch (Exception)
        {
            StatusText = "Failed to load portfolio — check service connection.";
        }
    }

    private async Task LoadLoanDetailAsync(LoanSummaryDto? summary)
    {
        if (summary is null)
        {
            SelectedContract = null;
            SelectedServicing = null;
            Accruals.Clear();
            CashTransactions.Clear();
            return;
        }

        IsDetailLoading = true;
        AccrualResultVisibility = Visibility.Collapsed;

        try
        {
            var contractTask = _apiClient.GetAsync<LoanContractDetailDto>(
                $"/api/loans/{summary.LoanId}");
            var servicingTask = _apiClient.GetAsync<LoanServicingStateDto>(
                $"/api/loans/{summary.LoanId}/servicing-state");
            var accrualsTask = _apiClient.GetAsync<List<DailyAccrualEntryDto>>(
                $"/api/loans/{summary.LoanId}/projections/accruals");
            var cashTask = _apiClient.GetAsync<List<CashTransactionDto>>(
                $"/api/loans/{summary.LoanId}/cash-transactions");

            await Task.WhenAll(contractTask, servicingTask, accrualsTask, cashTask)
                .ConfigureAwait(false);

            SelectedContract = contractTask.Result;
            SelectedServicing = servicingTask.Result;

            Accruals.Clear();
            if (accrualsTask.Result is { } accruals)
            {
                foreach (var a in accruals.OrderByDescending(a => a.AccrualDate))
                {
                    Accruals.Add(a);
                }
            }

            CashTransactions.Clear();
            if (cashTask.Result is { } cash)
            {
                foreach (var tx in cash.OrderByDescending(t => t.EffectiveDate))
                {
                    CashTransactions.Add(tx);
                }
            }
        }
        catch (Exception)
        {
            // Detail load failure is non-fatal — the list stays visible
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private async Task PostAccrualAsync(CancellationToken ct = default)
    {
        if (SelectedLoan is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var body = new { AccrualDate = today };

        try
        {
            var result = await _apiClient.PostAsync<DailyAccrualEntryDto>(
                $"/api/loans/{SelectedLoan.LoanId}/accruals/daily", body, ct)
                .ConfigureAwait(false);

            AccrualResultText = result is not null
                ? $"Accrual posted for {result.AccrualDate}: interest ${result.InterestAmount:N2}, " +
                  $"commitment fee ${result.CommitmentFeeAmount:N2}"
                : "Accrual posted.";
            AccrualResultVisibility = Visibility.Visible;

            await LoadLoanDetailAsync(SelectedLoan).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AccrualResultText = $"Accrual failed: {ex.Message}";
            AccrualResultVisibility = Visibility.Visible;
        }
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        IEnumerable<LoanSummaryDto> filtered = _allLoans;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            filtered = filtered.Where(l =>
                l.FacilityName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                l.BorrowerName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedStatusFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<LoanStatus>(SelectedStatusFilter, ignoreCase: true, out var status))
        {
            filtered = filtered.Where(l => l.Status == status);
        }

        Loans.Clear();
        foreach (var loan in filtered)
        {
            Loans.Add(loan);
        }
    }

    private void NotifyCommandsChanged()
    {
        PostAccrualCommand.NotifyCanExecuteChanged();
    }
}
