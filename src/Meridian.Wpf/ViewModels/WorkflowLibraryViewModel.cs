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
    private string _emptyStateText = "No workflows match the current filter.";

    public WorkflowLibraryViewModel(
        WorkflowLibraryService workflowLibraryService,
        NavigationService navigationService)
    {
        _workflowLibraryService = workflowLibraryService ?? throw new ArgumentNullException(nameof(workflowLibraryService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        Workflows = new ReadOnlyObservableCollection<WorkflowLibraryWorkflowItem>(_workflows);
        OpenWorkflowCommand = new RelayCommand<string>(OpenWorkflow, CanOpenWorkflow);
        RefreshCommand = new RelayCommand(Load);
        Load();
    }

    public ReadOnlyObservableCollection<WorkflowLibraryWorkflowItem> Workflows { get; }

    public RelayCommand<string> OpenWorkflowCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string EmptyStateText
    {
        get => _emptyStateText;
        private set => SetProperty(ref _emptyStateText, value);
    }

    public bool HasWorkflows => _workflows.Count > 0;

    public void Load()
    {
        var library = _workflowLibraryService.GetLibrary();
        _allWorkflows.Clear();
        _allWorkflows.AddRange(library.Workflows.Select(static workflow => new WorkflowLibraryWorkflowItem(workflow)));
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

        EmptyStateText = string.IsNullOrWhiteSpace(query)
            ? "No workflows are registered."
            : "No workflows match the current filter.";
        RaisePropertyChanged(nameof(HasWorkflows));
        OpenWorkflowCommand.NotifyCanExecuteChanged();
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
}

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
