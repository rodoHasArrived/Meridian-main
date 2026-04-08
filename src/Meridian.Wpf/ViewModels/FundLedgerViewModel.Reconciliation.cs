using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    private const string DefaultReconciliationOperator = "desktop-user";

    private readonly ObservableCollection<FundReconciliationBreakQueueRow> _reconciliationBreakQueueItems = [];
    private readonly ObservableCollection<FundReconciliationRunRow> _reconciliationRunItems = [];
    private readonly ObservableCollection<FundReconciliationCheckDetailRow> _reconciliationExceptionRows = [];
    private readonly ObservableCollection<FundReconciliationCheckDetailRow> _reconciliationAllCheckRows = [];
    private readonly ObservableCollection<FundReconciliationSecurityCoverageRow> _reconciliationSecurityCoverageRows = [];
    private readonly ObservableCollection<FundReconciliationAuditTrailRow> _reconciliationAuditRows = [];

    private IReadOnlyList<FundReconciliationBreakQueueRow> _allReconciliationBreakQueueItems = [];
    private IReadOnlyList<FundReconciliationRunRow> _allReconciliationRunItems = [];
    private FundReconciliationDetailModel? _currentReconciliationDetail;
    private CancellationTokenSource? _reconciliationDetailCts;

    private FundReconciliationQueueView _selectedReconciliationQueueView = FundReconciliationQueueView.BreakQueue;
    private FundReconciliationBreakQueueFilter _selectedReconciliationBreakQueueFilter = FundReconciliationBreakQueueFilter.Open;
    private FundReconciliationScopeFilter _selectedReconciliationScopeFilter = FundReconciliationScopeFilter.All;
    private bool _isApplyingReconciliationSelection;
    private bool _isReconciliationRefreshInFlight;
    private bool _isReconciliationDetailLoading;
    private bool _isReconciliationActionInFlight;
    private int _selectedReconciliationDetailTabIndex;
    private string _reconciliationSearchText = string.Empty;
    private string _reconciliationLegalEntityText = "-";
    private string _reconciliationBaseCurrencyText = "-";
    private string _reconciliationScopeText = "-";
    private string _reconciliationWorkspaceAsOfText = "-";
    private string _reconciliationLastRefreshText = "-";
    private string _inReviewBreaksText = "0";
    private string _reconciliationSecurityCoverageIssuesText = "0";
    private string _reconciliationActionFeedbackText = string.Empty;
    private string _reconciliationOperatorText = DefaultReconciliationOperator;
    private string _reconciliationNoteText = string.Empty;
    private string _reconciliationDetailTitle = "Select a break or run";
    private string _reconciliationDetailSubtitle = "Choose a break queue item or reconciliation run to load exception detail.";
    private string _reconciliationDetailStatusText = "Awaiting selection";
    private string _reconciliationDetailCoverageText = "Security coverage status appears here when a reconciliation item is selected.";
    private string _reconciliationDetailLastUpdatedText = "-";
    private string _reconciliationDetailGuidanceText = "Break queue items support inline review and resolution. Account runs stay read-only in Governance.";
    private string _reconciliationDetailTotalChecksText = "0";
    private string _reconciliationDetailMatchedText = "0";
    private string _reconciliationDetailBreaksText = "0";
    private string _reconciliationDetailBreakAmountText = "-";
    private string _reconciliationDetailSecurityIssuesText = "0";
    private string _reconciliationBreakQueueEmptyStateText = "No strategy-run breaks are queued for this fund.";
    private string _reconciliationRunsEmptyStateText = "No reconciliation runs are available for this fund.";
    private FundReconciliationBreakQueueRow? _selectedBreakQueueItem;
    private FundReconciliationRunRow? _selectedReconciliationRun;

    public ObservableCollection<FundReconciliationBreakQueueRow> ReconciliationBreakQueueItems => _reconciliationBreakQueueItems;

    public ObservableCollection<FundReconciliationRunRow> ReconciliationRunItems => _reconciliationRunItems;

    public ObservableCollection<FundReconciliationCheckDetailRow> ReconciliationExceptionRows => _reconciliationExceptionRows;

    public ObservableCollection<FundReconciliationCheckDetailRow> ReconciliationAllCheckRows => _reconciliationAllCheckRows;

    public ObservableCollection<FundReconciliationSecurityCoverageRow> ReconciliationSecurityCoverageRows => _reconciliationSecurityCoverageRows;

    public ObservableCollection<FundReconciliationAuditTrailRow> ReconciliationAuditRows => _reconciliationAuditRows;

    public IRelayCommand OpenSelectedReconciliationAccountWorkflowCommand { get; private set; } = null!;

    public int SelectedReconciliationQueueIndex
    {
        get => (int)_selectedReconciliationQueueView;
        set
        {
            var normalized = value <= 0
                ? FundReconciliationQueueView.BreakQueue
                : FundReconciliationQueueView.Runs;

            if (_selectedReconciliationQueueView == normalized)
            {
                return;
            }

            var previousActiveKey = GetActiveReconciliationSelectionKey();
            _selectedReconciliationQueueView = normalized;
            RaisePropertyChanged();
            ApplyReconciliationFiltersAndSelection(previousActiveKey, forceReload: true);
        }
    }

    public int SelectedReconciliationScopeFilterIndex
    {
        get => (int)_selectedReconciliationScopeFilter;
        set
        {
            var normalized = value switch
            {
                1 => FundReconciliationScopeFilter.Strategy,
                2 => FundReconciliationScopeFilter.Account,
                _ => FundReconciliationScopeFilter.All
            };

            if (_selectedReconciliationScopeFilter == normalized)
            {
                return;
            }

            var previousActiveKey = GetActiveReconciliationSelectionKey();
            _selectedReconciliationScopeFilter = normalized;
            RaisePropertyChanged();
            ApplyReconciliationFiltersAndSelection(previousActiveKey);
        }
    }

    public int SelectedReconciliationDetailTabIndex
    {
        get => _selectedReconciliationDetailTabIndex;
        set => SetProperty(ref _selectedReconciliationDetailTabIndex, value);
    }

    public FundReconciliationBreakQueueRow? SelectedBreakQueueItem
    {
        get => _selectedBreakQueueItem;
        set
        {
            if (!SetProperty(ref _selectedBreakQueueItem, value))
            {
                return;
            }

            NotifyReconciliationDerivedStateChanged();
            if (!_isApplyingReconciliationSelection && _selectedReconciliationQueueView == FundReconciliationQueueView.BreakQueue)
            {
                _ = LoadSelectedReconciliationDetailAsync();
            }
        }
    }

    public FundReconciliationRunRow? SelectedReconciliationRun
    {
        get => _selectedReconciliationRun;
        set
        {
            if (!SetProperty(ref _selectedReconciliationRun, value))
            {
                return;
            }

            NotifyReconciliationDerivedStateChanged();
            if (!_isApplyingReconciliationSelection && _selectedReconciliationQueueView == FundReconciliationQueueView.Runs)
            {
                _ = LoadSelectedReconciliationDetailAsync();
            }
        }
    }

    public string ReconciliationSearchText
    {
        get => _reconciliationSearchText;
        set
        {
            if (!SetProperty(ref _reconciliationSearchText, value))
            {
                return;
            }

            var previousActiveKey = GetActiveReconciliationSelectionKey();
            ApplyReconciliationFiltersAndSelection(previousActiveKey);
        }
    }

    public string ReconciliationLegalEntityText
    {
        get => _reconciliationLegalEntityText;
        private set => SetProperty(ref _reconciliationLegalEntityText, value);
    }

    public string ReconciliationBaseCurrencyText
    {
        get => _reconciliationBaseCurrencyText;
        private set => SetProperty(ref _reconciliationBaseCurrencyText, value);
    }

    public string ReconciliationScopeText
    {
        get => _reconciliationScopeText;
        private set => SetProperty(ref _reconciliationScopeText, value);
    }

    public string ReconciliationWorkspaceAsOfText
    {
        get => _reconciliationWorkspaceAsOfText;
        private set => SetProperty(ref _reconciliationWorkspaceAsOfText, value);
    }

    public string ReconciliationLastRefreshText
    {
        get => _reconciliationLastRefreshText;
        private set => SetProperty(ref _reconciliationLastRefreshText, value);
    }

    public string InReviewBreaksText
    {
        get => _inReviewBreaksText;
        private set => SetProperty(ref _inReviewBreaksText, value);
    }

    public string ReconciliationSecurityCoverageIssuesText
    {
        get => _reconciliationSecurityCoverageIssuesText;
        private set => SetProperty(ref _reconciliationSecurityCoverageIssuesText, value);
    }

    public string ReconciliationActionFeedbackText
    {
        get => _reconciliationActionFeedbackText;
        private set => SetProperty(ref _reconciliationActionFeedbackText, value);
    }

    public string ReconciliationOperatorText
    {
        get => _reconciliationOperatorText;
        set
        {
            if (SetProperty(ref _reconciliationOperatorText, value))
            {
                NotifyReconciliationDerivedStateChanged();
            }
        }
    }

    public string ReconciliationNoteText
    {
        get => _reconciliationNoteText;
        set
        {
            if (SetProperty(ref _reconciliationNoteText, value))
            {
                NotifyReconciliationDerivedStateChanged();
            }
        }
    }

    public string ReconciliationDetailTitle
    {
        get => _reconciliationDetailTitle;
        private set => SetProperty(ref _reconciliationDetailTitle, value);
    }

    public string ReconciliationDetailSubtitle
    {
        get => _reconciliationDetailSubtitle;
        private set => SetProperty(ref _reconciliationDetailSubtitle, value);
    }

    public string ReconciliationDetailStatusText
    {
        get => _reconciliationDetailStatusText;
        private set => SetProperty(ref _reconciliationDetailStatusText, value);
    }

    public string ReconciliationDetailCoverageText
    {
        get => _reconciliationDetailCoverageText;
        private set => SetProperty(ref _reconciliationDetailCoverageText, value);
    }

    public string ReconciliationDetailLastUpdatedText
    {
        get => _reconciliationDetailLastUpdatedText;
        private set => SetProperty(ref _reconciliationDetailLastUpdatedText, value);
    }

    public string ReconciliationDetailGuidanceText
    {
        get => _reconciliationDetailGuidanceText;
        private set => SetProperty(ref _reconciliationDetailGuidanceText, value);
    }

    public string ReconciliationDetailTotalChecksText
    {
        get => _reconciliationDetailTotalChecksText;
        private set => SetProperty(ref _reconciliationDetailTotalChecksText, value);
    }

    public string ReconciliationDetailMatchedText
    {
        get => _reconciliationDetailMatchedText;
        private set => SetProperty(ref _reconciliationDetailMatchedText, value);
    }

    public string ReconciliationDetailBreaksText
    {
        get => _reconciliationDetailBreaksText;
        private set => SetProperty(ref _reconciliationDetailBreaksText, value);
    }

    public string ReconciliationDetailBreakAmountText
    {
        get => _reconciliationDetailBreakAmountText;
        private set => SetProperty(ref _reconciliationDetailBreakAmountText, value);
    }

    public string ReconciliationDetailSecurityIssuesText
    {
        get => _reconciliationDetailSecurityIssuesText;
        private set => SetProperty(ref _reconciliationDetailSecurityIssuesText, value);
    }

    public string ReconciliationBreakQueueEmptyStateText
    {
        get => _reconciliationBreakQueueEmptyStateText;
        private set => SetProperty(ref _reconciliationBreakQueueEmptyStateText, value);
    }

    public string ReconciliationRunsEmptyStateText
    {
        get => _reconciliationRunsEmptyStateText;
        private set => SetProperty(ref _reconciliationRunsEmptyStateText, value);
    }

    public bool IsOpenBreakQueueFilterSelected
    {
        get => _selectedReconciliationBreakQueueFilter == FundReconciliationBreakQueueFilter.Open;
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
        get => _selectedReconciliationBreakQueueFilter == FundReconciliationBreakQueueFilter.InReview;
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
        get => _selectedReconciliationBreakQueueFilter == FundReconciliationBreakQueueFilter.All;
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
        _selectedReconciliationQueueView == FundReconciliationQueueView.BreakQueue;

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

        ResetReconciliationWorkbenchState();
    }

    private Task ApplyReconciliationWorkbenchAsync(
        FundProfileDetail activeFund,
        FundReconciliationWorkbenchSnapshot snapshot,
        CancellationToken ct)
    {
        _allReconciliationBreakQueueItems = snapshot.BreakQueueItems;
        _allReconciliationRunItems = snapshot.RunRows;

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

            if (result.Item is not null && !MatchesBreakQueueFilter(result.Item.Status, _selectedReconciliationBreakQueueFilter))
            {
                _selectedReconciliationBreakQueueFilter = FundReconciliationBreakQueueFilter.All;
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
        var filteredBreaks = FilterBreakQueueItems(_allReconciliationBreakQueueItems).ToArray();
        var filteredRuns = FilterRunItems(_allReconciliationRunItems).ToArray();
        var preferredBreakId = SelectedBreakQueueItem?.BreakId;
        var preferredRunKey = SelectedReconciliationRun?.RowKey;

        _isApplyingReconciliationSelection = true;
        try
        {
            SynchronizeCollection(_reconciliationBreakQueueItems, filteredBreaks);
            SynchronizeCollection(_reconciliationRunItems, filteredRuns);

            SetProperty(
                ref _selectedBreakQueueItem,
                ResolveSelection(filteredBreaks, preferredBreakId, static item => item.BreakId),
                nameof(SelectedBreakQueueItem));
            SetProperty(
                ref _selectedReconciliationRun,
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
        if (_selectedReconciliationScopeFilter == FundReconciliationScopeFilter.Account)
        {
            return [];
        }

        var query = ReconciliationSearchText.Trim();
        return rows.Where(item =>
            MatchesBreakQueueFilter(item.Status, _selectedReconciliationBreakQueueFilter) &&
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
            MatchesScopeFilter(item.SourceType, _selectedReconciliationScopeFilter) &&
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

        var selectedBreak = _selectedReconciliationQueueView == FundReconciliationQueueView.BreakQueue
            ? SelectedBreakQueueItem
            : null;
        var selectedRun = _selectedReconciliationQueueView == FundReconciliationQueueView.Runs
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
        ReconciliationDetailLastUpdatedText = detail.LastUpdatedText;
        ReconciliationDetailGuidanceText = detail.SourceType == FundReconciliationSourceType.AccountRun
            ? "Account-level reconciliation is read-only in Governance. Open the account workflow to rerun or resolve it."
            : detail.SupportsBreakActions
                ? "Inline break actions update the shared workstation queue and keep operator notes attached to the exception."
                : "Select a break from Break Queue to start review, resolve, or dismiss it inline.";
        ReconciliationDetailTotalChecksText = detail.TotalChecks.ToString("N0");
        ReconciliationDetailMatchedText = detail.TotalMatched.ToString("N0");
        ReconciliationDetailBreaksText = detail.TotalBreaks.ToString("N0");
        ReconciliationDetailBreakAmountText = FormatCurrency(detail.BreakAmountTotal, baseCurrency);
        ReconciliationDetailSecurityIssuesText = detail.SecurityIssueCount.ToString("N0");

        SynchronizeCollection(_reconciliationExceptionRows, detail.ExceptionRows);
        SynchronizeCollection(_reconciliationAllCheckRows, detail.AllCheckRows);
        SynchronizeCollection(_reconciliationSecurityCoverageRows, detail.SecurityCoverageRows);
        SynchronizeCollection(_reconciliationAuditRows, detail.AuditRows);

        NotifyReconciliationDerivedStateChanged();
    }

    private void ClearReconciliationDetail()
    {
        _currentReconciliationDetail = null;
        ReconciliationDetailTitle = "Select a break or run";
        ReconciliationDetailSubtitle = "Choose a break queue item or reconciliation run to inspect exceptions, coverage, and audit history.";
        ReconciliationDetailStatusText = "Awaiting selection";
        ReconciliationDetailCoverageText = "Security coverage status appears here when a reconciliation item is selected.";
        ReconciliationDetailLastUpdatedText = "-";
        ReconciliationDetailGuidanceText = "Break queue items support inline review and resolution. Account runs stay read-only in Governance.";
        ReconciliationDetailTotalChecksText = "0";
        ReconciliationDetailMatchedText = "0";
        ReconciliationDetailBreaksText = "0";
        ReconciliationDetailBreakAmountText = "-";
        ReconciliationDetailSecurityIssuesText = "0";
        _reconciliationExceptionRows.Clear();
        _reconciliationAllCheckRows.Clear();
        _reconciliationSecurityCoverageRows.Clear();
        _reconciliationAuditRows.Clear();
        NotifyReconciliationDerivedStateChanged();
    }

    private void ResetReconciliationWorkbenchState()
    {
        CancelReconciliationDetailLoad();
        _allReconciliationBreakQueueItems = [];
        _allReconciliationRunItems = [];

        _isApplyingReconciliationSelection = true;
        try
        {
            _selectedReconciliationQueueView = FundReconciliationQueueView.BreakQueue;
            _selectedReconciliationBreakQueueFilter = FundReconciliationBreakQueueFilter.Open;
            _selectedReconciliationScopeFilter = FundReconciliationScopeFilter.All;
            _selectedReconciliationDetailTabIndex = 0;
            _reconciliationSearchText = string.Empty;
            _selectedBreakQueueItem = null;
            _selectedReconciliationRun = null;
            _reconciliationOperatorText = DefaultReconciliationOperator;
            _reconciliationNoteText = string.Empty;
            _reconciliationActionFeedbackText = string.Empty;
            _reconciliationBreakQueueItems.Clear();
            _reconciliationRunItems.Clear();
            _reconciliationExceptionRows.Clear();
            _reconciliationAllCheckRows.Clear();
            _reconciliationSecurityCoverageRows.Clear();
            _reconciliationAuditRows.Clear();
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
    }

    private void DisposeReconciliationWorkbench()
    {
        CancelReconciliationDetailLoad();
    }

    private void CancelReconciliationDetailLoad()
    {
        try
        {
            _reconciliationDetailCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
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

    private void SetBreakQueueFilter(FundReconciliationBreakQueueFilter filter)
    {
        if (_selectedReconciliationBreakQueueFilter == filter)
        {
            return;
        }

        var previousActiveKey = GetActiveReconciliationSelectionKey();
        _selectedReconciliationBreakQueueFilter = filter;
        RaisePropertyChanged(nameof(IsOpenBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsInReviewBreakQueueFilterSelected));
        RaisePropertyChanged(nameof(IsAllBreakQueueFilterSelected));
        ApplyReconciliationFiltersAndSelection(previousActiveKey);
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
        RaisePropertyChanged(nameof(CanDismissSelectedBreak));
        RaisePropertyChanged(nameof(CanOpenSelectedReconciliationAccountWorkflow));
        RaisePropertyChanged(nameof(HasReconciliationDetail));
        RaisePropertyChanged(nameof(IsReconciliationBusy));
        OpenSelectedReconciliationAccountWorkflowCommand.NotifyCanExecuteChanged();
    }

    private void UpdateReconciliationEmptyStateText(int filteredBreakCount, int filteredRunCount)
    {
        ReconciliationBreakQueueEmptyStateText = filteredBreakCount > 0
            ? string.Empty
            : _selectedReconciliationScopeFilter == FundReconciliationScopeFilter.Account
                ? "Break Queue is strategy-scoped only. Switch the scope filter back to Strategy or All to review queue items."
                : _allReconciliationBreakQueueItems.Count == 0
                    ? "No strategy-run breaks are queued for this fund."
                    : "No break queue items match the current filter.";

        ReconciliationRunsEmptyStateText = filteredRunCount > 0
            ? string.Empty
            : _allReconciliationRunItems.Count == 0
                ? "No reconciliation runs are available for this fund."
                : "No runs match the current scope or search filter.";
    }

    private string? GetActiveReconciliationSelectionKey()
        => _selectedReconciliationQueueView switch
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
