namespace Meridian.Wpf.Models;

public static partial class ShellNavigationCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, WorkspaceShellLayoutDescriptor>> WorkspaceLayoutsById =
        new(BuildWorkspaceLayoutLookup);

    public static WorkspaceShellLayoutDescriptor GetWorkspaceLayoutDescriptor(string? workspaceId)
    {
        var resolvedWorkspaceId = ResolveWorkspaceLayoutId(workspaceId);
        return WorkspaceLayoutsById.Value.TryGetValue(resolvedWorkspaceId, out var descriptor)
            ? descriptor
            : WorkspaceLayoutsById.Value["strategy"];
    }

    public static ShellPosture GetWorkspacePosture(string? workspaceId)
        => GetWorkspaceLayoutDescriptor(workspaceId).Posture;

    private static IReadOnlyDictionary<string, WorkspaceShellLayoutDescriptor> BuildWorkspaceLayoutLookup()
    {
        var descriptors = new[]
        {
            BuildLayout("trading", ShellPosture.Terminal),
            BuildLayout("portfolio", ShellPosture.Cockpit),
            BuildLayout("accounting", ShellPosture.Cockpit),
            BuildLayout("reporting", ShellPosture.Cockpit),
            BuildLayout("strategy", ShellPosture.Workbench),
            BuildLayout("data", ShellPosture.Terminal),
            BuildLayout("settings", ShellPosture.Cockpit)
        };

        return descriptors.ToDictionary(static descriptor => descriptor.WorkspaceId, StringComparer.OrdinalIgnoreCase);
    }

    private static WorkspaceShellLayoutDescriptor BuildLayout(string workspaceId, ShellPosture posture)
        => new(
            WorkspaceId: workspaceId,
            Posture: posture,
            HomeTemplateAutomationId: $"WorkspaceHome{ToPascalWorkspaceId(workspaceId)}{posture}Template",
            EvidenceStripAutomationId: $"WorkspaceEvidenceStrip{ToPascalWorkspaceId(workspaceId)}",
            CommandSurfaceAutomationId: $"WorkspaceCommandSurface{ToPascalWorkspaceId(workspaceId)}",
            InspectorHostAutomationId: $"WorkspaceInspectorHost{ToPascalWorkspaceId(workspaceId)}",
            VisualPriority: GetVisualPriority(posture));

    private static IReadOnlyList<WorkspaceVisualPriority> GetVisualPriority(ShellPosture posture)
        => posture switch
        {
            ShellPosture.Terminal =>
            [
                WorkspaceVisualPriority.Evidence,
                WorkspaceVisualPriority.Commands,
                WorkspaceVisualPriority.Inspector
            ],
            ShellPosture.Cockpit =>
            [
                WorkspaceVisualPriority.Commands,
                WorkspaceVisualPriority.Evidence,
                WorkspaceVisualPriority.Inspector
            ],
            _ =>
            [
                WorkspaceVisualPriority.Inspector,
                WorkspaceVisualPriority.Commands,
                WorkspaceVisualPriority.Evidence
            ]
        };

    private static string ResolveWorkspaceLayoutId(string? workspaceId)
    {
        if (ShellNavigationCatalog.GetWorkspace(workspaceId) is { } workspace)
        {
            return workspace.Id;
        }

        return workspaceId?.Trim().ToLowerInvariant() switch
        {
            "research" => "strategy",
            "data-operations" => "data",
            "data operations" => "data",
            "governance" => "accounting",
            _ => "strategy"
        };
    }

    private static string ToPascalWorkspaceId(string workspaceId)
        => string.Concat(
            workspaceId
                .Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
}

