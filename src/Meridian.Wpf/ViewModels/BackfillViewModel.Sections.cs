using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class BackfillWorkbenchSectionViewModel : BindableBase
{
    private string _backfillStatusText = string.Empty;
    private string _overallProgressText = string.Empty;
    private double _overallProgressPercent;
    private string _pauseButtonContent = "Pause";
    private bool _isProgressVisible;
    private bool _hasProviderProgress;
    private bool _hasRemediationQueue;
    private string _providerProgressObservedText = "No provider-attempt snapshot loaded";
    private string _remediationDefaultProviderText = "Automatic remediation default: stooq";
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
    public ObservableCollection<BackfillRemediationQueuePresentation> RemediationQueue { get; } = new();
    public WorkstationTableModel<SymbolProgressInfo> SymbolProgressTable { get; }
    public WorkstationTableModel<GapAnalysisItem> GapItemsTable { get; }

    public BackfillWorkbenchSectionViewModel()
    {
        SymbolProgressTable = new WorkstationTableModel<SymbolProgressInfo>(
            SymbolProgress,
            [
                new("Symbol", nameof(SymbolProgressInfo.Symbol), 90),
                new("Range", nameof(SymbolProgressInfo.RangeText), 172),
                new("Provider", nameof(SymbolProgressInfo.CurrentProvider), 96),
                new("Fallback Attempt", nameof(SymbolProgressInfo.FallbackAttemptText), 132),
                new("Retry", nameof(SymbolProgressInfo.RetryText), 76),
                new("Progress", nameof(SymbolProgressInfo.ProgressText), 82),
                new("Bars", nameof(SymbolProgressInfo.BarsText), 96),
                new("Live State", nameof(SymbolProgressInfo.StatusText), 100),
                new("Updated", nameof(SymbolProgressInfo.TimeText), 110),
                new("Error", nameof(SymbolProgressInfo.ErrorText), 180)
            ],
            "Backfill per-symbol progress",
            "No active symbols",
            "Start a backfill run to populate per-symbol progress.");

        GapItemsTable = new WorkstationTableModel<GapAnalysisItem>(
            GapItems,
            [
                new("Symbol", nameof(GapAnalysisItem.Symbol), 90),
                new("Coverage", nameof(GapAnalysisItem.CoverageText), 100),
                new("Gap Days", nameof(GapAnalysisItem.GapDaysText), 120),
                new("Coverage %", nameof(GapAnalysisItem.CoveragePercent), 100)
            ],
            "Backfill gap analysis",
            "No gap scan loaded",
            "Scan selected symbols to preview coverage gaps.");
    }

    public string BackfillStatusText { get => _backfillStatusText; set => SetProperty(ref _backfillStatusText, value); }
    public string OverallProgressText { get => _overallProgressText; set => SetProperty(ref _overallProgressText, value); }
    public double OverallProgressPercent { get => _overallProgressPercent; set => SetProperty(ref _overallProgressPercent, value); }
    public string PauseButtonContent { get => _pauseButtonContent; set => SetProperty(ref _pauseButtonContent, value); }
    public bool IsProgressVisible { get => _isProgressVisible; set => SetProperty(ref _isProgressVisible, value); }
    public bool HasProviderProgress { get => _hasProviderProgress; set => SetProperty(ref _hasProviderProgress, value); }
    public bool HasRemediationQueue { get => _hasRemediationQueue; set => SetProperty(ref _hasRemediationQueue, value); }
    public string ProviderProgressObservedText { get => _providerProgressObservedText; set => SetProperty(ref _providerProgressObservedText, value); }
    public string RemediationDefaultProviderText { get => _remediationDefaultProviderText; set => SetProperty(ref _remediationDefaultProviderText, value); }
    public bool HasNoScheduledJobs { get => _hasNoScheduledJobs; set => SetProperty(ref _hasNoScheduledJobs, value); }
    public bool HasNoResumableJobs { get => _hasNoResumableJobs; set => SetProperty(ref _hasNoResumableJobs, value); }
    public string GapAnalysisSummaryText { get => _gapAnalysisSummaryText; set => SetProperty(ref _gapAnalysisSummaryText, value); }
    public string GapActionHintText { get => _gapActionHintText; set => SetProperty(ref _gapActionHintText, value); }
    public bool IsGapAnalysisCardVisible { get => _isGapAnalysisCardVisible; set => SetProperty(ref _isGapAnalysisCardVisible, value); }
    public bool IsGapListVisible { get => _isGapListVisible; set => SetProperty(ref _isGapListVisible, value); }
    public bool IsGapActionPanelVisible { get => _isGapActionPanelVisible; set => SetProperty(ref _isGapActionPanelVisible, value); }
}

public sealed class BackfillStatusSectionViewModel : BindableBase
{
    private BackfillResultDto? _lastApiStatus;
    private bool _hasApiStatus;
    private Visibility _lastStatusVisibility = Visibility.Collapsed;
    private Visibility _emptyStatusVisibility = Visibility.Visible;
    private string _lastRunStatusText = string.Empty;
    private Brush _lastRunStatusBrush = Brushes.Transparent;
    private string _lastRunProviderText = "Unknown";
    private string _lastRunSymbolsText = "N/A";
    private string _lastRunBarsWrittenText = "0";
    private string _lastRunStartedText = "Unknown";
    private string _lastRunCompletedText = "N/A";
    private string _backfillStatsTotalBarsText = "--";
    private string _backfillStatsSymbolsProcessedText = "--";
    private string _backfillStatsRunWindowText = "No run loaded";
    private string _backfillStatsLastSuccessfulRunText = "No successful run loaded";

    public BackfillResultDto? LastApiStatus { get => _lastApiStatus; set => SetProperty(ref _lastApiStatus, value); }
    public bool HasApiStatus { get => _hasApiStatus; set => SetProperty(ref _hasApiStatus, value); }
    public Visibility LastStatusVisibility { get => _lastStatusVisibility; set => SetProperty(ref _lastStatusVisibility, value); }
    public Visibility EmptyStatusVisibility { get => _emptyStatusVisibility; set => SetProperty(ref _emptyStatusVisibility, value); }
    public string LastRunStatusText { get => _lastRunStatusText; set => SetProperty(ref _lastRunStatusText, value); }
    public Brush LastRunStatusBrush { get => _lastRunStatusBrush; set => SetProperty(ref _lastRunStatusBrush, value); }
    public string LastRunProviderText { get => _lastRunProviderText; set => SetProperty(ref _lastRunProviderText, value); }
    public string LastRunSymbolsText { get => _lastRunSymbolsText; set => SetProperty(ref _lastRunSymbolsText, value); }
    public string LastRunBarsWrittenText { get => _lastRunBarsWrittenText; set => SetProperty(ref _lastRunBarsWrittenText, value); }
    public string LastRunStartedText { get => _lastRunStartedText; set => SetProperty(ref _lastRunStartedText, value); }
    public string LastRunCompletedText { get => _lastRunCompletedText; set => SetProperty(ref _lastRunCompletedText, value); }
    public string BackfillStatsTotalBarsText { get => _backfillStatsTotalBarsText; set => SetProperty(ref _backfillStatsTotalBarsText, value); }
    public string BackfillStatsSymbolsProcessedText { get => _backfillStatsSymbolsProcessedText; set => SetProperty(ref _backfillStatsSymbolsProcessedText, value); }
    public string BackfillStatsRunWindowText { get => _backfillStatsRunWindowText; set => SetProperty(ref _backfillStatsRunWindowText, value); }
    public string BackfillStatsLastSuccessfulRunText { get => _backfillStatsLastSuccessfulRunText; set => SetProperty(ref _backfillStatsLastSuccessfulRunText, value); }
}

public sealed partial class BackfillViewModel
{
    private readonly BackfillWorkbenchSectionViewModel _workbenchSection = new();
    private readonly BackfillStatusSectionViewModel _statusSection = new();

    public BackfillWorkbenchSectionViewModel WorkbenchSection => _workbenchSection;
    public BackfillStatusSectionViewModel StatusSection => _statusSection;

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
