using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;

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
}

public sealed class FundLedgerReconciliationSectionViewModel : BindableBase
{
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
    private string _detailGuidanceText = "Break queue items support inline review and resolution. Account runs stay read-only in Governance.";
    private string _detailTotalChecksText = "0";
    private string _detailMatchedText = "0";
    private string _detailBreaksText = "0";
    private string _detailBreakAmountText = "-";
    private string _detailSecurityIssuesText = "0";
    private string _breakQueueEmptyStateText = "No strategy-run breaks are queued for this fund.";
    private string _nextBestActionText = "Select a break to view the recommended next action.";
    private string _blockerReasonText = "No blocker is selected.";
    private string _evidenceLinksText = "Evidence links appear after selecting a break.";
    private string _runsEmptyStateText = "No reconciliation runs are available for this fund.";

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
    public string BreakQueueEmptyStateText { get => _breakQueueEmptyStateText; set => SetProperty(ref _breakQueueEmptyStateText, value); }
    public string NextBestActionText { get => _nextBestActionText; set => SetProperty(ref _nextBestActionText, value); }
    public string BlockerReasonText { get => _blockerReasonText; set => SetProperty(ref _blockerReasonText, value); }
    public string EvidenceLinksText { get => _evidenceLinksText; set => SetProperty(ref _evidenceLinksText, value); }
    public string RunsEmptyStateText { get => _runsEmptyStateText; set => SetProperty(ref _runsEmptyStateText, value); }
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
