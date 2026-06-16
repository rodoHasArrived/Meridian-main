using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Features.Settings.Shell;

public interface ISettingsWorkspaceShellPresentationService
{
    SettingsWorkspaceShellPresentation Build(SettingsWorkspaceShellSnapshot snapshot);
}

public sealed class SettingsWorkspaceShellPresentationService : ISettingsWorkspaceShellPresentationService, IWorkspaceScopedService
{
    public SettingsWorkspaceShellPresentation Build(SettingsWorkspaceShellSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var hasCredentialGaps = snapshot.MissingCredentialCount > 0;
        var heroTone = hasCredentialGaps ? WorkspaceTone.Warning : WorkspaceTone.Success;

        return new SettingsWorkspaceShellPresentation
        {
            Context = new WorkspaceShellContextInput
            {
                WorkspaceTitle = "Settings",
                WorkspaceSubtitle = "Preferences, credentials, diagnostics, notifications, services, and support.",
                PrimaryScopeLabel = "Shell density",
                PrimaryScopeValue = snapshot.ShellDensityLabel,
                AsOfValue = snapshot.AsOfUtc.ToLocalTime().ToString("g"),
                FreshnessValue = "Local workstation",
                ReviewStateLabel = "Credential posture",
                ReviewStateValue = hasCredentialGaps ? "Needs review" : "Ready",
                ReviewStateTone = heroTone,
                CriticalLabel = "Provider credentials",
                CriticalValue = $"{snapshot.ConfiguredCredentialCount}/{snapshot.ProviderCount} ready",
                CriticalTone = heroTone
            },
            CommandGroup = BuildCommandGroup(),
            HeroBadgeText = hasCredentialGaps ? "Review" : "Ready",
            HeroBadgeTone = heroTone,
            HeroFocusText = hasCredentialGaps
                ? "Credential review needed"
                : "Support and diagnostics ready",
            HeroSummaryText = hasCredentialGaps
                ? $"{snapshot.MissingCredentialCount} provider credential path(s) need setup or validation before live operator workflows depend on them."
                : "Provider credentials, diagnostics, notifications, and support routes are available from this workspace.",
            HeroDetailText = "Use the default panes for preferences, diagnostics, system health, and notifications without leaving the Settings workspace.",
            OperationsItems = BuildOperationsItems(snapshot, heroTone),
            SupportItems = BuildSupportItems(),
            QuickLinks = BuildQuickLinks()
        };
    }

    private static WorkspaceCommandGroup BuildCommandGroup() => new()
    {
        PrimaryCommands =
        [
            new WorkspaceCommandItem { Id = "Settings", Label = "Preferences", Description = "Open workstation preferences.", Glyph = "\uE713" },
            new WorkspaceCommandItem { Id = "CredentialManagement", Label = "Credentials", Description = "Review provider credential readiness.", Glyph = "\uE72E" },
            new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics and troubleshooting.", Glyph = "\uE90F" }
        ],
        SecondaryCommands =
        [
            new WorkspaceCommandItem { Id = "SystemHealth", Label = "System Health", Description = "Open workstation and dependency readiness.", Glyph = "\uE9D9" },
            new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open alerts and workstation events.", Glyph = "\uE7F4" },
            new WorkspaceCommandItem { Id = "Help", Label = "Help", Description = "Open support resources.", Glyph = "\uE897" }
        ]
    };

    private static IReadOnlyList<WorkspaceQueueItem> BuildOperationsItems(SettingsWorkspaceShellSnapshot snapshot, string credentialTone) =>
    [
        new WorkspaceQueueItem
        {
            Title = "Credential readiness",
            Detail = snapshot.MissingCredentialCount > 0
                ? "Some provider credentials need setup or validation before dependent workflows can be trusted."
                : "Provider credential requirements are satisfied or not required for the current catalog.",
            StatusLabel = snapshot.MissingCredentialCount > 0 ? "Needs review" : "Ready",
            CountLabel = $"{snapshot.ConfiguredCredentialCount}/{snapshot.ProviderCount}",
            Tone = credentialTone,
            PrimaryActionId = "CredentialManagement",
            PrimaryActionLabel = "Review Credentials",
            SecondaryActionId = "Settings",
            SecondaryActionLabel = "Preferences"
        },
        new WorkspaceQueueItem
        {
            Title = "Diagnostics and service health",
            Detail = "Keep diagnostics, service state, and workstation readiness docked beside settings changes.",
            StatusLabel = "Available",
            CountLabel = "2 panes",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "Diagnostics",
            PrimaryActionLabel = "Diagnostics",
            SecondaryActionId = "SystemHealth",
            SecondaryActionLabel = "System Health"
        }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildSupportItems() =>
    [
        new WorkspaceQueueItem
        {
            Title = "Notifications and activity",
            Detail = "Review alerts, workstation events, and messaging flow before handing off support work.",
            StatusLabel = "Available",
            CountLabel = "Alerts",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "NotificationCenter",
            PrimaryActionLabel = "Notifications",
            SecondaryActionId = "ActivityLog",
            SecondaryActionLabel = "Activity"
        },
        new WorkspaceQueueItem
        {
            Title = "Help and setup",
            Detail = "Open support guidance, keyboard shortcuts, and guided setup without leaving Settings.",
            StatusLabel = "Support",
            CountLabel = "Guides",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "Help",
            PrimaryActionLabel = "Help",
            SecondaryActionId = "SetupWizard",
            SecondaryActionLabel = "Setup Wizard"
        }
    ];

    private static IReadOnlyList<WorkspaceRecentItem> BuildQuickLinks() =>
    [
        new WorkspaceRecentItem { Title = "Workspace layouts", Detail = "Review saved shell layouts and workspace presets.", Meta = "Settings", Tone = WorkspaceTone.Info, ActionId = "Workspaces", ActionLabel = "Open Layouts" },
        new WorkspaceRecentItem { Title = "Workflow library", Detail = "Open reusable operator workflow templates and actions.", Meta = "Settings", Tone = WorkspaceTone.Info, ActionId = "WorkflowLibrary", ActionLabel = "Open Library" },
        new WorkspaceRecentItem { Title = "Service manager", Detail = "Inspect background services and operational controls.", Meta = "Settings", Tone = WorkspaceTone.Warning, ActionId = "ServiceManager", ActionLabel = "Open Services" }
    ];
}
