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
                RaisePropertyChanged(nameof(SelectedAccountSummary));
            }
        }
    }

    public string SelectedAccountSummary
        => SelectedAccount is null
            ? "Select an account to inspect balances, reconciliation, and provider routing."
            : $"{SelectedAccount.DisplayName} • {SelectedAccount.AccountType} • {SelectedAccount.BaseCurrency} • {SelectedAccount.Institution ?? "Institution not set"}";

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
        private set => SetProperty(ref _providerRoutingStatus, value);
    }

    public IAsyncRelayCommand LoadFundAccountsCommand { get; }
    public IAsyncRelayCommand CreateAccountCommand { get; }
    public IAsyncRelayCommand UpdateDetailsCommand { get; }
    public IAsyncRelayCommand RecordSnapshotCommand { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }
    public IAsyncRelayCommand RefreshProviderRoutingCommand { get; }

    public async Task LoadFundAccountsAsync()
    {
        await _fundProfileCatalog.LoadAsync().ConfigureAwait(false);
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
            var dto = await _service.GetFundAccountsAsync(SelectedFundId.Value).ConfigureAwait(false);

            ReplaceCollection(CustodianAccounts, dto.CustodianAccounts);
            ReplaceCollection(BankAccounts, dto.BankAccounts);
            ReplaceCollection(BrokerageAccounts, dto.BrokerageAccounts);
            ReplaceCollection(OtherAccounts, dto.OtherAccounts);

            SelectedAccount ??= BrokerageAccounts.FirstOrDefault()
                ?? CustodianAccounts.FirstOrDefault()
                ?? BankAccounts.FirstOrDefault()
                ?? OtherAccounts.FirstOrDefault();

            if (SelectedAccount is not null)
                await RefreshProviderRoutingAsync().ConfigureAwait(false);
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
    {
        if (SelectedAccount is null)
            return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var history = await _service.GetBalanceHistoryAsync(SelectedAccount.AccountId).ConfigureAwait(false);
            ReplaceCollection(BalanceHistory, history);
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

            LastReconciliationRun = await _service.ReconcileAccountAsync(request).ConfigureAwait(false);
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

            await Task.WhenAll(connectionsTask, bindingsTask, trustTask).ConfigureAwait(false);

            if (!connectionsTask.Result.Success || !bindingsTask.Result.Success || !trustTask.Result.Success)
            {
                ProviderRoutingStatus = connectionsTask.Result.Error
                    ?? bindingsTask.Result.Error
                    ?? trustTask.Result.Error
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

            await Task.WhenAll(previewTasks).ConfigureAwait(false);

            ApplyProviderInsights(
                SelectedAccount,
                connectionsTask.Result.Connections,
                bindingsTask.Result.Bindings,
                trustTask.Result.Snapshots,
                previewTasks
                    .Where(task => task.Result.Success && task.Result.Preview is not null)
                    .Select(task => task.Result.Preview!)
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
        IReadOnlyList<RoutePreviewResponse> previews)
    {
        ProviderBindings.Clear();
        RoutePreviews.Clear();

        var connectionById = connections.ToDictionary(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase);
        var trustByConnectionId = trustSnapshots.ToDictionary(snapshot => snapshot.ConnectionId, StringComparer.OrdinalIgnoreCase);

        foreach (var binding in bindings
            .Where(binding => ScopeMatches(binding.Target, account))
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

    private static bool ScopeMatches(ProviderRouteScopeDto? scope, AccountSummaryDto account)
    {
        if (scope is null)
            return true;

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
