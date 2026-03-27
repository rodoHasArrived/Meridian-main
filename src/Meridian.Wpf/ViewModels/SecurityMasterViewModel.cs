using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Security Master workstation page.
/// Wraps <see cref="SecurityMasterWorkstationDto"/>-backed search and detail
/// surfaced by the <c>/api/workstation/security-master</c> endpoints.
/// </summary>
public sealed class SecurityMasterViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private CancellationTokenSource? _cts;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SecurityMasterWorkstationDto> Results { get; } = new();

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    private bool _activeOnly = true;
    public bool ActiveOnly
    {
        get => _activeOnly;
        set => SetProperty(ref _activeOnly, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "Enter a query and press Search.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private SecurityMasterWorkstationDto? _selectedSecurity;
    public SecurityMasterWorkstationDto? SelectedSecurity
    {
        get => _selectedSecurity;
        set
        {
            if (SetProperty(ref _selectedSecurity, value))
            {
                RaisePropertyChanged(nameof(HasSelectedSecurity));
                RaisePropertyChanged(nameof(SelectedAssetClass));
                RaisePropertyChanged(nameof(SelectedCurrency));
                RaisePropertyChanged(nameof(SelectedStatusBadge));
                RaisePropertyChanged(nameof(SelectedIdentifier));
            }
        }
    }

    private string _historyText = string.Empty;
    public string HistoryText
    {
        get => _historyText;
        private set => SetProperty(ref _historyText, value);
    }

    private bool _isEditPanelVisible;
    public bool IsEditPanelVisible
    {
        get => _isEditPanelVisible;
        set => SetProperty(ref _isEditPanelVisible, value);
    }

    private bool _isDeactivatePanelVisible;
    public bool IsDeactivatePanelVisible
    {
        get => _isDeactivatePanelVisible;
        set => SetProperty(ref _isDeactivatePanelVisible, value);
    }

    private SecurityMasterEditViewModel? _editVm;
    public SecurityMasterEditViewModel? EditVm
    {
        get => _editVm;
        private set => SetProperty(ref _editVm, value);
    }

    private SecurityMasterDeactivateViewModel? _deactivateVm;
    public SecurityMasterDeactivateViewModel? DeactivateVm
    {
        get => _deactivateVm;
        private set => SetProperty(ref _deactivateVm, value);
    }

    // ── Derived display helpers ─────────────────────────────────────────────
    public bool HasSelectedSecurity => SelectedSecurity is not null;

    public string SelectedAssetClass =>
        SelectedSecurity?.Classification.AssetClass ?? string.Empty;

    public string SelectedCurrency =>
        SelectedSecurity?.EconomicDefinition.Currency ?? string.Empty;

    public string SelectedStatusBadge =>
        SelectedSecurity?.Status.ToString() ?? string.Empty;

    public string SelectedIdentifier =>
        SelectedSecurity?.Classification.PrimaryIdentifierValue is { } v
            ? $"{SelectedSecurity!.Classification.PrimaryIdentifierKind}: {v}"
            : string.Empty;

    // ── Commands ────────────────────────────────────────────────────────────
    public IRelayCommand CreateNewCommand { get; }
    public IRelayCommand EditSelectedCommand { get; }
    public IRelayCommand DeactivateSelectedCommand { get; }

    // ── Constructor ─────────────────────────────────────────────────────────
    public SecurityMasterViewModel(
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;

        CreateNewCommand = new RelayCommand(OnCreateNew);
        EditSelectedCommand = new RelayCommand(OnEditSelected, () => HasSelectedSecurity);
        DeactivateSelectedCommand = new RelayCommand(OnDeactivateSelected, () => HasSelectedSecurity && IsSelectedSecurityActive());
    }

    private void OnCreateNew()
    {
        EditVm = SecurityMasterEditViewModel.CreateNew(_loggingService, _notificationService);
        WireEditVmEvents();
        IsEditPanelVisible = true;
    }

    private void OnEditSelected()
    {
        if (SelectedSecurity is null)
            return;

        // Fetch the full detail so we have all the required information
        _ = LoadAndEditAsync();
    }

    private async Task LoadAndEditAsync()
    {
        if (SelectedSecurity?.SecurityId is not { } id)
            return;

        try
        {
            var detail = await ApiClientService.Instance
                .GetAsync<SecurityDetailDto>($"/api/workstation/security-master/securities/{id}", CancellationToken.None)
                .ConfigureAwait(false);

            if (detail is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    EditVm = new SecurityMasterEditViewModel(_loggingService, _notificationService);
                    EditVm.LoadForEdit(detail);
                    WireEditVmEvents();
                    IsEditPanelVisible = true;
                });
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to load security {id} for edit", ex);
            StatusText = "Failed to load security for editing.";
            _notificationService.ShowNotification("Security Master", "Failed to load security.", NotificationType.Error);
        }
    }

    private void OnDeactivateSelected()
    {
        if (SelectedSecurity is null)
            return;

        DeactivateVm = new SecurityMasterDeactivateViewModel(_loggingService, _notificationService)
        {
            SecurityName = SelectedSecurity.DisplayName,
            SecurityId = SelectedSecurity.SecurityId,
            Version = SelectedSecurity.EconomicDefinition?.Version ?? 0
        };
        WireDeactivateVmEvents();
        IsDeactivatePanelVisible = true;
    }

    private bool IsSelectedSecurityActive()
    {
        return SelectedSecurity?.Status == SecurityStatusDto.Active;
    }

    private void WireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested += OnEditCancelled;
        EditVm.SaveCompleted += OnEditSaveCompleted;
    }

    private void UnwireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested -= OnEditCancelled;
        EditVm.SaveCompleted -= OnEditSaveCompleted;
    }

    private void OnEditCancelled()
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
    }

    private void OnEditSaveCompleted(SecurityDetailDto result)
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
        StatusText = "Security saved successfully.";
        
        // Refresh search results
        _ = SearchAsync();
    }

    private void WireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested += OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted += OnDeactivateCompleted;
    }

    private void UnwireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested -= OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted -= OnDeactivateCompleted;
    }

    private void OnDeactivateCancelled()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
    }

    private void OnDeactivateCompleted()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
        StatusText = "Security deactivated successfully.";
        
        // Refresh search results
        _ = SearchAsync();
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    // ── Search ──────────────────────────────────────────────────────────────
    public async Task SearchAsync(CancellationToken ct = default)
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText = "Enter a query and press Search.";
            return;
        }

        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linked = _cts.Token;

        IsLoading = true;
        Results.Clear();
        SelectedSecurity = null;
        HistoryText = string.Empty;
        StatusText = "Searching…";

        try
        {
            var endpoint = $"/api/workstation/security-master/securities" +
                           $"?query={Uri.EscapeDataString(query)}&take=50&activeOnly={ActiveOnly}";

            var results = await ApiClientService.Instance
                .GetAsync<SecurityMasterWorkstationDto[]>(endpoint, linked)
                .ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Clear();
                if (results is { Length: > 0 })
                {
                    foreach (var r in results)
                        Results.Add(r);
                    StatusText = $"{results.Length} result{(results.Length == 1 ? "" : "s")} found.";
                }
                else
                {
                    StatusText = "No securities matched the query.";
                }
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master search failed", ex);
            StatusText = "Search failed. Check connection to backend.";
            _notificationService.ShowNotification("Security Master", "Search failed.", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Detail + history ─────────────────────────────────────────────────────
    public async Task LoadDetailAsync(Guid securityId, CancellationToken ct = default)
    {
        IsLoading = true;
        HistoryText = string.Empty;
        try
        {
            var detailTask = ApiClientService.Instance
                .GetAsync<SecurityMasterWorkstationDto>($"/api/workstation/security-master/securities/{securityId}", ct);
            var historyTask = ApiClientService.Instance
                .GetAsync<SecurityMasterEventEnvelope[]>($"/api/workstation/security-master/securities/{securityId}/history?take=20", ct);

            await Task.WhenAll(detailTask, historyTask).ConfigureAwait(false);

            var detail = await detailTask;
            var history = await historyTask;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (detail is not null)
                    SelectedSecurity = detail;

                HistoryText = history is { Length: > 0 }
                    ? string.Join(Environment.NewLine, history.Select(e =>
                        $"[{e.EventTimestamp:yyyy-MM-dd HH:mm}] {e.EventType}  v{e.StreamVersion}"))
                    : "(no history)";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError($"Security Master detail load failed for {securityId}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose() => Stop();
}
