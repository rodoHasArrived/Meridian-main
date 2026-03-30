using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for deactivating a security in the Security Master.
/// Wraps POST /api/security-master/{id}/deactivate with reason and confirmation logic.
/// </summary>
public sealed class SecurityMasterDeactivateViewModel : BindableBase
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _securityName = string.Empty;
    public string SecurityName
    {
        get => _securityName;
        set => SetProperty(ref _securityName, value);
    }

    private Guid _securityId;
    public Guid SecurityId
    {
        get => _securityId;
        set => SetProperty(ref _securityId, value);
    }

    private long _version;
    public long Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    private string _reason = string.Empty;
    public string Reason
    {
        get => _reason;
        set => SetProperty(ref _reason, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ── Commands ────────────────────────────────────────────────────────────
    public IAsyncRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    // ── Events ──────────────────────────────────────────────────────────────
    public event Action? CancelRequested;
    public event Action? DeactivateCompleted;

    // ── Constructor ─────────────────────────────────────────────────────────
    public SecurityMasterDeactivateViewModel(
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;

        ConfirmCommand = new AsyncRelayCommand(DeactivateAsync);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke());
    }

    // ── Deactivation logic ──────────────────────────────────────────────────
    private async Task DeactivateAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Deactivating…";

        try
        {
            var request = new DeactivateSecurityRequest(
                SecurityId: SecurityId,
                ExpectedVersion: Version,
                EffectiveTo: DateTimeOffset.UtcNow,
                SourceSystem: "WPF-UI",
                UpdatedBy: "User",
                SourceRecordId: null,
                Reason: string.IsNullOrWhiteSpace(Reason) ? "Deactivated via WPF UI" : Reason);

            var response = await ApiClientService.Instance
                .PostWithResponseAsync<object>(
                    $"/api/security-master/deactivate",
                    request,
                    ct)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                StatusText = $"Deactivation failed: {response.ErrorMessage ?? "Unknown error"}";
                _notificationService.ShowNotification(
                    "Security Master",
                    "Failed to deactivate security.",
                    NotificationType.Error);
                return;
            }

            StatusText = "Security deactivated successfully.";
            DeactivateCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Deactivation cancelled.";
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master deactivation failed", ex);
            StatusText = "Deactivation failed. Check connection to backend.";
            _notificationService.ShowNotification(
                "Security Master",
                "Failed to deactivate security.",
                NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
