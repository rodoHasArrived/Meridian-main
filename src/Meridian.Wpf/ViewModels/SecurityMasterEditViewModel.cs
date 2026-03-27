using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for creating or editing a security in the Security Master.
/// Supports both create (POST /api/security-master/create) and amend (POST /api/security-master/{id}/amend).
/// </summary>
public sealed class SecurityMasterEditViewModel : BindableBase
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;

    // Static asset class and identifier kind lists
    private static readonly string[] AssetClassesArray =
    [
        "Equity", "Bond", "Option", "Future", "FxSpot",
        "Deposit", "Repo", "Swap", "DirectLoan",
        "CertificateOfDeposit", "CommercialPaper",
        "TreasuryBill", "CashSweep", "OtherSecurity"
    ];

    private static readonly string[] IdentifierKindsArray =
    [
        "Ticker", "ISIN", "CUSIP", "FIGI", "SEDOL"
    ];

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    private string _assetClass = "Equity";
    public string AssetClass
    {
        get => _assetClass;
        set => SetProperty(ref _assetClass, value);
    }

    private string _currency = "USD";
    public string Currency
    {
        get => _currency;
        set => SetProperty(ref _currency, value);
    }

    private string _exchange = string.Empty;
    public string Exchange
    {
        get => _exchange;
        set => SetProperty(ref _exchange, value);
    }

    private string _issuerName = string.Empty;
    public string IssuerName
    {
        get => _issuerName;
        set => SetProperty(ref _issuerName, value);
    }

    private string _primaryIdentifierKind = "Ticker";
    public string PrimaryIdentifierKind
    {
        get => _primaryIdentifierKind;
        set => SetProperty(ref _primaryIdentifierKind, value);
    }

    private string _primaryIdentifierValue = string.Empty;
    public string PrimaryIdentifierValue
    {
        get => _primaryIdentifierValue;
        set => SetProperty(ref _primaryIdentifierValue, value);
    }

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set => SetProperty(ref _isEditMode, value);
    }

    private Guid? _editingSecurityId;
    public Guid? EditingSecurityId
    {
        get => _editingSecurityId;
        set => SetProperty(ref _editingSecurityId, value);
    }

    private long? _editingVersion;
    public long? EditingVersion
    {
        get => _editingVersion;
        set => SetProperty(ref _editingVersion, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private ObservableCollection<string> _validationErrors = new();
    public ObservableCollection<string> ValidationErrors
    {
        get => _validationErrors;
        set => SetProperty(ref _validationErrors, value);
    }

    public bool HasErrors => ValidationErrors.Count > 0;

    public IReadOnlyList<string> AssetClasses => AssetClassesArray.ToList().AsReadOnly();
    public IReadOnlyList<string> IdentifierKinds => IdentifierKindsArray.ToList().AsReadOnly();

    // ── Commands ────────────────────────────────────────────────────────────
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    // ── Events ──────────────────────────────────────────────────────────────
    public event Action? CancelRequested;
    public event Action<SecurityDetailDto>? SaveCompleted;

    // ── Constructor ─────────────────────────────────────────────────────────
    public SecurityMasterEditViewModel(
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke());
    }

    // ── Initialization ──────────────────────────────────────────────────────
    public static SecurityMasterEditViewModel CreateNew(
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        return new SecurityMasterEditViewModel(loggingService, notificationService)
        {
            IsEditMode = false,
            Currency = "USD",
            AssetClass = "Equity",
            PrimaryIdentifierKind = "Ticker",
            DisplayName = string.Empty,
            Exchange = string.Empty,
            IssuerName = string.Empty,
            PrimaryIdentifierValue = string.Empty
        };
    }

    public void LoadForEdit(SecurityDetailDto dto)
    {
        IsEditMode = true;
        EditingSecurityId = dto.SecurityId;
        EditingVersion = dto.Version;
        DisplayName = dto.DisplayName;
        AssetClass = dto.AssetClass;
        Currency = dto.Currency;
        PrimaryIdentifierValue = dto.Identifiers
            .FirstOrDefault(i => i.IsPrimary)?.Value ?? string.Empty;
        PrimaryIdentifierKind = dto.Identifiers
            .FirstOrDefault(i => i.IsPrimary)?.Kind.ToString() ?? "Ticker";
        StatusText = string.Empty;
        ValidationErrors.Clear();
    }

    // ── Save logic ──────────────────────────────────────────────────────────
    private async Task SaveAsync(CancellationToken ct)
    {
        ValidationErrors.Clear();
        IsBusy = true;
        StatusText = "Saving…";

        try
        {
            SecurityDetailDto? result;

            if (IsEditMode && EditingSecurityId.HasValue && EditingVersion.HasValue)
            {
                result = await AmendSecurityAsync(ct);
            }
            else
            {
                result = await CreateSecurityAsync(ct);
            }

            if (result is not null)
            {
                StatusText = "Security saved successfully.";
                SaveCompleted?.Invoke(result);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Save cancelled.";
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master save failed", ex);
            StatusText = "Save failed. Check the validation errors.";
            _notificationService.ShowNotification("Security Master", "Save failed.", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<SecurityDetailDto?> CreateSecurityAsync(CancellationToken ct)
    {
        var identifiers = new List<SecurityIdentifierDto>
        {
            new(
                Kind: ConvertIdentifierKind(PrimaryIdentifierKind),
                Value: PrimaryIdentifierValue,
                IsPrimary: true,
                ValidFrom: DateTimeOffset.UtcNow)
        };

        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: AssetClass,
            CommonTerms: JsonDocument.Parse("{}").RootElement,
            AssetSpecificTerms: JsonDocument.Parse("{}").RootElement,
            Identifiers: identifiers,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "WPF-UI",
            UpdatedBy: "User",
            SourceRecordId: null,
            Reason: "Created via WPF UI");

        var response = await ApiClientService.Instance
            .PostWithResponseAsync<SecurityDetailDto>("/api/security-master/create", request, ct)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            HandleError(response);
            return null;
        }

        return response.Data;
    }

    private async Task<SecurityDetailDto?> AmendSecurityAsync(CancellationToken ct)
    {
        var request = new AmendSecurityTermsRequest(
            SecurityId: EditingSecurityId!.Value,
            ExpectedVersion: EditingVersion!.Value,
            CommonTerms: null,
            AssetSpecificTermsPatch: null,
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "WPF-UI",
            UpdatedBy: "User",
            SourceRecordId: null,
            Reason: "Amended via WPF UI");

        var response = await ApiClientService.Instance
            .PostWithResponseAsync<SecurityDetailDto>(
                $"/api/security-master/amend",
                request,
                ct)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            HandleError(response);
            return null;
        }

        return response.Data;
    }

    private void HandleError(ApiResponse<SecurityDetailDto> response)
    {
        if (response.StatusCode == 422)
        {
            // Try to parse validation errors
            try
            {
                var options = DesktopJsonOptions.Api;
                if (JsonSerializer.Deserialize<ValidationProblemDetails>(
                    response.ErrorMessage ?? "{}",
                    options) is { Errors: not null } details)
                {
                    foreach (var error in details.Errors.Values.SelectMany(v => v))
                    {
                        ValidationErrors.Add(error);
                    }
                    RaisePropertyChanged(nameof(HasErrors));
                    StatusText = $"{ValidationErrors.Count} validation error(s).";
                    return;
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(response.ErrorMessage))
        {
            ValidationErrors.Add(response.ErrorMessage);
        }
        else
        {
            ValidationErrors.Add($"HTTP {response.StatusCode}: Request failed.");
        }
        RaisePropertyChanged(nameof(HasErrors));
    }

    private static SecurityIdentifierKind ConvertIdentifierKind(string displayName) => displayName switch
    {
        "ISIN" => SecurityIdentifierKind.Isin,
        "CUSIP" => SecurityIdentifierKind.Cusip,
        "FIGI" => SecurityIdentifierKind.Figi,
        "SEDOL" => SecurityIdentifierKind.Sedol,
        "Ticker" => SecurityIdentifierKind.Ticker,
        _ => SecurityIdentifierKind.Ticker
    };
}

// ── Contracts for validation errors ──────────────────────────────────────

public sealed record ValidationProblemDetails(
    [System.Text.Json.Serialization.JsonPropertyName("errors")]
    Dictionary<string, string[]>? Errors);
