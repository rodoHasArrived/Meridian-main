using Meridian.Wpf.Models;

namespace Meridian.Wpf.Features.Settings.Shell;

public sealed class SettingsWorkspaceShellSnapshot
{
    public int ProviderCount { get; init; }

    public int ConfiguredCredentialCount { get; init; }

    public int MissingCredentialCount { get; init; }

    public string ShellDensityLabel { get; init; } = "Standard";

    public DateTimeOffset AsOfUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SettingsWorkspaceShellPresentation
{
    public WorkspaceShellContextInput Context { get; init; } = new();

    public WorkspaceCommandGroup CommandGroup { get; init; } = new();

    public string HeroBadgeText { get; init; } = "Ready";

    public string HeroBadgeTone { get; init; } = WorkspaceTone.Info;

    public string HeroFocusText { get; init; } = "Settings workspace ready";

    public string HeroSummaryText { get; init; } = "Preferences, credentials, diagnostics, services, alerts, and support routes stay together in one operator shell.";

    public string HeroDetailText { get; init; } = "Open a support lane or keep the default panes docked for readiness review.";

    public IReadOnlyList<WorkspaceQueueItem> OperationsItems { get; init; } = Array.Empty<WorkspaceQueueItem>();

    public IReadOnlyList<WorkspaceQueueItem> SupportItems { get; init; } = Array.Empty<WorkspaceQueueItem>();

    public IReadOnlyList<WorkspaceRecentItem> QuickLinks { get; init; } = Array.Empty<WorkspaceRecentItem>();
}
