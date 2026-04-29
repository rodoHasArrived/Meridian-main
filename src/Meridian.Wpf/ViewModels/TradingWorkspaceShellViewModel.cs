using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.ViewModels;

public sealed class TradingWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    private readonly TradingWorkspaceShellPresentationService? _presentationService;
    private bool _isStarted;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _activeFundText = "No operating context selected";
    private string _activeFundDetailText = "Runs, allocations, and accounting posture scope to the active operating context.";
    private string _paperRunsText = "-";
    private string _liveRunsText = "-";
    private string _totalEquityText = "-";
    private string _drawdownText = "-";
    private string _positionLimitText = "-";
    private string _orderRateText = "-";
    private string _capitalCashText = "-";
    private string _capitalGrossExposureText = "-";
    private string _capitalNetExposureText = "-";
    private string _capitalFinancingText = "-";
    private string _capitalControlsDetailText = "Select an operating context to unlock capital, financing, and reconciliation posture.";
    private string _tradingActiveRunText = "No active trading run";
    private string _tradingActiveRunMetaText = "Use Research to promote a run, or open a live/paper panel below.";
    private string _watchlistStatusText = "Watchlists and active strategies populate once paper or live runs are started.";
    private string _marketCoreText = "Live data, order book, portfolio, and accounting consequences are ready to dock below.";
    private string _riskRailText = "Risk, reconciliation, and audit surfaces become specific once an active run is selected.";
    private string _deskActionStatusText = "Desk actions update here after a pause, stop, flatten, or alert acknowledgement.";
    private string _tradingStatusSummaryText = TradingWorkspaceShellPresentationDefaults.StatusCard.SummaryText;
    private string _tradingStatusBadgeText = TradingWorkspaceShellPresentationDefaults.StatusCard.BadgeText;
    private TradingWorkspaceStatusTone _tradingStatusBadgeTone = TradingWorkspaceShellPresentationDefaults.StatusCard.BadgeTone;
    private string _promotionStatusLabelText = TradingWorkspaceShellPresentationDefaults.StatusCard.PromotionStatus.Label;
    private string _promotionStatusDetailText = TradingWorkspaceShellPresentationDefaults.StatusCard.PromotionStatus.Detail;
    private TradingWorkspaceStatusTone _promotionStatusTone = TradingWorkspaceShellPresentationDefaults.StatusCard.PromotionStatus.Tone;
    private string _auditStatusLabelText = TradingWorkspaceShellPresentationDefaults.StatusCard.AuditStatus.Label;
    private string _auditStatusDetailText = TradingWorkspaceShellPresentationDefaults.StatusCard.AuditStatus.Detail;
    private TradingWorkspaceStatusTone _auditStatusTone = TradingWorkspaceShellPresentationDefaults.StatusCard.AuditStatus.Tone;
    private string _validationStatusLabelText = TradingWorkspaceShellPresentationDefaults.StatusCard.ValidationStatus.Label;
    private string _validationStatusDetailText = TradingWorkspaceShellPresentationDefaults.StatusCard.ValidationStatus.Detail;
    private TradingWorkspaceStatusTone _validationStatusTone = TradingWorkspaceShellPresentationDefaults.StatusCard.ValidationStatus.Tone;
    private string _tradingWorkflowTargetText = "Target page: TradingShell";
    private string _tradingWorkflowPrimaryButtonText = "Choose Context";
    private string _tradingHeroFocusText = TradingWorkspaceShellPresentationDefaults.DeskHero.FocusLabel;
    private string _tradingHeroSummaryText = TradingWorkspaceShellPresentationDefaults.DeskHero.Summary;
    private string _tradingHeroDetailText = TradingWorkspaceShellPresentationDefaults.DeskHero.Detail;
    private string _tradingHeroBadgeText = TradingWorkspaceShellPresentationDefaults.DeskHero.BadgeText;
    private TradingWorkspaceStatusTone _tradingHeroBadgeTone = TradingWorkspaceShellPresentationDefaults.DeskHero.BadgeTone;
    private string _tradingHeroHandoffTitleText = TradingWorkspaceShellPresentationDefaults.DeskHero.HandoffTitle;
    private string _tradingHeroHandoffDetailText = TradingWorkspaceShellPresentationDefaults.DeskHero.HandoffDetail;
    private string _tradingHeroPrimaryActionText = TradingWorkspaceShellPresentationDefaults.DeskHero.PrimaryActionLabel;
    private string _tradingHeroSecondaryActionText = TradingWorkspaceShellPresentationDefaults.DeskHero.SecondaryActionLabel;
    private string _tradingHeroTargetText = TradingWorkspaceShellPresentationDefaults.DeskHero.TargetLabel;
    private Visibility _tradingHeroSecondaryActionVisibility = Visibility.Visible;
    private WorkspaceShellContext _shellContext = new();
    private IReadOnlyList<TradingActivePositionItem> _activePositions = Array.Empty<TradingActivePositionItem>();
    private Visibility _noPositionsVisibility = Visibility.Collapsed;
    private ActiveRunContext? _activeRunContext;
    private WorkflowNextAction? _currentWorkflowAction;
    private string _heroPrimaryActionId = TradingWorkspaceShellPresentationDefaults.DeskHero.PrimaryActionId;
    private string _heroSecondaryActionId = TradingWorkspaceShellPresentationDefaults.DeskHero.SecondaryActionId;

    public TradingWorkspaceShellViewModel(TradingWorkspaceShellPresentationService? presentationService = null)
        : base(ShellNavigationCatalog.GetWorkspaceShell("trading")!)
    {
        _presentationService = presentationService;
        CommandGroup = TradingWorkspaceShellPresentationService.BuildCommandGroup();
    }

    public event EventHandler? RefreshRequested;

    internal event EventHandler<TradingWorkspaceShellActionRequest>? ActionRequested;

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

    public string ActiveFundText
    {
        get => _activeFundText;
        private set => SetProperty(ref _activeFundText, value);
    }

    public string ActiveFundDetailText
    {
        get => _activeFundDetailText;
        private set => SetProperty(ref _activeFundDetailText, value);
    }

    public string PaperRunsText
    {
        get => _paperRunsText;
        private set => SetProperty(ref _paperRunsText, value);
    }

    public string LiveRunsText
    {
        get => _liveRunsText;
        private set => SetProperty(ref _liveRunsText, value);
    }

    public string TotalEquityText
    {
        get => _totalEquityText;
        private set => SetProperty(ref _totalEquityText, value);
    }

    public string DrawdownText
    {
        get => _drawdownText;
        private set => SetProperty(ref _drawdownText, value);
    }

    public string PositionLimitText
    {
        get => _positionLimitText;
        private set => SetProperty(ref _positionLimitText, value);
    }

    public string OrderRateText
    {
        get => _orderRateText;
        private set => SetProperty(ref _orderRateText, value);
    }

    public string CapitalCashText
    {
        get => _capitalCashText;
        private set => SetProperty(ref _capitalCashText, value);
    }

    public string CapitalGrossExposureText
    {
        get => _capitalGrossExposureText;
        private set => SetProperty(ref _capitalGrossExposureText, value);
    }

    public string CapitalNetExposureText
    {
        get => _capitalNetExposureText;
        private set => SetProperty(ref _capitalNetExposureText, value);
    }

    public string CapitalFinancingText
    {
        get => _capitalFinancingText;
        private set => SetProperty(ref _capitalFinancingText, value);
    }

    public string CapitalControlsDetailText
    {
        get => _capitalControlsDetailText;
        private set => SetProperty(ref _capitalControlsDetailText, value);
    }

    public string TradingActiveRunText
    {
        get => _tradingActiveRunText;
        private set => SetProperty(ref _tradingActiveRunText, value);
    }

    public string TradingActiveRunMetaText
    {
        get => _tradingActiveRunMetaText;
        private set => SetProperty(ref _tradingActiveRunMetaText, value);
    }

    public string WatchlistStatusText
    {
        get => _watchlistStatusText;
        private set => SetProperty(ref _watchlistStatusText, value);
    }

    public string MarketCoreText
    {
        get => _marketCoreText;
        private set => SetProperty(ref _marketCoreText, value);
    }

    public string RiskRailText
    {
        get => _riskRailText;
        private set => SetProperty(ref _riskRailText, value);
    }

    public string DeskActionStatusText
    {
        get => _deskActionStatusText;
        private set => SetProperty(ref _deskActionStatusText, value);
    }

    public string TradingStatusSummaryText
    {
        get => _tradingStatusSummaryText;
        private set => SetProperty(ref _tradingStatusSummaryText, value);
    }

    public string TradingStatusBadgeText
    {
        get => _tradingStatusBadgeText;
        private set => SetProperty(ref _tradingStatusBadgeText, value);
    }

    public TradingWorkspaceStatusTone TradingStatusBadgeTone
    {
        get => _tradingStatusBadgeTone;
        private set => SetProperty(ref _tradingStatusBadgeTone, value);
    }

    public string PromotionStatusLabelText
    {
        get => _promotionStatusLabelText;
        private set => SetProperty(ref _promotionStatusLabelText, value);
    }

    public string PromotionStatusDetailText
    {
        get => _promotionStatusDetailText;
        private set => SetProperty(ref _promotionStatusDetailText, value);
    }

    public TradingWorkspaceStatusTone PromotionStatusTone
    {
        get => _promotionStatusTone;
        private set => SetProperty(ref _promotionStatusTone, value);
    }

    public string AuditStatusLabelText
    {
        get => _auditStatusLabelText;
        private set => SetProperty(ref _auditStatusLabelText, value);
    }

    public string AuditStatusDetailText
    {
        get => _auditStatusDetailText;
        private set => SetProperty(ref _auditStatusDetailText, value);
    }

    public TradingWorkspaceStatusTone AuditStatusTone
    {
        get => _auditStatusTone;
        private set => SetProperty(ref _auditStatusTone, value);
    }

    public string ValidationStatusLabelText
    {
        get => _validationStatusLabelText;
        private set => SetProperty(ref _validationStatusLabelText, value);
    }

    public string ValidationStatusDetailText
    {
        get => _validationStatusDetailText;
        private set => SetProperty(ref _validationStatusDetailText, value);
    }

    public TradingWorkspaceStatusTone ValidationStatusTone
    {
        get => _validationStatusTone;
        private set => SetProperty(ref _validationStatusTone, value);
    }

    public string TradingWorkflowTargetText
    {
        get => _tradingWorkflowTargetText;
        private set => SetProperty(ref _tradingWorkflowTargetText, value);
    }

    public string TradingWorkflowPrimaryButtonText
    {
        get => _tradingWorkflowPrimaryButtonText;
        private set => SetProperty(ref _tradingWorkflowPrimaryButtonText, value);
    }

    public string TradingHeroFocusText
    {
        get => _tradingHeroFocusText;
        private set => SetProperty(ref _tradingHeroFocusText, value);
    }

    public string TradingHeroSummaryText
    {
        get => _tradingHeroSummaryText;
        private set => SetProperty(ref _tradingHeroSummaryText, value);
    }

    public string TradingHeroDetailText
    {
        get => _tradingHeroDetailText;
        private set => SetProperty(ref _tradingHeroDetailText, value);
    }

    public string TradingHeroBadgeText
    {
        get => _tradingHeroBadgeText;
        private set => SetProperty(ref _tradingHeroBadgeText, value);
    }

    public TradingWorkspaceStatusTone TradingHeroBadgeTone
    {
        get => _tradingHeroBadgeTone;
        private set => SetProperty(ref _tradingHeroBadgeTone, value);
    }

    public string TradingHeroHandoffTitleText
    {
        get => _tradingHeroHandoffTitleText;
        private set => SetProperty(ref _tradingHeroHandoffTitleText, value);
    }

    public string TradingHeroHandoffDetailText
    {
        get => _tradingHeroHandoffDetailText;
        private set => SetProperty(ref _tradingHeroHandoffDetailText, value);
    }

    public string TradingHeroPrimaryActionText
    {
        get => _tradingHeroPrimaryActionText;
        private set => SetProperty(ref _tradingHeroPrimaryActionText, value);
    }

    public string TradingHeroSecondaryActionText
    {
        get => _tradingHeroSecondaryActionText;
        private set => SetProperty(ref _tradingHeroSecondaryActionText, value);
    }

    public Visibility TradingHeroSecondaryActionVisibility
    {
        get => _tradingHeroSecondaryActionVisibility;
        private set => SetProperty(ref _tradingHeroSecondaryActionVisibility, value);
    }

    public string TradingHeroTargetText
    {
        get => _tradingHeroTargetText;
        private set => SetProperty(ref _tradingHeroTargetText, value);
    }

    public IReadOnlyList<TradingActivePositionItem> ActivePositions
    {
        get => _activePositions;
        private set => SetProperty(ref _activePositions, value);
    }

    public Visibility NoPositionsVisibility
    {
        get => _noPositionsVisibility;
        private set => SetProperty(ref _noPositionsVisibility, value);
    }

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        private set => SetProperty(ref _shellContext, value);
    }

    internal ActiveRunContext? ActiveRunContext => _activeRunContext;

    internal WorkflowNextAction? CurrentWorkflowAction => _currentWorkflowAction;

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
            ApplyState(new TradingWorkspaceShellPresentationState
            {
                CommandGroup = TradingWorkspaceShellPresentationService.BuildCommandGroup()
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
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Refresh failed: {ex.Message}");
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
        => RaiseActionRequest(TradingWorkspaceShellPresentationService.CreateActionRequest(actionId, _activeRunContext));

    public void ExecuteHeroPrimaryAction()
        => RaiseActionRequest(TradingWorkspaceShellPresentationService.CreateActionRequest(_heroPrimaryActionId, _activeRunContext));

    public void ExecuteHeroSecondaryAction()
        => RaiseActionRequest(TradingWorkspaceShellPresentationService.CreateActionRequest(_heroSecondaryActionId, _activeRunContext));

    public void ExecuteWorkflowNextAction()
        => RaiseActionRequest(TradingWorkspaceShellPresentationService.CreateWorkflowActionRequest(_currentWorkflowAction, _activeRunContext));

    private void ApplyState(TradingWorkspaceShellPresentationState state)
    {
        ActiveFundText = state.ActiveFundText;
        ActiveFundDetailText = state.ActiveFundDetailText;
        PaperRunsText = state.PaperRunsText;
        LiveRunsText = state.LiveRunsText;
        TotalEquityText = state.TotalEquityText;
        DrawdownText = state.DrawdownText;
        PositionLimitText = state.PositionLimitText;
        OrderRateText = state.OrderRateText;
        CapitalCashText = state.CapitalCashText;
        CapitalGrossExposureText = state.CapitalGrossExposureText;
        CapitalNetExposureText = state.CapitalNetExposureText;
        CapitalFinancingText = state.CapitalFinancingText;
        CapitalControlsDetailText = state.CapitalControlsDetailText;
        TradingActiveRunText = state.TradingActiveRunText;
        TradingActiveRunMetaText = state.TradingActiveRunMetaText;
        WatchlistStatusText = state.WatchlistStatusText;
        MarketCoreText = state.MarketCoreText;
        RiskRailText = state.RiskRailText;
        DeskActionStatusText = state.DeskActionStatusText;
        ActivePositions = state.ActivePositions;
        NoPositionsVisibility = state.ActivePositions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShellContext = state.ShellContext;
        CommandGroup = state.CommandGroup;
        _activeRunContext = state.ActiveRunContext;
        _currentWorkflowAction = state.WorkflowNextAction;

        ApplyStatusCard(state.StatusCard);
        ApplyDeskHero(state.DeskHero);
    }

    private void ApplyStatusCard(TradingStatusCardPresentation presentation)
    {
        TradingStatusSummaryText = presentation.SummaryText;
        TradingStatusBadgeText = presentation.BadgeText;
        TradingStatusBadgeTone = presentation.BadgeTone;
        PromotionStatusLabelText = presentation.PromotionStatus.Label;
        PromotionStatusDetailText = presentation.PromotionStatus.Detail;
        PromotionStatusTone = presentation.PromotionStatus.Tone;
        AuditStatusLabelText = presentation.AuditStatus.Label;
        AuditStatusDetailText = presentation.AuditStatus.Detail;
        AuditStatusTone = presentation.AuditStatus.Tone;
        ValidationStatusLabelText = presentation.ValidationStatus.Label;
        ValidationStatusDetailText = presentation.ValidationStatus.Detail;
        ValidationStatusTone = presentation.ValidationStatus.Tone;
        TradingWorkflowTargetText = _currentWorkflowAction is null
            ? "Target page: TradingShell"
            : $"Target page: {_currentWorkflowAction.TargetPageTag}";
        TradingWorkflowPrimaryButtonText = _currentWorkflowAction?.Label ?? "Choose Context";
    }

    private void ApplyDeskHero(TradingDeskHeroState hero)
    {
        TradingHeroFocusText = hero.FocusLabel;
        TradingHeroSummaryText = hero.Summary;
        TradingHeroDetailText = hero.Detail;
        TradingHeroBadgeText = hero.BadgeText;
        TradingHeroBadgeTone = hero.BadgeTone;
        TradingHeroHandoffTitleText = hero.HandoffTitle;
        TradingHeroHandoffDetailText = hero.HandoffDetail;
        TradingHeroPrimaryActionText = hero.PrimaryActionLabel;
        TradingHeroSecondaryActionText = hero.SecondaryActionLabel;
        TradingHeroTargetText = hero.TargetLabel;
        TradingHeroSecondaryActionVisibility = string.IsNullOrWhiteSpace(hero.SecondaryActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _heroPrimaryActionId = hero.PrimaryActionId;
        _heroSecondaryActionId = hero.SecondaryActionId;
    }

    private void RaiseActionRequest(TradingWorkspaceShellActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ActionId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.StatusMessage))
        {
            DeskActionStatusText = request.StatusMessage;
        }

        ActionRequested?.Invoke(this, request);
    }

    private void OnPresentationInvalidated(object? sender, EventArgs e)
        => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
