using System.Windows.Controls;

namespace Meridian.Wpf.Views;

// Primary navigation pages
public partial class DashboardPage : Page { }
public partial class WatchlistPage : Page { }

// Data Sources pages
public partial class ProviderPage : Page { }
public partial class ProviderHealthPage : Page { }
public partial class DataSourcesPage : Page { }

// Data Management pages
public partial class LiveDataViewerPage : Page { }
public partial class SymbolsPage : Page { }
public partial class SymbolMappingPage : Page { }
public partial class SymbolStoragePage : Page { }
public partial class StoragePage : Page { }
public partial class BackfillPage : Page { }
public partial class PortfolioImportPage : Page { }
public partial class IndexSubscriptionPage : Page { }
public partial class OptionsPage : Page { }
public partial class ScheduleManagerPage : Page { }

// Monitoring pages
public partial class DataQualityPage : Page { }
public partial class QualityArchivePage : Page { }
public partial class CollectionSessionPage : Page { }
public partial class ArchiveHealthPage : Page { }
public partial class ClusterStatusPage : UserControl { }
public partial class ServiceManagerPage : Page { }
public partial class SystemHealthPage : Page { }
public partial class DiagnosticsPage : Page { }

// Tools pages
public partial class DataExportPage : Page { }
public partial class DataSamplingPage : Page { }
public partial class TimeSeriesAlignmentPage : Page { }
public partial class ExportPresetsPage : Page { }
public partial class AnalysisExportPage : Page { }
public partial class AnalysisExportWizardPage : Page { }
public partial class EventReplayPage : Page { }
public partial class PackageManagerPage : Page { }
public partial class TradingHoursPage : Page { }

// Analytics & Visualization pages
public partial class AdvancedAnalyticsPage : Page { }
public partial class ChartingPage : Page { }
public partial class OrderBookPage : Page { }
public partial class DataCalendarPage : Page { }
public partial class RunMatPage : Page { }
public partial class QuantScriptPage : Page { }

// Storage & Maintenance pages
public partial class StorageOptimizationPage : Page { }
public partial class RetentionAssurancePage : Page { }
public partial class AdminMaintenancePage : Page { }

// Integrations pages
public partial class LeanIntegrationPage : Page { }
public partial class MessagingHubPage : Page { }

// Backtesting pages
public partial class BacktestPage : Page { }

// Strategy Run workstation pages (browser, detail drill-ins, portfolio, ledger, cash flow)
public partial class StrategyRunsPage : Page { }
public partial class RunDetailPage : Page { }
public partial class RunPortfolioPage : Page { }
public partial class RunLedgerPage : Page { }
public partial class RunCashFlowPage : Page { }

// Security Master workstation page
public partial class SecurityMasterPage : Page { }

// Direct Lending workstation page
public partial class DirectLendingPage : Page { }

// Data Browser page
public partial class DataBrowserPage : Page { }

// Workspace shell landing pages
public partial class ResearchWorkspaceShellPage : Page { }
public partial class TradingWorkspaceShellPage : Page { }

// Workspaces & Notifications pages
public partial class WorkspacePage : Page { }
public partial class NotificationCenterPage : Page { }

// Support & Setup pages
public partial class HelpPage : Page { }
public partial class WelcomePage : Page { }
public partial class SettingsPage : Page { }
public partial class CredentialManagementPage : Page { }
public partial class KeyboardShortcutsPage : Page { }
public partial class SetupWizardPage : Page { }
public partial class AddProviderWizardPage : Page { }

// Activity Log page
public partial class ActivityLogPage : Page { }

// Plugin Management page
public partial class PluginManagementPage : Page { }

// AI Agent page
public partial class AgentPage : Page { }

