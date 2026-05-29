namespace Meridian.Wpf.Models;

public enum ShellPosture
{
    Terminal,
    Cockpit,
    Workbench
}

public enum WorkspaceVisualPriority
{
    Evidence,
    Commands,
    Inspector
}

public sealed record WorkspaceShellLayoutDescriptor(
    string WorkspaceId,
    ShellPosture Posture,
    string HomeTemplateAutomationId,
    string EvidenceStripAutomationId,
    string CommandSurfaceAutomationId,
    string InspectorHostAutomationId,
    IReadOnlyList<WorkspaceVisualPriority> VisualPriority);

