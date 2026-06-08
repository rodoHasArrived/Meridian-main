using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.DirectLending;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

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
    private InspectorPanelModel _selectedLoanInspector = BuildEmptyLoanInspector();
    private InspectorPanelModel _loanActionInspector = BuildLoanActionInspector(null, isDetailLoading: false, isPostingAccrual: false, string.Empty);
    private bool _hasLoadedPortfolioSnapshot;
    private bool _isRefreshing;
    private bool _isPostingAccrual;

    // ── Collections ─────────────────────────────────────────────────────────

    public ObservableCollection<LoanSummaryDto> Loans { get; } = [];
    public ObservableCollection<DailyAccrualEntryDto> Accruals { get; } = [];
    public ObservableCollection<CashTransactionDto> CashTransactions { get; } = [];
    public WorkstationTableModel<LoanSummaryDto> LoansTable { get; }
    public WorkstationTableModel<DailyAccrualEntryDto> AccrualsTable { get; }
    public WorkstationTableModel<CashTransactionDto> CashTransactionsTable { get; }

    // ── Selected loan state ──────────────────────────────────────────────────

    private LoanSummaryDto? _selectedLoan;
    public LoanSummaryDto? SelectedLoan
    {
        get => _selectedLoan;
        set
        {
            if (SetProperty(ref _selectedLoan, value))
            {
                RaiseSelectedLoanStateChanged();
                _ = LoadLoanDetailAsync(value);
            }
        }
    }

    private LoanContractDetailDto? _selectedContract;
    public LoanContractDetailDto? SelectedContract
    {
        get => _selectedContract;
        private set
        {
            if (SetProperty(ref _selectedContract, value))
            {
                UpdateLoanInspectors();
            }
        }
    }

    private LoanServicingStateDto? _selectedServicing;
    public LoanServicingStateDto? SelectedServicing
    {
        get => _selectedServicing;
        private set
        {
            if (SetProperty(ref _selectedServicing, value))
            {
                UpdateLoanInspectors();
            }
        }
    }

    // ── Status & filter ──────────────────────────────────────────────────────

    private string _statusText = "Loading portfolio…";
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(LoanActionStateDetail));
                UpdateLoanInspectors();
            }
        }
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
        private set
        {
            if (SetProperty(ref _isDetailLoading, value))
            {
                RaiseSelectedLoanStateChanged();
            }
        }
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
        private set
        {
            if (SetProperty(ref _accrualResultText, value))
            {
                UpdateLoanInspectors();
            }
        }
    }

    // ── Computed ─────────────────────────────────────────────────────────────

    public bool HasSelectedLoan => SelectedLoan is not null;

    public bool HasLoans => Loans.Count > 0;

    public bool IsLoanGridVisible => HasLoans;

    public bool IsLoanEmptyStateVisible => !HasLoans;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRefreshPortfolio));
                OnPropertyChanged(nameof(RefreshTooltip));
                OnPropertyChanged(nameof(LoanActionStateDetail));
                UpdateLoanInspectors();
            }
        }
    }

    public bool IsPostingAccrual
    {
        get => _isPostingAccrual;
        private set
        {
            if (SetProperty(ref _isPostingAccrual, value))
            {
                RaiseSelectedLoanStateChanged();
            }
        }
    }

    public bool CanRefreshPortfolio => CanRefreshPortfolioForState(IsRefreshing);

    public bool CanPostAccrual => CanPostAccrualForState(SelectedLoan, IsDetailLoading, IsPostingAccrual);

    public string RefreshTooltip => IsRefreshing
        ? "Portfolio refresh is already running."
        : "Refresh the direct-lending portfolio from the shared workstation API.";

    public string PostAccrualTooltip => BuildPostAccrualTooltip(SelectedLoan, IsDetailLoading, IsPostingAccrual);

    public string LoanActionStateTitle => HasSelectedLoan ? "Selected loan ready for review" : "Select a loan";

    public string LoanActionStateDetail => HasSelectedLoan
        ? PostAccrualTooltip
        : "Select a loan row before posting accruals or reviewing servicing evidence.";

    public InspectorPanelModel SelectedLoanInspector
    {
        get => _selectedLoanInspector;
        private set => SetProperty(ref _selectedLoanInspector, value);
    }

    public InspectorPanelModel LoanActionInspector
    {
        get => _loanActionInspector;
        private set => SetProperty(ref _loanActionInspector, value);
    }

    public IReadOnlyList<string> StatusFilters { get; } =
        ["All", "Draft", "Approved", "Active", "Suspended", "Matured", "Closed", "Defaulted"];

    // ── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PostAccrualCommand { get; }

    // ── Constructor ──────────────────────────────────────────────────────────

    internal DirectLendingViewModel(ApiClientService apiClient)
    {
        _apiClient = apiClient;

        LoansTable = new WorkstationTableModel<LoanSummaryDto>(
            Loans,
            [
                new("Facility", nameof(LoanSummaryDto.FacilityName), 190),
                new("Borrower", nameof(LoanSummaryDto.BorrowerName), 150),
                new("Status", nameof(LoanSummaryDto.Status), 95),
                new("Principal", nameof(LoanSummaryDto.PrincipalOutstanding), 110, "{0:C0}"),
                new("Available", nameof(LoanSummaryDto.AvailableToDraw), 105, "{0:C0}"),
                new("Maturity", nameof(LoanSummaryDto.MaturityDate), 95, "{0:d}")
            ],
            "Direct lending loans",
            "No loans available",
            "Refresh after the direct-lending API is reachable or clear filters that hide retained loans.");

        AccrualsTable = new WorkstationTableModel<DailyAccrualEntryDto>(
            Accruals,
            [
                new("Date", nameof(DailyAccrualEntryDto.AccrualDate), 95, "{0:d}"),
                new("Interest", nameof(DailyAccrualEntryDto.InterestAmount), 105, "{0:C2}"),
                new("Commit. Fee", nameof(DailyAccrualEntryDto.CommitmentFeeAmount), 105, "{0:C2}"),
                new("Penalty", nameof(DailyAccrualEntryDto.PenaltyAmount), 95, "{0:C2}"),
                new("Rate", nameof(DailyAccrualEntryDto.AnnualRateApplied), 80, "{0:P3}")
            ],
            "Loan accruals",
            "No accrual evidence",
            "Select a retained loan with accrual projections before reviewing daily accrual evidence.");

        CashTransactionsTable = new WorkstationTableModel<CashTransactionDto>(
            CashTransactions,
            [
                new("Effective", nameof(CashTransactionDto.EffectiveDate), 95, "{0:d}"),
                new("Type", nameof(CashTransactionDto.TransactionType), 120),
                new("Amount", nameof(CashTransactionDto.Amount), 110, "{0:C2}"),
                new("Currency", nameof(CashTransactionDto.Currency), 85),
                new("External Ref", nameof(CashTransactionDto.ExternalRef), 160)
            ],
            "Loan cash transactions",
            "No cash transactions",
            "Select a retained loan with cash activity before reviewing transaction evidence.");

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => CanRefreshPortfolio);
        PostAccrualCommand = new AsyncRelayCommand(() => PostAccrualAsync(), () => CanPostAccrual);
    }

    // ── Initialisation ───────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IsRefreshing = true;
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

            _hasLoadedPortfolioSnapshot = portfolio is not null;
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
            _hasLoadedPortfolioSnapshot = false;
            _allLoans.Clear();
            ApplyFilter();
            StatusText = "Failed to load portfolio — check service connection.";
        }
        finally
        {
            IsRefreshing = false;
            RaiseLoanCollectionStateChanged();
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
            IsDetailLoading = false;
            UpdateLoanInspectors();
            return;
        }

        var requestedLoanId = summary.LoanId;
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

            if (SelectedLoan?.LoanId != requestedLoanId)
            {
                return;
            }

            var contract = await contractTask.ConfigureAwait(false);
            var servicing = await servicingTask.ConfigureAwait(false);
            var accruals = await accrualsTask.ConfigureAwait(false);
            var cash = await cashTask.ConfigureAwait(false);

            SelectedContract = contract;
            SelectedServicing = servicing;

            Accruals.Clear();
            if (accruals is { })
            {
                foreach (var a in accruals.OrderByDescending(a => a.AccrualDate))
                {
                    Accruals.Add(a);
                }
            }

            CashTransactions.Clear();
            if (cash is { })
            {
                foreach (var tx in cash.OrderByDescending(t => t.EffectiveDate))
                {
                    CashTransactions.Add(tx);
                }
            }
        }
        catch (Exception)
        {
            if (SelectedLoan?.LoanId == requestedLoanId)
            {
                SelectedContract = null;
                SelectedServicing = null;
                Accruals.Clear();
                CashTransactions.Clear();
                AccrualResultText = "Loan detail unavailable — check the direct-lending API before posting accruals.";
                AccrualResultVisibility = Visibility.Visible;
            }
        }
        finally
        {
            if (SelectedLoan?.LoanId == requestedLoanId)
            {
                IsDetailLoading = false;
                UpdateLoanInspectors();
            }
        }
    }

    private async Task PostAccrualAsync(CancellationToken ct = default)
    {
        if (!CanPostAccrual || SelectedLoan is null)
        {
            return;
        }

        IsPostingAccrual = true;
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
        finally
        {
            IsPostingAccrual = false;
        }
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        var selectedLoanId = SelectedLoan?.LoanId;
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

        var nextSelection = selectedLoanId is Guid loanId
            ? Loans.FirstOrDefault(l => l.LoanId == loanId)
            : null;
        nextSelection ??= Loans.FirstOrDefault();

        if (!Equals(SelectedLoan, nextSelection))
        {
            SelectedLoan = nextSelection;
        }
        else
        {
            UpdateLoanInspectors();
        }

        RaiseLoanCollectionStateChanged();
    }

    private void NotifyCommandsChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        PostAccrualCommand.NotifyCanExecuteChanged();
    }

    private void RaiseSelectedLoanStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedLoan));
        OnPropertyChanged(nameof(CanPostAccrual));
        OnPropertyChanged(nameof(PostAccrualTooltip));
        OnPropertyChanged(nameof(LoanActionStateTitle));
        OnPropertyChanged(nameof(LoanActionStateDetail));
        NotifyCommandsChanged();
        UpdateLoanInspectors();
    }

    private void RaiseLoanCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasLoans));
        OnPropertyChanged(nameof(IsLoanGridVisible));
        OnPropertyChanged(nameof(IsLoanEmptyStateVisible));
        OnPropertyChanged(nameof(LoanActionStateTitle));
        OnPropertyChanged(nameof(LoanActionStateDetail));
        UpdateLoanInspectors();
    }

    private void UpdateLoanInspectors()
    {
        SelectedLoanInspector = SelectedLoan is null
            ? BuildEmptyLoanInspector()
            : BuildLoanInspector(SelectedLoan, SelectedContract, SelectedServicing, IsDetailLoading);
        LoanActionInspector = BuildLoanActionInspector(SelectedLoan, IsDetailLoading, IsPostingAccrual, AccrualResultText);
    }

    public static bool CanRefreshPortfolioForState(bool isRefreshing) => !isRefreshing;

    public static bool CanPostAccrualForState(
        LoanSummaryDto? selectedLoan,
        bool isDetailLoading,
        bool isPostingAccrual)
        => selectedLoan is not null && !isDetailLoading && !isPostingAccrual;

    internal static string BuildPostAccrualTooltip(
        LoanSummaryDto? selectedLoan,
        bool isDetailLoading,
        bool isPostingAccrual)
    {
        if (selectedLoan is null)
        {
            return "Select a direct-lending loan before posting daily accruals.";
        }

        if (isPostingAccrual)
        {
            return "Daily accrual posting is already running for the selected loan.";
        }

        if (isDetailLoading)
        {
            return "Wait for the selected loan detail to load before posting accruals.";
        }

        return $"Post today's daily accrual for {selectedLoan.FacilityName}.";
    }

    internal static InspectorPanelModel BuildLoanInspector(
        LoanSummaryDto selected,
        LoanContractDetailDto? contract,
        LoanServicingStateDto? servicing,
        bool isDetailLoading)
    {
        var detail = isDetailLoading
            ? "Loan contract and servicing evidence are loading from the shared direct-lending API."
            : contract is null || servicing is null
                ? "Loan detail is unavailable; refresh or verify the direct-lending API before acting."
                : "Review retained contract, servicing balance, and activity evidence before posting accruals.";

        return new InspectorPanelModel
        {
            Title = selected.FacilityName,
            Subtitle = selected.BorrowerName,
            Detail = detail,
            Badge = new WorkstationBadgeModel("Status", selected.Status.ToString(), "\uE9D9", ToneForLoanStatus(selected.Status)),
            Facts =
            [
                new("Principal", selected.PrincipalOutstanding.ToString("C0")),
                new("Available", selected.AvailableToDraw.ToString("C0")),
                new("Accrued interest", selected.InterestAccruedUnpaid.ToString("C2")),
                new("Penalty", selected.PenaltyAccruedUnpaid.ToString("C2")),
                new("Maturity", selected.MaturityDate.ToString("d")),
                new("Last accrual", selected.LastAccrualDate?.ToString("d") ?? "Not posted"),
                new("Commitment", (contract?.CurrentTerms.CommitmentAmount ?? selected.CommitmentAmount).ToString("C0")),
                new("Revision", servicing?.ServicingRevision.ToString("N0") ?? "Unavailable")
            ]
        };
    }

    private static InspectorPanelModel BuildEmptyLoanInspector()
        => new()
        {
            Title = "No loan selected",
            Subtitle = "Direct lending inspector",
            Detail = "Select a retained loan row to inspect contract terms, servicing balances, accrual evidence, and cash activity."
        };

    internal static InspectorPanelModel BuildLoanActionInspector(
        LoanSummaryDto? selected,
        bool isDetailLoading,
        bool isPostingAccrual,
        string accrualResultText)
        => new()
        {
            Title = selected is null ? "Loan actions unavailable" : "Selected loan actions",
            Subtitle = selected?.FacilityName ?? "No row selected",
            Detail = selected is null
                ? "Accrual posting stays disabled until an operator selects a retained loan."
                : BuildPostAccrualTooltip(selected, isDetailLoading, isPostingAccrual),
            Badge = new WorkstationBadgeModel(
                "Accrual",
                CanPostAccrualForState(selected, isDetailLoading, isPostingAccrual) ? "Ready" : "Blocked",
                "\uE8FD",
                CanPostAccrualForState(selected, isDetailLoading, isPostingAccrual) ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Post accrual", BuildPostAccrualReadiness(selected, isDetailLoading, isPostingAccrual)),
                new("Target", selected?.FacilityName ?? "Loan table"),
                new("Result", string.IsNullOrWhiteSpace(accrualResultText) ? "No accrual submitted this session" : accrualResultText)
            ]
        };

    private static string BuildPostAccrualReadiness(
        LoanSummaryDto? selected,
        bool isDetailLoading,
        bool isPostingAccrual)
    {
        if (selected is null)
        {
            return "Select loan";
        }

        if (isPostingAccrual)
        {
            return "Posting";
        }

        return isDetailLoading ? "Detail loading" : "Ready";
    }

    private static string ToneForLoanStatus(LoanStatus status)
        => status switch
        {
            LoanStatus.Active or LoanStatus.Approved => WorkspaceTone.Success,
            LoanStatus.Suspended or LoanStatus.Matured or LoanStatus.Workout => WorkspaceTone.Warning,
            LoanStatus.Defaulted or LoanStatus.NonPerforming => WorkspaceTone.Danger,
            LoanStatus.Closed => WorkspaceTone.Neutral,
            _ => WorkspaceTone.Info
        };
}
