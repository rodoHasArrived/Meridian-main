using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Presentation model for reusable workstation workflows.
/// </summary>
public sealed class WorkflowLibraryViewModel : BindableBase
{
    private readonly WorkflowLibraryService _workflowLibraryService;
    private readonly NavigationService _navigationService;
    private readonly List<WorkflowLibraryWorkflowItem> _allWorkflows = [];
    private readonly ObservableCollection<WorkflowLibraryWorkflowItem> _workflows = [];
    private string _searchQuery = string.Empty;
    private string _summaryText = string.Empty;
    private string _resultScopeText = "No workflows loaded";
    private string _emptyStateTitle = "No workflows are registered";
    private string _emptyStateDetail = "Refresh the library after workflow providers are available.";
    private string _emptyStateActionText = "Refresh library";
    private int _totalActionCount;

    public WorkflowLibraryViewModel(
        WorkflowLibraryService workflowLibraryService,
        NavigationService navigationService)
    {
        _workflowLibraryService = workflowLibraryService ?? throw new ArgumentNullException(nameof(workflowLibraryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        Workflows = new ReadOnlyObservableCollection<WorkflowLibraryWorkflowItem>(_workflows);
        OpenWorkflowCommand = new RelayCommand<string>(OpenWorkflow, CanOpenWorkflow);
        RefreshCommand = new RelayCommand(Load);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => HasActiveSearch);
        Load();
    }

    public ReadOnlyObservableCollection<WorkflowLibraryWorkflowItem> Workflows { get; }

    public RelayCommand<string> OpenWorkflowCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HasActiveSearch));
                ClearSearchCommand.NotifyCanExecuteChanged();
                ApplyFilter();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string ResultScopeText
    {
        get => _resultScopeText;
        private set => SetProperty(ref _resultScopeText, value);
    }

    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetProperty(ref _emptyStateTitle, value);
    }

    public string EmptyStateDetail
    {
        get => _emptyStateDetail;
        private set => SetProperty(ref _emptyStateDetail, value);
    }

    public string EmptyStateActionText
    {
        get => _emptyStateActionText;
        private set => SetProperty(ref _emptyStateActionText, value);
    }

    public bool HasWorkflows => _workflows.Count > 0;

    public bool HasActiveSearch => !string.IsNullOrWhiteSpace(SearchQuery);

    public void Load()
    {
        var library = _workflowLibraryService.GetLibrary();
        _allWorkflows.Clear();
        _allWorkflows.AddRange(library.Workflows.Select(static workflow => new WorkflowLibraryWorkflowItem(workflow)));
        _totalActionCount = library.Actions.Count;
        SummaryText = $"{library.Workflows.Count} workflows / {library.Actions.Count} actions";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchQuery.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allWorkflows
            : _allWorkflows
                .Where(item => item.Matches(query))
                .ToList();

        _workflows.Clear();
        foreach (var item in filtered)
        {
            _workflows.Add(item);
        }

        var presentation = BuildFilterPresentation(_allWorkflows.Count, _workflows.Count, _totalActionCount, query);
        ResultScopeText = presentation.ResultScopeText;
        EmptyStateTitle = presentation.EmptyStateTitle;
        EmptyStateDetail = presentation.EmptyStateDetail;
        EmptyStateActionText = presentation.EmptyStateActionText;
        RaisePropertyChanged(nameof(HasWorkflows));
        OpenWorkflowCommand.NotifyCanExecuteChanged();
    }

    public static WorkflowLibraryFilterPresentation BuildFilterPresentation(
        int totalWorkflowCount,
        int visibleWorkflowCount,
        int totalActionCount,
        string? searchQuery)
    {
        var query = (searchQuery ?? string.Empty).Trim();
        var hasQuery = query.Length > 0;
        var workflowText = FormatCount(totalWorkflowCount, "workflow", "workflows");
        var visibleText = FormatCount(visibleWorkflowCount, "workflow", "workflows");
        var actionText = FormatCount(totalActionCount, "action", "actions");

        if (totalWorkflowCount == 0)
        {
            return new WorkflowLibraryFilterPresentation(
                $"0 workflows / {actionText}",
                "No workflows are registered",
                "Refresh the library after workflow providers are available.",
                "Refresh library");
        }

        if (visibleWorkflowCount == 0 && hasQuery)
        {
            return new WorkflowLibraryFilterPresentation(
                $"0 of {workflowText} match \"{Shorten(query, 32)}\"",
                "No workflows match the current filter",
                "Clear the search or try a workspace, evidence tag, market pattern, or action name.",
                "Clear search");
        }

        if (hasQuery)
        {
            return new WorkflowLibraryFilterPresentation(
                $"{visibleText} of {workflowText} match \"{Shorten(query, 32)}\"",
                "No workflows match the current filter",
                "Clear the search or try a workspace, evidence tag, market pattern, or action name.",
                "Clear search");
        }

        return new WorkflowLibraryFilterPresentation(
            $"Showing {workflowText} / {actionText}",
            "No workflows are registered",
            "Refresh the library after workflow providers are available.",
            "Refresh library");
    }

    private void OpenWorkflow(string? pageTag)
    {
        if (!CanOpenWorkflow(pageTag))
        {
            return;
        }

        _navigationService.NavigateTo(pageTag!);
    }

    private static bool CanOpenWorkflow(string? pageTag)
        => !string.IsNullOrWhiteSpace(pageTag);

    private void ClearSearch()
    {
        if (!HasActiveSearch)
        {
            return;
        }

        SearchQuery = string.Empty;
    }

    private static string FormatCount(int value, string singular, string plural)
        => $"{value} {(value == 1 ? singular : plural)}";

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 1)] + "...";
    }
}

public readonly record struct WorkflowLibraryFilterPresentation(
    string ResultScopeText,
    string EmptyStateTitle,
    string EmptyStateDetail,
    string EmptyStateActionText);

public sealed class WorkflowLibraryWorkflowItem
{
    public WorkflowLibraryWorkflowItem(WorkflowDefinitionDto definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Actions = definition.Actions.Select(static action => new WorkflowLibraryActionItem(action)).ToArray();
        EvidenceText = definition.EvidenceTags.Count == 0
            ? "No evidence tags"
            : string.Join(", ", definition.EvidenceTags);
        MarketPatternText = definition.MarketPatternTags.Count == 0
            ? "No market pattern tags"
            : string.Join(", ", definition.MarketPatternTags);
    }

    public WorkflowDefinitionDto Definition { get; }

    public IReadOnlyList<WorkflowLibraryActionItem> Actions { get; }

    public string WorkflowId => Definition.WorkflowId;

    public string Title => Definition.Title;

    public string Summary => Definition.Summary;

    public string WorkspaceTitle => Definition.WorkspaceTitle;

    public string EntryPageTag => Definition.EntryPageTag;

    public string Tone => Definition.Tone;

    public string ActionCountText => $"{Definition.Actions.Count} action(s)";

    public string EvidenceText { get; }

    public string MarketPatternText { get; }

    public bool Matches(string query)
        => Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           WorkspaceTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           WorkflowId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           EvidenceText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           MarketPatternText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           Actions.Any(action => action.Matches(query));
}

public sealed class WorkflowLibraryActionItem
{
    public WorkflowLibraryActionItem(WorkflowActionDto action)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public WorkflowActionDto Action { get; }

    public string ActionId => Action.ActionId;

    public string Label => Action.Label;

    public string Detail => Action.Detail;

    public string TargetPageTag => Action.TargetPageTag;

    public string TargetText => $"Open {Action.TargetPageTag}";

    public bool Matches(string query)
        => Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           Detail.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           TargetPageTag.Contains(query, StringComparison.OrdinalIgnoreCase) ||
           ActionId.Contains(query, StringComparison.OrdinalIgnoreCase);
}
