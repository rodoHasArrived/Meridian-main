namespace Meridian.Wpf.Models;

public static class ShellNavigationSectionIds
{
    public const string Favorites = "favorites";
    public const string Recent = "recent";
    public const string WorkspaceMenu = "workspace-menu";
}

public sealed record ShellNavigationPanelItem(
    string PageTag,
    string DisplayLabel,
    string Description,
    string WorkspaceId,
    string WorkspaceLabel,
    string WorkflowLabel,
    string Glyph,
    IReadOnlyList<string> SearchAliases,
    IReadOnlyList<string> SearchKeywords)
{
    public string AutomationId => $"ShellNavigationItem_{PageTag}";

    public string AutomationName => $"Open {DisplayLabel}";

    public string MetaLine
    {
        get
        {
            var parts = new[] { WorkspaceLabel, WorkflowLabel }
                .Where(static part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" · ", parts);
        }
    }
}

public sealed record ShellNavigationWorkspaceGroup(
    string WorkspaceId,
    string DisplayLabel,
    string AutomationId,
    IReadOnlyList<ShellNavigationPanelItem> Items);

public sealed record ShellNavigationPanelSection(
    string Id,
    string DisplayLabel,
    string AutomationId,
    string AutomationName,
    string EmptyText,
    IReadOnlyList<ShellNavigationPanelItem> Items,
    IReadOnlyList<ShellNavigationWorkspaceGroup> WorkspaceGroups)
{
    public bool HasItems => Items.Count > 0 || WorkspaceGroups.Any(static group => group.Items.Count > 0);
}
