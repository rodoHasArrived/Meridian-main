using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class BackfillWorkbenchSectionViewModel : BindableBase
{
    private string _backfillStatusText = string.Empty;
    private string _overallProgressText = string.Empty;
    private string _pauseButtonContent = "Pause";
    private bool _isProgressVisible;
    private bool _hasNoScheduledJobs = true;
    private bool _hasNoResumableJobs = true;
    private string _gapAnalysisSummaryText = string.Empty;
    private string _gapActionHintText = string.Empty;
    private bool _isGapAnalysisCardVisible;
    private bool _isGapListVisible;
    private bool _isGapActionPanelVisible;

    public ObservableCollection<SymbolProgressInfo> SymbolProgress { get; } = new();
    public ObservableCollection<ScheduledJobInfo> ScheduledJobs { get; } = new();
    public ObservableCollection<ResumableJobInfo> ResumableJobs { get; } = new();
    public ObservableCollection<GapAnalysisItem> GapItems { get; } = new();

    public string BackfillStatusText { get => _backfillStatusText; set => SetProperty(ref _backfillStatusText, value); }
    public string OverallProgressText { get => _overallProgressText; set => SetProperty(ref _overallProgressText, value); }
    public string PauseButtonContent { get => _pauseButtonContent; set => SetProperty(ref _pauseButtonContent, value); }
    public bool IsProgressVisible { get => _isProgressVisible; set => SetProperty(ref _isProgressVisible, value); }
    public bool HasNoScheduledJobs { get => _hasNoScheduledJobs; set => SetProperty(ref _hasNoScheduledJobs, value); }
    public bool HasNoResumableJobs { get => _hasNoResumableJobs; set => SetProperty(ref _hasNoResumableJobs, value); }
    public string GapAnalysisSummaryText { get => _gapAnalysisSummaryText; set => SetProperty(ref _gapAnalysisSummaryText, value); }
    public string GapActionHintText { get => _gapActionHintText; set => SetProperty(ref _gapActionHintText, value); }
    public bool IsGapAnalysisCardVisible { get => _isGapAnalysisCardVisible; set => SetProperty(ref _isGapAnalysisCardVisible, value); }
    public bool IsGapListVisible { get => _isGapListVisible; set => SetProperty(ref _isGapListVisible, value); }
    public bool IsGapActionPanelVisible { get => _isGapActionPanelVisible; set => SetProperty(ref _isGapActionPanelVisible, value); }
}

public sealed partial class BackfillViewModel
{
    private readonly BackfillWorkbenchSectionViewModel _workbenchSection = new();

    public BackfillWorkbenchSectionViewModel WorkbenchSection => _workbenchSection;

    private bool SetBackfillSectionProperty<T>(
        T current,
        Action<T> apply,
        T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        apply(value);
        RaisePropertyChanged(propertyName);
        return true;
    }
}
