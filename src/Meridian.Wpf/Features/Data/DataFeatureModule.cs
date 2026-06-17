using Meridian.Core.Config;
using Meridian.Ui.Services;
using Meridian.Ui.Services.DataQuality;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Features.Data.Shell;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Features.Data;

public sealed class DataFeatureModule : IDesktopFeatureModule
{
    private static IReadOnlyList<string> LegacyDataShellAliases =>
    [
        "DataOperationsShell",
        "DataOperationsWorkspace"
    ];

    private static readonly IReadOnlyList<ShellPageDescriptor> Pages =
    [
        ShellPageRegistryBuilder.Page<DataWorkspaceShellPage>("DataShell", "Data Workspace", "Monitor providers, collection jobs, storage, quality, and data products.", "data", "Launchpad", "\uEE94", 0, ShellNavigationVisibilityTier.Primary, ["data", "data operations", "home", "workspace"], ["Provider", "ProviderHealth", "Backfill", "Storage", "DataQuality"], LegacyDataShellAliases),
        ShellPageRegistryBuilder.Page<ProviderPage>("Provider", "Providers", "Configure provider integrations and review readiness.", "data", "Operations Queue", "\uEC05", 10, ShellNavigationVisibilityTier.Primary, ["provider", "integrations", "configure"], ["ProviderHealth", "DataSources", "AddProviderWizard"], ["Providers"]),
        ShellPageRegistryBuilder.Page<BackfillPage>("Backfill", "Backfill", "Monitor gap-fill jobs and review blockers.", "data", "Operations Queue", "\uE896", 20, ShellNavigationVisibilityTier.Primary, ["historical", "backfill", "jobs", "monitor"], ["CollectionSessions", "DataQuality", "Storage"]),
        ShellPageRegistryBuilder.Page<SymbolsPage>("Symbols", "Symbols", "Review, add, and curate symbols used across workflows.", "data", "Catalog", "\uE8AB", 30, ShellNavigationVisibilityTier.Primary, ["symbol", "catalog", "search", "review"], ["SymbolMapping", "SymbolStorage", "SecurityMaster"]),
        ShellPageRegistryBuilder.Page<StoragePage>("Storage", "Storage", "Monitor storage health, capacity, and retention.", "data", "Platform", "\uEE94", 40, ShellNavigationVisibilityTier.Primary, ["storage", "archive", "capacity", "monitor"], ["StorageOptimization", "ArchiveHealth"]),
        ShellPageRegistryBuilder.Page<DataExportPage>("DataExport", "Data export", "Configure export jobs and deliver datasets.", "data", "Packaging", "\uEDE1", 50, ShellNavigationVisibilityTier.Primary, ["export", "package", "configure"], ["ExportPresets", "AnalysisExport"]),
        ShellPageRegistryBuilder.Page<DataSourcesPage>("DataSources", "Data sources", "Review source connectivity and feed coverage.", "data", "Operations Queue", "\uEC05", 60, ShellNavigationVisibilityTier.Secondary, ["sources", "connectivity", "review"], ["Provider", "ProviderHealth"]),
        ShellPageRegistryBuilder.Page<ProviderHealthPage>("ProviderHealth", "Provider health", "Inspect provider reachability, degraded states, and recovery guidance.", "data", "Operations Queue", "\uEB51", 65, ShellNavigationVisibilityTier.Secondary, ["provider health", "reachability"], ["Provider", "DataSources", "CollectionSessions", "DataQuality"]),
        ShellPageRegistryBuilder.Page<DataQualityPage>("DataQuality", "Data quality", "Track validation signals, integrity issues, and remediation state.", "data", "Assurance", "\uE73E", 70, ShellNavigationVisibilityTier.Secondary, ["quality", "validation"], ["ProviderHealth", "ArchiveHealth", "Diagnostics"]),
        ShellPageRegistryBuilder.Page<SecurityMasterPage>("SecurityMaster", "Security master", "Review reference data, listings, and security lifecycle readiness.", "data", "Assurance", "\uE72E", 80, ShellNavigationVisibilityTier.Secondary, ["reference data", "security"], ["SecurityInstrumentExplorer", "Symbols", "SymbolMapping", "DataQuality"]),
        ShellPageRegistryBuilder.Page<SymbolMappingPage>("SymbolMapping", "Symbol mapping", "Reconcile vendor symbols with Meridian identifiers.", "data", "Catalog", "\uE8AB", 90, ShellNavigationVisibilityTier.Secondary, ["mapping", "vendor", "reconcile"], ["Symbols", "SecurityMaster"]),
        ShellPageRegistryBuilder.Page<SymbolStoragePage>("SymbolStorage", "Symbol storage", "Review symbol persistence and storage layout.", "data", "Catalog", "\uEE94", 100, ShellNavigationVisibilityTier.Secondary, ["symbol storage", "persistence", "review"], ["Symbols", "Storage"]),
        ShellPageRegistryBuilder.Page<ScheduleManagerPage>("Schedules", "Schedules", "Configure collection and maintenance schedules.", "data", "Platform", "\uE916", 110, ShellNavigationVisibilityTier.Secondary, ["schedule", "jobs", "configure"], ["Backfill", "CollectionSessions"]),
        ShellPageRegistryBuilder.Page<CollectionSessionPage>("CollectionSessions", "Collection sessions", "Monitor ingest sessions and collector state.", "data", "Operations Queue", "\uE8EF", 120, ShellNavigationVisibilityTier.Secondary, ["collector", "sessions", "monitor"], ["Backfill", "ProviderHealth"]),
        ShellPageRegistryBuilder.Page<PackageManagerPage>("PackageManager", "Package manager", "Review datasets, packages, and installed content.", "data", "Packaging", "\uE8B7", 130, ShellNavigationVisibilityTier.Secondary, ["packages", "content", "review"], ["DataExport", "AnalysisExport"]),
        ShellPageRegistryBuilder.Page<DataBrowserPage>("DataBrowser", "Data browser", "Review stored datasets and sample time-series records.", "data", "Inspectors", "\uE721", 140, ShellNavigationVisibilityTier.Overflow, ["browser", "dataset", "review"], ["DataSampling", "TimeSeriesAlignment"]),
        ShellPageRegistryBuilder.Page<DataCalendarPage>("DataCalendar", "Data calendar", "Review date coverage and schedule gaps.", "data", "Inspectors", "\uE787", 150, ShellNavigationVisibilityTier.Overflow, ["calendar", "coverage", "review"], ["TradingHours", "Backfill"]),
        ShellPageRegistryBuilder.Page<DataSamplingPage>("DataSampling", "Data sampling", "Review sample quality before export.", "data", "Inspectors", "\uF1AD", 160, ShellNavigationVisibilityTier.Overflow, ["sampling", "quality", "review"], ["DataBrowser", "DataQuality"]),
        ShellPageRegistryBuilder.Page<TimeSeriesAlignmentPage>("TimeSeriesAlignment", "Time series alignment", "Reconcile timestamps across feeds and intervals.", "data", "Inspectors", "\uE81E", 170, ShellNavigationVisibilityTier.Overflow, ["alignment", "timeseries", "reconcile"], ["DataSampling", "DataQuality"]),
        ShellPageRegistryBuilder.Page<IndexSubscriptionPage>("IndexSubscription", "Index subscription", "Configure derived index subscriptions and coverage.", "data", "Catalog", "\uE8FD", 180, ShellNavigationVisibilityTier.Overflow, ["index", "subscription", "configure"], ["Symbols", "DataSources"]),
        ShellPageRegistryBuilder.Page<OptionsPage>("Options", "Options", "Review options and derivatives coverage.", "data", "Catalog", "\uE7C5", 190, ShellNavigationVisibilityTier.Overflow, ["options", "derivatives", "review"], ["Symbols", "SecurityMaster"]),
        ShellPageRegistryBuilder.Page<AddProviderWizardPage>("AddProviderWizard", "Add provider wizard", "Configure a new provider integration step by step.", "data", "Operations Queue", "\uE710", 200, ShellNavigationVisibilityTier.Overflow, ["provider wizard", "setup", "configure"], ["Provider", "CredentialManagement"]),
        ShellPageRegistryBuilder.Page<ArchiveHealthPage>("ArchiveHealth", "Archive health", "Review archive integrity, retention readiness, and gap signals.", "data", "Assurance", "\uE73E", 210, ShellNavigationVisibilityTier.Overflow, ["archive", "retention"], ["Storage", "RetentionAssurance"]),
        ShellPageRegistryBuilder.Page<StorageOptimizationPage>("StorageOptimization", "Storage optimization", "Tune retention, footprint, and storage efficiency.", "data", "Assurance", "\uE713", 220, ShellNavigationVisibilityTier.Overflow, ["optimization", "retention"], ["Storage", "ArchiveHealth"]),
        ShellPageRegistryBuilder.Page<RetentionAssurancePage>("RetentionAssurance", "Retention assurance", "Validate retention-policy adherence and lifecycle readiness.", "data", "Assurance", "\uE8A5", 230, ShellNavigationVisibilityTier.Overflow, ["retention", "policy"], ["ArchiveHealth", "StorageOptimization"])
    ];

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWorkspaceScoped<IDataWorkspaceShellSnapshotService, DataWorkspaceShellSnapshotService>();
        services.AddWorkspaceScoped<IDataWorkspaceShellPresentationService, DataWorkspaceShellPresentationService>();
        services.AddTransient<DataWorkspaceShellViewModel>();
        services.AddTransient<DataWorkspaceShellPage>();
        services.AddTransient<DataCalendarService>();
        services.AddTransient<SecurityMasterViewModel>();
        services.AddTransient<QualityArchivePage>();
        services.AddTransient<ClusterStatusPage>();
        services.AddSingleton<BackfillProviderConfigService>(_ => BackfillProviderConfigService.Instance);
        services.AddSingleton<BackfillCheckpointService>(_ => BackfillCheckpointService.Instance);
        services.AddSingleton<BackfillApiService>();
        services.AddSingleton<CollectionSessionService>(_ => CollectionSessionService.Instance);
        services.AddSingleton<ScheduleManagerService>(_ => ScheduleManagerService.Instance);
        services.AddSingleton<Meridian.Wpf.Services.StorageService>(_ => Meridian.Wpf.Services.StorageService.Instance);
        services.AddSingleton<WorkspaceStateTokenStore>();
        services.AddSingleton<BatchExportSchedulerService>();
        services.AddSingleton<ActivityFeedService>(_ => ActivityFeedService.Instance);
        services.AddSingleton<CommandPaletteService>(_ => CommandPaletteService.Instance);
        services.AddSingleton<SymbolManagementService>(_ => SymbolManagementService.Instance);
        services.AddSingleton<BackfillService>(_ => BackfillService.Instance);
        services.AddSingleton<IDataQualityApiClient, DataQualityApiClient>();
        services.AddSingleton<IDataQualityPresentationService, DataQualityPresentationService>();
        services.AddTransient<IDataQualityRefreshService, DataQualityRefreshService>();
        services.AddTransient<BackfillViewModel>();
        services.AddTransient<ProviderViewModel>();
        services.AddTransient<DataQualityViewModel>();
        services.AddTransient<CollectionSessionViewModel>();
        services.AddTransient<WatchlistViewModel>();
        services.AddSingleton<IQualityArchiveStore, QualityArchiveStore>();
        services.AddTransient<QualityArchiveViewModel>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Pages;

    public IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() =>
        FeatureCapabilityCatalog.Data;

    public WorkspaceCapabilityDescriptor DescribeWorkspace()
        => ShellNavigationCatalog.BuildCapability(
            WorkspaceCopyCatalog.Data.Descriptor,
            "DataShell",
            new WorkspaceShellDefinition(
                WorkspaceId: WorkspaceCopyCatalog.Data.WorkspaceId,
                LayoutId: "data-workspace",
                DisplayName: WorkspaceCopyCatalog.Data.Descriptor.ShellDisplayName,
                DefaultPanes:
                [
                    ShellPageRegistryBuilder.Pane("Provider", PaneDropAction.Replace),
                    ShellPageRegistryBuilder.Pane("Backfill", PaneDropAction.SplitRight),
                    ShellPageRegistryBuilder.Pane("Storage", PaneDropAction.SplitBelow),
                    ShellPageRegistryBuilder.Pane("DataQuality", PaneDropAction.OpenTab)
                ],
                PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
                ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
                StateProviderType: typeof(DataWorkspaceShellStateProvider),
                ViewModelType: typeof(DataWorkspaceShellViewModel)),
            Pages);
}
