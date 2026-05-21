using Meridian.Wpf.Models;

namespace Meridian.Wpf.Workstation.Commands;

public static class WorkspaceCommandAdapters
{
    public static IReadOnlyList<CommandViewModel> ToCommandDescriptors(WorkspaceCommandGroup? group)
    {
        if (group is null)
        {
            return Array.Empty<CommandViewModel>();
        }

        var items = new List<CommandViewModel>(group.PrimaryCommands.Count + group.SecondaryCommands.Count);
        AddCommands(items, group.PrimaryCommands);
        AddCommands(items, group.SecondaryCommands);
        return items;
    }

    private static void AddCommands(List<CommandViewModel> items, IReadOnlyList<WorkspaceCommandItem> source)
    {
        foreach (var entry in source)
        {
            items.Add(new CommandViewModel
            {
                Id = entry.Id,
                Label = entry.Label,
                IconGlyph = entry.Glyph,
                Tone = entry.Tone,
                Tooltip = string.IsNullOrWhiteSpace(entry.Description) ? entry.Label : entry.Description,
                IsEnabled = entry.IsEnabled
            });
        }
    }
}
