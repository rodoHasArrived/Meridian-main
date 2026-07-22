using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    private const string DefaultReconciliationOperator = "desktop-user";

    /// <summary>
    /// Audit finding P8: reconciliation decisions are attributed to the authenticated desktop
    /// operator when a session exists. The constant stays as the anonymous-session sentinel,
    /// which the action-posture rendering treats as "owner not confirmed".
    /// </summary>
    private string ResolveReconciliationOperator() =>
        _authenticationSession?.CurrentActor is { Length: > 0 } actor ? actor : DefaultReconciliationOperator;

    private FundReconciliationDetailModel? _currentReconciliationDetail;
    private CancellationTokenSource? _reconciliationDetailCts;
    private readonly List<FundReconciliationMatchCandidateRow> _reconciliationMatchRowsWithSubscriptions = [];

    private bool _isApplyingReconciliationSelection;
    private bool _isReconciliationRefreshInFlight;
    private bool _isReconciliationDetailLoading;
    private bool _isReconciliationActionInFlight;

    public ObservableCollection<FundReconciliationBreakQueueRow> ReconciliationBreakQueueItems => ReconciliationSection.BreakQueueItems;

    public ObservableCollection<FundReconciliationRunRow> ReconciliationRunItems => ReconciliationSection.RunItems;

    public ObservableCollection<FundReconciliationMatchCandidateRow> ReconciliationEntryRows => ReconciliationSection.EntryRows;

    public ObservableCollection<FundReconciliationMatchCandidateRow> ReconciliationSourceDataRows => ReconciliationSection.SourceDataRows;

    public ObservableCollection<FundReconciliationCheckDetailRow> ReconciliationExceptionRows => ReconciliationSection.ExceptionRows;

    public ObservableCollection<FundReconciliationCheckDetailRow> ReconciliationAllCheckRows => ReconciliationSection.AllCheckRows;

    public ObservableCollection<FundReconciliationSecurityCoverageRow> ReconciliationSecurityCoverageRows => ReconciliationSection.SecurityCoverageRows;

    public ObservableCollection<FundReconciliationAuditTrailRow> ReconciliationAuditRows => ReconciliationSection.AuditRows;

    public ObservableCollection<FundReconciliationCalibrationProfileRow> ReconciliationCalibrationProfiles => ReconciliationSection.CalibrationProfiles;

    public IRelayCommand OpenSelectedReconciliationAccountWorkflowCommand { get; private set; } = null!;

    public IRelayCommand ResetReconciliationFiltersCommand { get; private set; } = null!;

    public int SelectedReconciliationQueueIndex
    {
        get => (int)ReconciliationSection.SelectedQueueView;
        set
        {
            var normalized = value <= 0
                ? FundReconciliationQueueView.BreakQueue
                : FundReconciliationQueueView.Runs;

            if (ReconciliationSection.SelectedQueueView == normalized)
            {
                return;
            }

            var previousActiveKey = GetActiveReconciliationSelectionKey();
            ReconciliationSection.SelectedQueueView = normalized;
            RaisePropertyChanged();
            ApplyReconciliationFiltersAndSelection(previousActiveKey, forceReload: true);
        }
    }

    public int SelectedReconciliationScopeFilterIndex
    {
        get => (int)ReconciliationSection.SelectedScopeFilter;
        set
        {
            var normalized = value switch
            {
                1 => FundReconciliationScopeFilter.Strategy,
                2 => FundReconciliationScopeFilter.Account,
                _ => FundReconciliationScopeFilter.All
            };

            if (ReconciliationSection.SelectedScopeFilter == normalized)
            {
                return;
            }

            var previousActiveKey = GetActiveReconciliationSelectionKey();
            ReconciliationSection.SelectedScopeFilter = normalized;
            RaisePropertyChanged();
            ApplyReconciliationFiltersAndSelection(previousActiveKey);
            NotifyReconciliationFilterStateChanged();
        }
    }

    public int SelectedReconciliationDetailTabIndex
    {
        get => ReconciliationSection.SelectedDetailTabIndex;
        set
        {
            if (ReconciliationSection.SelectedDetailTabIndex == value)
            {
                return;
            }

            ReconciliationSection.SelectedDetailTabIndex = value;
            RaisePropertyChanged();
        }
    }

    public FundReconciliationBreakQueueRow? SelectedBreakQueueItem
    {
        get => ReconciliationSection.SelectedBreakQueueItem;
        set
        {
            if (ReferenceEquals(ReconciliationSection.SelectedBreakQueueItem, value))
            {
                return;
            }

            ReconciliationSection.SelectedBreakQueueItem = value;
            RaisePropertyChanged();
            NotifyReconciliationDerivedStateChanged();
            UpdateReconciliationOperatorGuidance();
            if (!_isApplyingReconciliationSelection && ReconciliationSection.SelectedQueueView == FundReconciliationQueueView.BreakQueue)
            {
                _ = LoadSelectedReconciliationDetailAsync();
            }
        }
    }

    public FundReconciliationRunRow? SelectedReconciliationRun
    {
        get => ReconciliationSection.SelectedRun;
        set
        {
            if (ReferenceEquals(ReconciliationSection.SelectedRun, value))
            {
                return;
            }

            ReconciliationSection.SelectedRun = value;
            RaisePropertyChanged();
            NotifyReconciliationDerivedStateChanged();
            if (!_isApplyingReconciliationSelection && ReconciliationSection.SelectedQueueView == FundReconciliationQueueView.Runs)
            {
                _ = LoadSelectedReconciliationDetailAsync();
            }
        }
    }

    public string ReconciliationSearchText
    {
        get => ReconciliationSection.SearchText;
        set
        {
            if (string.Equals(ReconciliationSection.SearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            ReconciliationSection.SearchText = value;
            RaisePropertyChanged();
            var previousActiveKey = GetActiveReconciliationSelectionKey();
            ApplyReconciliationFiltersAndSelection(previousActiveKey);
            NotifyReconciliationFilterStateChanged();
        }
    }

    public string ReconciliationLegalEntityText
    {
        get => ReconciliationSection.LegalEntityText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.LegalEntityText, text => ReconciliationSection.LegalEntityText = text, value);
    }

    public string ReconciliationBaseCurrencyText
    {
        get => ReconciliationSection.BaseCurrencyText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.BaseCurrencyText, text => ReconciliationSection.BaseCurrencyText = text, value);
    }

    public string ReconciliationScopeText
    {
        get => ReconciliationSection.ScopeText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.ScopeText, text => ReconciliationSection.ScopeText = text, value);
    }

    public string ReconciliationWorkspaceAsOfText
    {
        get => ReconciliationSection.WorkspaceAsOfText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.WorkspaceAsOfText, text => ReconciliationSection.WorkspaceAsOfText = text, value);
    }

    public string ReconciliationLastRefreshText
    {
        get => ReconciliationSection.LastRefreshText;
        private set
        {
            if (SetReconciliationSectionProperty(ReconciliationSection.LastRefreshText, text => ReconciliationSection.LastRefreshText = text, value))
            {
                UpdateReconciliationWorkbenchPresentation();
            }
        }
    }

    public string InReviewBreaksText
    {
        get => ReconciliationSection.InReviewBreaksText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.InReviewBreaksText, text => ReconciliationSection.InReviewBreaksText = text, value);
    }

    public string ReconciliationSecurityCoverageIssuesText
    {
        get => ReconciliationSection.SecurityCoverageIssuesText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.SecurityCoverageIssuesText, text => ReconciliationSection.SecurityCoverageIssuesText = text, value);
    }

    public string ReconciliationCalibrationStatusText
    {
        get => ReconciliationSection.CalibrationStatusText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.CalibrationStatusText, text => ReconciliationSection.CalibrationStatusText = text, value);
    }

    public string ReconciliationCalibrationSummaryText
    {
        get => ReconciliationSection.CalibrationSummaryText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.CalibrationSummaryText, text => ReconciliationSection.CalibrationSummaryText = text, value);
    }

    public string ReconciliationCalibrationProfilesText
    {
        get => ReconciliationSection.CalibrationProfilesText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.CalibrationProfilesText, text => ReconciliationSection.CalibrationProfilesText = text, value);
    }

    public string ReconciliationCalibrationPendingSignoffText
    {
        get => ReconciliationSection.CalibrationPendingSignoffText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.CalibrationPendingSignoffText, text => ReconciliationSection.CalibrationPendingSignoffText = text, value);
    }

    public string ReconciliationCalibrationMissingMetadataText
    {
        get => ReconciliationSection.CalibrationMissingMetadataText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.CalibrationMissingMetadataText, text => ReconciliationSection.CalibrationMissingMetadataText = text, value);
    }

    public string ReconciliationActionFeedbackText
    {
        get => ReconciliationSection.ActionFeedbackText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.ActionFeedbackText, text => ReconciliationSection.ActionFeedbackText = text, value);
    }

    public string ReconciliationOperatorText
    {
        get => ReconciliationSection.OperatorText;
        set
        {
            if (SetReconciliationSectionProperty(ReconciliationSection.OperatorText, text => ReconciliationSection.OperatorText = text, value))
            {
                NotifyReconciliationDerivedStateChanged();
                UpdateReconciliationWorkbenchPresentation();
                UpdateReportPackWorkbenchPresentation();
            }
        }
    }

    public string ReconciliationNoteText
    {
        get => ReconciliationSection.NoteText;
        set
        {
            if (SetReconciliationSectionProperty(ReconciliationSection.NoteText, text => ReconciliationSection.NoteText = text, value))
            {
                NotifyReconciliationDerivedStateChanged();
            }
        }
    }

    public string ReconciliationDetailTitle
    {
        get => ReconciliationSection.DetailTitle;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailTitle, text => ReconciliationSection.DetailTitle = text, value);
    }

    public string ReconciliationDetailSubtitle
    {
        get => ReconciliationSection.DetailSubtitle;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailSubtitle, text => ReconciliationSection.DetailSubtitle = text, value);
    }

    public string ReconciliationDetailStatusText
    {
        get => ReconciliationSection.DetailStatusText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailStatusText, text => ReconciliationSection.DetailStatusText = text, value);
    }

    public string ReconciliationDetailCoverageText
    {
        get => ReconciliationSection.DetailCoverageText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailCoverageText, text => ReconciliationSection.DetailCoverageText = text, value);
    }

    public string ReconciliationDetailLifecycleText
    {
        get => ReconciliationSection.DetailLifecycleText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailLifecycleText, text => ReconciliationSection.DetailLifecycleText = text, value);
    }

    public string ReconciliationDetailSignoffText
    {
        get => ReconciliationSection.DetailSignoffText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailSignoffText, text => ReconciliationSection.DetailSignoffText = text, value);
    }

    public string ReconciliationDetailLastUpdatedText
    {
        get => ReconciliationSection.DetailLastUpdatedText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailLastUpdatedText, text => ReconciliationSection.DetailLastUpdatedText = text, value);
    }

    public string ReconciliationDetailGuidanceText
    {
        get => ReconciliationSection.DetailGuidanceText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailGuidanceText, text => ReconciliationSection.DetailGuidanceText = text, value);
    }

    public string ReconciliationDetailTotalChecksText
    {
        get => ReconciliationSection.DetailTotalChecksText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailTotalChecksText, text => ReconciliationSection.DetailTotalChecksText = text, value);
    }

    public string ReconciliationDetailMatchedText
    {
        get => ReconciliationSection.DetailMatchedText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailMatchedText, text => ReconciliationSection.DetailMatchedText = text, value);
    }

    public string ReconciliationDetailBreaksText
    {
        get => ReconciliationSection.DetailBreaksText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailBreaksText, text => ReconciliationSection.DetailBreaksText = text, value);
    }

    public string ReconciliationDetailBreakAmountText
    {
        get => ReconciliationSection.DetailBreakAmountText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailBreakAmountText, text => ReconciliationSection.DetailBreakAmountText = text, value);
    }

    public string ReconciliationDetailSecurityIssuesText
    {
        get => ReconciliationSection.DetailSecurityIssuesText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.DetailSecurityIssuesText, text => ReconciliationSection.DetailSecurityIssuesText = text, value);
    }

    public string ReconciliationMatchSelectionText
    {
        get => ReconciliationSection.MatchSelectionText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.MatchSelectionText, text => ReconciliationSection.MatchSelectionText = text, value);
    }

    public string ReconciliationBreakQueueEmptyStateText
    {
        get => ReconciliationSection.BreakQueueEmptyStateText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.BreakQueueEmptyStateText, text => ReconciliationSection.BreakQueueEmptyStateText = text, value);
    }


    public string ReconciliationNextBestActionText
    {
        get => ReconciliationSection.NextBestActionText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.NextBestActionText, text => ReconciliationSection.NextBestActionText = text, value);
    }

    public string ReconciliationBlockerReasonText
    {
        get => ReconciliationSection.BlockerReasonText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.BlockerReasonText, text => ReconciliationSection.BlockerReasonText = text, value);
    }

    public string ReconciliationEvidenceLinksText
    {
        get => ReconciliationSection.EvidenceLinksText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.EvidenceLinksText, text => ReconciliationSection.EvidenceLinksText = text, value);
    }

    public string ReconciliationRunsEmptyStateText
    {
        get => ReconciliationSection.RunsEmptyStateText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.RunsEmptyStateText, text => ReconciliationSection.RunsEmptyStateText = text, value);
    }

    public bool HasActiveReconciliationFilters =>
        ReconciliationSection.SelectedBreakQueueFilter != FundReconciliationBreakQueueFilter.Open ||
        ReconciliationSection.SelectedScopeFilter != FundReconciliationScopeFilter.All ||
        !string.IsNullOrWhiteSpace(ReconciliationSearchText);

    public bool IsOpenBreakQueueFilterSelected
    {
        get => ReconciliationSection.SelectedBreakQueueFilter == FundReconciliationBreakQueueFilter.Open;
        set
        {
            if (value)
            {
                SetBreakQueueFilter(FundReconciliationBreakQueueFilter.Open);
            }
        }
    }

    public bool IsInReviewBreakQueueFilterSelected
    {
        get => ReconciliationSection.SelectedBreakQueueFilter == FundReconciliationBreakQueueFilter.InReview;
        set
        {
            if (value)
            {
                SetBreakQueueFilter(FundReconciliationBreakQueueFilter.InReview);
            }
        }
    }

    public bool IsAllBreakQueueFilterSelected
    {
        get => ReconciliationSection.SelectedBreakQueueFilter == FundReconciliationBreakQueueFilter.All;
        set
        {
            if (value)
            {
                SetBreakQueueFilter(FundReconciliationBreakQueueFilter.All);
            }
        }
    }

    public bool SupportsSelectedBreakActions =>
        _currentReconciliationDetail?.SupportsBreakActions == true &&
        SelectedBreakQueueItem is not null &&
        ReconciliationSection.SelectedQueueView == FundReconciliationQueueView.BreakQueue;

    public bool CanStartReviewSelectedBreak =>
        SupportsSelectedBreakActions &&
        SelectedBreakQueueItem?.Status == ReconciliationBreakQueueStatus.Open &&
        !_isReconciliationActionInFlight &&
        !string.IsNullOrWhiteSpace(ReconciliationOperatorText);

    public bool CanResolveSelectedBreak =>
        SupportsSelectedBreakActions &&
        SelectedBreakQueueItem?.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview &&
        !_isReconciliationActionInFlight &&
        !string.IsNullOrWhiteSpace(ReconciliationOperatorText) &&
        !string.IsNullOrWhiteSpace(ReconciliationNoteText);

    public bool HasReconciliationMatchCandidates =>
        ReconciliationEntryRows.Count > 0 ||
        ReconciliationSourceDataRows.Count > 0;

    public bool CanMatchSelectedReconciliationItems =>
        SupportsSelectedBreakActions &&
        SelectedBreakQueueItem?.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview &&
        !_isReconciliationActionInFlight &&
        !string.IsNullOrWhiteSpace(ReconciliationOperatorText) &&
        ReconciliationEntryRows.Any(static row => row.IsSelected) &&
        ReconciliationSourceDataRows.Any(static row => row.IsSelected);

    public bool CanDismissSelectedBreak => CanResolveSelectedBreak;

    public bool CanOpenSelectedReconciliationAccountWorkflow =>
        _currentReconciliationDetail?.SourceType == FundReconciliationSourceType.AccountRun &&
        _currentReconciliationDetail.AccountId is Guid accountId &&
        accountId != Guid.Empty;

    public bool HasReconciliationDetail => _currentReconciliationDetail is not null;

    public bool IsReconciliationBusy =>
        _isReconciliationRefreshInFlight ||
        _isReconciliationDetailLoading ||
        _isReconciliationActionInFlight;

    private void InitializeReconciliationWorkbench()
    {
        OpenSelectedReconciliationAccountWorkflowCommand = new RelayCommand(
            OpenSelectedReconciliationAccountWorkflow,
            () => CanOpenSelectedReconciliationAccountWorkflow);
        ResetReconciliationFiltersCommand = new RelayCommand(
            ResetReconciliationFilters,
            () => HasActiveReconciliationFilters);

        ResetReconciliationWorkbenchState();
    }

    private Task ApplyReconciliationWorkbenchAsync(
        FundProfileDetail activeFund,
        FundReconciliationWorkbenchSnapshot snapshot,
        CancellationToken ct)
    {
        ReconciliationSection.AllBreakQueueItems = snapshot.BreakQueueItems;
        ReconciliationSection.AllRunItems = snapshot.RunRows;

        ApplyReconciliation(snapshot.Summary);

        ReconciliationLegalEntityText = string.IsNullOrWhiteSpace(activeFund.LegalEntityName)
            ? "Not configured"
            : activeFund.LegalEntityName;
        ReconciliationBaseCurrencyText = string.IsNullOrWhiteSpace(activeFund.BaseCurrency)
            ? "Not configured"
            : activeFund.BaseCurrency;
        ReconciliationScopeText = HumanizeLedgerScope(activeFund.DefaultLedgerScope);
        ReconciliationWorkspaceAsOfText = ResolveWorkspaceAsOfText(snapshot.Summary);
        ReconciliationLastRefreshText = snapshot.RefreshedAt.LocalDateTime.ToString("g");
        InReviewBreaksText = snapshot.InReviewBreakCount.ToString("N0");
        ReconciliationSecurityCoverageIssuesText = snapshot.Summary.SecurityCoverageIssueCount.ToString("N0");
        ApplyReconciliationCalibration(snapshot);
        ReconciliationStatusText = snapshot.RunRows.Count == 0
            ? "No reconciliation runs are recorded for this fund yet."
            : $"{snapshot.BreakQueueItems.Count} break queue item(s) and {snapshot.RunRows.Count} run(s) are ready for review.";

        var previousActiveKey = GetActiveReconciliationSelectionKey();
        ApplyReconciliationFiltersAndSelection(previousActiveKey, forceReload: true, cancellationToken: ct);
        return Task.CompletedTask;
    }

    public async Task RefreshReconciliationWorkbenchAsync(CancellationToken ct = default)
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            ResetReconciliationWorkbenchState();
            return;
        }

        await RefreshReconciliationWorkbenchCoreAsync(activeFund, ct);
    }

    public async Task StartReviewSelectedBreakAsync(CancellationToken ct = default)
    {
        if (!CanStartReviewSelectedBreak || SelectedBreakQueueItem is null)
        {
            ReconciliationActionFeedbackText = "Select an open break and provide an operator before starting review.";
            return;
        }

        await ExecuteBreakQueueActionAsync(
            SelectedBreakQueueItem,
            (breakRow, token) => _fundReconciliationWorkbenchService.StartReviewAsync(
                breakRow,
                ReconciliationOperatorText.Trim(),
                string.IsNullOrWhiteSpace(ReconciliationNoteText) ? null : ReconciliationNoteText.Trim(),
                token),
            "Break moved into active review.",
            ct);
    }

    public async Task ResolveSelectedBreakAsync(CancellationToken ct = default)
    {
        if (!CanResolveSelectedBreak || SelectedBreakQueueItem is null)
        {
            ReconciliationActionFeedbackText = "Resolve needs an operator, a note, and an open or in-review break.";
            return;
        }

        await ExecuteBreakQueueActionAsync(
            SelectedBreakQueueItem,
            (breakRow, token) => _fundReconciliationWorkbenchService.ResolveAsync(
                breakRow,
                ReconciliationOperatorText.Trim(),
                ReconciliationNoteText.Trim(),
                token),
            "Break resolved and audit note captured.",
            ct);
    }

    public async Task MatchSelectedReconciliationItemsAsync(CancellationToken ct = default)
    {
        if (!CanMatchSelectedReconciliationItems || SelectedBreakQueueItem is null)
        {
            ReconciliationActionFeedbackText = "Match needs an operator plus at least one selected ledger entry and source-data row.";
            return;
        }

        var resolutionNote = BuildSelectedMatchResolutionNote();
        await ExecuteBreakQueueActionAsync(
            SelectedBreakQueueItem,
            (breakRow, token) => _fundReconciliationWorkbenchService.ResolveAsync(
                breakRow,
                ReconciliationOperatorText.Trim(),
                resolutionNote,
                token),
            "Selected ledger entries and source data marked reconciled.",
            ct);
    }

    public async Task DismissSelectedBreakAsync(CancellationToken ct = default)
    {
        if (!CanDismissSelectedBreak || SelectedBreakQueueItem is null)
        {
            ReconciliationActionFeedbackText = "Dismiss needs an operator, a note, and an open or in-review break.";
            return;
        }

        await ExecuteBreakQueueActionAsync(
            SelectedBreakQueueItem,
            (breakRow, token) => _fundReconciliationWorkbenchService.DismissAsync(
                breakRow,
                ReconciliationOperatorText.Trim(),
                ReconciliationNoteText.Trim(),
                token),
            "Break dismissed and audit note captured.",
            ct);
    }

    private async Task RefreshReconciliationWorkbenchCoreAsync(
        FundProfileDetail activeFund,
        CancellationToken ct)
    {
        SetReconciliationRefreshInFlight(true);
        ReconciliationActionFeedbackText = "Refreshing reconciliation workbench...";

        try
        {
            var snapshot = await _fundReconciliationWorkbenchService
                .GetSnapshotAsync(activeFund.FundProfileId, ct)
                ;
            await ApplyReconciliationWorkbenchAsync(activeFund, snapshot, ct);
            ReconciliationActionFeedbackText = $"Reconciliation workbench refreshed at {snapshot.RefreshedAt.LocalDateTime:g}.";
        }
        finally
        {
            SetReconciliationRefreshInFlight(false);
        }
    }

    private async Task ExecuteBreakQueueActionAsync(
        FundReconciliationBreakQueueRow selectedBreak,
        Func<FundReconciliationBreakQueueRow, CancellationToken, Task<WorkstationReconciliationActionResult>> action,
        string successMessage,
        CancellationToken ct)
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            ReconciliationActionFeedbackText = "A fund must be selected before reconciliation actions can run.";
            return;
        }

        SetReconciliationActionInFlight(true);
        ReconciliationActionFeedbackText = "Submitting reconciliation update...";

        try
        {
            var result = await action(selectedBreak, ct);
            if (!result.Success)
            {
                ReconciliationActionFeedbackText = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "The reconciliation action did not complete."
                    : result.ErrorMessage;
                return;
            }

            if (result.Item is not null && !MatchesBreakQueueFilter(result.Item.Status, ReconciliationSection.SelectedBreakQueueFilter))
            {
                ReconciliationSection.SelectedBreakQueueFilter = FundReconciliationBreakQueueFilter.All;
                RaisePropertyChanged(nameof(IsOpenBreakQueueFilterSelected));
                RaisePropertyChanged(nameof(IsInReviewBreakQueueFilterSelected));
                RaisePropertyChanged(nameof(IsAllBreakQueueFilterSelected));
            }

            await RefreshReconciliationWorkbenchCoreAsync(activeFund, ct);
            ReconciliationActionFeedbackText = successMessage;
        }
        finally
        {
            SetReconciliationActionInFlight(false);
        }
    }

    private void ApplyReconciliationFiltersAndSelection(
        string? previousActiveKey,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        var filteredBreaks = FilterBreakQueueItems(ReconciliationSection.AllBreakQueueItems).ToArray();
        var filteredRuns = FilterRunItems(ReconciliationSection.AllRunItems).ToArray();
        var preferredBreakId = SelectedBreakQueueItem?.BreakId;
        var preferredRunKey = SelectedReconciliationRun?.RowKey;

        _isApplyingReconciliationSelection = true;
        try
        {
            SynchronizeCollection(ReconciliationSection.BreakQueueItems, filteredBreaks);
            SynchronizeCollection(ReconciliationSection.RunItems, filteredRuns);

            SetReconciliationSectionProperty(
                ReconciliationSection.SelectedBreakQueueItem,
                item => ReconciliationSection.SelectedBreakQueueItem = item,
                ResolveSelection(filteredBreaks, preferredBreakId, static item => item.BreakId),
                nameof(SelectedBreakQueueItem));
            SetReconciliationSectionProperty(
                ReconciliationSection.SelectedRun,
                run => ReconciliationSection.SelectedRun = run,
                ResolveSelection(filteredRuns, preferredRunKey, static item => item.RowKey),
                nameof(SelectedReconciliationRun));
        }
        finally
        {
            _isApplyingReconciliationSelection = false;
        }

        UpdateReconciliationEmptyStateText(filteredBreaks.Length, filteredRuns.Length);
        NotifyReconciliationDerivedStateChanged();

        var activeKey = GetActiveReconciliationSelectionKey();
        if (forceReload || !string.Equals(previousActiveKey, activeKey, StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadSelectedReconciliationDetailAsync(cancellationToken);
        }
        else if (activeKey is null)
        {
            ClearReconciliationDetail();
        }
    }

    private IEnumerable<FundReconciliationBreakQueueRow> FilterBreakQueueItems(IEnumerable<FundReconciliationBreakQueueRow> rows)
    {
        if (ReconciliationSection.SelectedScopeFilter == FundReconciliationScopeFilter.Account)
        {
            return [];
        }

        var query = ReconciliationSearchText.Trim();
        return rows.Where(item =>
            MatchesBreakQueueFilter(item.Status, ReconciliationSection.SelectedBreakQueueFilter) &&
            (string.IsNullOrWhiteSpace(query) ||
             ContainsIgnoreCase(item.DisplayLabel, query) ||
             ContainsIgnoreCase(item.CategoryLabel, query) ||
             ContainsIgnoreCase(item.Reason, query) ||
             ContainsIgnoreCase(item.AssignedToLabel, query)));
    }

    private IEnumerable<FundReconciliationRunRow> FilterRunItems(IEnumerable<FundReconciliationRunRow> rows)
    {
        var query = ReconciliationSearchText.Trim();
        return rows.Where(item =>
            MatchesScopeFilter(item.SourceType, ReconciliationSection.SelectedScopeFilter) &&
            (string.IsNullOrWhiteSpace(query) ||
             ContainsIgnoreCase(item.PrimaryLabel, query) ||
             ContainsIgnoreCase(item.SecondaryLabel, query) ||
             ContainsIgnoreCase(item.StatusLabel, query) ||
             ContainsIgnoreCase(item.ScopeLabel, query) ||
             ContainsIgnoreCase(item.CoverageLabel, query)));
    }

    private async Task LoadSelectedReconciliationDetailAsync(CancellationToken ct = default)
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            ClearReconciliationDetail();
            return;
        }

        var selectedBreak = ReconciliationSection.SelectedQueueView == FundReconciliationQueueView.BreakQueue
            ? SelectedBreakQueueItem
            : null;
        var selectedRun = ReconciliationSection.SelectedQueueView == FundReconciliationQueueView.Runs
            ? SelectedReconciliationRun
            : null;

        if (selectedBreak is null && selectedRun is null)
        {
            ClearReconciliationDetail();
            return;
        }

        CancelReconciliationDetailLoad();
        _reconciliationDetailCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var detailToken = _reconciliationDetailCts.Token;

        SetReconciliationDetailLoading(true);
        try
        {
            FundReconciliationDetailModel? detail = selectedBreak is not null
                ? await _fundReconciliationWorkbenchService.GetBreakDetailAsync(selectedBreak, activeFund.BaseCurrency, detailToken)
                : await _fundReconciliationWorkbenchService.GetRunDetailAsync(selectedRun!, activeFund.BaseCurrency, detailToken);

            if (!detailToken.IsCancellationRequested)
            {
                ApplyReconciliationDetail(detail, activeFund.BaseCurrency);
            }
        }
        catch (OperationCanceledException) when (detailToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReconciliationActionFeedbackText = $"Unable to load reconciliation detail: {ex.Message}";
            ClearReconciliationDetail();
        }
        finally
        {
            if (_reconciliationDetailCts is not null && _reconciliationDetailCts.Token == detailToken)
            {
                SetReconciliationDetailLoading(false);
            }
        }
    }

    private void ApplyReconciliationDetail(FundReconciliationDetailModel? detail, string baseCurrency)
    {
        _currentReconciliationDetail = detail;

        if (detail is null)
        {
            ClearReconciliationDetail();
            return;
        }

        ReconciliationDetailTitle = detail.Title;
        ReconciliationDetailSubtitle = detail.Subtitle;
        ReconciliationDetailStatusText = detail.StatusLabel;
        ReconciliationDetailCoverageText = detail.CoverageSummary;
        ReconciliationDetailLifecycleText = BuildReconciliationDetailLifecycleText(detail, SelectedBreakQueueItem);
        ReconciliationDetailSignoffText = BuildReconciliationDetailSignoffText(detail, SelectedBreakQueueItem);
        ReconciliationDetailLastUpdatedText = detail.LastUpdatedText;
        ReconciliationDetailGuidanceText = detail.SourceType == FundReconciliationSourceType.AccountRun
            ? "Account-level reconciliation is read-only in Accounting. Open the account workflow to rerun or resolve it."
            : detail.SupportsBreakActions
                ? "Inline break actions update the shared workstation queue and keep operator notes attached to the review item."
                : "Select a break from Break Queue to start review, resolve, or dismiss it inline.";
        ReconciliationDetailTotalChecksText = detail.TotalChecks.ToString("N0");
        ReconciliationDetailMatchedText = detail.TotalMatched.ToString("N0");
        ReconciliationDetailBreaksText = detail.TotalBreaks.ToString("N0");
        ReconciliationDetailBreakAmountText = FormatCurrency(detail.BreakAmountTotal, baseCurrency);
        ReconciliationDetailSecurityIssuesText = detail.SecurityIssueCount.ToString("N0");

        SynchronizeCollection(ReconciliationSection.ExceptionRows, detail.ExceptionRows);
        SynchronizeCollection(ReconciliationSection.AllCheckRows, detail.AllCheckRows);
        SynchronizeCollection(ReconciliationSection.SecurityCoverageRows, detail.SecurityCoverageRows);
        SynchronizeCollection(ReconciliationSection.AuditRows, detail.AuditRows);
        SynchronizeReconciliationMatchCandidates(detail);

        UpdateReconciliationOperatorGuidance();
        NotifyReconciliationDerivedStateChanged();
    }

    private void ClearReconciliationDetail()
    {
        _currentReconciliationDetail = null;
        ReconciliationDetailTitle = "Select a break or run";
        ReconciliationDetailSubtitle = "Choose a break queue item or reconciliation run to inspect exceptions, coverage, and audit history.";
        ReconciliationDetailStatusText = "Awaiting selection";
        ReconciliationDetailCoverageText = "Security coverage status appears here when a reconciliation item is selected.";
        ReconciliationDetailLifecycleText = "Select a break queue item to see detection, review, decision, and audit posture.";
        ReconciliationDetailSignoffText = "Sign-off posture appears here when a reconciliation break is selected.";
        ReconciliationDetailLastUpdatedText = "-";
        ReconciliationDetailGuidanceText = "Break queue items support inline review and resolution. Account runs stay read-only in Accounting.";
        ReconciliationDetailTotalChecksText = "0";
        ReconciliationDetailMatchedText = "0";
        ReconciliationDetailBreaksText = "0";
        ReconciliationDetailBreakAmountText = "-";
        ReconciliationDetailSecurityIssuesText = "0";
        ReconciliationSection.ExceptionRows.Clear();
        ReconciliationSection.AllCheckRows.Clear();
        ClearReconciliationMatchCandidates();
        ReconciliationSection.SecurityCoverageRows.Clear();
        ReconciliationSection.AuditRows.Clear();
        NotifyReconciliationDerivedStateChanged();
    }

    private void ResetReconciliationWorkbenchState()
    {
        CancelReconciliationDetailLoad();
        ReconciliationSection.AllBreakQueueItems = [];
        ReconciliationSection.AllRunItems = [];

        _isApplyingReconciliationSelection = true;
        try
        {
            ReconciliationSection.SelectedQueueView = FundReconciliationQueueView.BreakQueue;
            ReconciliationSection.SelectedBreakQueueFilter = FundReconciliationBreakQueueFilter.Open;
            ReconciliationSection.SelectedScopeFilter = FundReconciliationScopeFilter.All;
            ReconciliationSection.SelectedDetailTabIndex = 0;
            ReconciliationSection.SearchText = string.Empty;
            ReconciliationSection.SelectedBreakQueueItem = null;
            ReconciliationSection.SelectedRun = null;
            ReconciliationSection.OperatorText = ResolveReconciliationOperator();
            ReconciliationSection.NoteText = string.Empty;
            ReconciliationSection.ActionFeedbackText = string.Empty;
            ReconciliationSection.BreakQueueItems.Clear();
            ReconciliationSection.RunItems.Clear();
            ReconciliationSection.ExceptionRows.Clear();
            ReconciliationSection.AllCheckRows.Clear();
            ClearReconciliationMatchCandidates();
            ReconciliationSection.SecurityCoverageRows.Clear();
            ReconciliationSection.AuditRows.Clear();
            ReconciliationSection.CalibrationProfiles.Clear();
        }
        finally
        {
            _isApplyingReconciliationSelection = false;
        }

        ReconciliationLegalEntityText = "-";
        ReconciliationBaseCurrencyText = "-";
        ReconciliationScopeText = "-";
        ReconciliationWorkspaceAsOfText = "-";
        ReconciliationLastRefreshText = "-";
        InReviewBreaksText = "0";
        ReconciliationSecurityCoverageIssuesText = "0";
        ReconciliationCalibrationStatusText = "Not loaded";
        ReconciliationCalibrationSummaryText = "Calibration posture appears after the reconciliation workbench loads.";
        ReconciliationCalibrationProfilesText = "0";
        ReconciliationCalibrationPendingSignoffText = "0";
        ReconciliationCalibrationMissingMetadataText = "0";
        ReconciliationBreakQueueEmptyStateText = "No strategy-run breaks are queued for this fund.";
        ReconciliationRunsEmptyStateText = "No reconciliation runs are available for this fund.";
        ClearReconciliationDetail();

        RaisePropertyChanged(nameof(SelectedReconciliationQueueIndex));
        RaisePropertyChanged(nameof(SelectedReconciliationScopeFilterIndex));
        RaisePropertyChanged(nameof(SelectedReconciliationDetailTabIndex));
        RaisePropertyChanged(nameof(ReconciliationSearchText));
        RaisePropertyChanged(nameof(SelectedBreakQueueItem));
        RaisePropertyChanged(nameof(SelectedReconciliationRun));
        RaisePropertyChanged(nameof(ReconciliationOperatorText));
        RaisePropertyChanged(nameof(ReconciliationNoteText));
        RaisePropertyChanged(nameof(IsOpenBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsInReviewBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsAllBreakQueueFilterSelected));
        NotifyReconciliationDerivedStateChanged();
        NotifyReconciliationFilterStateChanged();
    }

    private void DisposeReconciliationWorkbench()
    {
        CancelReconciliationDetailLoad();
        ClearReconciliationMatchCandidates();
    }

    private void CancelReconciliationDetailLoad()
    {
        try
        {
            _reconciliationDetailCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The token source was already disposed; nothing to cancel.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Ignored cancel on already-disposed token source.",
                ("view", nameof(FundLedgerViewModel)));
        }
        finally
        {
            _reconciliationDetailCts?.Dispose();
            _reconciliationDetailCts = null;
        }
    }

    private void OpenSelectedReconciliationAccountWorkflow()
    {
        if (!CanOpenSelectedReconciliationAccountWorkflow || _currentReconciliationDetail?.AccountId is not Guid accountId)
        {
            return;
        }

        _navigationService.NavigateTo("AccountPortfolio", new FundOperationsNavigationContext(
            Tab: FundOperationsTab.Accounts,
            FundProfileId: _fundContextService.CurrentFundProfile?.FundProfileId,
            AccountId: accountId));
    }

    private void ApplyReconciliationCalibration(FundReconciliationWorkbenchSnapshot snapshot)
    {
        SynchronizeCollection(ReconciliationSection.CalibrationProfiles, snapshot.CalibrationProfiles);

        var summary = snapshot.CalibrationSummary;
        if (summary is null)
        {
            ReconciliationCalibrationStatusText = "Unavailable";
            ReconciliationCalibrationSummaryText = "The workstation service did not return reconciliation calibration posture.";
            ReconciliationCalibrationProfilesText = "0";
            ReconciliationCalibrationPendingSignoffText = "0";
            ReconciliationCalibrationMissingMetadataText = "0";
            return;
        }

        ReconciliationCalibrationStatusText = FormatCalibrationStatus(summary.Status);
        var kpiSummary = $"Break trend {summary.BreakCountTrend:+#;-#;0}; auto-match {summary.AutoMatchRate:P0}; T+0 closure {summary.T0ClosureRate:P0}.";
        ReconciliationCalibrationSummaryText = string.IsNullOrWhiteSpace(summary.Summary)
            ? kpiSummary
            : $"{summary.Summary} {kpiSummary}";
        ReconciliationCalibrationProfilesText = snapshot.CalibrationProfiles.Count.ToString("N0");
        ReconciliationCalibrationPendingSignoffText = summary.PendingSignoffCount.ToString("N0");
        ReconciliationCalibrationMissingMetadataText = summary.MissingCalibrationMetadataCount.ToString("N0");
    }

    private void SetBreakQueueFilter(FundReconciliationBreakQueueFilter filter)
    {
        if (ReconciliationSection.SelectedBreakQueueFilter == filter)
        {
            return;
        }

        var previousActiveKey = GetActiveReconciliationSelectionKey();
        ReconciliationSection.SelectedBreakQueueFilter = filter;
        RaisePropertyChanged(nameof(IsOpenBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsInReviewBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsAllBreakQueueFilterSelected));
        ApplyReconciliationFiltersAndSelection(previousActiveKey);
        NotifyReconciliationFilterStateChanged();
    }

    private void ResetReconciliationFilters()
    {
        if (!HasActiveReconciliationFilters)
        {
            return;
        }

        var previousActiveKey = GetActiveReconciliationSelectionKey();
        ReconciliationSection.SelectedBreakQueueFilter = FundReconciliationBreakQueueFilter.Open;
        ReconciliationSection.SelectedScopeFilter = FundReconciliationScopeFilter.All;
        ReconciliationSection.SearchText = string.Empty;

        RaisePropertyChanged(nameof(IsOpenBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsInReviewBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsAllBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(SelectedReconciliationScopeFilterIndex));
        RaisePropertyChanged(nameof(ReconciliationSearchText));

        ApplyReconciliationFiltersAndSelection(previousActiveKey);
        ReconciliationActionFeedbackText = "Reconciliation filters reset to the open queue, all scopes, and blank search.";
        NotifyReconciliationFilterStateChanged();
    }

    private void SetReconciliationRefreshInFlight(bool value)
    {
        if (SetProperty(ref _isReconciliationRefreshInFlight, value))
        {
            RaisePropertyChanged(nameof(IsReconciliationBusy));
            NotifyReconciliationDerivedStateChanged();
        }
    }

    private void SetReconciliationDetailLoading(bool value)
    {
        if (SetProperty(ref _isReconciliationDetailLoading, value))
        {
            RaisePropertyChanged(nameof(IsReconciliationBusy));
            NotifyReconciliationDerivedStateChanged();
        }
    }

    private void SetReconciliationActionInFlight(bool value)
    {
        if (SetProperty(ref _isReconciliationActionInFlight, value))
        {
            RaisePropertyChanged(nameof(IsReconciliationBusy));
            NotifyReconciliationDerivedStateChanged();
        }
    }

    private void NotifyReconciliationDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(SupportsSelectedBreakActions));
        RaisePropertyChanged(nameof(CanStartReviewSelectedBreak));
        RaisePropertyChanged(nameof(CanResolveSelectedBreak));
        RaisePropertyChanged(nameof(HasReconciliationMatchCandidates));
        RaisePropertyChanged(nameof(CanMatchSelectedReconciliationItems));
        RaisePropertyChanged(nameof(CanDismissSelectedBreak));
        RaisePropertyChanged(nameof(CanOpenSelectedReconciliationAccountWorkflow));
        RaisePropertyChanged(nameof(HasReconciliationDetail));
        RaisePropertyChanged(nameof(IsReconciliationBusy));
        OpenSelectedReconciliationAccountWorkflowCommand.NotifyCanExecuteChanged();
    }

    private void SynchronizeReconciliationMatchCandidates(FundReconciliationDetailModel detail)
    {
        ClearReconciliationMatchCandidates();

        var sourceRows = detail.ExceptionRows.Count > 0 ? detail.ExceptionRows : detail.AllCheckRows;
        var entryRows = sourceRows
            .Select(row => new FundReconciliationMatchCandidateRow(
                row.RowKey + ":entry",
                row.CheckLabel,
                ResolveEntrySourceLabel(row.SourceLabel),
                row.ExpectedAmountText,
                row.ExpectedAsOfText,
                row.StatusLabel,
                row.Reason,
                row.IsHighlighted))
            .ToArray();
        var sourceDataRows = sourceRows
            .Select(row => new FundReconciliationMatchCandidateRow(
                row.RowKey + ":source",
                row.CheckLabel,
                ResolveSourceDataLabel(row.SourceLabel),
                row.ActualAmountText,
                row.ActualAsOfText,
                row.StatusLabel,
                row.Reason,
                row.IsHighlighted))
            .ToArray();

        foreach (var row in entryRows)
        {
            ReconciliationSection.EntryRows.Add(row);
            SubscribeReconciliationMatchCandidate(row);
        }

        foreach (var row in sourceDataRows)
        {
            ReconciliationSection.SourceDataRows.Add(row);
            SubscribeReconciliationMatchCandidate(row);
        }

        UpdateReconciliationMatchSelectionText();
    }

    private void ClearReconciliationMatchCandidates()
    {
        foreach (var row in _reconciliationMatchRowsWithSubscriptions)
        {
            row.PropertyChanged -= OnReconciliationMatchCandidatePropertyChanged;
        }

        _reconciliationMatchRowsWithSubscriptions.Clear();
        ReconciliationSection.EntryRows.Clear();
        ReconciliationSection.SourceDataRows.Clear();
        UpdateReconciliationMatchSelectionText();
    }

    private void SubscribeReconciliationMatchCandidate(FundReconciliationMatchCandidateRow row)
    {
        row.PropertyChanged += OnReconciliationMatchCandidatePropertyChanged;
        _reconciliationMatchRowsWithSubscriptions.Add(row);
    }

    private void OnReconciliationMatchCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(FundReconciliationMatchCandidateRow.IsSelected), StringComparison.Ordinal))
        {
            UpdateReconciliationMatchSelectionText();
            NotifyReconciliationDerivedStateChanged();
        }
    }

    private void UpdateReconciliationMatchSelectionText()
    {
        var entryCount = ReconciliationSection.EntryRows.Count(static row => row.IsSelected);
        var sourceDataCount = ReconciliationSection.SourceDataRows.Count(static row => row.IsSelected);

        ReconciliationMatchSelectionText = HasReconciliationMatchCandidates
            ? $"{entryCount:N0} ledger entry row(s) and {sourceDataCount:N0} source-data row(s) selected."
            : "No reconciliation rows are available to match for the current selection.";
    }

    private string BuildSelectedMatchResolutionNote()
    {
        var entrySummary = BuildSelectedMatchRowSummary(ReconciliationSection.EntryRows);
        var sourceDataSummary = BuildSelectedMatchRowSummary(ReconciliationSection.SourceDataRows);
        var generatedNote = $"Matched ledger entry selection ({entrySummary}) to source-data selection ({sourceDataSummary}).";
        return string.IsNullOrWhiteSpace(ReconciliationNoteText)
            ? generatedNote
            : $"{ReconciliationNoteText.Trim()} {generatedNote}";
    }

    private static string BuildSelectedMatchRowSummary(IEnumerable<FundReconciliationMatchCandidateRow> rows)
    {
        var selected = rows
            .Where(static row => row.IsSelected)
            .Select(static row => $"{row.CheckLabel} {row.AmountText}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selected.Length == 0 ? "none" : string.Join("; ", selected);
    }

    private static string ResolveEntrySourceLabel(string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            return "Ledger";
        }

        var arrowIndex = sourceLabel.IndexOf('\u2192');
        if (arrowIndex > 0)
        {
            return sourceLabel[..arrowIndex].Trim();
        }

        return sourceLabel.Contains("ledger", StringComparison.OrdinalIgnoreCase)
            ? sourceLabel
            : "Ledger";
    }

    private static string ResolveSourceDataLabel(string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            return "Source data";
        }

        var arrowIndex = sourceLabel.IndexOf('\u2192');
        if (arrowIndex >= 0 && arrowIndex < sourceLabel.Length - 1)
        {
            return sourceLabel[(arrowIndex + 1)..].Trim();
        }

        return sourceLabel;
    }

    private void UpdateReconciliationEmptyStateText(int filteredBreakCount, int filteredRunCount)
    {
        ReconciliationBreakQueueEmptyStateText = filteredBreakCount > 0
            ? string.Empty
            : ReconciliationSection.SelectedScopeFilter == FundReconciliationScopeFilter.Account
                ? "Break Queue is strategy-scoped only. Reset filters or switch scope back to Strategy or All to review queue items."
                : ReconciliationSection.AllBreakQueueItems.Count == 0
                    ? "No strategy-run breaks are queued for this fund."
                    : "No break queue items match the current filter. Reset filters to return to the open queue.";

        ReconciliationRunsEmptyStateText = filteredRunCount > 0
            ? string.Empty
            : ReconciliationSection.AllRunItems.Count == 0
                ? "No reconciliation runs are available for this fund."
                : "No runs match the current scope or search filter. Reset filters to return to the full run list.";
    }

    private void NotifyReconciliationFilterStateChanged()
    {
        RaisePropertyChanged(nameof(HasActiveReconciliationFilters));
        ResetReconciliationFiltersCommand.NotifyCanExecuteChanged();
    }

    private string? GetActiveReconciliationSelectionKey()
        => ReconciliationSection.SelectedQueueView switch
        {
            FundReconciliationQueueView.BreakQueue => SelectedBreakQueueItem?.BreakId,
            FundReconciliationQueueView.Runs => SelectedReconciliationRun?.RowKey,
            _ => null
        };

    private static bool MatchesBreakQueueFilter(
        ReconciliationBreakQueueStatus status,
        FundReconciliationBreakQueueFilter filter)
        => filter switch
        {
            FundReconciliationBreakQueueFilter.Open => status == ReconciliationBreakQueueStatus.Open,
            FundReconciliationBreakQueueFilter.InReview => status == ReconciliationBreakQueueStatus.InReview,
            _ => true
        };

    private static bool MatchesScopeFilter(
        FundReconciliationSourceType sourceType,
        FundReconciliationScopeFilter filter)
        => filter switch
        {
            FundReconciliationScopeFilter.Strategy => sourceType == FundReconciliationSourceType.StrategyRun,
            FundReconciliationScopeFilter.Account => sourceType == FundReconciliationSourceType.AccountRun,
            _ => true
        };

    private static bool ContainsIgnoreCase(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string BuildReconciliationDetailLifecycleText(
        FundReconciliationDetailModel detail,
        FundReconciliationBreakQueueRow? selectedBreak)
    {
        if (detail.SourceType == FundReconciliationSourceType.AccountRun)
        {
            return "Account reconciliation is read-only here; open the account workflow to rerun source checks before close sign-off.";
        }

        if (selectedBreak is null || !string.Equals(detail.FocusBreakId, selectedBreak.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            return "Select a break queue item to see detection, review, decision, and audit posture.";
        }

        if (selectedBreak.ResolvedAt is not null)
        {
            return $"Detected {selectedBreak.DetectedAtText}; reviewed by {FormatOperatorLabel(selectedBreak.ReviewedBy, selectedBreak.AssignedToLabel)}; closed by {FormatOperatorLabel(selectedBreak.ResolvedBy, selectedBreak.AssignedToLabel)}. Review the audit tab for the decision note.";
        }

        if (selectedBreak.ReviewedAt is not null)
        {
            return $"Detected {selectedBreak.DetectedAtText}; active review is assigned to {selectedBreak.AssignedToLabel}. Capture the operator decision note before resolving or dismissing.";
        }

        return $"Detected {selectedBreak.DetectedAtText}; not yet in review. Start Review records ownership before a resolve or dismiss decision.";
    }

    private static string BuildReconciliationDetailSignoffText(
        FundReconciliationDetailModel detail,
        FundReconciliationBreakQueueRow? selectedBreak)
    {
        if (detail.SourceType == FundReconciliationSourceType.AccountRun)
        {
            return "Account workflow sign-off is owned by the account reconciliation route, not the strategy break queue.";
        }

        if (selectedBreak is null || !string.Equals(detail.FocusBreakId, selectedBreak.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            return "Select a break queue item to inspect required sign-off before close approval.";
        }

        var role = selectedBreak.RequiredSignoffRoleLabel;
        var status = selectedBreak.SignoffStatusLabel;
        if (string.Equals(role, "Not configured", StringComparison.OrdinalIgnoreCase))
        {
            return $"Sign-off: {status}. Required role is not configured; keep the break out of close approval until accounting ownership is assigned.";
        }

        if (selectedBreak.ResolvedAt is not null && !status.Contains("signed", StringComparison.OrdinalIgnoreCase))
        {
            return $"Decision captured; sign-off: {status} by {role}. Keep close approval blocked until accounting sign-off is recorded.";
        }

        if (status.Contains("signed", StringComparison.OrdinalIgnoreCase))
        {
            return $"Sign-off: {status} for {role}. Confirm close-pack evidence before final reporting.";
        }

        return $"Sign-off: {status} by {role}. Keep the break in the accounting queue until review, decision, and audit evidence align.";
    }

    private static string FormatOperatorLabel(string? actor, string fallback)
        => string.IsNullOrWhiteSpace(actor) ? fallback : actor;

    private static string FormatCalibrationStatus(ReconciliationCalibrationStatusDto status)
        => status switch
        {
            ReconciliationCalibrationStatusDto.Ready => "Ready",
            ReconciliationCalibrationStatusDto.ReviewRequired => "Review Required",
            ReconciliationCalibrationStatusDto.Blocked => "Blocked",
            _ => status.ToString()
        };

    private static TItem? ResolveSelection<TItem>(
        IReadOnlyList<TItem> items,
        string? preferredKey,
        Func<TItem, string?> keySelector)
        where TItem : class
    {
        if (!string.IsNullOrWhiteSpace(preferredKey))
        {
            var match = items.FirstOrDefault(item => string.Equals(keySelector(item), preferredKey, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return items.FirstOrDefault();
    }

    private static void SynchronizeCollection<TItem>(
        ObservableCollection<TItem> target,
        IReadOnlyList<TItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void UpdateReconciliationOperatorGuidance()
    {
        if (SelectedBreakQueueItem is null)
        {
            ReconciliationNextBestActionText = "Select a break to view the recommended next action.";
            ReconciliationBlockerReasonText = "No blocker is selected.";
            ReconciliationEvidenceLinksText = "Evidence links appear after selecting a break.";
            ReconciliationSection.AccountingSignifierState = WorkstationStateModel.Empty(
                "Select reconciliation evidence",
                "Choose a break queue item or reconciliation run to see lifecycle, sign-off, evidence, and recovery posture.",
                "Select a break",
                "Fund Reconciliation");
            return;
        }

        ReconciliationNextBestActionText = string.IsNullOrWhiteSpace(SelectedBreakQueueItem.SuggestedNextActionLabel)
            ? SelectedBreakQueueItem.Status is ReconciliationBreakQueueStatus.Open
                ? "Review evidence, assign owner, and move the break to InReview."
                : "Capture operator decision and finalize resolution notes."
            : SelectedBreakQueueItem.SuggestedNextActionLabel;
        ReconciliationBlockerReasonText = SelectedBreakQueueItem.ProbableCauseLabel == "Not reported"
            ? string.IsNullOrWhiteSpace(SelectedBreakQueueItem.Reason)
                ? "Awaiting break summary from the workstation service."
                : SelectedBreakQueueItem.Reason
            : $"{SelectedBreakQueueItem.ProbableCauseLabel} Ledger impact: {SelectedBreakQueueItem.LedgerImpactLabel}";
        ReconciliationEvidenceLinksText = SelectedBreakQueueItem.EvidenceLinksLabel == "No evidence links reported"
            ? $"/api/workstation/reconciliation/break-queue/{SelectedBreakQueueItem.BreakId}"
            : SelectedBreakQueueItem.EvidenceLinksLabel;
        ReconciliationSection.AccountingSignifierState = BuildSelectedBreakSignifierState(SelectedBreakQueueItem);
    }

    private WorkstationStateModel BuildSelectedBreakSignifierState(FundReconciliationBreakQueueRow breakRow)
    {
        var actionLabel = breakRow.Status switch
        {
            ReconciliationBreakQueueStatus.Open => "Start Review",
            ReconciliationBreakQueueStatus.InReview => "Resolve or Dismiss",
            _ => "Audit Decision"
        };
        var target = $"/api/workstation/reconciliation/break-queue/{breakRow.BreakId}";
        var reason = string.IsNullOrWhiteSpace(breakRow.ExplanationSummary) || breakRow.ExplanationSummary == "No structured break explanation reported"
            ? "Awaiting break summary from the workstation service."
            : breakRow.ExplanationSummary;
        var owner = string.IsNullOrWhiteSpace(ReconciliationOperatorText) ||
                    string.Equals(ReconciliationOperatorText, DefaultReconciliationOperator, StringComparison.OrdinalIgnoreCase)
            ? "Owner not confirmed"
            : $"Owner {ReconciliationOperatorText}";
        var status = breakRow.Status.ToString();
        var actionPosture = new WorkstationActionPostureModel(
            actionLabel,
            reason,
            target,
            owner,
            breakRow.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed
                ? WorkstationReadinessTone.EvidenceLinked
                : WorkstationReadinessTone.Blocked,
            breakRow.Status is ReconciliationBreakQueueStatus.Open
                ? WorkspaceTone.Danger
                : WorkspaceTone.Warning);
        var signoffStatus = string.IsNullOrWhiteSpace(breakRow.SignoffStatusLabel)
            ? "Pending"
            : breakRow.SignoffStatusLabel;
        var signoffRole = string.IsNullOrWhiteSpace(breakRow.RequiredSignoffRoleLabel)
            ? "Fund operations reviewer"
            : breakRow.RequiredSignoffRoleLabel;

        return new WorkstationStateModel(
            breakRow.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed
                ? WorkstationStateKind.Ready
                : WorkstationStateKind.Blocked,
            $"{status} reconciliation break",
            ReconciliationDetailLifecycleText,
            actionPosture.Label,
            actionPosture.Target,
            ReconciliationEvidenceLinksText,
            breakRow.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed ? "\uE73E" : "\uE783",
            breakRow.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed ? WorkspaceTone.Success : WorkspaceTone.Danger,
            breakRow.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed ? WorkstationReadinessTone.EvidenceLinked : WorkstationReadinessTone.Blocked,
            actionPosture,
            EvidenceLinks:
            [
                new WorkstationEvidenceLinkModel(
                    "Break queue evidence",
                    target,
                    "shared workstation endpoint",
                    ReconciliationDetailLastUpdatedText),
                new WorkstationEvidenceLinkModel(
                    "Explain the Break evidence",
                    breakRow.EvidenceLinksLabel,
                    breakRow.SourceSystemsLabel,
                    breakRow.LedgerImpactLabel),
                new WorkstationEvidenceLinkModel(
                    "Audit trail",
                    "FundAuditTrail",
                    "reconciliation audit",
                    ReconciliationDetailSignoffText)
            ],
            RecoveryActions:
            [
                new WorkstationRecoveryActionModel(
                    actionLabel,
                    ReconciliationNextBestActionText,
                    target)
            ],
            SignoffRequirement: new WorkstationSignoffRequirementModel(
                signoffRole,
                signoffStatus,
                ReconciliationDetailSignoffText));
    }

    private static string HumanizeLedgerScope(FundLedgerScope scope)
        => scope switch
        {
            FundLedgerScope.Consolidated => "Consolidated",
            FundLedgerScope.Entity => "Entity default",
            FundLedgerScope.Sleeve => "Sleeve default",
            FundLedgerScope.Vehicle => "Vehicle default",
            _ => scope.ToString()
        };

    private static string ResolveWorkspaceAsOfText(ReconciliationSummary summary)
    {
        if (summary.RecentRuns.Count == 0)
        {
            return "-";
        }

        var latestAsOf = summary.RecentRuns.Max(item => item.AsOfDate);
        return latestAsOf.ToString("yyyy-MM-dd");
    }

    private static string FormatCurrency(decimal amount, string currencyCode)
        => $"{currencyCode} {amount:N2}";
}
