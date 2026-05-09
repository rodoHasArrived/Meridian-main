using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Meridian.Ui.Shared.Workflows;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Shell.ViewModels;

public sealed class WorkflowSummaryStripViewModel : BindableBase
{
    private readonly ObservableCollection<WorkspaceWorkflowSummary> _summaries = [];
    private readonly ObservableCollection<WorkspaceWorkflowSummary> _secondarySummaries = [];
    private WorkspaceWorkflowSummary? _primarySummary;
    private bool _areSecondarySummariesExpanded;

    public WorkflowSummaryStripViewModel()
    {
        Summaries = new ReadOnlyObservableCollection<WorkspaceWorkflowSummary>(_summaries);
        SecondarySummaries = new ReadOnlyObservableCollection<WorkspaceWorkflowSummary>(_secondarySummaries);
    }

    public ReadOnlyObservableCollection<WorkspaceWorkflowSummary> Summaries { get; }

    public ReadOnlyObservableCollection<WorkspaceWorkflowSummary> SecondarySummaries { get; }

    public WorkspaceWorkflowSummary? PrimarySummary
    {
        get => _primarySummary;
        private set
        {
            if (!SetProperty(ref _primarySummary, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(HasPrimarySummary));
            RaisePropertyChanged(nameof(PrimaryTargetText));
            RaisePropertyChanged(nameof(PrimaryDetailVisibility));
        }
    }

    public bool HasPrimarySummary => PrimarySummary is not null;

    public string PrimaryTargetText => PrimarySummary is null
        ? "Target page: -"
        : $"Target page: {PrimarySummary.NextAction.TargetPageTag}";

    public bool HasSecondarySummaries => _secondarySummaries.Count > 0;

    public bool AreSecondarySummariesExpanded
    {
        get => _areSecondarySummariesExpanded;
        private set
        {
            if (!SetProperty(ref _areSecondarySummariesExpanded, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(SecondarySummariesVisibility));
            RaisePropertyChanged(nameof(SecondaryToggleText));
            RaisePropertyChanged(nameof(PrimaryDetailVisibility));
        }
    }

    public Visibility SecondarySummariesVisibility => HasSecondarySummaries && AreSecondarySummariesExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SecondaryToggleText => AreSecondarySummariesExpanded
        ? $"Hide {SecondarySummaries.Count} other workspace action{(SecondarySummaries.Count == 1 ? string.Empty : "s")}"
        : $"Show {SecondarySummaries.Count} other workspace action{(SecondarySummaries.Count == 1 ? string.Empty : "s")}";

    public Visibility PrimaryDetailVisibility => ShouldShowPrimaryDetail(PrimarySummary)
        || AreSecondarySummariesExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void Apply(IReadOnlyCollection<WorkspaceWorkflowSummary> summaries, string currentWorkspace)
    {
        ReplaceCollection(_summaries, summaries);
        UpdatePrimary(currentWorkspace);
    }

    public void UpdatePrimary(string currentWorkspace)
    {
        var primary = _summaries.FirstOrDefault(summary =>
            string.Equals(summary.WorkspaceId, currentWorkspace, StringComparison.OrdinalIgnoreCase))
            ?? _summaries.FirstOrDefault();

        PrimarySummary = primary;

        ReplaceCollection(
            _secondarySummaries,
            _summaries
                .Where(summary => primary is null || !string.Equals(summary.WorkspaceId, primary.WorkspaceId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        if (!HasSecondarySummaries)
        {
            AreSecondarySummariesExpanded = false;
        }

        RaisePropertyChanged(nameof(HasSecondarySummaries));
        RaisePropertyChanged(nameof(SecondarySummariesVisibility));
        RaisePropertyChanged(nameof(SecondaryToggleText));
    }

    public void ToggleSecondarySummaries()
    {
        if (!HasSecondarySummaries)
        {
            return;
        }

        AreSecondarySummariesExpanded = !AreSecondarySummariesExpanded;
    }

    private static bool ShouldShowPrimaryDetail(WorkspaceWorkflowSummary? summary)
        => summary is not null
            && (summary.PrimaryBlocker.IsBlocking
                || IsAttentionTone(summary.StatusTone)
                || IsAttentionTone(summary.PrimaryBlocker.Tone));

    private static bool IsAttentionTone(string? tone)
        => string.Equals(tone, WorkspaceTone.Warning, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tone, WorkspaceTone.Danger, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tone, "Critical", StringComparison.OrdinalIgnoreCase);

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyCollection<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
