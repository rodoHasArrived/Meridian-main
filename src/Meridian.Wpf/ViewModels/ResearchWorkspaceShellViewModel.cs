using System.Windows;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.ViewModels;

public sealed class ResearchWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    private readonly ResearchWorkspaceShellPresentationService? _presentationService;
    private bool _isStarted;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _totalRunsText = "-";
    private string _promotedText = "-";
    private string _pendingReviewText = "-";
    private string _promotionCountBadgeText = "0";
    private IReadOnlyList<ResearchRunSummaryItem> _recentRuns = Array.Empty<ResearchRunSummaryItem>();
    private IReadOnlyList<ResearchPromotionCandidateItem> _promotionCandidates = Array.Empty<ResearchPromotionCandidateItem>();
    private Visibility _noRunsVisibility = Visibility.Visible;
    private Visibility _noPromotionsVisibility = Visibility.Visible;
    private string _activeRunNameText = "No selected run";
    private string _activeRunMetaText = "Start a backtest or choose a run from history.";
    private string _scenarioStrategyText = "No strategy selected";
    private string _scenarioCoverageText = "No research session restored.";
    private string _runStatusText = "Awaiting run selection";
    private string _runPerformanceText = "Compare runs, equity, and fills from a selected strategy run.";
    private string _runCompareText = "Use the bottom history rail to select a run and load detail panels.";
    private string _portfolioPreviewText = "Portfolio inspector opens here once a run is selected.";
    private string _ledgerPreviewText = "Accounting impact preview opens here once a run is selected.";
    private string _riskPreviewText = "Risk and audit preview becomes available after a completed run is selected.";
    private string _briefingSummaryText = "Pinned research context, watchlists, saved comparisons, and workflow alerts.";
    private string _briefingGeneratedText = "Updated just now";
    private IReadOnlyList<InsightWidget> _briefingInsights = Array.Empty<InsightWidget>();
    private IReadOnlyList<WorkstationWatchlist> _briefingWatchlists = Array.Empty<WorkstationWatchlist>();
    private IReadOnlyList<ResearchWhatChangedItem> _briefingWhatChanged = Array.Empty<ResearchWhatChangedItem>();
    private IReadOnlyList<ResearchBriefingAlert> _briefingAlerts = Array.Empty<ResearchBriefingAlert>();
    private IReadOnlyList<ResearchSavedComparison> _briefingComparisons = Array.Empty<ResearchSavedComparison>();
    private Visibility _noBriefingInsightsVisibility = Visibility.Visible;
    private Visibility _noBriefingWatchlistsVisibility = Visibility.Visible;
    private Visibility _noBriefingWhatChangedVisibility = Visibility.Visible;
    private Visibility _noBriefingAlertsVisibility = Visibility.Visible;
    private Visibility _noBriefingComparisonsVisibility = Visibility.Visible;
    private WorkspaceShellContext _shellContext = new();
    private string _researchHeroFocusText = ResearchWorkspaceShellPresentationDefaults.DeskHero.FocusLabel;
    private string _researchWorkflowStatusText = ResearchWorkspaceShellPresentationDefaults.DeskHero.Summary;
    private string _researchWorkflowDetailText = ResearchWorkspaceShellPresentationDefaults.DeskHero.Detail;
    private string _researchHeroBadgeText = ResearchWorkspaceShellPresentationDefaults.DeskHero.BadgeText;
    private ResearchDeskHeroTone _researchHeroBadgeTone = ResearchWorkspaceShellPresentationDefaults.DeskHero.BadgeTone;
    private string _researchHeroActionTitleText = ResearchWorkspaceShellPresentationDefaults.DeskHero.HandoffTitle;
    private string _researchHeroActionDetailText = ResearchWorkspaceShellPresentationDefaults.DeskHero.HandoffDetail;
    private string _researchHeroPrimaryActionText = ResearchWorkspaceShellPresentationDefaults.DeskHero.PrimaryActionLabel;
    private string _researchHeroSecondaryActionText = ResearchWorkspaceShellPresentationDefaults.DeskHero.SecondaryActionLabel;
    private Visibility _researchHeroSecondaryActionVisibility = Visibility.Visible;
    private string _researchWorkflowTargetText = ResearchWorkspaceShellPresentationDefaults.DeskHero.TargetLabel;
    private string _researchWorkflowBlockerLabelText = ResearchWorkspaceShellPresentationDefaults.Workflow.PrimaryBlocker.Label;
    private string _researchWorkflowBlockerDetailText = ResearchWorkspaceShellPresentationDefaults.Workflow.PrimaryBlocker.Detail;
    private IReadOnlyList<string> _researchWorkflowEvidenceItems = Array.Empty<string>();
    private ActiveRunContext? _activeRunContext;
    private string _heroPrimaryActionId = ResearchWorkspaceShellPresentationDefaults.DeskHero.PrimaryActionId;
    private string _heroSecondaryActionId = ResearchWorkspaceShellPresentationDefaults.DeskHero.SecondaryActionId;

    public ResearchWorkspaceShellViewModel()
        : this(null, usePresentationService: false)
    {
    }

    public ResearchWorkspaceShellViewModel(ResearchWorkspaceShellPresentationService presentationService)
        : this(presentationService, usePresentationService: true)
    {
    }

    private ResearchWorkspaceShellViewModel(
        ResearchWorkspaceShellPresentationService? presentationService,
        bool usePresentationService)
        : base(ShellNavigationCatalog.GetWorkspaceShell("research")
            ?? ShellNavigationCatalog.GetWorkspaceShell("strategy")!)
    {
        _presentationService = usePresentationService ? presentationService : null;
        CommandGroup = ResearchWorkspaceShellPresentationService.BuildCommandGroup();
    }

    public event EventHandler? RefreshRequested;

    internal event EventHandler<ResearchWorkspaceShellActionRequest>? ActionRequested;

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

    public IReadOnlyList<ResearchRunSummaryItem> RecentRuns
    {
        get => _recentRuns;
        private set => SetProperty(ref _recentRuns, value);
    }

    public IReadOnlyList<ResearchPromotionCandidateItem> PromotionCandidates
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

    public IReadOnlyList<ResearchWhatChangedItem> BriefingWhatChanged
    {
        get => _briefingWhatChanged;
        private set => SetProperty(ref _briefingWhatChanged, value);
    }

    public IReadOnlyList<ResearchBriefingAlert> BriefingAlerts
    {
        get => _briefingAlerts;
        private set => SetProperty(ref _briefingAlerts, value);
    }

    public IReadOnlyList<ResearchSavedComparison> BriefingComparisons
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

    public string ResearchHeroFocusText
    {
        get => _researchHeroFocusText;
        private set => SetProperty(ref _researchHeroFocusText, value);
    }

    public string ResearchWorkflowStatusText
    {
        get => _researchWorkflowStatusText;
        private set => SetProperty(ref _researchWorkflowStatusText, value);
    }

    public string ResearchWorkflowDetailText
    {
        get => _researchWorkflowDetailText;
        private set => SetProperty(ref _researchWorkflowDetailText, value);
    }

    public string ResearchHeroBadgeText
    {
        get => _researchHeroBadgeText;
        private set => SetProperty(ref _researchHeroBadgeText, value);
    }

    public ResearchDeskHeroTone ResearchHeroBadgeTone
    {
        get => _researchHeroBadgeTone;
        private set => SetProperty(ref _researchHeroBadgeTone, value);
    }

    public string ResearchHeroActionTitleText
    {
        get => _researchHeroActionTitleText;
        private set => SetProperty(ref _researchHeroActionTitleText, value);
    }

    public string ResearchHeroActionDetailText
    {
        get => _researchHeroActionDetailText;
        private set => SetProperty(ref _researchHeroActionDetailText, value);
    }

    public string ResearchHeroPrimaryActionText
    {
        get => _researchHeroPrimaryActionText;
        private set => SetProperty(ref _researchHeroPrimaryActionText, value);
    }

    public string ResearchHeroSecondaryActionText
    {
        get => _researchHeroSecondaryActionText;
        private set => SetProperty(ref _researchHeroSecondaryActionText, value);
    }

    public Visibility ResearchHeroSecondaryActionVisibility
    {
        get => _researchHeroSecondaryActionVisibility;
        private set => SetProperty(ref _researchHeroSecondaryActionVisibility, value);
    }

    public string ResearchWorkflowTargetText
    {
        get => _researchWorkflowTargetText;
        private set => SetProperty(ref _researchWorkflowTargetText, value);
    }

    public string ResearchWorkflowBlockerLabelText
    {
        get => _researchWorkflowBlockerLabelText;
        private set => SetProperty(ref _researchWorkflowBlockerLabelText, value);
    }

    public string ResearchWorkflowBlockerDetailText
    {
        get => _researchWorkflowBlockerDetailText;
        private set => SetProperty(ref _researchWorkflowBlockerDetailText, value);
    }

    public IReadOnlyList<string> ResearchWorkflowEvidenceItems
    {
        get => _researchWorkflowEvidenceItems;
        private set => SetProperty(ref _researchWorkflowEvidenceItems, value);
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
            ApplyState(new ResearchWorkspaceShellPresentationState
            {
                CommandGroup = ResearchWorkspaceShellPresentationService.BuildCommandGroup()
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
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Refresh failed: {ex.Message}");
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

        RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateActionRequest(actionId, _activeRunContext));
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

        RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateOpenRunStudioActionRequest(runId));
        await RefreshAsync().ConfigureAwait(true);
    }

    public void ReviewPromotion(string? runId)
        => RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateRunReviewActionRequest(runId));

    public void OpenBriefingAlert(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateActionRequest("StrategyRuns", _activeRunContext));
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

        RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateComparisonActionRequest(runId));
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

        RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateActionRequest(actionId, _activeRunContext));
    }

    private async Task PromoteActiveRunAsync()
    {
        if (_activeRunContext is null)
        {
            return;
        }

        if (_presentationService is null)
        {
            RaiseActionRequest(ResearchWorkspaceShellPresentationService.CreateActionRequest("RunDetail", _activeRunContext));
            return;
        }

        var request = await _presentationService.PromoteActiveRunAsync(_activeRunContext).ConfigureAwait(true);
        RaiseActionRequest(request);
        await RefreshAsync().ConfigureAwait(true);
    }

    private void ApplyState(ResearchWorkspaceShellPresentationState state)
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
        ResearchWorkflowBlockerLabelText = workflow.PrimaryBlocker.Label;
        ResearchWorkflowBlockerDetailText = workflow.PrimaryBlocker.Detail;
        ResearchWorkflowEvidenceItems = workflow.Evidence
            .Select(static evidence => $"{evidence.Label}: {evidence.Value}")
            .ToArray();
    }

    private void ApplyDeskHero(ResearchDeskHeroState hero)
    {
        ResearchHeroFocusText = hero.FocusLabel;
        ResearchWorkflowStatusText = hero.Summary;
        ResearchWorkflowDetailText = hero.Detail;
        ResearchHeroBadgeText = hero.BadgeText;
        ResearchHeroBadgeTone = hero.BadgeTone;
        ResearchHeroActionTitleText = hero.HandoffTitle;
        ResearchHeroActionDetailText = hero.HandoffDetail;
        ResearchHeroPrimaryActionText = hero.PrimaryActionLabel;
        ResearchHeroSecondaryActionText = hero.SecondaryActionLabel;
        ResearchHeroSecondaryActionVisibility = string.IsNullOrWhiteSpace(hero.SecondaryActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ResearchWorkflowTargetText = hero.TargetLabel;
        _heroPrimaryActionId = hero.PrimaryActionId;
        _heroSecondaryActionId = hero.SecondaryActionId;
    }

    private void RaiseActionRequest(ResearchWorkspaceShellActionRequest request)
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
