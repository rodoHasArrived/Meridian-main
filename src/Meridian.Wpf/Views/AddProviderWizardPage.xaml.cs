using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Api;
using Meridian.ProviderSdk;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;
using CredentialFieldInfo = Meridian.Contracts.Api.CredentialFieldInfo;
using ProviderCatalogEntry = Meridian.Ui.Services.Services.ProviderCatalogEntry;
using ProviderCatalogCredentialStatus = Meridian.Ui.Services.Services.ProviderCredentialStatus;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Relationship-first onboarding workflow for provider connections and bindings.
/// </summary>
public partial class AddProviderWizardPage : Page
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly SettingsConfigurationService _settingsConfigService;
    private readonly ProviderManagementService _providerManagementService;
    private readonly AddProviderWizardViewModel _viewModel;

    private IReadOnlyList<ProviderCatalogEntry> _allProviders = Array.Empty<ProviderCatalogEntry>();
    private ProviderCatalogEntry? _selectedProvider;
    private ProviderConnectionDto? _savedConnection;
    private ProviderPresetDto? _selectedPreset;

    public AddProviderWizardPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _settingsConfigService = SettingsConfigurationService.Instance;
        _providerManagementService = ProviderManagementService.Instance;
        _viewModel = new AddProviderWizardViewModel();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _allProviders = _settingsConfigService.GetProviderCatalog();
        ProviderCatalogList.ItemsSource = BuildCatalogViewModels(_allProviders, _settingsConfigService.GetProviderCredentialStatuses());

        ConnectionTypeCombo.ItemsSource = Enum.GetValues<ProviderConnectionType>();
        ConnectionTypeCombo.SelectedItem = ProviderConnectionType.DataVendor;

        OperatingModeCombo.ItemsSource = Enum.GetValues<ProviderConnectionMode>();
        OperatingModeCombo.SelectedItem = ProviderConnectionMode.ReadOnly;

        SafetyModeCombo.ItemsSource = Enum.GetValues<ProviderSafetyMode>();
        SafetyModeCombo.SelectedItem = ProviderSafetyMode.HealthAwareFailover;

        var presets = await _providerManagementService.GetProviderPresetsAsync();
        if (presets.Success)
        {
            PresetCombo.ItemsSource = presets.Presets;
            _selectedPreset = presets.Presets.FirstOrDefault(p => p.IsEnabled) ?? presets.Presets.FirstOrDefault();
            PresetCombo.SelectedItem = _selectedPreset;
        }

        BuildCredentialFields();
        BuildCapabilityFields();
        UpdateSummary();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Settings");

    private void ConnectionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BuildCapabilityFields();
        UpdateSummary();
        _viewModel.CurrentStep = 1;
    }

    private void OperatingMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSummary();
        _viewModel.CurrentStep = Math.Max(_viewModel.CurrentStep, 2);
    }

    private void SafetyMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSummary();
        _viewModel.CurrentStep = Math.Max(_viewModel.CurrentStep, 3);
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPreset = PresetCombo.SelectedItem as ProviderPresetDto;
        UpdateSummary();
    }

    private async void PresetApply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset is null)
        {
            _viewModel.SetSaveError("Choose a preset before applying it.");
            return;
        }

        var result = await _providerManagementService.ApplyProviderPresetAsync(_selectedPreset.PresetId);
        if (!result.Success || result.Preset is null)
        {
            _viewModel.SetSaveError(result.Error ?? "Failed to apply preset.");
            return;
        }

        _selectedPreset = result.Preset;
        _viewModel.SetSaveSuccess($"Applied preset '{result.Preset.Name}'.");
        UpdateSummary();
    }

    private void ProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerId })
            return;

        _selectedProvider = _allProviders.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (_selectedProvider is null)
            return;

        ProviderFamilyIdBox.Text = _selectedProvider.Id;
        if (string.IsNullOrWhiteSpace(ConnectionNameBox.Text))
            ConnectionNameBox.Text = $"{_selectedProvider.DisplayName} {GetSelectedConnectionType()}";

        BuildCredentialFields();
        UpdateSummary();
        _viewModel.CurrentStep = 2;
    }

    private void TextInput_Changed(object sender, TextChangedEventArgs e)
        => UpdateSummary();

    private void StateOption_Changed(object sender, RoutedEventArgs e)
        => UpdateSummary();

    private void CapabilityOption_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSummary();
        _viewModel.CurrentStep = Math.Max(_viewModel.CurrentStep, 3);
    }

    private void BuildCredentialFields()
    {
        CredentialFieldsPanel.Children.Clear();

        var providerName = _selectedProvider?.DisplayName ?? ProviderFamilyIdBox.Text.Trim();
        if (_selectedProvider is null)
        {
            _viewModel.ApplyCredentialsInfo(string.IsNullOrWhiteSpace(providerName) ? "manual family" : providerName, false);
            return;
        }

        _viewModel.ApplyCredentialsInfo(_selectedProvider.DisplayName, _selectedProvider.CredentialFields.Length > 0);
        foreach (var field in _selectedProvider.CredentialFields)
        {
            var label = new TextBlock
            {
                Text = field.DisplayName,
                Style = (Style)FindResource("FormLabelStyle"),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var input = new TextBox
            {
                Style = (Style)FindResource("FormTextBoxStyle"),
                Text = GetConfiguredEnvironmentValue(field) ?? string.Empty,
                Tag = field
            };
            input.TextChanged += TextInput_Changed;

            CredentialFieldsPanel.Children.Add(label);
            CredentialFieldsPanel.Children.Add(input);
        }
    }

    private void BuildCapabilityFields()
    {
        CapabilityOptionsPanel.Children.Clear();
        foreach (var capability in GetRecommendedCapabilities(GetSelectedConnectionType()))
        {
            var checkbox = new CheckBox
            {
                Content = ToDisplayLabel(capability.ToString()),
                Tag = capability.ToString(),
                IsChecked = true,
                Margin = new Thickness(0, 0, 14, 8),
                Foreground = (Brush)FindResource("ConsoleTextPrimaryBrush")
            };
            checkbox.Checked += CapabilityOption_Changed;
            checkbox.Unchecked += CapabilityOption_Changed;
            CapabilityOptionsPanel.Children.Add(checkbox);
        }
    }

    private void TestProviderConnection_Click(object sender, RoutedEventArgs e)
    {
        var connectionLabel = string.IsNullOrWhiteSpace(ConnectionNameBox.Text) ? "draft relationship" : ConnectionNameBox.Text.Trim();
        _viewModel.SetConnectionTestTesting(connectionLabel);

        if (!ValidateDraft(out var error))
        {
            _viewModel.SetConnectionTestError(error);
            return;
        }

        _viewModel.SetConnectionTestSuccess("Draft looks complete. Save the relationship to persist bindings and enable certification.");
        _viewModel.CurrentStep = 4;
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateDraft(out var error))
        {
            _viewModel.SetSaveError(error);
            return;
        }

        SaveCredentials();

        var connectionResult = await _providerManagementService.UpsertProviderConnectionAsync(BuildConnectionRequest());
        if (!connectionResult.Success || connectionResult.Connection is null)
        {
            _viewModel.SetSaveError(connectionResult.Error ?? "Failed to save provider connection.");
            return;
        }

        _savedConnection = connectionResult.Connection;
        var bindingErrors = new List<string>();
        foreach (var capability in GetSelectedCapabilities())
        {
            var bindingResult = await _providerManagementService.UpsertProviderBindingAsync(
                new UpdateProviderBindingRequest(
                    BindingId: null,
                    Capability: capability,
                    ConnectionId: _savedConnection.ConnectionId,
                    Target: BuildScope(),
                    Priority: 100,
                    Enabled: EnabledCheck.IsChecked == true,
                    FailoverConnectionIds: GetFailoverConnectionIds(),
                    SafetyModeOverride: SafetyModeCombo.SelectedItem?.ToString(),
                    Notes: DescriptionBox.Text.Trim()));

            if (!bindingResult.Success)
                bindingErrors.Add($"{capability}: {bindingResult.Error}");
        }

        if (bindingErrors.Count > 0)
        {
            _viewModel.SetSaveError(string.Join(Environment.NewLine, bindingErrors));
            return;
        }

        _viewModel.SetSaveSuccess($"Saved '{_savedConnection.DisplayName}' with {GetSelectedCapabilities().Count} capability bindings.");
        _viewModel.CurrentStep = 4;
        UpdateSummary();
        await PreviewRouteInternalAsync();
    }

    private async void PreviewRoute_Click(object sender, RoutedEventArgs e)
        => await PreviewRouteInternalAsync();

    private async Task PreviewRouteInternalAsync()
    {
        if (_savedConnection is null)
        {
            _viewModel.SetRoutePreview("Save the relationship before previewing its effective route.", false);
            return;
        }

        var firstCapability = GetSelectedCapabilities().FirstOrDefault();
        if (firstCapability is null)
        {
            _viewModel.SetRoutePreview("Select at least one capability to preview routing.", false);
            return;
        }

        var preview = await _providerManagementService.PreviewRouteAsync(new RoutePreviewRequest(
            Capability: firstCapability,
            Workspace: WorkspaceBox.Text.TrimOrNull(),
            FundProfileId: FundProfileIdBox.Text.TrimOrNull(),
            EntityId: ParseGuid(EntityIdBox.Text),
            SleeveId: ParseGuid(SleeveIdBox.Text),
            VehicleId: ParseGuid(VehicleIdBox.Text),
            AccountId: ParseGuid(AccountIdBox.Text),
            RequireProductionReady: GetSelectedConnectionMode() == ProviderConnectionMode.Live));

        if (!preview.Success || preview.Preview is null)
        {
            _viewModel.SetRoutePreview(preview.Error ?? "Route preview failed.", false);
            return;
        }

        var route = preview.Preview;
        var selected = string.IsNullOrWhiteSpace(route.SelectedConnectionId)
            ? route.PolicyGate ?? "No route selected."
            : $"{firstCapability} -> {route.SelectedConnectionId} ({route.SafetyMode})";
        _viewModel.SetRoutePreview(selected, route.IsRoutable);
    }

    private async void RunCertification_Click(object sender, RoutedEventArgs e)
    {
        if (_savedConnection is null)
        {
            _viewModel.SetCertificationStatus("Save the relationship before running certification.", false);
            return;
        }

        var result = await _providerManagementService.RunProviderCertificationAsync(_savedConnection.ConnectionId);
        if (!result.Success || result.Certification is null)
        {
            _viewModel.SetCertificationStatus(result.Error ?? "Certification failed.", false);
            return;
        }

        _savedConnection = _savedConnection with { ProductionReady = result.Certification.ProductionReady };
        var status = $"{result.Certification.Status} • expires {result.Certification.ExpiresAt:yyyy-MM-dd}";
        _viewModel.SetCertificationStatus(status, result.Certification.ProductionReady);
        _viewModel.SetConnectionTestSuccess("Certification completed. The connection can now participate in production-ready routing when policy allows.");
        UpdateSummary();
    }

    private bool ValidateDraft(out string error)
    {
        if (string.IsNullOrWhiteSpace(ConnectionNameBox.Text))
        {
            error = "Enter a connection label.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProviderFamilyIdBox.Text))
        {
            error = "Choose a provider family from the catalog or enter one manually.";
            return false;
        }

        if (GetSelectedCapabilities().Count == 0)
        {
            error = "Select at least one capability binding.";
            return false;
        }

        if (_selectedProvider is not null)
        {
            var missingField = CredentialFieldsPanel.Children
                .OfType<TextBox>()
                .FirstOrDefault(box => box.Tag is CredentialFieldInfo field && field.Required && string.IsNullOrWhiteSpace(box.Text));

            if (missingField?.Tag is CredentialFieldInfo missingInfo)
            {
                error = $"Provide a value for '{missingInfo.DisplayName}'.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void SaveCredentials()
    {
        foreach (var input in CredentialFieldsPanel.Children.OfType<TextBox>())
        {
            if (input.Tag is not CredentialFieldInfo field)
                continue;

            var value = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var envVar in field.AllEnvironmentVariables)
                Environment.SetEnvironmentVariable(envVar, value, EnvironmentVariableTarget.User);
        }
    }

    private CreateProviderConnectionRequest BuildConnectionRequest()
        => new(
            ConnectionId: _savedConnection?.ConnectionId,
            ProviderFamilyId: ProviderFamilyIdBox.Text.Trim(),
            DisplayName: ConnectionNameBox.Text.Trim(),
            ConnectionType: GetSelectedConnectionType().ToString(),
            ConnectionMode: GetSelectedConnectionMode().ToString(),
            Enabled: EnabledCheck.IsChecked == true,
            CredentialReference: CredentialReferenceBox.Text.TrimOrNull(),
            InstitutionId: InstitutionIdBox.Text.TrimOrNull(),
            ExternalAccountId: ExternalAccountIdBox.Text.TrimOrNull(),
            Scope: BuildScope(),
            Tags: [GetSelectedConnectionType().ToString(), "relationship-wizard"],
            Description: DescriptionBox.Text.TrimOrNull(),
            ProductionReady: ProductionReadyCheck.IsChecked == true);

    private ProviderRouteScopeDto? BuildScope()
    {
        var scope = new ProviderRouteScopeDto
        {
            Workspace = WorkspaceBox.Text.TrimOrNull(),
            FundProfileId = FundProfileIdBox.Text.TrimOrNull(),
            EntityId = ParseGuid(EntityIdBox.Text),
            SleeveId = ParseGuid(SleeveIdBox.Text),
            VehicleId = ParseGuid(VehicleIdBox.Text),
            AccountId = ParseGuid(AccountIdBox.Text)
        };

        return string.IsNullOrWhiteSpace(scope.Workspace) &&
               string.IsNullOrWhiteSpace(scope.FundProfileId) &&
               scope.EntityId is null &&
               scope.SleeveId is null &&
               scope.VehicleId is null &&
               scope.AccountId is null
            ? null
            : scope;
    }

    private List<string> GetSelectedCapabilities()
        => CapabilityOptionsPanel.Children
            .OfType<CheckBox>()
            .Where(box => box.IsChecked == true && box.Tag is string)
            .Select(box => (string)box.Tag)
            .ToList();

    private string[] GetFailoverConnectionIds()
        => FailoverConnectionIdsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private ProviderConnectionType GetSelectedConnectionType()
        => ConnectionTypeCombo.SelectedItem is ProviderConnectionType type ? type : ProviderConnectionType.DataVendor;

    private ProviderConnectionMode GetSelectedConnectionMode()
        => OperatingModeCombo.SelectedItem is ProviderConnectionMode mode ? mode : ProviderConnectionMode.ReadOnly;

    private void UpdateSummary()
    {
        var providerName = _selectedProvider?.DisplayName
            ?? ProviderFamilyIdBox.Text.TrimOrNull()
            ?? "Select a relationship";
        var providerDescription = _selectedProvider?.Description
            ?? "Manual provider family. Meridian will persist the relationship even if the provider is not in the catalog yet.";
        var scopeSummary = string.Join(" | ", new[]
            {
                WorkspaceBox.Text.TrimOrNull(),
                FundProfileIdBox.Text.TrimOrNull(),
                ParseGuid(EntityIdBox.Text)?.ToString("N")[..8],
                ParseGuid(SleeveIdBox.Text)?.ToString("N")[..8],
                ParseGuid(VehicleIdBox.Text)?.ToString("N")[..8],
                ParseGuid(AccountIdBox.Text)?.ToString("N")[..8]
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        _viewModel.ApplyRelationshipSummary(
            providerName,
            providerDescription,
            GetSelectedConnectionType().ToString(),
            GetSelectedConnectionMode().ToString(),
            string.IsNullOrWhiteSpace(scopeSummary) ? "Global" : scopeSummary,
            GetSelectedCapabilities().Count == 0 ? "No capabilities selected" : string.Join(", ", GetSelectedCapabilities().Select(ToDisplayLabel)),
            string.IsNullOrWhiteSpace(CredentialReferenceBox.Text) ? "No credential reference yet" : CredentialReferenceBox.Text.Trim(),
            _selectedPreset?.Name ?? "No preset applied",
            _savedConnection?.ProductionReady == true ? "Production ready" : _viewModel.SummaryCertificationText);
    }

    private static Guid? ParseGuid(string value)
        => Guid.TryParse(value?.Trim(), out var parsed) ? parsed : null;

    private static string ToDisplayLabel(string value)
        => string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private static string? GetConfiguredEnvironmentValue(CredentialFieldInfo field)
    {
        foreach (var envVar in field.AllEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static IEnumerable<ProviderCatalogViewModel> BuildCatalogViewModels(
        IEnumerable<ProviderCatalogEntry> providers,
        IEnumerable<ProviderCatalogCredentialStatus> credentialStatuses)
        => providers.Select(provider =>
        {
            var credentialStatus = credentialStatuses.FirstOrDefault(status => status.ProviderId == provider.Id);
            return new ProviderCatalogViewModel(provider, credentialStatus);
        });

    private static IReadOnlyList<ProviderCapabilityKind> GetRecommendedCapabilities(ProviderConnectionType type)
        => type switch
        {
            ProviderConnectionType.Brokerage =>
            [
                ProviderCapabilityKind.OrderExecution,
                ProviderCapabilityKind.ExecutionHistory,
                ProviderCapabilityKind.AccountBalances,
                ProviderCapabilityKind.AccountPositions,
                ProviderCapabilityKind.ReconciliationFeed
            ],
            ProviderConnectionType.Bank =>
            [
                ProviderCapabilityKind.CashTransactions,
                ProviderCapabilityKind.BankStatements,
                ProviderCapabilityKind.AccountBalances,
                ProviderCapabilityKind.ReconciliationFeed
            ],
            ProviderConnectionType.Custodian =>
            [
                ProviderCapabilityKind.AccountPositions,
                ProviderCapabilityKind.ExecutionHistory,
                ProviderCapabilityKind.ReconciliationFeed
            ],
            ProviderConnectionType.Exchange =>
            [
                ProviderCapabilityKind.RealtimeMarketData,
                ProviderCapabilityKind.HistoricalTrades,
                ProviderCapabilityKind.HistoricalQuotes,
                ProviderCapabilityKind.ReferenceData
            ],
            _ =>
            [
                ProviderCapabilityKind.RealtimeMarketData,
                ProviderCapabilityKind.HistoricalBars,
                ProviderCapabilityKind.ReferenceData,
                ProviderCapabilityKind.SecurityMasterSeed,
                ProviderCapabilityKind.CorporateActions,
                ProviderCapabilityKind.OptionsChain
            ]
        };
}

internal sealed class ProviderCatalogViewModel
{
    public ProviderCatalogViewModel(ProviderCatalogEntry entry, ProviderCatalogCredentialStatus? credentialStatus)
    {
        Id = entry.Id;
        DisplayName = entry.DisplayName;
        Description = entry.Description;
        TierLabel = entry.Tier.ToString().ToUpperInvariant();
        TierBrush = entry.Tier switch
        {
            ProviderTier.Free or ProviderTier.FreeWithAccount => new SolidColorBrush(Color.FromArgb(40, 63, 185, 80)),
            ProviderTier.LimitedFree => new SolidColorBrush(Color.FromArgb(40, 210, 153, 34)),
            ProviderTier.Premium => new SolidColorBrush(Color.FromArgb(40, 88, 166, 255)),
            _ => new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
        };
        CredentialStatusBrush = credentialStatus?.State switch
        {
            CredentialState.Configured or CredentialState.NotRequired => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            CredentialState.Partial => new SolidColorBrush(Color.FromRgb(210, 153, 34)),
            CredentialState.Missing => new SolidColorBrush(Color.FromRgb(248, 81, 73)),
            _ => new SolidColorBrush(Color.FromRgb(139, 148, 158))
        };
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string TierLabel { get; }
    public Brush TierBrush { get; }
    public Brush CredentialStatusBrush { get; }
}

internal static class AddProviderWizardTextExtensions
{
    public static string? TrimOrNull(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
