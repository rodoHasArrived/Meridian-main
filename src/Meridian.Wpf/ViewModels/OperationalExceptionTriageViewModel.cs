using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class OperationalExceptionTriageViewModel : BindableBase
{
    private readonly IOperationalExceptionTriageService _service;
    private readonly List<OperationalExceptionRecord> _allExceptions;
    private string _workspaceFilter = "All";
    private string _categoryFilter = "All";
    private OperationalExceptionRecord? _selectedException;

    public OperationalExceptionTriageViewModel(IOperationalExceptionTriageService service)
    {
        _service = service;
        _allExceptions = _service.GetExceptions()
            .OrderBy(static exception => SeverityRank(exception.Severity))
            .ThenBy(static exception => exception.Status == OperationalExceptionStatus.Resolved)
            .ThenBy(static exception => exception.Id)
            .ToList();

        WorkspaceFilters = ["All", .. _allExceptions.Select(static exception => exception.SourceWorkspace).Distinct().Order()];
        CategoryFilters = ["All", "Reconciliation", "Provider/Data quality", "Operational exception"];
        ApplyFilters();
    }

    public ObservableCollection<OperationalExceptionRecord> Exceptions { get; } = new();

    public IReadOnlyList<string> WorkspaceFilters { get; }

    public IReadOnlyList<string> CategoryFilters { get; }

    public string WorkspaceFilter
    {
        get => _workspaceFilter;
        set
        {
            if (SetProperty(ref _workspaceFilter, string.IsNullOrWhiteSpace(value) ? "All" : value))
            {
                ApplyFilters();
            }
        }
    }

    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (SetProperty(ref _categoryFilter, string.IsNullOrWhiteSpace(value) ? "All" : value))
            {
                ApplyFilters();
            }
        }
    }

    public OperationalExceptionRecord? SelectedException
    {
        get => _selectedException;
        set
        {
            if (SetProperty(ref _selectedException, value))
            {
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(SelectedAuditHistory));
            }
        }
    }

    public bool HasSelection => SelectedException is not null;

    public IReadOnlyList<OperationalExceptionAuditEntry> SelectedAuditHistory =>
        SelectedException?.AuditHistory.OrderByDescending(static entry => entry.Timestamp).ToArray()
        ?? [];

    public string QueueSummary => $"{Exceptions.Count} operational exceptions in scope; {Exceptions.Count(static e => e.Status != OperationalExceptionStatus.Resolved)} open.";

    public void ApplyDeepLinkFilter(string? sourceWorkspace = null, string? category = null)
    {
        WorkspaceFilter = string.IsNullOrWhiteSpace(sourceWorkspace) ? "All" : sourceWorkspace;
        CategoryFilter = string.IsNullOrWhiteSpace(category) ? "All" : category;
    }

    private static int SeverityRank(OperationalExceptionSeverity severity)
        => severity switch
        {
            OperationalExceptionSeverity.Critical => 0,
            OperationalExceptionSeverity.High => 1,
            OperationalExceptionSeverity.Medium => 2,
            _ => 3
        };

    private void ApplyFilters()
    {
        var selectedId = SelectedException?.Id;
        var filtered = _allExceptions.Where(exception =>
                (WorkspaceFilter == "All" || exception.SourceWorkspace == WorkspaceFilter) &&
                (CategoryFilter == "All" || exception.SourceCategory == CategoryFilter))
            .ToList();

        Exceptions.Clear();
        foreach (var exception in filtered)
        {
            Exceptions.Add(exception);
        }

        SelectedException = Exceptions.FirstOrDefault(exception => exception.Id == selectedId) ?? Exceptions.FirstOrDefault();
        RaisePropertyChanged(nameof(QueueSummary));
    }
}
