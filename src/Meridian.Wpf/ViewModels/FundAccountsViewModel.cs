using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.ProviderSdk;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the fund accounts workbench and account-level provider panels.
/// </summary>
public sealed partial class FundAccountsViewModel : BindableBase
{
    private readonly IFundAccountService _service;
    private readonly IFundProfileCatalog _fundProfileCatalog;
    private readonly ProviderManagementService _providerManagementService;
    private readonly ILogger<FundAccountsViewModel> _logger;

    public FundAccountsViewModel(
        IFundAccountService service,
        IFundProfileCatalog fundProfileCatalog,
        ProviderManagementService providerManagementService,
        ILogger<FundAccountsViewModel> logger)
    {
        _service = service;
        _fundProfileCatalog = fundProfileCatalog;
        _providerManagementService = providerManagementService;
        _logger = logger;

        LoadFundAccountsCommand = new AsyncRelayCommand(LoadFundAccountsAsync);
        CreateAccountCommand = new AsyncRelayCommand(CreateAccountAsync);
        UpdateDetailsCommand = new AsyncRelayCommand(UpdateDetailsAsync, CanUpdateDetails);
        RecordSnapshotCommand = new AsyncRelayCommand(RecordSnapshotAsync, CanRecordSnapshot);
        ReconcileCommand = new AsyncRelayCommand(ReconcileAsync, CanReconcile);
        RefreshProviderRoutingCommand = new AsyncRelayCommand(RefreshProviderRoutingAsync, CanRefreshProviderRouting);
        InspectSelectedAccountCommand = new AsyncRelayCommand(InspectSelectedAccountAsync, CanInspectSelectedAccount);
    }

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

    private string? _selectedFundProfileId;
    public string? SelectedFundProfileId
    {
        get => _selectedFundProfileId;
        private set
        {
            if (SetProperty(ref _selectedFundProfileId, value))
                RaisePropertyChanged(nameof(FundContextLabel));
        }
    }

    public string FundContextLabel
    {
        get
        {
            var profile = _fundProfileCatalog.CurrentFundProfile;
            if (profile is null)
                return "Select a fund profile to load accounts and provider routing.";

            return $"{profile.DisplayName} • {profile.BaseCurrency} • workspace {profile.DefaultWorkspaceId}";
        }
    }

    public ObservableCollection<AccountSummaryDto> CustodianAccounts { get; } = [];
    public ObservableCollection<AccountSummaryDto> BankAccounts { get; } = [];
    public ObservableCollection<AccountSummaryDto> BrokerageAccounts { get; } = [];
    public ObservableCollection<AccountSummaryDto> OtherAccounts { get; } = [];
    public ObservableCollection<AccountBalanceSnapshotDto> BalanceHistory { get; } = [];
    public ObservableCollection<FundAccountProviderBindingItem> ProviderBindings { get; } = [];
    public ObservableCollection<FundAccountRoutePreviewItem> RoutePreviews { get; } = [];

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
                RefreshProviderRoutingCommand.NotifyCanExecuteChanged();
                InspectSelectedAccountCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedAccountSummary));
                RaiseSelectedAccountInspectorProperties();
            }
        }
    }

    public string SelectedAccountSummary
        => SelectedAccount is null
            ? "Select an account to inspect balances, reconciliation, and provider routing."
            : $"{SelectedAccount.DisplayName} • {SelectedAccount.AccountType} • {SelectedAccount.BaseCurrency} • {SelectedAccount.Institution ?? "Institution not set"}";

    public string AccountQueueStatusText
        => $"{BrokerageAccounts.Count + CustodianAccounts.Count + BankAccounts.Count + OtherAccounts.Count} account(s) across {BrokerageAccounts.Count} brokerage, {CustodianAccounts.Count} custody, {BankAccounts.Count} bank, and {OtherAccounts.Count} other governance lanes.";

    public string TotalAccountCountText
        => (BrokerageAccounts.Count + CustodianAccounts.Count + BankAccounts.Count + OtherAccounts.Count).ToString();

    public string CustodyAndBankCountText
        => (CustodianAccounts.Count + BankAccounts.Count).ToString();

    public string BrokerageAccountCountText
        => BrokerageAccounts.Count.ToString();

    public string OtherAccountCountText
        => OtherAccounts.Count.ToString();

    public string SelectedAccountLifecycleText
        => SelectedAccount is null
            ? "Select an account to review lifecycle status."
            : $"{(SelectedAccount.IsActive ? "Active" : "Inactive")} • effective {SelectedAccount.EffectiveFrom:MMM dd yyyy}" +
              $"{(SelectedAccount.EffectiveTo is null ? string.Empty : $" through {SelectedAccount.EffectiveTo.Value:MMM dd yyyy}")}";

    public string SelectedAccountScopeText
        => SelectedAccount is null
            ? "No governance scope selected."
            : BuildSelectedAccountScopeText(SelectedAccount);

    public string SelectedAccountWorkflowLinkText
        => SelectedAccount is null
            ? "No workflow link available."
            : BuildSelectedAccountWorkflowLinkText(SelectedAccount);

    public string SelectedAccountRoutingReadinessText
        => SelectedAccount is null
            ? "Select an account to inspect routing readiness."
            : RoutePreviews.Count == 0
                ? "No route previews loaded for the selected account."
                : $"{RoutePreviews.Count} route preview(s) loaded, {RoutePreviews.Count(preview => string.Equals(preview.StatusLabel, "Blocked", StringComparison.OrdinalIgnoreCase))} blocked, {ProviderBindings.Count} scoped binding(s) matched.";

    public string SelectedAccountSecurityMasterText
        => SelectedAccount?.SharedDataAccess is { SecurityMaster: var securityMaster }
            ? securityMaster.IsAvailable
                ? $"Security Master ready • {securityMaster.AvailabilityDescription}"
                : $"Security Master unavailable • {securityMaster.AvailabilityDescription}"
            : "Security Master posture is not available for this account.";

    public string SelectedAccountHistoricalPriceText
        => SelectedAccount?.SharedDataAccess is { HistoricalPrices: var historicalPrices }
            ? historicalPrices.IsAvailable
                ? $"Historical prices ready • {historicalPrices.AvailabilityDescription}"
                : $"Historical prices unavailable • {historicalPrices.AvailabilityDescription}"
            : "Historical price posture is not available for this account.";

    public string SelectedAccountBackfillText
        => SelectedAccount?.SharedDataAccess is { Backfill: var backfill }
            ? backfill.IsAvailable
                ? $"Backfill available • {backfill.ProviderCount} provider(s) • {backfill.LastProvider ?? "no recent provider"}"
                : "Backfill is not available for this account scope."
            : "Backfill posture is not available for this account.";

    public string BalanceHistorySummaryText
        => SelectedAccount is null
            ? "Select an account to inspect recent balance snapshots."
            : BalanceHistory.Count == 0
                ? "No recent balance snapshots are loaded for the selected account."
                : $"Latest snapshot {BalanceHistory[0].AsOfDate:MMM dd yyyy} from {BalanceHistory[0].Source} with cash {BalanceHistory[0].CashBalance:C0}.";

    private AccountReconciliationRunDto? _lastReconciliationRun;
    public AccountReconciliationRunDto? LastReconciliationRun
    {
        get => _lastReconciliationRun;
        private set => SetProperty(ref _lastReconciliationRun, value);
    }

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

    private bool _isProviderRoutingBusy;
    public bool IsProviderRoutingBusy
    {
        get => _isProviderRoutingBusy;
        private set => SetProperty(ref _isProviderRoutingBusy, value);
    }

    private string? _providerRoutingStatus;
    public string? ProviderRoutingStatus
    {
        get => _providerRoutingStatus;
        private set
        {
            if (SetProperty(ref _providerRoutingStatus, value))
            {
                RaisePropertyChanged(nameof(SelectedAccountRoutingReadinessText));
            }
        }
    }

    public IAsyncRelayCommand LoadFundAccountsCommand { get; }
    public IAsyncRelayCommand CreateAccountCommand { get; }
    public IAsyncRelayCommand UpdateDetailsCommand { get; }
    public IAsyncRelayCommand RecordSnapshotCommand { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }
    public IAsyncRelayCommand RefreshProviderRoutingCommand { get; }
    public IAsyncRelayCommand InspectSelectedAccountCommand { get; }

    public async Task LoadFundAccountsAsync()
    {
        await _fundProfileCatalog.LoadAsync();
        SelectedFundProfileId = _fundProfileCatalog.CurrentFundProfile?.FundProfileId ?? _fundProfileCatalog.LastSelectedFundProfileId;

        if (SelectedFundId is null && !string.IsNullOrWhiteSpace(SelectedFundProfileId))
            SelectedFundId = FundProfileKeyTranslator.ToFundId(SelectedFundProfileId);

        if (SelectedFundId is null)
        {
            StatusMessage = "Select a fund profile first.";
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var dto = await _service.GetFundAccountsAsync(SelectedFundId.Value);

            ReplaceCollection(CustodianAccounts, dto.CustodianAccounts);
            ReplaceCollection(BankAccounts, dto.BankAccounts);
            ReplaceCollection(BrokerageAccounts, dto.BrokerageAccounts);
            ReplaceCollection(OtherAccounts, dto.OtherAccounts);

            SelectedAccount ??= BrokerageAccounts.FirstOrDefault()
                ?? CustodianAccounts.FirstOrDefault()
                ?? BankAccounts.FirstOrDefault()
                ?? OtherAccounts.FirstOrDefault();

            if (SelectedAccount is not null)
            {
                await InspectSelectedAccountAsync();
            }

            RaisePropertyChanged(nameof(AccountQueueStatusText));
            RaisePropertyChanged(nameof(TotalAccountCountText));
            RaisePropertyChanged(nameof(CustodyAndBankCountText));
            RaisePropertyChanged(nameof(BrokerageAccountCountText));
            RaisePropertyChanged(nameof(OtherAccountCountText));
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
        => await LoadBalanceHistoryAsync();

    private async Task LoadBalanceHistoryAsync()
    {
        if (SelectedAccount is null)
            return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var history = await _service.GetBalanceHistoryAsync(SelectedAccount.AccountId);
            ReplaceCollection(BalanceHistory, history);
            RaisePropertyChanged(nameof(BalanceHistorySummaryText));
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
        if (SelectedAccount is null)
            return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var request = new ReconcileAccountRequest(
                SelectedAccount.AccountId,
                AsOfDate: DateOnly.FromDateTime(DateTime.Today),
                RequestedBy: "desktop-user");

            LastReconciliationRun = await _service.ReconcileAccountAsync(request);
            StatusMessage = $"Reconciliation complete: {LastReconciliationRun.Status} ({LastReconciliationRun.TotalMatched}/{LastReconciliationRun.TotalChecks} checks matched)";
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

    private bool CanRefreshProviderRouting() => SelectedAccount is not null;

    private bool CanInspectSelectedAccount() => SelectedAccount is not null;

    public async Task InspectSelectedAccountAsync()
    {
        if (SelectedAccount is null)
        {
            ProviderBindings.Clear();
            RoutePreviews.Clear();
            BalanceHistory.Clear();
            ProviderRoutingStatus = "Select an account to inspect provider routing.";
            RaisePropertyChanged(nameof(BalanceHistorySummaryText));
            return;
        }

        await LoadBalanceHistoryAsync();
        await RefreshProviderRoutingAsync();
    }

    public async Task RefreshProviderRoutingAsync()
    {
        ProviderBindings.Clear();
        RoutePreviews.Clear();

        if (SelectedAccount is null)
        {
            ProviderRoutingStatus = "Select an account to inspect provider routing.";
            return;
        }

        IsProviderRoutingBusy = true;
        ProviderRoutingStatus = null;

        try
        {
            var connectionsTask = _providerManagementService.GetProviderConnectionsAsync();
            var bindingsTask = _providerManagementService.GetProviderBindingsAsync();
            var trustTask = _providerManagementService.GetProviderTrustSnapshotsAsync();

            await Task.WhenAll(connectionsTask, bindingsTask, trustTask);

            var connectionsResponse = await connectionsTask;
            var bindingsResponse = await bindingsTask;
            var trustResponse = await trustTask;

            if (!connectionsResponse.Success || !bindingsResponse.Success || !trustResponse.Success)
            {
                ProviderRoutingStatus = connectionsResponse.Error
                    ?? bindingsResponse.Error
                    ?? trustResponse.Error
                    ?? "Provider routing data is unavailable.";
                return;
            }

            var previewTasks = GetRelevantCapabilities(SelectedAccount.AccountType)
                .Select(capability => _providerManagementService.PreviewRouteAsync(new RoutePreviewRequest(
                    Capability: capability,
                    Workspace: _fundProfileCatalog.CurrentFundProfile?.DefaultWorkspaceId,
                    FundProfileId: SelectedFundProfileId,
                    EntityId: SelectedAccount.EntityId,
                    SleeveId: SelectedAccount.SleeveId,
                    VehicleId: SelectedAccount.VehicleId,
                    AccountId: SelectedAccount.AccountId,
                    RequireProductionReady: capability == ProviderCapabilityKind.OrderExecution.ToString())))
                .ToArray();

            var previewResponses = await Task.WhenAll(previewTasks);

            ApplyProviderInsights(
                SelectedAccount,
                connectionsResponse.Connections,
                bindingsResponse.Bindings,
                trustResponse.Snapshots,
                _fundProfileCatalog.CurrentFundProfile?.DefaultWorkspaceId,
                SelectedFundProfileId,
                previewResponses
                    .Where(response => response.Success && response.Preview is not null)
                    .Select(response => response.Preview!)
                    .ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load provider routing for account {AccountId}", SelectedAccount.AccountId);
            ProviderRoutingStatus = $"Provider routing error: {ex.Message}";
        }
        finally
        {
            IsProviderRoutingBusy = false;
        }
    }

    public void ApplyProviderInsights(
        AccountSummaryDto account,
        IReadOnlyList<ProviderConnectionDto> connections,
        IReadOnlyList<ProviderBindingDto> bindings,
        IReadOnlyList<ProviderTrustSnapshotDto> trustSnapshots,
        string? workspaceId,
        string? fundProfileId,
        IReadOnlyList<RoutePreviewResponse> previews)
    {
        ProviderBindings.Clear();
        RoutePreviews.Clear();

        var connectionById = connections.ToDictionary(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase);
        var trustByConnectionId = trustSnapshots.ToDictionary(snapshot => snapshot.ConnectionId, StringComparer.OrdinalIgnoreCase);

        foreach (var binding in bindings
            .Where(binding => ScopeMatches(binding.Target, account, workspaceId, fundProfileId))
            .OrderBy(binding => binding.Priority)
            .ThenBy(binding => binding.Capability, StringComparer.OrdinalIgnoreCase))
        {
            connectionById.TryGetValue(binding.ConnectionId, out var connection);
            trustByConnectionId.TryGetValue(binding.ConnectionId, out var trust);

            ProviderBindings.Add(new FundAccountProviderBindingItem(
                Capability: binding.Capability,
                ConnectionLabel: connection?.DisplayName ?? binding.ConnectionId,
                ConnectionType: connection?.ConnectionType ?? "Unknown",
                ScopeLabel: DescribeScope(binding.Target),
                SafetyMode: binding.SafetyModeOverride ?? "Inherited",
                TrustLabel: trust is null ? "Trust unavailable" : $"{trust.Score:F0}/100 • {trust.HealthStatus}",
                StatusLabel: connection is null
                    ? "Connection missing"
                    : connection.ProductionReady
                        ? "Production ready"
                        : "Draft / not certified"));
        }

        foreach (var preview in previews.OrderBy(preview => preview.Capability, StringComparer.OrdinalIgnoreCase))
        {
            var selectedLabel = preview.SelectedConnectionId is not null && connectionById.TryGetValue(preview.SelectedConnectionId, out var connection)
                ? connection.DisplayName
                : preview.SelectedConnectionId ?? "No route";

            RoutePreviews.Add(new FundAccountRoutePreviewItem(
                Capability: preview.Capability,
                SelectedConnectionLabel: selectedLabel,
                SafetyMode: preview.SafetyMode,
                StatusLabel: preview.IsRoutable ? "Routable" : "Blocked",
                ReasonSummary: preview.ReasonCodes.FirstOrDefault() ?? preview.PolicyGate ?? "No route explanation available.",
                FallbackSummary: preview.FallbackConnectionIds.Length == 0
                    ? "No fallback"
                    : string.Join(", ", preview.FallbackConnectionIds)));
        }

        ProviderRoutingStatus = ProviderBindings.Count == 0 && RoutePreviews.Count == 0
            ? "No scoped provider bindings matched this account."
            : $"Loaded {ProviderBindings.Count} binding(s) and {RoutePreviews.Count} route preview(s).";
    }

    private void RaiseSelectedAccountInspectorProperties()
    {
        RaisePropertyChanged(nameof(SelectedAccountLifecycleText));
        RaisePropertyChanged(nameof(SelectedAccountScopeText));
        RaisePropertyChanged(nameof(SelectedAccountWorkflowLinkText));
        RaisePropertyChanged(nameof(SelectedAccountRoutingReadinessText));
        RaisePropertyChanged(nameof(SelectedAccountSecurityMasterText));
        RaisePropertyChanged(nameof(SelectedAccountHistoricalPriceText));
        RaisePropertyChanged(nameof(SelectedAccountBackfillText));
        RaisePropertyChanged(nameof(BalanceHistorySummaryText));
    }

    private static string BuildSelectedAccountScopeText(AccountSummaryDto account)
    {
        var segments = new List<string> { account.AccountType.ToString(), account.BaseCurrency };

        if (account.EntityId.HasValue)
            segments.Add($"Entity {account.EntityId.Value.ToString("N")[..8]}");

        if (account.SleeveId.HasValue)
            segments.Add($"Sleeve {account.SleeveId.Value.ToString("N")[..8]}");

        if (account.VehicleId.HasValue)
            segments.Add($"Vehicle {account.VehicleId.Value.ToString("N")[..8]}");

        if (account.FundId.HasValue)
            segments.Add($"Fund {account.FundId.Value.ToString("N")[..8]}");

        return string.Join(" • ", segments);
    }

    private static string BuildSelectedAccountWorkflowLinkText(AccountSummaryDto account)
    {
        var links = new List<string>();

        if (!string.IsNullOrWhiteSpace(account.PortfolioId))
            links.Add($"Portfolio {account.PortfolioId}");

        if (!string.IsNullOrWhiteSpace(account.LedgerReference))
            links.Add($"Ledger {account.LedgerReference}");

        if (!string.IsNullOrWhiteSpace(account.StrategyId))
            links.Add($"Strategy {account.StrategyId}");

        if (!string.IsNullOrWhiteSpace(account.RunId))
            links.Add($"Run {account.RunId}");

        return links.Count == 0
            ? "No linked portfolio, ledger, strategy, or run has been assigned yet."
            : string.Join(" • ", links);
    }

    private static bool ScopeMatches(
        ProviderRouteScopeDto? scope,
        AccountSummaryDto account,
        string? workspaceId,
        string? fundProfileId)
    {
        if (scope is null)
            return true;

        if (!string.IsNullOrWhiteSpace(scope.Workspace) &&
            !string.Equals(scope.Workspace, workspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(scope.FundProfileId) &&
            !string.Equals(scope.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (scope.AccountId.HasValue && scope.AccountId != account.AccountId)
            return false;

        if (scope.EntityId.HasValue && scope.EntityId != account.EntityId)
            return false;

        if (scope.SleeveId.HasValue && scope.SleeveId != account.SleeveId)
            return false;

        if (scope.VehicleId.HasValue && scope.VehicleId != account.VehicleId)
            return false;

        return true;
    }

    private static string DescribeScope(ProviderRouteScopeDto? scope)
    {
        if (scope is null)
            return "Global";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(scope.Workspace))
            parts.Add(scope.Workspace);
        if (!string.IsNullOrWhiteSpace(scope.FundProfileId))
            parts.Add(scope.FundProfileId);
        if (scope.EntityId.HasValue)
            parts.Add($"Entity {scope.EntityId.Value.ToString("N")[..8]}");
        if (scope.SleeveId.HasValue)
            parts.Add($"Sleeve {scope.SleeveId.Value.ToString("N")[..8]}");
        if (scope.VehicleId.HasValue)
            parts.Add($"Vehicle {scope.VehicleId.Value.ToString("N")[..8]}");
        if (scope.AccountId.HasValue)
            parts.Add($"Account {scope.AccountId.Value.ToString("N")[..8]}");

        return parts.Count == 0 ? "Global" : string.Join(" • ", parts);
    }

    private static IReadOnlyList<string> GetRelevantCapabilities(AccountTypeDto accountType)
        => accountType switch
        {
            AccountTypeDto.Brokerage or AccountTypeDto.PrimeBroker =>
            [
                ProviderCapabilityKind.OrderExecution.ToString(),
                ProviderCapabilityKind.ExecutionHistory.ToString(),
                ProviderCapabilityKind.AccountBalances.ToString(),
                ProviderCapabilityKind.AccountPositions.ToString()
            ],
            AccountTypeDto.Bank =>
            [
                ProviderCapabilityKind.CashTransactions.ToString(),
                ProviderCapabilityKind.BankStatements.ToString(),
                ProviderCapabilityKind.AccountBalances.ToString()
            ],
            AccountTypeDto.Custody =>
            [
                ProviderCapabilityKind.AccountPositions.ToString(),
                ProviderCapabilityKind.ExecutionHistory.ToString(),
                ProviderCapabilityKind.ReconciliationFeed.ToString()
            ],
            _ =>
            [
                ProviderCapabilityKind.AccountBalances.ToString(),
                ProviderCapabilityKind.ReconciliationFeed.ToString()
            ]
        };

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }
}
