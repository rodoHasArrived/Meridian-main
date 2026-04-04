using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Fund Accounts page.
/// Displays custodian accounts and bank accounts for the selected fund,
/// allows creating new accounts, recording balance snapshots, ingesting
/// statements, and running account-level reconciliation.
/// </summary>
public sealed partial class FundAccountsViewModel : BindableBase
{
    private readonly IFundAccountService _service;
    private readonly ILogger<FundAccountsViewModel> _logger;

    public FundAccountsViewModel(
        IFundAccountService service,
        ILogger<FundAccountsViewModel> logger)
    {
        _service = service;
        _logger  = logger;

        LoadFundAccountsCommand = new AsyncRelayCommand(LoadFundAccountsAsync);
        CreateAccountCommand    = new AsyncRelayCommand(CreateAccountAsync);
        UpdateDetailsCommand    = new AsyncRelayCommand(UpdateDetailsAsync, CanUpdateDetails);
        RecordSnapshotCommand   = new AsyncRelayCommand(RecordSnapshotAsync, CanRecordSnapshot);
        ReconcileCommand        = new AsyncRelayCommand(ReconcileAsync, CanReconcile);
    }

    // ── Selected fund ────────────────────────────────────────────────────────

    private Guid? _selectedFundId;
    public Guid? SelectedFundId
    {
        get => _selectedFundId;
        set
        {
            if (SetProperty(ref _selectedFundId, value))
                LoadFundAccountsCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Account lists ────────────────────────────────────────────────────────

    public ObservableCollection<AccountSummaryDto> CustodianAccounts { get; } = [];
    public ObservableCollection<AccountSummaryDto> BankAccounts      { get; } = [];
    public ObservableCollection<AccountSummaryDto> BrokerageAccounts { get; } = [];
    public ObservableCollection<AccountSummaryDto> OtherAccounts     { get; } = [];

    // ── Selected account ─────────────────────────────────────────────────────

    private AccountSummaryDto? _selectedAccount;
    public AccountSummaryDto? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                UpdateDetailsCommand.NotifyCanExecuteChanged();
                RecordSnapshotCommand.NotifyCanExecuteChanged();
                ReconcileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // ── Balance history ──────────────────────────────────────────────────────

    public ObservableCollection<AccountBalanceSnapshotDto> BalanceHistory { get; } = [];

    // ── Last reconciliation run ──────────────────────────────────────────────

    private AccountReconciliationRunDto? _lastReconciliationRun;
    public AccountReconciliationRunDto? LastReconciliationRun
    {
        get => _lastReconciliationRun;
        private set => SetProperty(ref _lastReconciliationRun, value);
    }

    // ── Status ───────────────────────────────────────────────────────────────

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand LoadFundAccountsCommand  { get; }
    public IAsyncRelayCommand CreateAccountCommand     { get; }
    public IAsyncRelayCommand UpdateDetailsCommand     { get; }
    public IAsyncRelayCommand RecordSnapshotCommand    { get; }
    public IAsyncRelayCommand ReconcileCommand         { get; }

    // ── Command implementations ───────────────────────────────────────────────

    private async Task LoadFundAccountsAsync()
    {
        if (SelectedFundId is null) return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var dto = await _service
                .GetFundAccountsAsync(SelectedFundId.Value)
                .ConfigureAwait(false);

            CustodianAccounts.Clear();
            BankAccounts.Clear();
            BrokerageAccounts.Clear();
            OtherAccounts.Clear();

            foreach (var a in dto.CustodianAccounts) CustodianAccounts.Add(a);
            foreach (var a in dto.BankAccounts)      BankAccounts.Add(a);
            foreach (var a in dto.BrokerageAccounts) BrokerageAccounts.Add(a);
            foreach (var a in dto.OtherAccounts)     OtherAccounts.Add(a);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load fund accounts for fund {FundId}", SelectedFundId);
            StatusMessage = $"Error loading accounts: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task CreateAccountAsync()
    {
        // Full dialog/wizard wired up in the View layer.
        // ViewModel exposes the command; the View opens the creation dialog.
        StatusMessage = "Open account creation dialog.";
        return Task.CompletedTask;
    }

    private bool CanUpdateDetails() => SelectedAccount is not null;
    private Task UpdateDetailsAsync()
    {
        StatusMessage = "Open account details editor.";
        return Task.CompletedTask;
    }

    private bool CanRecordSnapshot() => SelectedAccount is not null;
    private async Task RecordSnapshotAsync()
    {
        if (SelectedAccount is null) return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var history = await _service
                .GetBalanceHistoryAsync(SelectedAccount.AccountId)
                .ConfigureAwait(false);

            BalanceHistory.Clear();
            foreach (var s in history) BalanceHistory.Add(s);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load balance history for account {AccountId}", SelectedAccount.AccountId);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanReconcile() => SelectedAccount is not null;
    private async Task ReconcileAsync()
    {
        if (SelectedAccount is null) return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var request = new ReconcileAccountRequest(
                SelectedAccount.AccountId,
                AsOfDate: DateOnly.FromDateTime(DateTime.Today),
                RequestedBy: "desktop-user");

            LastReconciliationRun = await _service
                .ReconcileAccountAsync(request)
                .ConfigureAwait(false);

            StatusMessage = $"Reconciliation complete: {LastReconciliationRun.Status} " +
                            $"({LastReconciliationRun.TotalMatched}/{LastReconciliationRun.TotalChecks} checks matched)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation failed for account {AccountId}", SelectedAccount.AccountId);
            StatusMessage = $"Reconciliation error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
