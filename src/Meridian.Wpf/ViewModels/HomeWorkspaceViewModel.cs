using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class HomeWorkspaceViewModel : BindableBase
{
    private readonly INavigationService _navigationService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private readonly FundContextService? _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly ObservableCollection<HomeWorkspaceSectionViewModel> _sections = [];
    private readonly ObservableCollection<HomeWorkspaceNavigationItem> _workspaceNavigationItems = [];
    private readonly ObservableCollection<HomeWorkspaceActivityItem> _recentActivity = [];
    private string _summaryText = "Review operational readiness before opening a deep workspace.";
    private string _generatedAtText = "Not refreshed";
    private bool _isLoading;

    public HomeWorkspaceViewModel(
        INavigationService navigationService,
        WorkstationWorkflowSummaryService? workflowSummaryService = null,
        FundContextService? fundContextService = null,
        WorkstationOperatingContextService? operatingContextService = null)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _workflowSummaryService = workflowSummaryService;
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;

        Sections = new ReadOnlyObservableCollection<HomeWorkspaceSectionViewModel>(_sections);
        WorkspaceNavigationItems = new ReadOnlyObservableCollection<HomeWorkspaceNavigationItem>(_workspaceNavigationItems);
        RecentActivity = new ReadOnlyObservableCollection<HomeWorkspaceActivityItem>(_recentActivity);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);

        foreach (var item in BuildWorkspaceNavigationItems(_navigationService))
        {
            _workspaceNavigationItems.Add(item);
        }

        ApplySummary(BuildFallbackSummary());
    }

    public ReadOnlyObservableCollection<HomeWorkspaceSectionViewModel> Sections { get; }

    public ReadOnlyObservableCollection<HomeWorkspaceNavigationItem> WorkspaceNavigationItems { get; }

    public ReadOnlyObservableCollection<HomeWorkspaceActivityItem> RecentActivity { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string GeneratedAtText
    {
        get => _generatedAtText;
        private set => SetProperty(ref _generatedAtText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            ApplySummary(_workflowSummaryService is null
                ? BuildFallbackSummary()
                : await LoadSharedSummaryAsync(ct).ConfigureAwait(true));
        }
        catch
        {
            ApplySummary(BuildFallbackSummary("Shared home summary unavailable; deterministic launch guidance is shown."));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public static IReadOnlyList<HomeWorkspaceSectionViewModel> BuildSections(IReadOnlyList<WorkspaceWorkflowSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return new[]
        {
            CreateSection("Provider health", "Provider and service degradation that can block acquisition or execution.", "ProviderHealth", summaries, "data", "trading"),
            CreateSection("Data freshness", "Acquisition, validation, and source-evidence currency before downstream work.", "DataQuality", summaries, "data"),
            CreateSection("Reconciliation", "Cash, position, trade, and ledger breaks requiring investigation.", "FundReconciliation", summaries, "accounting", "portfolio"),
            CreateSection("Approvals", "Human review and sign-off queues before records become governed outputs.", "AccountingApprovals", summaries, "accounting", "trading"),
            CreateSection("Accounting/reporting readiness", "Close package, ledger evidence, and report-pack publication posture.", "FundReportPack", summaries, "accounting", "reporting"),
            CreateSection("Recent activity", "Strategy, trading, settings, and operator workflow changes worth reviewing.", "StrategyShell", summaries, "strategy", "settings")
        };
    }

    private async Task<OperatorWorkflowHomeSummary> LoadSharedSummaryAsync(CancellationToken ct)
    {
        var operatingContext = _operatingContextService?.CurrentContext;
        var fund = _fundContextService?.CurrentFundProfile;
        return await _workflowSummaryService!.GetAsync(
            hasOperatingContext: operatingContext is not null || fund is not null,
            operatingContextDisplayName: operatingContext?.DisplayName,
            fundProfileId: fund?.FundProfileId,
            fundAccountId: WorkstationOperatingContextScopeResolver.ResolveFundAccountIdString(operatingContext),
            fundDisplayName: fund?.DisplayName,
            ct: ct).ConfigureAwait(false);
    }

    private OperatorWorkflowHomeSummary BuildFallbackSummary(string? detail = null)
    {
        var summaries = ShellNavigationCatalog.Workspaces.Select(workspace => new WorkspaceWorkflowSummary(
            workspace.Id,
            workspace.Title,
            "Launch ready",
            detail ?? $"Open {workspace.Title} when this home checkpoint indicates your next task.",
            WorkspaceTone.Info,
            new WorkflowNextAction($"Open {workspace.Title}", workspace.Description, workspace.HomePageTag, WorkspaceTone.Primary),
            new WorkflowBlockerSummary("home-fallback", "Shared summary pending", "Refresh when services finish starting to load source-backed workflow posture.", WorkspaceTone.Info, false),
            [])).ToArray();

        return new OperatorWorkflowHomeSummary(DateTimeOffset.UtcNow, false, "Startup", "No fund scope selected", summaries);
    }

    private void ApplySummary(OperatorWorkflowHomeSummary summary)
    {
        SummaryText = $"{summary.OperatingContextLabel} • {summary.FundDisplayName} • {summary.AssuranceScore.Status} ({summary.AssuranceScore.Score}/100)";
        GeneratedAtText = $"Updated {summary.GeneratedAt.LocalDateTime:g}";

        _sections.Clear();
        foreach (var section in BuildSections(summary.Workspaces))
        {
            _sections.Add(section);
        }

        _recentActivity.Clear();
        foreach (var item in summary.Workspaces
            .OrderByDescending(static workspace => SeverityRank(workspace.StatusTone))
            .ThenBy(static workspace => workspace.WorkspaceTitle, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(static workspace => new HomeWorkspaceActivityItem(
                workspace.WorkspaceTitle,
                workspace.StatusLabel,
                workspace.StatusDetail,
                workspace.StatusTone)))
        {
            _recentActivity.Add(item);
        }
    }

    private static HomeWorkspaceSectionViewModel CreateSection(
        string title,
        string description,
        string targetPageTag,
        IReadOnlyList<WorkspaceWorkflowSummary> summaries,
        params string[] workspaceIds)
    {
        var selected = summaries
            .Where(summary => workspaceIds.Contains(summary.WorkspaceId, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(static summary => SeverityRank(summary.PrimaryBlocker.Tone))
            .ThenByDescending(static summary => SeverityRank(summary.StatusTone))
            .ThenBy(static summary => summary.WorkspaceTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<HomeWorkspaceSummaryItem> items = selected.Length == 0
            ? new[] { new HomeWorkspaceSummaryItem("No source summary", "Shared workflow summary has not produced this section yet.", WorkspaceTone.Neutral, "Unmeasured", false) }
            : selected.Select(static summary => new HomeWorkspaceSummaryItem(
                summary.WorkspaceTitle,
                summary.PrimaryBlocker.IsBlocking ? summary.PrimaryBlocker.Detail : summary.StatusDetail,
                HighestSeverityTone(summary.StatusTone, summary.PrimaryBlocker.Tone),
                summary.PrimaryBlocker.IsBlocking ? summary.PrimaryBlocker.Label : summary.StatusLabel,
                summary.PrimaryBlocker.IsBlocking)).ToArray();

        return new HomeWorkspaceSectionViewModel(title, description, targetPageTag, items);
    }

    private static IReadOnlyList<HomeWorkspaceNavigationItem> BuildWorkspaceNavigationItems(INavigationService navigationService)
        => ShellNavigationCatalog.Workspaces
            .Select(workspace => new HomeWorkspaceNavigationItem(
                workspace.Title,
                workspace.Summary,
                workspace.HomePageTag,
                new RelayCommand(() => navigationService.NavigateTo(workspace.HomePageTag))))
            .ToArray();

    private static string HighestSeverityTone(string left, string right)
        => SeverityRank(left) >= SeverityRank(right) ? left : right;

    private static int SeverityRank(string? tone) => tone switch
    {
        WorkspaceTone.Danger => 4,
        WorkspaceTone.Warning => 3,
        WorkspaceTone.Info => 2,
        WorkspaceTone.Success => 1,
        _ => 0
    };
}

public sealed record HomeWorkspaceSectionViewModel(
    string Title,
    string Description,
    string TargetPageTag,
    IReadOnlyList<HomeWorkspaceSummaryItem> Items);

public sealed record HomeWorkspaceSummaryItem(
    string Title,
    string Detail,
    string Tone,
    string StatusLabel,
    bool IsBlocking);

public sealed record HomeWorkspaceNavigationItem(
    string Title,
    string Detail,
    string TargetPageTag,
    IRelayCommand NavigateCommand);

public sealed record HomeWorkspaceActivityItem(
    string WorkspaceTitle,
    string Label,
    string Detail,
    string Tone);
