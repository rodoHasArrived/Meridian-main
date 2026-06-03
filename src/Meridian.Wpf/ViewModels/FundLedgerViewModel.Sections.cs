using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed class FundLedgerCollectionsSectionViewModel
{
    public ObservableCollection<FundTrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<FundJournalLine> Journal { get; } = [];
    public ObservableCollection<FundAccountSummary> Accounts { get; } = [];
    public ObservableCollection<BankAccountSnapshot> BankSnapshots { get; } = [];
    public ObservableCollection<FundPortfolioPosition> PortfolioPositions { get; } = [];
    public ObservableCollection<CashFlowEntryDto> CashFlowEntries { get; } = [];
    public ObservableCollection<CashLadderBucketDto> CashFlowBuckets { get; } = [];
    public ObservableCollection<FundTrialBalanceLine> VisibleTrialBalance { get; } = [];
    public ObservableCollection<FundJournalLine> VisibleJournal { get; } = [];
    public ObservableCollection<FundLedgerDimensionView> LedgerDimensions { get; } = [];
    public ObservableCollection<FundReconciliationItem> ReconciliationRuns { get; } = [];
    public ObservableCollection<string> CashFinancingHighlights { get; } = [];
    public ObservableCollection<FundAuditEntry> AuditTrail { get; } = [];
    public ObservableCollection<FundReportAssetClassSectionDto> ReportPackAssetSections { get; } = [];
    public ObservableCollection<FundAccountingRecordEvidenceCategoryRow> AccountingRecordEvidenceCategories { get; } = [];
}

internal sealed class FundLedgerWorkbenchSectionViewModel : BindableBase
{
    private string _currentWorkbenchModeText = "Overview Mode";
    private string _currentWorkbenchTitleText = "Overview Workbench";
    private string _currentWorkbenchSubtitleText = "Fund-wide operating summary, liquidity posture, and exception pressure.";
    private string _routeBannerTitleText = string.Empty;
    private string _routeBannerDetailText = string.Empty;
    private bool _hasRouteBanner;
    private string _reconciliationOwnershipText = "Assign an operator before reconciliation sign-off.";
    private string _reconciliationSnapshotWarningText = "Queue refresh timing is not confirmed. Refresh before resolving breaks or signing off.";
    private string _reportPackOwnershipText = "Accounting operator sign-off is pending.";
    private string _reportPackSnapshotWarningText = "Report-pack freshness is unknown. Refresh the preview before distributing reporting artifacts.";
    private string _accountingRecordStatusText = "Accounting-record evidence is waiting for fund context.";
    private string _accountingRecordSummaryText = "Load Operations Continuity detail to inspect retained source records, normalized activity, reconciliation cases, ledger evidence, approvals, and report-pack lineage.";
    private string _accountingRecordEvidenceText = "0/8 evidence categories complete; 60 sec target not measured";
    private WorkstationStateModel _reportPackReadinessState = WorkstationStateModel.Empty(
        "Report pack waiting for fund context",
        "Select a fund profile and refresh the preview before distributing reporting artifacts.",
        "Refresh Preview",
        "Report Pack");
    private WorkstationStateModel _accountingRecordReadinessState = WorkstationStateModel.Empty(
        "Accounting record waiting for fund context",
        "Select a fund profile to inspect retained source, normalized activity, reconciliation, ledger, approval, and report-pack evidence.",
        "Open Operations Continuity",
        "OperationsContinuity");
    private bool _isReportPackLoading;
    private int _selectedTabIndex;

    public string CurrentWorkbenchModeText { get => _currentWorkbenchModeText; set => SetProperty(ref _currentWorkbenchModeText, value); }
    public string CurrentWorkbenchTitleText { get => _currentWorkbenchTitleText; set => SetProperty(ref _currentWorkbenchTitleText, value); }
    public string CurrentWorkbenchSubtitleText { get => _currentWorkbenchSubtitleText; set => SetProperty(ref _currentWorkbenchSubtitleText, value); }
    public string RouteBannerTitleText { get => _routeBannerTitleText; set => SetProperty(ref _routeBannerTitleText, value); }
    public string RouteBannerDetailText { get => _routeBannerDetailText; set => SetProperty(ref _routeBannerDetailText, value); }
    public bool HasRouteBanner { get => _hasRouteBanner; set => SetProperty(ref _hasRouteBanner, value); }
    public string ReconciliationOwnershipText { get => _reconciliationOwnershipText; set => SetProperty(ref _reconciliationOwnershipText, value); }
    public string ReconciliationSnapshotWarningText { get => _reconciliationSnapshotWarningText; set => SetProperty(ref _reconciliationSnapshotWarningText, value); }
    public string ReportPackOwnershipText { get => _reportPackOwnershipText; set => SetProperty(ref _reportPackOwnershipText, value); }
    public string ReportPackSnapshotWarningText { get => _reportPackSnapshotWarningText; set => SetProperty(ref _reportPackSnapshotWarningText, value); }
    public string AccountingRecordStatusText { get => _accountingRecordStatusText; set => SetProperty(ref _accountingRecordStatusText, value); }
    public string AccountingRecordSummaryText { get => _accountingRecordSummaryText; set => SetProperty(ref _accountingRecordSummaryText, value); }
    public string AccountingRecordEvidenceText { get => _accountingRecordEvidenceText; set => SetProperty(ref _accountingRecordEvidenceText, value); }
    public WorkstationStateModel ReportPackReadinessState { get => _reportPackReadinessState; set => SetProperty(ref _reportPackReadinessState, value); }
    public WorkstationStateModel AccountingRecordReadinessState { get => _accountingRecordReadinessState; set => SetProperty(ref _accountingRecordReadinessState, value); }
    public bool IsReportPackLoading { get => _isReportPackLoading; set => SetProperty(ref _isReportPackLoading, value); }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
}

public sealed class FundReconciliationMatchCandidateRow : BindableBase
{
    private bool _isSelected;

    public FundReconciliationMatchCandidateRow(
        string rowKey,
        string checkLabel,
        string sourceLabel,
        string amountText,
        string asOfText,
        string statusLabel,
        string reason,
        bool isHighlighted)
    {
        RowKey = rowKey;
        CheckLabel = checkLabel;
        SourceLabel = sourceLabel;
        AmountText = amountText;
        AsOfText = asOfText;
        StatusLabel = statusLabel;
        Reason = reason;
        IsHighlighted = isHighlighted;
    }

    public string RowKey { get; }
    public string CheckLabel { get; }
    public string SourceLabel { get; }
    public string AmountText { get; }
    public string AsOfText { get; }
    public string StatusLabel { get; }
    public string Reason { get; }
    public bool IsHighlighted { get; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class FundLedgerReconciliationSectionViewModel : BindableBase
{
    public ObservableCollection<FundReconciliationBreakQueueRow> BreakQueueItems { get; } = [];
    public ObservableCollection<FundReconciliationRunRow> RunItems { get; } = [];
    public ObservableCollection<FundReconciliationMatchCandidateRow> EntryRows { get; } = [];
    public ObservableCollection<FundReconciliationMatchCandidateRow> SourceDataRows { get; } = [];
    public ObservableCollection<FundReconciliationCheckDetailRow> ExceptionRows { get; } = [];
    public ObservableCollection<FundReconciliationCheckDetailRow> AllCheckRows { get; } = [];
    public ObservableCollection<FundReconciliationSecurityCoverageRow> SecurityCoverageRows { get; } = [];
    public ObservableCollection<FundReconciliationAuditTrailRow> AuditRows { get; } = [];
    public ObservableCollection<FundReconciliationCalibrationProfileRow> CalibrationProfiles { get; } = [];
    public ObservableCollection<StatementRunWorkbenchRow> StatementRuns { get; } = [];
    public ObservableCollection<StatementValidationIssueRow> StatementValidationIssues { get; } = [];
    public ObservableCollection<StatementUnresolvedBreakRow> StatementUnresolvedBreaks { get; } = [];
    public ObservableCollection<StatementCaseActionRow> StatementCaseActions { get; } = [];

    public IReadOnlyList<FundReconciliationBreakQueueRow> AllBreakQueueItems { get; set; } = [];
    public IReadOnlyList<FundReconciliationRunRow> AllRunItems { get; set; } = [];

    private StatementRunWorkbenchRow? _selectedStatementRun;
    private StatementUnresolvedBreakRow? _selectedStatementBreak;
    private string _statementStatusText = "Statement reconciliation evidence has not been loaded yet.";
    private string _statementRunsText = "0";
    private string _statementValidationIssuesText = "0";
    private string _statementUnresolvedBreaksText = "0";
    private string _statementCasesText = "0";
    private string _statementLastRefreshText = "-";
    private string _statementDetailTitle = "Select a statement run";
    private string _statementDetailSubtitle = "Statement run detail, validation issues, unresolved breaks, and case actions appear after refresh.";
    private string _statementDisabledReasonText = "Select an unresolved statement break to inspect case actions.";
    private WorkstationStateModel _statementSignifierState = WorkstationStateModel.Empty(
        "Select statement evidence",
        "Choose a statement run or unresolved break to see evidence links, recovery actions, readiness tone, and sign-off posture.",
        "Select statement",
        "Statement Reconciliation");

    private FundReconciliationQueueView _selectedQueueView = FundReconciliationQueueView.BreakQueue;
    private FundReconciliationBreakQueueFilter _selectedBreakQueueFilter = FundReconciliationBreakQueueFilter.Open;
    private FundReconciliationScopeFilter _selectedScopeFilter = FundReconciliationScopeFilter.All;
    private int _selectedDetailTabIndex;
    private string _searchText = string.Empty;
    private FundReconciliationBreakQueueRow? _selectedBreakQueueItem;
    private FundReconciliationRunRow? _selectedRun;
    private string _legalEntityText = "-";
    private string _baseCurrencyText = "-";
    private string _scopeText = "-";
    private string _workspaceAsOfText = "-";
    private string _lastRefreshText = "-";
    private string _inReviewBreaksText = "0";
    private string _securityCoverageIssuesText = "0";
    private string _calibrationStatusText = "Not loaded";
    private string _calibrationSummaryText = "Calibration posture appears after the reconciliation workbench loads.";
    private string _calibrationProfilesText = "0";
    private string _calibrationPendingSignoffText = "0";
    private string _calibrationMissingMetadataText = "0";
    private string _actionFeedbackText = string.Empty;
    private string _operatorText = "desktop-user";
    private string _noteText = string.Empty;
    private string _detailTitle = "Select a break or run";
    private string _detailSubtitle = "Choose a break queue item or reconciliation run to load exception detail.";
    private string _detailStatusText = "Awaiting selection";
    private string _detailCoverageText = "Security coverage status appears here when a reconciliation item is selected.";
    private string _detailLifecycleText = "Select a break queue item to see detection, review, decision, and audit posture.";
    private string _detailSignoffText = "Sign-off posture appears here when a reconciliation break is selected.";
    private string _detailLastUpdatedText = "-";
    private string _detailGuidanceText = "Break queue items support inline review and resolution. Account runs stay read-only in Accounting.";
    private string _detailTotalChecksText = "0";
    private string _detailMatchedText = "0";
    private string _detailBreaksText = "0";
    private string _detailBreakAmountText = "-";
    private string _detailSecurityIssuesText = "0";
    private string _matchSelectionText = "Select at least one ledger entry and one source-data row to match.";
    private string _breakQueueEmptyStateText = "No strategy-run breaks are queued for this fund.";
    private string _nextBestActionText = "Select a break to view the recommended next action.";
    private string _blockerReasonText = "No blocker is selected.";
    private string _evidenceLinksText = "Evidence links appear after selecting a break.";
    private string _runsEmptyStateText = "No reconciliation runs are available for this fund.";
    private WorkstationStateModel _accountingSignifierState = WorkstationStateModel.Empty(
        "Select reconciliation evidence",
        "Choose a break queue item or reconciliation run to see lifecycle, sign-off, evidence, and recovery posture.",
        "Select a break",
        "Fund Reconciliation");

    public FundReconciliationQueueView SelectedQueueView { get => _selectedQueueView; set => SetProperty(ref _selectedQueueView, value); }
    public FundReconciliationBreakQueueFilter SelectedBreakQueueFilter { get => _selectedBreakQueueFilter; set => SetProperty(ref _selectedBreakQueueFilter, value); }
    public FundReconciliationScopeFilter SelectedScopeFilter { get => _selectedScopeFilter; set => SetProperty(ref _selectedScopeFilter, value); }
    public int SelectedDetailTabIndex { get => _selectedDetailTabIndex; set => SetProperty(ref _selectedDetailTabIndex, value); }
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public FundReconciliationBreakQueueRow? SelectedBreakQueueItem { get => _selectedBreakQueueItem; set => SetProperty(ref _selectedBreakQueueItem, value); }
    public FundReconciliationRunRow? SelectedRun { get => _selectedRun; set => SetProperty(ref _selectedRun, value); }
    public string LegalEntityText { get => _legalEntityText; set => SetProperty(ref _legalEntityText, value); }
    public string BaseCurrencyText { get => _baseCurrencyText; set => SetProperty(ref _baseCurrencyText, value); }
    public string ScopeText { get => _scopeText; set => SetProperty(ref _scopeText, value); }
    public string WorkspaceAsOfText { get => _workspaceAsOfText; set => SetProperty(ref _workspaceAsOfText, value); }
    public string LastRefreshText { get => _lastRefreshText; set => SetProperty(ref _lastRefreshText, value); }
    public string InReviewBreaksText { get => _inReviewBreaksText; set => SetProperty(ref _inReviewBreaksText, value); }
    public string SecurityCoverageIssuesText { get => _securityCoverageIssuesText; set => SetProperty(ref _securityCoverageIssuesText, value); }
    public string CalibrationStatusText { get => _calibrationStatusText; set => SetProperty(ref _calibrationStatusText, value); }
    public string CalibrationSummaryText { get => _calibrationSummaryText; set => SetProperty(ref _calibrationSummaryText, value); }
    public string CalibrationProfilesText { get => _calibrationProfilesText; set => SetProperty(ref _calibrationProfilesText, value); }
    public string CalibrationPendingSignoffText { get => _calibrationPendingSignoffText; set => SetProperty(ref _calibrationPendingSignoffText, value); }
    public string CalibrationMissingMetadataText { get => _calibrationMissingMetadataText; set => SetProperty(ref _calibrationMissingMetadataText, value); }
    public string ActionFeedbackText { get => _actionFeedbackText; set => SetProperty(ref _actionFeedbackText, value); }
    public string OperatorText { get => _operatorText; set => SetProperty(ref _operatorText, value); }
    public string NoteText { get => _noteText; set => SetProperty(ref _noteText, value); }
    public string DetailTitle { get => _detailTitle; set => SetProperty(ref _detailTitle, value); }
    public string DetailSubtitle { get => _detailSubtitle; set => SetProperty(ref _detailSubtitle, value); }
    public string DetailStatusText { get => _detailStatusText; set => SetProperty(ref _detailStatusText, value); }
    public string DetailCoverageText { get => _detailCoverageText; set => SetProperty(ref _detailCoverageText, value); }
    public string DetailLifecycleText { get => _detailLifecycleText; set => SetProperty(ref _detailLifecycleText, value); }
    public string DetailSignoffText { get => _detailSignoffText; set => SetProperty(ref _detailSignoffText, value); }
    public string DetailLastUpdatedText { get => _detailLastUpdatedText; set => SetProperty(ref _detailLastUpdatedText, value); }
    public string DetailGuidanceText { get => _detailGuidanceText; set => SetProperty(ref _detailGuidanceText, value); }
    public string DetailTotalChecksText { get => _detailTotalChecksText; set => SetProperty(ref _detailTotalChecksText, value); }
    public string DetailMatchedText { get => _detailMatchedText; set => SetProperty(ref _detailMatchedText, value); }
    public string DetailBreaksText { get => _detailBreaksText; set => SetProperty(ref _detailBreaksText, value); }
    public string DetailBreakAmountText { get => _detailBreakAmountText; set => SetProperty(ref _detailBreakAmountText, value); }
    public string DetailSecurityIssuesText { get => _detailSecurityIssuesText; set => SetProperty(ref _detailSecurityIssuesText, value); }
    public string MatchSelectionText { get => _matchSelectionText; set => SetProperty(ref _matchSelectionText, value); }
    public string BreakQueueEmptyStateText { get => _breakQueueEmptyStateText; set => SetProperty(ref _breakQueueEmptyStateText, value); }
    public string NextBestActionText { get => _nextBestActionText; set => SetProperty(ref _nextBestActionText, value); }
    public string BlockerReasonText { get => _blockerReasonText; set => SetProperty(ref _blockerReasonText, value); }
    public string EvidenceLinksText { get => _evidenceLinksText; set => SetProperty(ref _evidenceLinksText, value); }
    public string RunsEmptyStateText { get => _runsEmptyStateText; set => SetProperty(ref _runsEmptyStateText, value); }
    public WorkstationStateModel AccountingSignifierState { get => _accountingSignifierState; set => SetProperty(ref _accountingSignifierState, value); }
    public WorkstationStateModel GovernanceSignifierState { get => AccountingSignifierState; set => AccountingSignifierState = value; }
    public StatementRunWorkbenchRow? SelectedStatementRun { get => _selectedStatementRun; set => SetProperty(ref _selectedStatementRun, value); }
    public StatementUnresolvedBreakRow? SelectedStatementBreak { get => _selectedStatementBreak; set => SetProperty(ref _selectedStatementBreak, value); }
    public string StatementStatusText { get => _statementStatusText; set => SetProperty(ref _statementStatusText, value); }
    public string StatementRunsText { get => _statementRunsText; set => SetProperty(ref _statementRunsText, value); }
    public string StatementValidationIssuesText { get => _statementValidationIssuesText; set => SetProperty(ref _statementValidationIssuesText, value); }
    public string StatementUnresolvedBreaksText { get => _statementUnresolvedBreaksText; set => SetProperty(ref _statementUnresolvedBreaksText, value); }
    public string StatementCasesText { get => _statementCasesText; set => SetProperty(ref _statementCasesText, value); }
    public string StatementLastRefreshText { get => _statementLastRefreshText; set => SetProperty(ref _statementLastRefreshText, value); }
    public string StatementDetailTitle { get => _statementDetailTitle; set => SetProperty(ref _statementDetailTitle, value); }
    public string StatementDetailSubtitle { get => _statementDetailSubtitle; set => SetProperty(ref _statementDetailSubtitle, value); }
    public string StatementDisabledReasonText { get => _statementDisabledReasonText; set => SetProperty(ref _statementDisabledReasonText, value); }
    public WorkstationStateModel StatementSignifierState { get => _statementSignifierState; set => SetProperty(ref _statementSignifierState, value); }
}

public sealed partial class FundLedgerViewModel
{
    private readonly FundLedgerReconciliationSectionViewModel _reconciliationSection = new();

    public FundLedgerReconciliationSectionViewModel ReconciliationSection => _reconciliationSection;

    private bool SetReconciliationSectionProperty<T>(
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
