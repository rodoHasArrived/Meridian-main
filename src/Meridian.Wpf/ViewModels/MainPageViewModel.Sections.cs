using System.Collections.ObjectModel;
using System.Windows;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.ViewModels;

internal sealed class MainPageNavigationSectionViewModel
{
    internal ObservableCollection<ShellNavigationItem> PrimaryItems { get; } = [];
    internal ObservableCollection<ShellNavigationItem> SecondaryItems { get; } = [];
    internal ObservableCollection<ShellNavigationItem> OverflowItems { get; } = [];
    internal ObservableCollection<ShellNavigationItem> RelatedWorkflowNavItems { get; } = [];
    internal ObservableCollection<MainPageViewModel.RecentPageEntry> RecentPageItems { get; } = [];
    internal ObservableCollection<MainPageViewModel.WorkspaceTileItem> WorkspaceTileItems { get; } = [];
    internal ObservableCollection<WorkstationOperatingContext> OperatingContextItems { get; } = [];
    internal ObservableCollection<BoundedWindowMode> WindowModeItems { get; }

    internal MainPageNavigationSectionViewModel()
    {
        WindowModeItems = [BoundedWindowMode.Focused, BoundedWindowMode.DockFloat, BoundedWindowMode.WorkbenchPreset];
        PrimaryNavigationItems = new ReadOnlyObservableCollection<ShellNavigationItem>(PrimaryItems);
        SecondaryNavigationItems = new ReadOnlyObservableCollection<ShellNavigationItem>(SecondaryItems);
        OverflowNavigationItems = new ReadOnlyObservableCollection<ShellNavigationItem>(OverflowItems);
        RelatedWorkflowItems = new ReadOnlyObservableCollection<ShellNavigationItem>(RelatedWorkflowNavItems);
        RecentPages = new ReadOnlyObservableCollection<MainPageViewModel.RecentPageEntry>(RecentPageItems);
        WorkspaceTiles = new ReadOnlyObservableCollection<MainPageViewModel.WorkspaceTileItem>(WorkspaceTileItems);
        OperatingContexts = new ReadOnlyObservableCollection<WorkstationOperatingContext>(OperatingContextItems);
        WindowModes = new ReadOnlyObservableCollection<BoundedWindowMode>(WindowModeItems);
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
    private readonly ObservableCollection<MainPageViewModel.PrimaryOperatorWorkflowStep> _primaryOperatorWorkflowStepItems = [];

    public MainPageWorkflowSectionViewModel(
        CommandPaletteViewModel commandPalette,
        OperatorInboxViewModel operatorInbox,
        WorkflowSummaryStripViewModel workflowSummaryStrip)
    {
        CommandPalette = commandPalette;
        OperatorInbox = operatorInbox;
        WorkflowSummaryStrip = workflowSummaryStrip;
        PrimaryOperatorWorkflowSteps = new ReadOnlyObservableCollection<MainPageViewModel.PrimaryOperatorWorkflowStep>(_primaryOperatorWorkflowStepItems);
    }

    public CommandPaletteViewModel CommandPalette { get; }
    public OperatorInboxViewModel OperatorInbox { get; }
    public WorkflowSummaryStripViewModel WorkflowSummaryStrip { get; }

    public ReadOnlyObservableCollection<ShellCommandPaletteEntry> CommandPalettePages => CommandPalette.Pages;
    public ReadOnlyObservableCollection<WorkspaceWorkflowSummary> WorkflowSummaries => WorkflowSummaryStrip.Summaries;
    public ReadOnlyObservableCollection<WorkspaceWorkflowSummary> SecondaryWorkflowSummaries => WorkflowSummaryStrip.SecondarySummaries;
    public ReadOnlyObservableCollection<MainPageViewModel.PrimaryOperatorWorkflowStep> PrimaryOperatorWorkflowSteps { get; }

    internal void ReplacePrimaryOperatorWorkflowSteps(IReadOnlyCollection<MainPageViewModel.PrimaryOperatorWorkflowStep> steps)
    {
        _primaryOperatorWorkflowStepItems.Clear();
        foreach (var step in steps)
        {
            _primaryOperatorWorkflowStepItems.Add(step);
        }
    }
}

internal sealed class MainPageChromeSectionViewModel : BindableBase
{
    private Visibility _backButtonVisibility = Visibility.Collapsed;
    private Visibility _recentPagesEmptyVisibility = Visibility.Visible;
    private Visibility _fixtureModeBannerVisibility = Visibility.Collapsed;
    private string _fixtureModeBannerText = string.Empty;
    private string _activeFundName = "Select Fund";
    private string _activeFundSubtitle = "Fund context required";
    private Visibility _activeFundVisibility = Visibility.Collapsed;

    public Visibility BackButtonVisibility
    {
        get => _backButtonVisibility;
        set => SetProperty(ref _backButtonVisibility, value);
    }

    public Visibility RecentPagesEmptyVisibility
    {
        get => _recentPagesEmptyVisibility;
        set => SetProperty(ref _recentPagesEmptyVisibility, value);
    }

    public Visibility FixtureModeBannerVisibility
    {
        get => _fixtureModeBannerVisibility;
        set => SetProperty(ref _fixtureModeBannerVisibility, value);
    }

    public string FixtureModeBannerText
    {
        get => _fixtureModeBannerText;
        set => SetProperty(ref _fixtureModeBannerText, value);
    }

    public string ActiveFundName
    {
        get => _activeFundName;
        set => SetProperty(ref _activeFundName, value);
    }

    public string ActiveFundSubtitle
    {
        get => _activeFundSubtitle;
        set => SetProperty(ref _activeFundSubtitle, value);
    }

    public Visibility ActiveFundVisibility
    {
        get => _activeFundVisibility;
        set => SetProperty(ref _activeFundVisibility, value);
    }
}
