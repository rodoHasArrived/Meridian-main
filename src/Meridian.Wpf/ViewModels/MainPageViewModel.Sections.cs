using System.Collections.ObjectModel;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.ViewModels;

internal sealed class MainPageNavigationSectionViewModel
{
    public MainPageNavigationSectionViewModel(
        ReadOnlyObservableCollection<ShellNavigationItem> primaryNavigationItems,
        ReadOnlyObservableCollection<ShellNavigationItem> secondaryNavigationItems,
        ReadOnlyObservableCollection<ShellNavigationItem> overflowNavigationItems,
        ReadOnlyObservableCollection<ShellNavigationItem> relatedWorkflowItems,
        ReadOnlyObservableCollection<MainPageViewModel.RecentPageEntry> recentPages,
        ReadOnlyObservableCollection<MainPageViewModel.WorkspaceTileItem> workspaceTiles,
        ReadOnlyObservableCollection<WorkstationOperatingContext> operatingContexts,
        ReadOnlyObservableCollection<BoundedWindowMode> windowModes)
    {
        PrimaryNavigationItems = primaryNavigationItems;
        SecondaryNavigationItems = secondaryNavigationItems;
        OverflowNavigationItems = overflowNavigationItems;
        RelatedWorkflowItems = relatedWorkflowItems;
        RecentPages = recentPages;
        WorkspaceTiles = workspaceTiles;
        OperatingContexts = operatingContexts;
        WindowModes = windowModes;
    }

    public ReadOnlyObservableCollection<ShellNavigationItem> PrimaryNavigationItems { get; }
    public ReadOnlyObservableCollection<ShellNavigationItem> SecondaryNavigationItems { get; }
    public ReadOnlyObservableCollection<ShellNavigationItem> OverflowNavigationItems { get; }
    public ReadOnlyObservableCollection<ShellNavigationItem> RelatedWorkflowItems { get; }
    public ReadOnlyObservableCollection<MainPageViewModel.RecentPageEntry> RecentPages { get; }
    public ReadOnlyObservableCollection<MainPageViewModel.WorkspaceTileItem> WorkspaceTiles { get; }
    public ReadOnlyObservableCollection<WorkstationOperatingContext> OperatingContexts { get; }
    public ReadOnlyObservableCollection<BoundedWindowMode> WindowModes { get; }
}

internal sealed class MainPageWorkflowSectionViewModel
{
    public MainPageWorkflowSectionViewModel(
        CommandPaletteViewModel commandPalette,
        OperatorInboxViewModel operatorInbox,
        WorkflowSummaryStripViewModel workflowSummaryStrip)
    {
        CommandPalette = commandPalette;
        OperatorInbox = operatorInbox;
        WorkflowSummaryStrip = workflowSummaryStrip;
    }

    public CommandPaletteViewModel CommandPalette { get; }
    public OperatorInboxViewModel OperatorInbox { get; }
    public WorkflowSummaryStripViewModel WorkflowSummaryStrip { get; }
}
