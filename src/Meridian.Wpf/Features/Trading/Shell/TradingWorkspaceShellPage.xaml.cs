using System.ComponentModel;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading cockpit shell - landing page for the Trading workspace.
/// Hosts the Trading shell view model and keeps code-behind limited to WPF
/// lifecycle, visual-resource tone application, and dock/navigation forwarding.
/// </summary>
public partial class TradingWorkspaceShellPage : TradingWorkspaceShellPageBase
{
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private bool _viewModelEventsAttached;

    public TradingWorkspaceShellPage(
        NavigationService navigationService,
        TradingWorkspaceShellStateProvider stateProvider,
        TradingWorkspaceShellViewModel viewModel,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService = null)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelEvents();
        ApplyToneBindings();
        await ViewModel.StartAsync().ConfigureAwait(true);
        ApplyToneBindings();
        await RestoreDockLayoutAsync(TradingDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModelEvents();
        ViewModel.Stop();
        _ = SaveDockLayoutAsync(TradingDockManager);
    }

    private void AttachViewModelEvents()
    {
        if (_viewModelEventsAttached)
        {
            return;
        }

        _viewModelEventsAttached = true;
        ViewModel.ActionRequested += OnViewModelActionRequested;
        ViewModel.RefreshRequested += OnViewModelRefreshRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModelEvents()
    {
        if (!_viewModelEventsAttached)
        {
            return;
        }

        _viewModelEventsAttached = false;
        ViewModel.ActionRequested -= OnViewModelActionRequested;
        ViewModel.RefreshRequested -= OnViewModelRefreshRequested;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelRefreshRequested(object? sender, EventArgs e)
        => DispatchRefresh(ViewModel.RefreshAsync);

    private void OnViewModelActionRequested(
        object? sender,
        TradingWorkspaceShellActionRequest request)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnViewModelActionRequested(sender, request));
            return;
        }

        if (request.RequestContextSelection)
        {
            RequestContextSelection(_fundContextService, _operatingContextService);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.PageTag))
        {
            return;
        }

        if (request.UseAppNavigation)
        {
            NavigationService.NavigateTo(request.PageTag);
            return;
        }

        OpenWorkspacePage(TradingDockManager, request.PageTag, request.Action, request.Parameter);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TradingWorkspaceShellViewModel.TradingHeroBadgeTone):
                ApplyTone(TradingHeroBadgeBorder, TradingHeroBadgeText, ViewModel.TradingHeroBadgeTone);
                break;
            case nameof(TradingWorkspaceShellViewModel.TradingStatusBadgeTone):
                ApplyTone(TradingStatusBadgeBorder, TradingStatusBadgeText, ViewModel.TradingStatusBadgeTone);
                break;
            case nameof(TradingWorkspaceShellViewModel.PromotionStatusTone):
                ApplyTone(PromotionStatusPill, PromotionStatusLabelText, ViewModel.PromotionStatusTone);
                break;
            case nameof(TradingWorkspaceShellViewModel.AuditStatusTone):
                ApplyTone(AuditStatusPill, AuditStatusLabelText, ViewModel.AuditStatusTone);
                break;
            case nameof(TradingWorkspaceShellViewModel.ValidationStatusTone):
                ApplyTone(ValidationStatusPill, ValidationStatusLabelText, ViewModel.ValidationStatusTone);
                break;
        }
    }

    private void ApplyToneBindings()
    {
        ApplyTone(TradingHeroBadgeBorder, TradingHeroBadgeText, ViewModel.TradingHeroBadgeTone);
        ApplyTone(TradingStatusBadgeBorder, TradingStatusBadgeText, ViewModel.TradingStatusBadgeTone);
        ApplyTone(PromotionStatusPill, PromotionStatusLabelText, ViewModel.PromotionStatusTone);
        ApplyTone(AuditStatusPill, AuditStatusLabelText, ViewModel.AuditStatusTone);
        ApplyTone(ValidationStatusPill, ValidationStatusLabelText, ViewModel.ValidationStatusTone);
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(TradingDockManager, e.PageTag, e.Action);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => ViewModel.ExecuteCommandAction(e.Command.Id);

    private void OpenLiveData_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("LiveData");

    private void OpenBlotter_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("PositionBlotter");

    private void OpenPortfolio_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("RunPortfolio");

    private void ImportPositions_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("PortfolioImport");

    private void OpenOrderBook_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("OrderBook");

    private void OpenRiskRail_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("RunRisk");

    private void OpenAlerts_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("NotificationCenter");

    private void OnTradingHeroPrimaryActionClick(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteHeroPrimaryAction();

    private void OnTradingHeroSecondaryActionClick(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteHeroSecondaryAction();

    private void OpenWorkflowNextAction_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteWorkflowNextAction();

    private void OpenAccountingConsequences_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("FundTrialBalance");

    private void OpenReconciliationReview_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("FundReconciliation");

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("FundAuditTrail");

    internal static TradingStatusCardPresentation BuildStatusCardPresentation(TradingWorkspaceSummary summary)
        => TradingWorkspaceShellPresentationService.BuildStatusCardPresentation(summary);

    internal static Guid? ResolveFundAccountId(WorkstationOperatingContext? context)
        => TradingWorkspaceShellPresentationService.ResolveFundAccountId(context);

    internal static TradingWorkspaceStatusItem BuildReplayStatusItem(TradingOperatorReadinessDto readiness)
        => TradingWorkspaceShellPresentationService.BuildReplayStatusItem(readiness);

    internal static TradingStatusCardPresentation BuildDegradedStatusCardPresentation()
        => TradingWorkspaceShellPresentationService.BuildDegradedStatusCardPresentation();

    internal static TradingDeskHeroState BuildDeskHeroState(
        ActiveRunContext? activeRun,
        WorkspaceWorkflowSummary? workflow,
        TradingOperatorReadinessDto? readiness,
        bool hasOperatingContext,
        string? operatingContextDisplayName)
        => TradingWorkspaceShellPresentationService.BuildDeskHeroState(
            activeRun,
            workflow,
            readiness,
            hasOperatingContext,
            operatingContextDisplayName);

    internal static string ResolveOperatorWorkItemActionId(OperatorWorkItemDto workItem)
        => TradingWorkspaceShellPresentationService.ResolveOperatorWorkItemActionId(workItem);

    internal static TradingDeskHeroState BuildDegradedDeskHeroState()
        => TradingWorkspaceShellPresentationService.BuildDegradedDeskHeroState();

    internal static WorkspaceCommandGroup BuildCommandGroup()
        => TradingWorkspaceShellPresentationService.BuildCommandGroup();

    internal static TradingPortfolioNavigationTarget ResolvePortfolioNavigationTarget(ActiveRunContext? activeRun)
        => TradingWorkspaceShellPresentationService.ResolvePortfolioNavigationTarget(activeRun);

    private void ApplyTone(Border border, TextBlock textBlock, TradingWorkspaceStatusTone tone)
    {
        var (backgroundKey, borderKey) = tone switch
        {
            TradingWorkspaceStatusTone.Success => ("ConsoleAccentGreenAlpha10Brush", "SuccessColorBrush"),
            TradingWorkspaceStatusTone.Warning => ("ConsoleAccentOrangeAlpha10Brush", "WarningColorBrush"),
            _ => ("ConsoleAccentBlueAlpha10Brush", "InfoColorBrush")
        };

        border.Background = GetBrush(backgroundKey);
        border.BorderBrush = GetBrush(borderKey);
        textBlock.Foreground = GetBrush(borderKey);
    }

    private Brush GetBrush(string resourceKey)
        => TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
}
