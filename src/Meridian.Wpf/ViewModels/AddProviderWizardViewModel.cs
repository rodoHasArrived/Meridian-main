using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Add Provider Wizard page.
/// Holds all display state for the provider-detail panel, credential guidance,
/// connection-test feedback, save feedback, and step-progress indicators.
/// The code-behind sets properties here instead of mutating UI elements directly.
/// </summary>
public sealed class AddProviderWizardViewModel : BindableBase
{
    private const string ProviderFilterAll = "all";
    private const string ProviderFilterFree = "free";
    private const string ProviderFilterStreaming = "streaming";
    private const string ProviderFilterHistorical = "historical";

    // ---- Static brush constants (avoids FindResource in ViewModel) ----
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(88, 166, 255));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(210, 153, 34));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(248, 81, 73));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(139, 148, 158));

    // ---- Backing fields ----

    // Provider-detail panel
    private string _selectedProviderName = "None selected";
    private string _selectedProviderDescription = string.Empty;
    private Visibility _detailsVisibility = Visibility.Collapsed;
    private string _detailStreamingText = string.Empty;
    private string _detailHistoricalText = string.Empty;
    private string _detailSearchText = string.Empty;
    private string _detailRateLimitText = string.Empty;
    private string _detailCredentialsText = string.Empty;

    // Credentials step
    private string _credentialsInfoText = string.Empty;
    private Visibility _noCredentialsVisibility = Visibility.Collapsed;

    // Connection test
    private Brush _connectionTestDotBrush = MutedBrush;
    private string _connectionTestStatusText = "Not tested yet";

    // Save status
    private string _saveStatusText = string.Empty;
    private Brush _saveStatusBrush = MutedBrush;

    // Operator-facing relationship summary
    private string _summaryConnectionTypeText = string.Empty;
    private string _summaryOperatingModeText = string.Empty;
    private string _summaryScopeText = string.Empty;
    private string _summaryCapabilitiesText = string.Empty;
    private string _summaryCredentialText = string.Empty;
    private string _summaryPresetText = string.Empty;
    private string _summaryCertificationText = string.Empty;
    private string _certificationStatusText = string.Empty;

    // Step progress
    private int _currentStep = 1;

    // Provider catalog filter
    private IReadOnlyList<ProviderCatalogEntry> _providerCatalogEntries = Array.Empty<ProviderCatalogEntry>();
    private IReadOnlyList<ProviderCredentialStatus> _providerCredentialStatuses = Array.Empty<ProviderCredentialStatus>();
    private string _activeProviderFilter = ProviderFilterAll;

    public AddProviderWizardViewModel()
    {
        SelectProviderFilterCommand = new RelayCommand<string>(SelectProviderFilter);
    }

    // ---- Provider catalog display ----

    public ObservableCollection<ProviderCatalogViewModel> ProviderCatalog { get; } = new();

    public IRelayCommand<string> SelectProviderFilterCommand { get; }

    public string ActiveProviderFilter
    {
        get => _activeProviderFilter;
        private set
        {
            if (SetProperty(ref _activeProviderFilter, value))
            {
                RaiseProviderFilterChanged();
            }
        }
    }

    public bool IsAllProviderFilterActive => ActiveProviderFilter == ProviderFilterAll;

    public bool IsFreeProviderFilterActive => ActiveProviderFilter == ProviderFilterFree;

    public bool IsStreamingProviderFilterActive => ActiveProviderFilter == ProviderFilterStreaming;

    public bool IsHistoricalProviderFilterActive => ActiveProviderFilter == ProviderFilterHistorical;

    public Visibility ProviderCatalogVisibility => ProviderCatalog.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProviderCatalogEmptyVisibility => ProviderCatalog.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProviderCatalogRecoveryVisibility =>
        ProviderCatalog.Count == 0 && ActiveProviderFilter != ProviderFilterAll
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string ProviderCatalogScopeText
    {
        get
        {
            var total = _providerCatalogEntries.Count;
            var visible = ProviderCatalog.Count;
            var label = GetProviderFilterLabel(ActiveProviderFilter);

            if (total == 0)
                return "Provider catalog unavailable.";

            if (ActiveProviderFilter == ProviderFilterAll)
                return visible == 1 ? "1 provider available." : $"{visible} providers available.";

            return visible == 1
                ? $"1 provider matches {label}."
                : $"{visible} providers match {label}.";
        }
    }

    public string ProviderCatalogEmptyTitle =>
        _providerCatalogEntries.Count == 0
            ? "No provider catalog loaded"
            : $"No {GetProviderFilterLabel(ActiveProviderFilter)} providers";

    public string ProviderCatalogEmptyDetail =>
        _providerCatalogEntries.Count == 0
            ? "Open Settings again after the provider registry is available."
            : "Switch to All providers to continue setup from the full catalog.";

    // ---- Selected-provider display properties ----

    public string SelectedProviderName
    {
        get => _selectedProviderName;
        set => SetProperty(ref _selectedProviderName, value);
    }

    public string SelectedProviderDescription
    {
        get => _selectedProviderDescription;
        set => SetProperty(ref _selectedProviderDescription, value);
    }

    public Visibility DetailsVisibility
    {
        get => _detailsVisibility;
        set => SetProperty(ref _detailsVisibility, value);
    }

    public string DetailStreamingText
    {
        get => _detailStreamingText;
        set => SetProperty(ref _detailStreamingText, value);
    }

    public string DetailHistoricalText
    {
        get => _detailHistoricalText;
        set => SetProperty(ref _detailHistoricalText, value);
    }

    public string DetailSearchText
    {
        get => _detailSearchText;
        set => SetProperty(ref _detailSearchText, value);
    }

    public string DetailRateLimitText
    {
        get => _detailRateLimitText;
        set => SetProperty(ref _detailRateLimitText, value);
    }

    public string DetailCredentialsText
    {
        get => _detailCredentialsText;
        set => SetProperty(ref _detailCredentialsText, value);
    }

    // ---- Credentials display ----

    public string CredentialsInfoText
    {
        get => _credentialsInfoText;
        set => SetProperty(ref _credentialsInfoText, value);
    }

    public Visibility NoCredentialsVisibility
    {
        get => _noCredentialsVisibility;
        set => SetProperty(ref _noCredentialsVisibility, value);
    }

    // ---- Connection test ----

    public Brush ConnectionTestDotBrush
    {
        get => _connectionTestDotBrush;
        set => SetProperty(ref _connectionTestDotBrush, value);
    }

    public string ConnectionTestStatusText
    {
        get => _connectionTestStatusText;
        set => SetProperty(ref _connectionTestStatusText, value);
    }

    // ---- Save status ----

    public string SaveStatusText
    {
        get => _saveStatusText;
        set => SetProperty(ref _saveStatusText, value);
    }

    public Brush SaveStatusBrush
    {
        get => _saveStatusBrush;
        set => SetProperty(ref _saveStatusBrush, value);
    }

    // ---- Relationship summary ----

    public string SummaryConnectionTypeText
    {
        get => _summaryConnectionTypeText;
        set => SetProperty(ref _summaryConnectionTypeText, value);
    }

    public string SummaryOperatingModeText
    {
        get => _summaryOperatingModeText;
        set => SetProperty(ref _summaryOperatingModeText, value);
    }

    public string SummaryScopeText
    {
        get => _summaryScopeText;
        set => SetProperty(ref _summaryScopeText, value);
    }

    public string SummaryCapabilitiesText
    {
        get => _summaryCapabilitiesText;
        set => SetProperty(ref _summaryCapabilitiesText, value);
    }

    public string SummaryCredentialText
    {
        get => _summaryCredentialText;
        set => SetProperty(ref _summaryCredentialText, value);
    }

    public string SummaryPresetText
    {
        get => _summaryPresetText;
        set => SetProperty(ref _summaryPresetText, value);
    }

    public string SummaryCertificationText
    {
        get => _summaryCertificationText;
        set => SetProperty(ref _summaryCertificationText, value);
    }

    public string CertificationStatusText
    {
        get => _certificationStatusText;
        set => SetProperty(ref _certificationStatusText, value);
    }

    // ---- Step-progress (computed from CurrentStep) ----

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                RaisePropertyChanged(nameof(Step1Fill));
                RaisePropertyChanged(nameof(Step2Fill));
                RaisePropertyChanged(nameof(Step3Fill));
                RaisePropertyChanged(nameof(Step4Fill));
            }
        }
    }

    /// <summary>Step 1 dot fill — green once step 1 is reached.</summary>
    public Brush Step1Fill => _currentStep >= 1 ? SuccessBrush : MutedBrush;

    /// <summary>Step 2 dot fill — blue while active, green when passed.</summary>
    public Brush Step2Fill => _currentStep > 2 ? SuccessBrush : (_currentStep == 2 ? InfoBrush : MutedBrush);

    /// <summary>Step 3 dot fill — blue while active, green when passed.</summary>
    public Brush Step3Fill => _currentStep > 3 ? SuccessBrush : (_currentStep == 3 ? InfoBrush : MutedBrush);

    /// <summary>Step 4 dot fill — green when reached.</summary>
    public Brush Step4Fill => _currentStep >= 4 ? SuccessBrush : MutedBrush;

    // ---- Helper methods called by code-behind ----

    /// <summary>
    /// Loads provider catalog data and projects the active filter into display card view models.
    /// </summary>
    public void LoadProviderCatalog(
        IEnumerable<ProviderCatalogEntry> providers,
        IEnumerable<ProviderCredentialStatus> credentialStatuses)
    {
        _providerCatalogEntries = providers.ToArray();
        _providerCredentialStatuses = credentialStatuses.ToArray();
        RefreshProviderCatalog();
    }

    /// <summary>Gets the backing provider entry for a selected catalog card.</summary>
    public ProviderCatalogEntry? FindProvider(string providerId)
    {
        return _providerCatalogEntries.FirstOrDefault(provider =>
            string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Populates the right-panel detail properties from the selected provider entry.</summary>
    public void ApplySelectedProvider(ProviderCatalogEntry provider)
    {
        SelectedProviderName = provider.DisplayName;
        SelectedProviderDescription = provider.Description;
        DetailsVisibility = Visibility.Visible;
        DetailStreamingText = provider.SupportsStreaming ? "Yes" : "No";
        DetailHistoricalText = provider.SupportsHistorical ? "Yes" : "No";
        DetailSearchText = provider.SupportsSymbolSearch ? "Yes" : "No";
        DetailRateLimitText = provider.RateLimitPerMinute > 0
            ? $"{provider.RateLimitPerMinute}/min"
            : "None";
        DetailCredentialsText = provider.CredentialFields.Length > 0
            ? $"{provider.CredentialFields.Length} required"
            : "None";
    }

    /// <summary>
    /// Populates the operator-facing relationship summary shown in the wizard side rail.
    /// </summary>
    public void ApplyRelationshipSummary(
        string providerName,
        string providerDescription,
        string connectionType,
        string operatingMode,
        string scopeSummary,
        string capabilitiesSummary,
        string credentialSummary,
        string presetSummary,
        string certificationSummary)
    {
        SelectedProviderName = providerName;
        SelectedProviderDescription = providerDescription;
        DetailsVisibility = Visibility.Visible;
        SummaryConnectionTypeText = connectionType;
        SummaryOperatingModeText = operatingMode;
        SummaryScopeText = scopeSummary;
        SummaryCapabilitiesText = capabilitiesSummary;
        SummaryCredentialText = credentialSummary;
        SummaryPresetText = presetSummary;
        SummaryCertificationText = certificationSummary;
    }

    /// <summary>Sets credential-step display state based on whether the provider needs credentials.</summary>
    public void ApplyCredentialsInfo(string providerName, bool hasFields)
    {
        if (hasFields)
        {
            CredentialsInfoText = $"Enter your {providerName} credentials. " +
                                       "These will be stored as user environment variables.";
            NoCredentialsVisibility = Visibility.Collapsed;
        }
        else
        {
            CredentialsInfoText = $"{providerName} does not require API credentials.";
            NoCredentialsVisibility = Visibility.Visible;
        }
    }

    /// <summary>Transitions the connection-test dot to the "testing" (warning) state.</summary>
    public void SetConnectionTestTesting(string providerName)
    {
        ConnectionTestDotBrush = WarningBrush;
        ConnectionTestStatusText = $"Testing {providerName} connectivity...";
    }

    /// <summary>Marks the connection test as successful.</summary>
    public void SetConnectionTestSuccess()
    {
        ConnectionTestDotBrush = SuccessBrush;
        ConnectionTestStatusText = "Credentials configured. Provider ready.";
    }

    /// <summary>Marks the connection test as failed due to missing credentials.</summary>
    public void SetConnectionTestError()
    {
        ConnectionTestDotBrush = ErrorBrush;
        ConnectionTestStatusText = "Missing credentials. Please fill in all required fields above.";
    }

    /// <summary>Sets a success message on the save-status line.</summary>
    public void SetSaveSuccess(string providerName)
    {
        SaveStatusText = $"{providerName} configured successfully.";
        SaveStatusBrush = SuccessBrush;
    }

    /// <summary>Sets an error message on the save-status line.</summary>
    public void SetSaveError(string message)
    {
        SaveStatusText = $"Failed to save: {message}";
        SaveStatusBrush = ErrorBrush;
    }

    /// <summary>
    /// Updates the certification callout and the compact summary badge copy.
    /// </summary>
    public void SetCertificationStatus(string message, bool success)
    {
        CertificationStatusText = message;
        SummaryCertificationText = success ? "Passed" : "Needs attention";
    }

    private void SelectProviderFilter(string? filter)
    {
        ActiveProviderFilter = NormalizeProviderFilter(filter);
        RefreshProviderCatalog();
    }

    private void RefreshProviderCatalog()
    {
        var statusesByProviderId = _providerCredentialStatuses
            .GroupBy(status => status.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var filtered = _providerCatalogEntries
            .Where(ProviderMatchesActiveFilter)
            .Select(provider =>
            {
                statusesByProviderId.TryGetValue(provider.Id, out var status);
                return new ProviderCatalogViewModel(provider, status);
            })
            .ToArray();

        ProviderCatalog.Clear();

        foreach (var provider in filtered)
        {
            ProviderCatalog.Add(provider);
        }

        RaiseProviderCatalogChanged();
    }

    private bool ProviderMatchesActiveFilter(ProviderCatalogEntry provider)
    {
        return ActiveProviderFilter switch
        {
            ProviderFilterFree => provider.Tier is ProviderTier.Free or ProviderTier.FreeWithAccount,
            ProviderFilterStreaming => provider.SupportsStreaming,
            ProviderFilterHistorical => provider.SupportsHistorical,
            _ => true,
        };
    }

    private static string NormalizeProviderFilter(string? filter)
    {
        return filter?.Trim().ToLowerInvariant() switch
        {
            ProviderFilterFree => ProviderFilterFree,
            ProviderFilterStreaming => ProviderFilterStreaming,
            ProviderFilterHistorical => ProviderFilterHistorical,
            _ => ProviderFilterAll,
        };
    }

    private static string GetProviderFilterLabel(string filter)
    {
        return filter switch
        {
            ProviderFilterFree => "free",
            ProviderFilterStreaming => "streaming",
            ProviderFilterHistorical => "historical",
            _ => "all",
        };
    }

    private void RaiseProviderFilterChanged()
    {
        RaisePropertyChanged(nameof(IsAllProviderFilterActive));
        RaisePropertyChanged(nameof(IsFreeProviderFilterActive));
        RaisePropertyChanged(nameof(IsStreamingProviderFilterActive));
        RaisePropertyChanged(nameof(IsHistoricalProviderFilterActive));
        RaiseProviderCatalogChanged();
    }

    private void RaiseProviderCatalogChanged()
    {
        RaisePropertyChanged(nameof(ProviderCatalogVisibility));
        RaisePropertyChanged(nameof(ProviderCatalogEmptyVisibility));
        RaisePropertyChanged(nameof(ProviderCatalogRecoveryVisibility));
        RaisePropertyChanged(nameof(ProviderCatalogScopeText));
        RaisePropertyChanged(nameof(ProviderCatalogEmptyTitle));
        RaisePropertyChanged(nameof(ProviderCatalogEmptyDetail));
    }
}

/// <summary>
/// View model for provider catalog cards in the wizard.
/// </summary>
public sealed class ProviderCatalogViewModel
{
    private static readonly Brush TierFreeBrush = CreateBrush(Color.FromArgb(40, 63, 185, 80));
    private static readonly Brush TierLimitedBrush = CreateBrush(Color.FromArgb(40, 210, 153, 34));
    private static readonly Brush TierPremiumBrush = CreateBrush(Color.FromArgb(40, 88, 166, 255));
    private static readonly Brush TierFallbackBrush = CreateBrush(Color.FromArgb(40, 128, 128, 128));
    private static readonly Brush CredentialConfiguredBrush = CreateBrush(Color.FromRgb(63, 185, 80));
    private static readonly Brush CredentialPartialBrush = CreateBrush(Color.FromRgb(210, 153, 34));
    private static readonly Brush CredentialMissingBrush = CreateBrush(Color.FromRgb(248, 81, 73));
    private static readonly Brush CredentialUnknownBrush = CreateBrush(Color.FromRgb(139, 148, 158));

    public ProviderCatalogViewModel(ProviderCatalogEntry entry, ProviderCredentialStatus? credStatus)
    {
        Id = entry.Id;
        DisplayName = entry.DisplayName;
        Description = entry.Description;
        SupportsStreaming = entry.SupportsStreaming;
        SupportsHistorical = entry.SupportsHistorical;

        TierLabel = entry.Tier switch
        {
            ProviderTier.Free => "FREE",
            ProviderTier.FreeWithAccount => "FREE*",
            ProviderTier.LimitedFree => "LIMITED",
            ProviderTier.Premium => "PREMIUM",
            _ => "FREE",
        };
        TierBrush = entry.Tier switch
        {
            ProviderTier.Free or ProviderTier.FreeWithAccount => TierFreeBrush,
            ProviderTier.LimitedFree => TierLimitedBrush,
            ProviderTier.Premium => TierPremiumBrush,
            _ => TierFallbackBrush,
        };

        CredentialStatusBrush = credStatus?.State switch
        {
            CredentialState.Configured => CredentialConfiguredBrush,
            CredentialState.Partial => CredentialPartialBrush,
            CredentialState.Missing => CredentialMissingBrush,
            CredentialState.NotRequired => CredentialConfiguredBrush,
            _ => CredentialUnknownBrush,
        };

        StreamingVisibility = entry.SupportsStreaming ? Visibility.Visible : Visibility.Collapsed;
        HistoricalVisibility = entry.SupportsHistorical ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool SupportsStreaming { get; }
    public bool SupportsHistorical { get; }
    public string TierLabel { get; }
    public Brush TierBrush { get; }
    public Brush CredentialStatusBrush { get; }
    public Visibility StreamingVisibility { get; }
    public Visibility HistoricalVisibility { get; }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
