using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Governance workspace shell — landing page for quality, health, diagnostics,
/// alerts, and operator control surfaces.
/// </summary>
public partial class GovernanceWorkspaceShellPage : Page
{
    private const string WorkspaceId = "governance";

    private readonly NavigationService _navigationService;
    private readonly FundContextService _fundContextService;

    public GovernanceWorkspaceShellPage(
        NavigationService navigationService,
        FundContextService fundContextService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _fundContextService = fundContextService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        UpdateActiveFundText();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _ = SaveDockLayoutAsync();
    }

    private async System.Threading.Tasks.Task RestoreDockLayoutAsync()
    {
        try
        {
            var xml = await WorkspaceService.Instance.GetDockLayoutAsync(WorkspaceId);
            if (!string.IsNullOrWhiteSpace(xml))
            {
                GovernanceDockManager.LoadLayout(xml);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to restore dock layout: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveDockLayoutAsync()
    {
        try
        {
            var xml = GovernanceDockManager.SaveLayout();
            if (!string.IsNullOrWhiteSpace(xml))
            {
                await WorkspaceService.Instance.SaveDockLayoutAsync(WorkspaceId, xml);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => _navigationService.NavigateTo(e.PageTag);

    private void OpenDataQuality_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("DataQuality");

    private void OpenProviderHealth_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("ProviderHealth");

    private void OpenSystemHealth_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("SystemHealth");

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Diagnostics");

    private void OpenNotifications_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("NotificationCenter");

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Settings");

    private void OpenFundLedger_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("FundLedger");

    private void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateActiveFundText);
            return;
        }

        UpdateActiveFundText();
    }

    private void UpdateActiveFundText()
    {
        var profile = _fundContextService.CurrentFundProfile;
        if (profile is null)
        {
            ActiveFundText.Text = "No fund selected";
            ActiveFundDetailText.Text = "Choose a fund to open fund-first governance views.";
            return;
        }

        ActiveFundText.Text = profile.DisplayName;
        ActiveFundDetailText.Text = $"{profile.LegalEntityName} · {profile.BaseCurrency} · default scope {profile.DefaultLedgerScope}";
    }
}
