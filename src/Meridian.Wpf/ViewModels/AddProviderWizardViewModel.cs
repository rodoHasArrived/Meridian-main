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
}
