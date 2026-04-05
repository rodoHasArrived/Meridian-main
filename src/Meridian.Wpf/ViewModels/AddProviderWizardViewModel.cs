using System.Windows;
using System.Windows.Media;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the relationship-first provider onboarding wizard.
/// </summary>
public sealed class AddProviderWizardViewModel : BindableBase
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(88, 166, 255));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(210, 153, 34));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(248, 81, 73));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(139, 148, 158));

    private string _selectedProviderName = "Select a relationship";
    private string _selectedProviderDescription = "Choose a connection type, then pick a provider family from the catalog or enter one manually.";
    private Visibility _detailsVisibility = Visibility.Visible;
    private string _summaryConnectionTypeText = "Not set";
    private string _summaryOperatingModeText = "ReadOnly";
    private string _summaryScopeText = "Global";
    private string _summaryCapabilitiesText = "No capabilities selected";
    private string _summaryCredentialText = "No credential reference yet";
    private string _summaryPresetText = "No preset applied";
    private string _summaryCertificationText = "Not certified";
    private string _credentialsInfoText = "Select a provider family to load credential guidance.";
    private Visibility _noCredentialsVisibility = Visibility.Collapsed;
    private Brush _connectionTestDotBrush = MutedBrush;
    private string _connectionTestStatusText = "No validation run yet.";
    private string _saveStatusText = string.Empty;
    private Brush _saveStatusBrush = MutedBrush;
    private string _routePreviewText = "Save a connection to preview the effective route.";
    private Brush _routePreviewBrush = MutedBrush;
    private string _certificationStatusText = "Certification not started.";
    private Brush _certificationStatusBrush = MutedBrush;
    private int _currentStep = 1;

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

    public string RoutePreviewText
    {
        get => _routePreviewText;
        set => SetProperty(ref _routePreviewText, value);
    }

    public Brush RoutePreviewBrush
    {
        get => _routePreviewBrush;
        set => SetProperty(ref _routePreviewBrush, value);
    }

    public string CertificationStatusText
    {
        get => _certificationStatusText;
        set => SetProperty(ref _certificationStatusText, value);
    }

    public Brush CertificationStatusBrush
    {
        get => _certificationStatusBrush;
        set => SetProperty(ref _certificationStatusBrush, value);
    }

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

    public Brush Step1Fill => _currentStep > 1 ? SuccessBrush : (_currentStep == 1 ? InfoBrush : MutedBrush);

    public Brush Step2Fill => _currentStep > 2 ? SuccessBrush : (_currentStep == 2 ? InfoBrush : MutedBrush);

    public Brush Step3Fill => _currentStep > 3 ? SuccessBrush : (_currentStep == 3 ? InfoBrush : MutedBrush);

    public Brush Step4Fill => _currentStep >= 4 ? SuccessBrush : MutedBrush;

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
        SelectedProviderName = string.IsNullOrWhiteSpace(providerName) ? "Select a relationship" : providerName;
        SelectedProviderDescription = string.IsNullOrWhiteSpace(providerDescription)
            ? "Choose a provider family from the catalog or enter one manually."
            : providerDescription;
        SummaryConnectionTypeText = connectionType;
        SummaryOperatingModeText = operatingMode;
        SummaryScopeText = scopeSummary;
        SummaryCapabilitiesText = capabilitiesSummary;
        SummaryCredentialText = credentialSummary;
        SummaryPresetText = presetSummary;
        SummaryCertificationText = certificationSummary;
        DetailsVisibility = Visibility.Visible;
    }

    public void ApplyCredentialsInfo(string providerName, bool hasFields)
    {
        if (hasFields)
        {
            CredentialsInfoText = $"Enter your {providerName} credential values and a reusable credential reference.";
            NoCredentialsVisibility = Visibility.Collapsed;
        }
        else
        {
            CredentialsInfoText = $"{providerName} has no catalog-defined environment fields. Use the credential reference for external secret resolution.";
            NoCredentialsVisibility = Visibility.Visible;
        }
    }

    public void SetConnectionTestTesting(string connectionName)
    {
        ConnectionTestDotBrush = WarningBrush;
        ConnectionTestStatusText = $"Validating configuration for {connectionName}...";
    }

    public void SetConnectionTestSuccess(string message)
    {
        ConnectionTestDotBrush = SuccessBrush;
        ConnectionTestStatusText = message;
    }

    public void SetConnectionTestWarning(string message)
    {
        ConnectionTestDotBrush = WarningBrush;
        ConnectionTestStatusText = message;
    }

    public void SetConnectionTestError(string message)
    {
        ConnectionTestDotBrush = ErrorBrush;
        ConnectionTestStatusText = message;
    }

    public void SetSaveSuccess(string message)
    {
        SaveStatusText = message;
        SaveStatusBrush = SuccessBrush;
    }

    public void SetSaveError(string message)
    {
        SaveStatusText = message;
        SaveStatusBrush = ErrorBrush;
    }

    public void SetRoutePreview(string message, bool success)
    {
        RoutePreviewText = message;
        RoutePreviewBrush = success ? SuccessBrush : WarningBrush;
    }

    public void SetCertificationStatus(string message, bool success)
    {
        CertificationStatusText = message;
        CertificationStatusBrush = success ? SuccessBrush : WarningBrush;
        SummaryCertificationText = success ? "Passed" : "Needs attention";
    }
}
