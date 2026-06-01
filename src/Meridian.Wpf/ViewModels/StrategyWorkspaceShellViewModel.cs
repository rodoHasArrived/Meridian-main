using System.Windows;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    private readonly StrategyWorkspaceShellPresentationService? _presentationService;
    private bool _isStarted;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _totalRunsText = "-";
    private string _promotedText = "-";
    private string _pendingReviewText = "-";
    private string _promotionCountBadgeText = "0";
    private IReadOnlyList<StrategyRunSummaryItem> _recentRuns = Array.Empty<StrategyRunSummaryItem>();
    private IReadOnlyList<StrategyPromotionCandidateItem> _promotionCandidates = Array.Empty<StrategyPromotionCandidateItem>();
    private Visibility _noRunsVisibility = Visibility.Visible;
    private Visibility _noPromotionsVisibility = Visibility.Visible;
    private string _activeRunNameText = "No selected run";
    private string _activeRunMetaText = "Start a backtest or choose a run from history.";
    private string _scenarioStrategyText = "No strategy selected";
    private string _scenarioCoverageText = "No strategy session restored.";
    private string _runStatusText = "Awaiting run selection";
    private string _runPerformanceText = "Compare runs, equity, and fills from a selected strategy run.";
    private string _runCompareText = "Use the bottom history rail to select a run and load detail panels.";
    private string _portfolioPreviewText = "Portfolio inspector opens here once a run is selected.";
    private string _ledgerPreviewText = "Accounting impact preview opens here once a run is selected.";
    private string _riskPreviewText = "Risk and audit preview becomes available after a completed run is selected.";
    private string _briefingSummaryText = "Pinned strategy context, watchlists, saved comparisons, and workflow alerts.";
    private string _briefingGeneratedText = "Updated just now";
    private IReadOnlyList<InsightWidget> _briefingInsights = Array.Empty<InsightWidget>();
    private IReadOnlyList<WorkstationWatchlist> _briefingWatchlists = Array.Empty<WorkstationWatchlist>();
    private IReadOnlyList<StrategyWhatChangedItem> _briefingWhatChanged = Array.Empty<StrategyWhatChangedItem>();
    private IReadOnlyList<StrategyBriefingAlert> _briefingAlerts = Array.Empty<StrategyBriefingAlert>();
    private IReadOnlyList<StrategySavedComparison> _briefingComparisons = Array.Empty<StrategySavedComparison>();
    private Visibility _noBriefingInsightsVisibility = Visibility.Visible;
    private Visibility _noBriefingWatchlistsVisibility = Visibility.Visible;
    private Visibility _noBriefingWhatChangedVisibility = Visibility.Visible;
    private Visibility _noBriefingAlertsVisibility = Visibility.Visible;
    private Visibility _noBriefingComparisonsVisibility = Visibility.Visible;
    private WorkspaceShellContext _shellContext = new();
    private string _strategyHeroFocusText = StrategyWorkspaceShellPresentationDefaults.DeskHero.FocusLabel;
    private string _strategyWorkflowStatusText = StrategyWorkspaceShellPresentationDefaults.DeskHero.Summary;
    private string _strategyWorkflowDetailText = StrategyWorkspaceShellPresentationDefaults.DeskHero.Detail;
    private string _strategyHeroBadgeText = StrategyWorkspaceShellPresentationDefaults.DeskHero.BadgeText;
    private StrategyDeskHeroTone _strategyHeroBadgeTone = StrategyWorkspaceShellPresentationDefaults.DeskHero.BadgeTone;
    private string _strategyHeroActionTitleText = StrategyWorkspaceShellPresentationDefaults.DeskHero.HandoffTitle;
    private string _strategyHeroActionDetailText = StrategyWorkspaceShellPresentationDefaults.DeskHero.HandoffDetail;
    private string _strategyHeroPrimaryActionText = StrategyWorkspaceShellPresentationDefaults.DeskHero.PrimaryActionLabel;
    private string _strategyHeroSecondaryActionText = StrategyWorkspaceShellPresentationDefaults.DeskHero.SecondaryActionLabel;
    private Visibility _strategyHeroSecondaryActionVisibility = Visibility.Visible;
    private string _strategyWorkflowTargetText = StrategyWorkspaceShellPresentationDefaults.DeskHero.TargetLabel;
    private string _strategyWorkflowBlockerLabelText = StrategyWorkspaceShellPresentationDefaults.Workflow.PrimaryBlocker.Label;
    private string _strategyWorkflowBlockerDetailText = StrategyWorkspaceShellPresentationDefaults.Workflow.PrimaryBlocker.Detail;
    private IReadOnlyList<string> _strategyWorkflowEvidenceItems = Array.Empty<string>();
    private ActiveRunContext? _activeRunContext;
    private string _heroPrimaryActionId = StrategyWorkspaceShellPresentationDefaults.DeskHero.PrimaryActionId;
    private string _heroSecondaryActionId = StrategyWorkspaceShellPresentationDefaults.DeskHero.SecondaryActionId;

    public StrategyWorkspaceShellViewModel()
        : this(null, usePresentationService: false)
    {
    }

    public StrategyWorkspaceShellViewModel(StrategyWorkspaceShellPresentationService presentationService)
        : this(presentationService, usePresentationService: true)
    {
    }

    private StrategyWorkspaceShellViewModel(
        StrategyWorkspaceShellPresentationService? presentationService,
        bool usePresentationService)
        : base(ShellNavigationCatalog.GetWorkspaceShell(WorkstationNavigationDefaults.StrategyWorkspaceId)!)
    {
        _presentationService = usePresentationService ? presentationService : null;
        CommandGroup = StrategyWorkspaceShellPresentationService.BuildCommandGroup();
    }

    public event EventHandler? RefreshRequested;

    internal event EventHandler<StrategyWorkspaceShellActionRequest>? ActionRequested;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string TotalRunsText
    {
        get => _totalRunsText;
        private set => SetProperty(ref _totalRunsText, value);
    }

    public string PromotedText
    {
        get => _promotedText;
        private set => SetProperty(ref _promotedText, value);
    }

    public string PendingReviewText
    {
        get => _pendingReviewText;
        private set => SetProperty(ref _pendingReviewText, value);
    }

    public string PromotionCountBadgeText
    {
        get => _promotionCountBadgeText;
        private set => SetProperty(ref _promotionCountBadgeText, value);
    }

    public IReadOnlyList<StrategyRunSummaryItem> RecentRuns
    {
        get => _recentRuns;
        private set => SetProperty(ref _recentRuns, value);
    }

    public IReadOnlyList<StrategyPromotionCandidateItem> PromotionCandidates
    {
        get => _promotionCandidates;
        private set => SetProperty(ref _promotionCandidates, value);
    }

    public Visibility NoRunsVisibility
    {
        get => _noRunsVisibility;
        private set => SetProperty(ref _noRunsVisibility, value);
    }

    public Visibility NoPromotionsVisibility
    {
        get => _noPromotionsVisibility;
        private set => SetProperty(ref _noPromotionsVisibility, value);
    }

    public string ActiveRunNameText
    {
        get => _activeRunNameText;
        private set => SetProperty(ref _activeRunNameText, value);
    }

    public string ActiveRunMetaText
    {
        get => _activeRunMetaText;
        private set => SetProperty(ref _activeRunMetaText, value);
    }

    public string ScenarioStrategyText
    {
        get => _scenarioStrategyText;
        private set => SetProperty(ref _scenarioStrategyText, value);
    }

    public string ScenarioCoverageText
    {
        get => _scenarioCoverageText;
        private set => SetProperty(ref _scenarioCoverageText, value);
    }

    public string RunStatusText
    {
        get => _runStatusText;
        private set => SetProperty(ref _runStatusText, value);
    }

    public string RunPerformanceText
    {
        get => _runPerformanceText;
        private set => SetProperty(ref _runPerformanceText, value);
    }

    public string RunCompareText
    {
        get => _runCompareText;
        private set => SetProperty(ref _runCompareText, value);
    }

    public string PortfolioPreviewText
    {
        get => _portfolioPreviewText;
        private set => SetProperty(ref _portfolioPreviewText, value);
    }

    public string LedgerPreviewText
    {
        get => _ledgerPreviewText;
        private set => SetProperty(ref _ledgerPreviewText, value);
    }

    public string RiskPreviewText
    {
        get => _riskPreviewText;
        private set => SetProperty(ref _riskPreviewText, value);
    }

    public string BriefingSummaryText
    {
        get => _briefingSummaryText;
        private set => SetProperty(ref _briefingSummaryText, value);
    }

    public string BriefingGeneratedText
    {
        get => _briefingGeneratedText;
        private set => SetProperty(ref _briefingGeneratedText, value);
    }

    public IReadOnlyList<InsightWidget> BriefingInsights
    {
        get => _briefingInsights;
        private set => SetProperty(ref _briefingInsights, value);
    }

    public IReadOnlyList<WorkstationWatchlist> BriefingWatchlists
    {
        get => _briefingWatchlists;
        private set => SetProperty(ref _briefingWatchlists, value);
    }

    public IReadOnlyList<StrategyWhatChangedItem> BriefingWhatChanged
    {
        get => _briefingWhatChanged;
        private set => SetProperty(ref _briefingWhatChanged, value);
    }

    public IReadOnlyList<StrategyBriefingAlert> BriefingAlerts
    {
        get => _briefingAlerts;
        private set => SetProperty(ref _briefingAlerts, value);
    }

    public IReadOnlyList<StrategySavedComparison> BriefingComparisons
    {
        get => _briefingComparisons;
        private set => SetProperty(ref _briefingComparisons, value);
    }

    public Visibility NoBriefingInsightsVisibility
    {
        get => _noBriefingInsightsVisibility;
        private set => SetProperty(ref _noBriefingInsightsVisibility, value);
    }

    public Visibility NoBriefingWatchlistsVisibility
    {
        get => _noBriefingWatchlistsVisibility;
        private set => SetProperty(ref _noBriefingWatchlistsVisibility, value);
    }

    public Visibility NoBriefingWhatChangedVisibility
    {
        get => _noBriefingWhatChangedVisibility;
        private set => SetProperty(ref _noBriefingWhatChangedVisibility, value);
    }

    public Visibility NoBriefingAlertsVisibility
    {
        get => _noBriefingAlertsVisibility;
        private set => SetProperty(ref _noBriefingAlertsVisibility, value);
    }

    public Visibility NoBriefingComparisonsVisibility
    {
        get => _noBriefingComparisonsVisibility;
        private set => SetProperty(ref _noBriefingComparisonsVisibility, value);
    }

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        private set => SetProperty(ref _shellContext, value);
    }

    public string StrategyHeroFocusText
    {
        get => _strategyHeroFocusText;
        private set => SetProperty(ref _strategyHeroFocusText, value);
    }

    public string StrategyWorkflowStatusText
    {
        get => _strategyWorkflowStatusText;
        private set => SetProperty(ref _strategyWorkflowStatusText, value);
    }

    public string StrategyWorkflowDetailText
    {
        get => _strategyWorkflowDetailText;
        private set => SetProperty(ref _strategyWorkflowDetailText, value);
    }

    public string StrategyHeroBadgeText
    {
        get => _strategyHeroBadgeText;
        private set => SetProperty(ref _strategyHeroBadgeText, value);
    }

    public StrategyDeskHeroTone StrategyHeroBadgeTone
    {
        get => _strategyHeroBadgeTone;
        private set => SetProperty(ref _strategyHeroBadgeTone, value);
    }

    public string StrategyHeroActionTitleText
    {
        get => _strategyHeroActionTitleText;
        private set => SetProperty(ref _strategyHeroActionTitleText, value);
    }

    public string StrategyHeroActionDetailText
    {
        get => _strategyHeroActionDetailText;
        private set => SetProperty(ref _strategyHeroActionDetailText, value);
    }

    public string StrategyHeroPrimaryActionText
    {
        get => _strategyHeroPrimaryActionText;
        private set => SetProperty(ref _strategyHeroPrimaryActionText, value);
    }

    public string StrategyHeroSecondaryActionText
    {
        get => _strategyHeroSecondaryActionText;
        private set => SetProperty(ref _strategyHeroSecondaryActionText, value);
    }

    public Visibility StrategyHeroSecondaryActionVisibility
    {
        get => _strategyHeroSecondaryActionVisibility;
        private set => SetProperty(ref _strategyHeroSecondaryActionVisibility, value);
    }

    public string StrategyWorkflowTargetText
    {
        get => _strategyWorkflowTargetText;
        private set => SetProperty(ref _strategyWorkflowTargetText, value);
    }

    public string StrategyWorkflowBlockerLabelText
    {
        get => _strategyWorkflowBlockerLabelText;
        private set => SetProperty(ref _strategyWorkflowBlockerLabelText, value);
    }

    public string StrategyWorkflowBlockerDetailText
    {
        get => _strategyWorkflowBlockerDetailText;
        private set => SetProperty(ref _strategyWorkflowBlockerDetailText, value);
    }

    public IReadOnlyList<string> StrategyWorkflowEvidenceItems
    {
        get => _strategyWorkflowEvidenceItems;
        private set => SetProperty(ref _strategyWorkflowEvidenceItems, value);
    }

    internal ActiveRunContext? ActiveRunContext => _activeRunContext;

    public async Task StartAsync()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        if (_presentationService is not null)
        {
            _presentationService.PresentationInvalidated += OnPresentationInvalidated;
            _presentationService.Start();
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;
        if (_presentationService is not null)
        {
            _presentationService.PresentationInvalidated -= OnPresentationInvalidated;
            _presentationService.Stop();
        }
    }

    public async Task RefreshAsync()
    {
        if (_presentationService is null)
        {
            ApplyState(new StrategyWorkspaceShellPresentationState
            {
                CommandGroup = StrategyWorkspaceShellPresentationService.BuildCommandGroup()
            });
            return;
        }

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            ApplyState(await _presentationService.BuildAsync().ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[StrategyWorkspaceShell] Refresh failed: {ex.Message}");
            HasError = true;
            ErrorMessage = ex.Message;
            ApplyState(_presentationService.BuildDegradedState());
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ExecuteCommandAction(string actionId)
        => _ = ExecuteCommandActionAsync(actionId);

    internal async Task ExecuteCommandActionAsync(string actionId)
    {
        if (string.Equals(actionId, "PromoteToPaper", StringComparison.Ordinal))
        {
            await PromoteActiveRunAsync().ConfigureAwait(true);
            return;
        }

        RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateActionRequest(actionId, _activeRunContext));
    }

    public void ExecuteHeroPrimaryAction()
        => _ = ExecuteHeroActionAsync(_heroPrimaryActionId);

    public void ExecuteHeroSecondaryAction()
        => _ = ExecuteHeroActionAsync(_heroSecondaryActionId);

    public void OpenRunStudio(string? runId)
        => _ = OpenRunStudioAsync(runId);

    public async Task OpenRunStudioAsync(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        if (_presentationService is not null)
        {
            await _presentationService.SetActiveRunContextAsync(runId).ConfigureAwait(true);
        }

        RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateOpenRunStudioActionRequest(runId));
        await RefreshAsync().ConfigureAwait(true);
    }

    public void ReviewPromotion(string? runId)
        => RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateRunReviewActionRequest(runId));

    public void OpenBriefingAlert(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateActionRequest("StrategyRuns", _activeRunContext));
            return;
        }

        OpenRunStudio(runId);
    }

    public void OpenBriefingComparison(string? runId)
        => _ = OpenBriefingComparisonAsync(runId);

    public async Task OpenBriefingComparisonAsync(string? runId)
    {
        if (!string.IsNullOrWhiteSpace(runId) && _presentationService is not null)
        {
            await _presentationService.SetActiveRunContextAsync(runId).ConfigureAwait(true);
        }

        RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateComparisonActionRequest(runId));
        if (!string.IsNullOrWhiteSpace(runId))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task ExecuteHeroActionAsync(string actionId)
    {
        if (string.Equals(actionId, "PromoteToPaper", StringComparison.Ordinal))
        {
            await PromoteActiveRunAsync().ConfigureAwait(true);
            return;
        }

        RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateActionRequest(actionId, _activeRunContext));
    }

    private async Task PromoteActiveRunAsync()
    {
        if (_activeRunContext is null)
        {
            return;
        }

        if (_presentationService is null)
        {
            RaiseActionRequest(StrategyWorkspaceShellPresentationService.CreateActionRequest("RunDetail", _activeRunContext));
            return;
        }

        var request = await _presentationService.PromoteActiveRunAsync(_activeRunContext).ConfigureAwait(true);
        RaiseActionRequest(request);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void ApplyState(StrategyWorkspaceShellPresentationState state)
    {
        TotalRunsText = state.TotalRunsText;
        PromotedText = state.PromotedText;
        PendingReviewText = state.PendingReviewText;
        PromotionCountBadgeText = state.PromotionCountBadgeText;
        RecentRuns = state.RecentRuns;
        PromotionCandidates = state.PromotionCandidates;
        NoRunsVisibility = state.RecentRuns.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoPromotionsVisibility = state.PromotionCandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveRunNameText = state.ActiveRunNameText;
        ActiveRunMetaText = state.ActiveRunMetaText;
        ScenarioStrategyText = state.ScenarioStrategyText;
        ScenarioCoverageText = state.ScenarioCoverageText;
        RunStatusText = state.RunStatusText;
        RunPerformanceText = state.RunPerformanceText;
        RunCompareText = state.RunCompareText;
        PortfolioPreviewText = state.PortfolioPreviewText;
        LedgerPreviewText = state.LedgerPreviewText;
        RiskPreviewText = state.RiskPreviewText;
        BriefingSummaryText = state.BriefingSummaryText;
        BriefingGeneratedText = state.BriefingGeneratedText;
        BriefingInsights = state.BriefingInsights;
        BriefingWatchlists = state.BriefingWatchlists;
        BriefingWhatChanged = state.BriefingWhatChanged;
        BriefingAlerts = state.BriefingAlerts;
        BriefingComparisons = state.BriefingComparisons;
        NoBriefingInsightsVisibility = state.BriefingInsights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoBriefingWatchlistsVisibility = state.BriefingWatchlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoBriefingWhatChangedVisibility = state.BriefingWhatChanged.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoBriefingAlertsVisibility = state.BriefingAlerts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoBriefingComparisonsVisibility = state.BriefingComparisons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShellContext = state.ShellContext;
        CommandGroup = state.CommandGroup;
        _activeRunContext = state.ActiveRunContext;
        ApplyWorkflow(state.Workflow);
        ApplyDeskHero(state.DeskHero);
    }

    private void ApplyWorkflow(WorkspaceWorkflowSummary workflow)
    {
        StrategyWorkflowBlockerLabelText = workflow.PrimaryBlocker.Label;
        StrategyWorkflowBlockerDetailText = workflow.PrimaryBlocker.Detail;
        StrategyWorkflowEvidenceItems = workflow.Evidence
            .Select(static evidence => $"{evidence.Label}: {evidence.Value}")
            .ToArray();
    }

    private void ApplyDeskHero(StrategyDeskHeroState hero)
    {
        StrategyHeroFocusText = hero.FocusLabel;
        StrategyWorkflowStatusText = hero.Summary;
        StrategyWorkflowDetailText = hero.Detail;
        StrategyHeroBadgeText = hero.BadgeText;
        StrategyHeroBadgeTone = hero.BadgeTone;
        StrategyHeroActionTitleText = hero.HandoffTitle;
        StrategyHeroActionDetailText = hero.HandoffDetail;
        StrategyHeroPrimaryActionText = hero.PrimaryActionLabel;
        StrategyHeroSecondaryActionText = hero.SecondaryActionLabel;
        StrategyHeroSecondaryActionVisibility = string.IsNullOrWhiteSpace(hero.SecondaryActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        StrategyWorkflowTargetText = hero.TargetLabel;
        _heroPrimaryActionId = hero.PrimaryActionId;
        _heroSecondaryActionId = hero.SecondaryActionId;
    }

    private void RaiseActionRequest(StrategyWorkspaceShellActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ActionId))
        {
            return;
        }

        ActionRequested?.Invoke(this, request);
    }

    private void OnPresentationInvalidated(object? sender, EventArgs e)
        => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
