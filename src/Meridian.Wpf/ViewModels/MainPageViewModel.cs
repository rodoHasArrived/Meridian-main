using System;
using System.Collections.Generic;
using Meridian.Wpf.Contracts;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the main shell page. Coordinates workspace navigation and split-pane layout.
/// </summary>
public sealed class MainPageViewModel : BindableBase
{
    private readonly INavigationService _navigationService;

    private const string DefaultWorkspace = "research";

    private string _currentWorkspace = DefaultWorkspace;
    private string _currentPageTag = "Dashboard";
    private bool _tickerStripVisible;

    private static readonly IReadOnlyDictionary<string, WorkspaceContent> WorkspaceData =
        new Dictionary<string, WorkspaceContent>(StringComparer.OrdinalIgnoreCase)
        {
            ["research"] = new("Research", "Runs, charts, replay, and analysis flows.", "Focus on model exploration and investigation."),
            ["trading"] = new("Trading", "Live monitoring, order flow, and execution tools.", "Focus on live market posture and execution."),
            ["data-operations"] = new("Data Operations", "Providers, symbols, backfills, and storage.", "Focus on data health and ingestion operations."),
            ["governance"] = new("Governance", "Quality, diagnostics, and policy controls.", "Focus on controls, diagnostics, and trust.")
        };

    /// <summary>Gets the split-pane layout view model.</summary>
    public SplitPaneViewModel SplitPane { get; } = new();

    // ── Workspace state ───────────────────────────────────────────────────

    /// <summary>Gets or sets the active workspace key (e.g. "research", "trading").</summary>
    public string CurrentWorkspace
    {
        get => _currentWorkspace;
        set
        {
            var normalized = WorkspaceData.ContainsKey(value) ? value : DefaultWorkspace;
            if (SetProperty(ref _currentWorkspace, normalized))
            {
                RaisePropertyChanged(nameof(WorkspaceHeading));
                RaisePropertyChanged(nameof(WorkspaceDescription));
                RaisePropertyChanged(nameof(WorkspaceSummary));
            }
        }
    }

    /// <summary>Gets the heading text for the active workspace.</summary>
    public string WorkspaceHeading => WorkspaceData[_currentWorkspace].Heading;

    /// <summary>Gets the description text for the active workspace.</summary>
    public string WorkspaceDescription => WorkspaceData[_currentWorkspace].Description;

    /// <summary>Gets the summary text for the active workspace.</summary>
    public string WorkspaceSummary => WorkspaceData[_currentWorkspace].Summary;

    // ── Navigation state ─────────────────────────────────────────────────

    /// <summary>Gets or sets the page tag of the currently displayed page.</summary>
    public string CurrentPageTag
    {
        get => _currentPageTag;
        set => SetProperty(ref _currentPageTag, value);
    }

    // ── Ticker strip state ───────────────────────────────────────────────

    /// <summary>Gets or sets whether the ticker strip overlay is visible.</summary>
    public bool TickerStripVisible
    {
        get => _tickerStripVisible;
        set
        {
            if (SetProperty(ref _tickerStripVisible, value))
                RaisePropertyChanged(nameof(TickerStripLabel));
        }
    }

    /// <summary>Gets the label text for the ticker strip toggle button.</summary>
    public string TickerStripLabel => _tickerStripVisible ? "Hide Ticker Strip" : "Ticker Strip";

    public MainPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private sealed record WorkspaceContent(string Heading, string Description, string Summary);
}
