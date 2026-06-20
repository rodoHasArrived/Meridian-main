using Meridian.Core.Config;
using Meridian.Contracts.Services;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Workflow.EnvironmentDesign;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using SettingsWorkspaceShellPage = Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellPage;
using SettingsWorkspaceShellPresentationService = Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellPresentationService;
using SettingsWorkspaceShellSnapshotService = Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellSnapshotService;
using SettingsWorkspaceShellViewModel = Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellViewModel;
using ISettingsWorkspaceShellPresentationService = Meridian.Wpf.Features.Settings.Shell.ISettingsWorkspaceShellPresentationService;
using ISettingsWorkspaceShellSnapshotService = Meridian.Wpf.Features.Settings.Shell.ISettingsWorkspaceShellSnapshotService;
using WpfCredentialService = Meridian.Wpf.Services.CredentialService;

namespace Meridian.Wpf.Features.Settings;

public sealed class SettingsFeatureModule : IDesktopFeatureModule
{
    private static readonly IReadOnlyList<ShellPageDescriptor> Pages =
    [
        ShellPageRegistryBuilder.Page<SettingsWorkspaceShellPage>("SettingsShell", "Settings Workspace", "Manage preferences, credentials, diagnostics, services, alerts, and help.", "settings", "Launchpad", "\uE713", 0, ShellNavigationVisibilityTier.Primary, ["settings", "preferences", "workspace", "support"], ["Settings", "Diagnostics", "SystemHealth", "Help"]),
        ShellPageRegistryBuilder.Page<SettingsPage>("Settings", "Settings", "Adjust workstation preferences, connections, and operator defaults.", "settings", "Preferences", "\uE713", 10, ShellNavigationVisibilityTier.Primary, ["preferences", "settings"], ["CredentialManagement", "KeyboardShortcuts", "Help"], ["Preferences"]),
        ShellPageRegistryBuilder.Page<CredentialManagementPage>("CredentialManagement", "Credential management", "Manage provider credentials and validate secure access.", "settings", "Preferences", "\uE72E", 20, ShellNavigationVisibilityTier.Primary, ["credentials", "security"], ["Settings", "AddProviderWizard"]),
        ShellPageRegistryBuilder.Page<SystemHealthPage>("SystemHealth", "System health", "Monitor workstation service health, dependencies, and readiness.", "settings", "Operations", "\uE9D9", 30, ShellNavigationVisibilityTier.Primary, ["health", "system"], ["Diagnostics", "ServiceManager"]),
        ShellPageRegistryBuilder.Page<DiagnosticsPage>("Diagnostics", "Diagnostics", "Run checks, inspect latency, and troubleshoot operator issues.", "settings", "Operations", "\uE90F", 40, ShellNavigationVisibilityTier.Primary, ["diagnostics", "troubleshooting"], ["SystemHealth", "ServiceManager"]),
        ShellPageRegistryBuilder.Page<ServiceManagerPage>("ServiceManager", "Service manager", "Inspect background services, logs, and operational controls.", "settings", "Operations", "\uECE7", 50, ShellNavigationVisibilityTier.Secondary, ["services", "logs"], ["SystemHealth", "Diagnostics"]),
        ShellPageRegistryBuilder.Page<AdminMaintenancePage>("AdminMaintenance", "Admin maintenance", "Execute privileged maintenance tasks and operations.", "settings", "Operations", "\uE74D", 60, ShellNavigationVisibilityTier.Secondary, ["admin", "maintenance"], ["Diagnostics", "Settings"]),
        ShellPageRegistryBuilder.Page<EnvironmentDesignerPage>("EnvironmentDesigner", "Environment designer", "Draft, validate, publish, and roll back operating models.", "settings", "Operations", "\uE70F", 70, ShellNavigationVisibilityTier.Secondary, ["environment", "designer", "lanes"], ["AdminMaintenance", "SettingsShell"]),
        ShellPageRegistryBuilder.Page<MessagingHubPage>("MessagingHub", "Messaging hub", "Inspect operator notifications and internal messaging pathways.", "settings", "Notifications", "\uE715", 80, ShellNavigationVisibilityTier.Secondary, ["messaging", "notifications"], ["NotificationCenter", "ActivityLog"]),
        ShellPageRegistryBuilder.Page<NotificationCenterPage>("NotificationCenter", "Notification center", "Review active notifications, alerts, and workstation events.", "settings", "Notifications", "\uE7F4", 90, ShellNavigationVisibilityTier.Secondary, ["notifications", "alerts"], ["MessagingHub", "ActivityLog"], ["Alerts"]),
        ShellPageRegistryBuilder.Page<ActivityLogPage>("ActivityLog", "Activity log", "Review recent workstation actions and state changes.", "settings", "Notifications", "\uE823", 100, ShellNavigationVisibilityTier.Overflow, ["activity", "history"], ["NotificationCenter", "FundAuditTrail"]),
        ShellPageRegistryBuilder.Page<KeyboardShortcutsPage>("KeyboardShortcuts", "Keyboard shortcuts", "Review accelerator keys and workstation shortcuts.", "settings", "Support", "\uE765", 110, ShellNavigationVisibilityTier.Overflow, ["shortcuts", "keyboard"], ["Help", "Settings"]),
        ShellPageRegistryBuilder.Page<HelpPage>("Help", "Help and support", "Access support resources and workstation guidance.", "settings", "Support", "\uE897", 120, ShellNavigationVisibilityTier.Overflow, ["help", "support"], ["KeyboardShortcuts", "Settings"]),
        ShellPageRegistryBuilder.Page<SetupWizardPage>("SetupWizard", "Setup wizard", "Complete initial workstation setup and guided configuration.", "settings", "Support", "\uE8B0", 130, ShellNavigationVisibilityTier.Overflow, ["setup", "wizard"], ["Settings", "CredentialManagement"], hideFromDefaultPalette: true),
        ShellPageRegistryBuilder.Page<Meridian.Wpf.Views.WorkspacePage>("Workspaces", "Workspace layouts", "Workspace layout catalog and saved-layout surface.", "settings", "Workspace layouts", "\uE8A9", 140, ShellNavigationVisibilityTier.Overflow, ["workspaces", "layouts"], ["SettingsShell"], hideFromDefaultPalette: true),
        ShellPageRegistryBuilder.Page<WorkflowLibraryPage>("WorkflowLibrary", "Workflow library", "Reusable workflow catalog and action launch surface.", "settings", "Workspace layouts", "\uE8FD", 145, ShellNavigationVisibilityTier.Secondary, ["workflow", "library", "actions", "templates"], ["Workspaces", "SettingsShell"]),
        ShellPageRegistryBuilder.Page<WelcomePage>("Welcome", "Welcome", "Onboarding entry point and first-run guidance.", "settings", "Support", "\uE80F", 150, ShellNavigationVisibilityTier.Overflow, ["welcome", "onboarding"], ["SetupWizard", "Help"], hideFromDefaultPalette: true)
    ];

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWorkspaceScoped<ISettingsWorkspaceShellSnapshotService, SettingsWorkspaceShellSnapshotService>();
        services.AddWorkspaceScoped<ISettingsWorkspaceShellPresentationService, SettingsWorkspaceShellPresentationService>();
        services.AddTransient<SettingsWorkspaceShellViewModel>();
        services.AddTransient<SettingsWorkspaceShellPage>();
        services.AddSingleton<ProviderManagementService>(_ => ProviderManagementService.Instance);
        services.AddSingleton<AdminMaintenanceServiceBase>(_ => AdminMaintenanceServiceBase.Instance);
        services.AddSingleton<IAdminMaintenanceService>(sp => sp.GetRequiredService<AdminMaintenanceServiceBase>());
        services.AddSingleton<SearchService>(_ => SearchService.Instance);
        services.AddSingleton<SetupWizardService>();
        services.AddSingleton<ISetupWizardStateService, SetupWizardStateService>();
        services.AddSingleton<ISetupWizardNotificationSink, SetupWizardNotificationSink>();
        services.AddSingleton<ISetupWizardNavigator, SetupWizardNavigator>();
        services.AddSingleton<ISetupWizardLinkLauncher, SetupWizardLinkLauncher>();
        services.AddSingleton<EnvironmentDesignerService>(_ => new EnvironmentDesignerService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Meridian",
                "environment-designer.json")));
        services.AddSingleton<IEnvironmentDesignService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<IEnvironmentValidationService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<IEnvironmentPublishService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddSingleton<IEnvironmentRuntimeProjectionService>(sp => sp.GetRequiredService<EnvironmentDesignerService>());
        services.AddTransient<PluginManagementPage>();
        services.AddTransient<PluginManagementViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<WorkflowLibraryViewModel>();
        services.AddSingleton<WpfCredentialService>();
        services.AddTransient<CredentialManagementViewModel>();
    }

    public IReadOnlyList<ShellPageDescriptor> DescribePages() => Pages;

    public IReadOnlyList<FeatureCapabilityDescriptor> DeclareCapabilities() =>
        FeatureCapabilityCatalog.Settings;

    public WorkspaceCapabilityDescriptor DescribeWorkspace()
        => ShellNavigationCatalog.BuildCapability(
            WorkspaceCopyCatalog.Settings.Descriptor,
            "SettingsShell",
            new WorkspaceShellDefinition(
                WorkspaceId: WorkspaceCopyCatalog.Settings.WorkspaceId,
                LayoutId: "settings-workspace",
                DisplayName: WorkspaceCopyCatalog.Settings.Descriptor.ShellDisplayName,
                DefaultPanes:
                [
                    ShellPageRegistryBuilder.Pane("Settings", PaneDropAction.Replace),
                    ShellPageRegistryBuilder.Pane("Diagnostics", PaneDropAction.SplitRight),
                    ShellPageRegistryBuilder.Pane("SystemHealth", PaneDropAction.SplitBelow),
                    ShellPageRegistryBuilder.Pane("NotificationCenter", PaneDropAction.OpenTab)
                ],
                PresetPanes: new Dictionary<string, IReadOnlyList<WorkspacePaneDefinition>>(StringComparer.OrdinalIgnoreCase),
                ContextlessPanes: Array.Empty<WorkspacePaneDefinition>(),
                StateProviderType: typeof(SettingsWorkspaceShellStateProvider),
                ViewModelType: typeof(SettingsWorkspaceShellViewModel)),
            Pages);
}
