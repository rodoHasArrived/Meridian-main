using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF-specific navigation service that extends <see cref="NavigationServiceBase"/> with
/// WPF Frame navigation and DI-aware page creation.
/// Implements <see cref="INavigationService"/> with singleton pattern.
/// Phase 6C.2: Shared base class extracts page registry, history tracking, and breadcrumb logic.
/// </summary>
public sealed class NavigationService : NavigationServiceBase, INavigationService
{
    private static readonly Lazy<NavigationService> _instance = new(() => new NavigationService());

    private Frame? _frame;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Gets the singleton instance of the NavigationService.
    /// </summary>
    public static NavigationService Instance => _instance.Value;

    /// <inheritdoc />
    public bool IsInitialized => _frame != null;

    /// <inheritdoc />
    public override bool CanGoBack => _frame?.CanGoBack ?? false;

    private NavigationService()
    {
    }

    /// <summary>
    /// Sets the DI service provider used to resolve page instances.
    /// Called once during application startup after the DI container is built.
    /// </summary>
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Initializes the navigation service with the main frame.
    /// </summary>
    public void Initialize(Frame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    /// <inheritdoc />
    protected override void RegisterAllPages()
    {
        // Primary navigation (2 pages)
        RegisterPage("Dashboard", typeof(DashboardPage));
        RegisterPage("Watchlist", typeof(WatchlistPage));

        // Data Sources (3 pages)
        RegisterPage("Provider", typeof(ProviderPage));
        RegisterPage("ProviderHealth", typeof(ProviderHealthPage));
        RegisterPage("DataSources", typeof(DataSourcesPage));

        // Data Management (9 pages)
        RegisterPage("LiveData", typeof(LiveDataViewerPage));
        RegisterPage("RunMat", typeof(RunMatPage));
        RegisterPage("DataBrowser", typeof(DataBrowserPage));
        RegisterPage("Symbols", typeof(SymbolsPage));
        RegisterPage("SymbolMapping", typeof(SymbolMappingPage));
        RegisterPage("SymbolStorage", typeof(SymbolStoragePage));
        RegisterPage("Storage", typeof(StoragePage));
        RegisterPage("Backfill", typeof(BackfillPage));
        RegisterPage("PortfolioImport", typeof(PortfolioImportPage));
        RegisterPage("IndexSubscription", typeof(IndexSubscriptionPage));
        RegisterPage("Options", typeof(OptionsPage));
        RegisterPage("Schedules", typeof(ScheduleManagerPage));

        // Trading & Backtesting (4 pages)
        RegisterPage("Backtest", typeof(BacktestPage));
        RegisterPage("TradingHours", typeof(TradingHoursPage));
        RegisterPage("StrategyRuns", typeof(StrategyRunsPage));
        RegisterPage("RunDetail", typeof(RunDetailPage));
        RegisterPage("RunPortfolio", typeof(RunPortfolioPage));
        RegisterPage("RunLedger", typeof(RunLedgerPage));
        RegisterPage("RunCashFlow", typeof(RunCashFlowPage));
        RegisterPage("PositionBlotter", typeof(PositionBlotterPage));
        RegisterPage("RunRisk", typeof(RunRiskPage));

        // Monitoring (6 pages)
        RegisterPage("DataQuality", typeof(DataQualityPage));
        RegisterPage("CollectionSessions", typeof(CollectionSessionPage));
        RegisterPage("ArchiveHealth", typeof(ArchiveHealthPage));
        RegisterPage("ServiceManager", typeof(ServiceManagerPage));
        RegisterPage("SystemHealth", typeof(SystemHealthPage));
        RegisterPage("Diagnostics", typeof(DiagnosticsPage));

        // Tools (8 pages)
        RegisterPage("DataExport", typeof(DataExportPage));
        RegisterPage("DataSampling", typeof(DataSamplingPage));
        RegisterPage("TimeSeriesAlignment", typeof(TimeSeriesAlignmentPage));
        RegisterPage("ExportPresets", typeof(ExportPresetsPage));
        RegisterPage("AnalysisExport", typeof(AnalysisExportPage));
        RegisterPage("AnalysisExportWizard", typeof(AnalysisExportWizardPage));
        RegisterPage("EventReplay", typeof(EventReplayPage));
        RegisterPage("PackageManager", typeof(PackageManagerPage));

        // Analytics & Visualization (4 pages)
        RegisterPage("AdvancedAnalytics", typeof(AdvancedAnalyticsPage));
        RegisterPage("Charts", typeof(ChartingPage));
        RegisterPage("OrderBook", typeof(OrderBookPage));
        RegisterPage("DataCalendar", typeof(DataCalendarPage));
        RegisterPage("QuantScript", typeof(QuantScriptPage));

        // Storage & Maintenance (3 pages)
        RegisterPage("StorageOptimization", typeof(StorageOptimizationPage));
        RegisterPage("RetentionAssurance", typeof(RetentionAssurancePage));
        RegisterPage("AdminMaintenance", typeof(AdminMaintenancePage));

        // Integrations (2 pages)
        RegisterPage("LeanIntegration", typeof(LeanIntegrationPage));
        RegisterPage("MessagingHub", typeof(MessagingHubPage));

        // Workspace shell landing pages
        RegisterPage("ResearchShell", typeof(ResearchWorkspaceShellPage));
        RegisterPage("TradingShell", typeof(TradingWorkspaceShellPage));

        // Workspaces & Notifications (2 pages)
        RegisterPage("Workspaces", typeof(WorkspacePage));
        RegisterPage("NotificationCenter", typeof(NotificationCenterPage));

        // Support & Setup (6 pages)
        RegisterPage("Help", typeof(HelpPage));
        RegisterPage("Welcome", typeof(WelcomePage));
        RegisterPage("Settings", typeof(SettingsPage));
        RegisterPage("CredentialManagement", typeof(CredentialManagementPage));
        RegisterPage("KeyboardShortcuts", typeof(KeyboardShortcutsPage));
        RegisterPage("SetupWizard", typeof(SetupWizardPage));
        RegisterPage("AddProviderWizard", typeof(AddProviderWizardPage));

        // Activity Log (1 page)
        RegisterPage("ActivityLog", typeof(ActivityLogPage));

        // Security Master workstation (1 page)
        RegisterPage("SecurityMaster", typeof(SecurityMasterPage));

        // Direct Lending workstation (1 page)
        RegisterPage("DirectLending", typeof(DirectLendingPage));
    }

    /// <inheritdoc />
    protected override bool NavigateToPageCore(Type pageType, object? parameter)
    {
        if (_frame == null) return false;

        try
        {
            var page = CreatePage(pageType);

            if (parameter != null && page is Page wpfPage && wpfPage.DataContext != null)
            {
                var parameterProperty = wpfPage.DataContext.GetType().GetProperty("Parameter");
                parameterProperty?.SetValue(wpfPage.DataContext, parameter);
            }

            var result = _frame.Navigate(page);

            if (result)
            {
                // Trigger onboarding tour for the navigated page if applicable
                var currentTag = GetCurrentPageTag();
                if (currentTag != null)
                {
                    CheckOnboardingTourForPage(currentTag);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            var pageName = pageType.Name;
            LoggingService.Instance.LogError($"Navigation to {pageName} failed: {ex}");

            _frame.Navigate(CreateNavigationErrorPage(pageName, ex));

            try
            {
                MessageBox.Show(
                    $"Navigation to '{pageName}' failed.\n\n{ex.Message}",
                    "Navigation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Last-resort path: keep the error page visible even if the dialog fails.
            }

            return false;
        }
    }

    /// <summary>
    /// Checks if an onboarding tour should be shown for the navigated page
    /// and raises the TourAvailable event if so.
    /// </summary>
    private static void CheckOnboardingTourForPage(string pageTag)
    {
        try
        {
            var tourService = Meridian.Ui.Services.OnboardingTourService.Instance;
            if (tourService.IsTourActive) return;

            var tour = tourService.GetTourForPage(pageTag);
            if (tour != null)
            {
                tourService.StartTour(tour.Id);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <inheritdoc />
    protected override void GoBackCore()
    {
        if (_frame?.CanGoBack ?? false)
        {
            _frame.GoBack();
        }
    }

    /// <inheritdoc />
    protected override void ClearHistoryCore()
    {
        while (_frame?.CanGoBack ?? false)
        {
            _frame.RemoveBackEntry();
        }
    }

    /// <summary>
    /// Creates a page instance using the DI container if available, falling back to Activator.
    /// When no service provider is configured and the page type has no parameterless constructor
    /// (e.g. in unit tests), a placeholder <see cref="Page"/> is returned so that navigation
    /// orchestration (history tracking, event raising) can still be validated.
    /// </summary>
    private object CreatePage(Type pageType)
    {
        if (_serviceProvider != null)
        {
            return _serviceProvider.GetService(pageType) ?? ActivatorUtilities.CreateInstance(_serviceProvider, pageType);
        }

        try
        {
            return Activator.CreateInstance(pageType) ?? new Page();
        }
        catch (MissingMethodException)
        {
            // Page requires constructor injection but no DI container is available.
            return new Page();
        }
    }

    private static Page CreateNavigationErrorPage(string pageName, Exception ex)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24)
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Unable to open {pageName}",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock
        {
            Text = ex.Message,
            TextWrapping = TextWrapping.Wrap
        });

        return new Page
        {
            Content = panel
        };
    }
}
