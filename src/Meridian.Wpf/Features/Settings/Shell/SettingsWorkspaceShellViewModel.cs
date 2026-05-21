using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Features.Settings.Shell;

public enum SettingsWorkspaceShellActionKind
{
    Navigate,
    OpenPane,
    Refresh
}

public sealed class SettingsWorkspaceShellActionRequestedEventArgs : EventArgs
{
    public SettingsWorkspaceShellActionRequestedEventArgs(
        SettingsWorkspaceShellActionKind kind,
        string actionId,
        PaneDropAction dockAction = PaneDropAction.OpenTab)
    {
        Kind = kind;
        ActionId = actionId;
        DockAction = dockAction;
    }

    public SettingsWorkspaceShellActionKind Kind { get; }

    public string ActionId { get; }

    public PaneDropAction DockAction { get; }
}

public sealed class SettingsWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    private readonly ISettingsWorkspaceShellSnapshotService? _snapshotService;
    private readonly ISettingsWorkspaceShellPresentationService _presentationService;
    private readonly WorkspaceShellContextService? _shellContextService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private WorkspaceShellContext _shellContext = new();
    private string _heroBadgeText = "Loading";
    private string _heroBadgeTone = WorkspaceTone.Info;
    private string _heroFocusText = "Loading Settings workspace";
    private string _heroSummaryText = "Refreshing preferences, credentials, diagnostics, services, alerts, and support routes.";
    private string _heroDetailText = "The shell is collecting current workstation settings posture.";
    private IReadOnlyList<WorkspaceQueueItem> _operationsItems = Array.Empty<WorkspaceQueueItem>();
    private IReadOnlyList<WorkspaceQueueItem> _supportItems = Array.Empty<WorkspaceQueueItem>();
    private IReadOnlyList<WorkspaceRecentItem> _quickLinks = Array.Empty<WorkspaceRecentItem>();

    public SettingsWorkspaceShellViewModel(
        ISettingsWorkspaceShellSnapshotService? snapshotService = null,
        ISettingsWorkspaceShellPresentationService? presentationService = null,
        WorkspaceShellContextService? shellContextService = null)
        : base(ShellNavigationCatalog.GetWorkspaceShell("settings")!)
    {
        _snapshotService = snapshotService;
        _presentationService = presentationService ?? new SettingsWorkspaceShellPresentationService();
        _shellContextService = shellContextService;
    }

    public event EventHandler<SettingsWorkspaceShellActionRequestedEventArgs>? ActionRequested;

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        private set => SetProperty(ref _shellContext, value);
    }

    public string HeroBadgeText
    {
        get => _heroBadgeText;
        private set => SetProperty(ref _heroBadgeText, value);
    }

    public string HeroBadgeTone
    {
        get => _heroBadgeTone;
        private set => SetProperty(ref _heroBadgeTone, value);
    }

    public string HeroFocusText
    {
        get => _heroFocusText;
        private set => SetProperty(ref _heroFocusText, value);
    }

    public string HeroSummaryText
    {
        get => _heroSummaryText;
        private set => SetProperty(ref _heroSummaryText, value);
    }

    public string HeroDetailText
    {
        get => _heroDetailText;
        private set => SetProperty(ref _heroDetailText, value);
    }

    public IReadOnlyList<WorkspaceQueueItem> OperationsItems
    {
        get => _operationsItems;
        private set => SetProperty(ref _operationsItems, value);
    }

    public IReadOnlyList<WorkspaceQueueItem> SupportItems
    {
        get => _supportItems;
        private set => SetProperty(ref _supportItems, value);
    }

    public IReadOnlyList<WorkspaceRecentItem> QuickLinks
    {
        get => _quickLinks;
        private set => SetProperty(ref _quickLinks, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshotService is null)
        {
            ApplyErrorState();
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            ApplyLoadingState();
            var snapshot = await _snapshotService.LoadAsync(cancellationToken);
            var presentation = _presentationService.Build(snapshot);
            var shellContext = _shellContextService is null
                ? ShellContext
                : await _shellContextService.CreateAsync(presentation.Context, cancellationToken);

            ApplyPresentation(presentation, shellContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError("[SettingsWorkspaceShell] Refresh failed", ex);
            ApplyErrorState();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task ExecuteActionAsync(string actionId, bool navigate)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (string.Equals(actionId, "Refresh", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshAsync();
            ActionRequested?.Invoke(
                this,
                new SettingsWorkspaceShellActionRequestedEventArgs(SettingsWorkspaceShellActionKind.Refresh, actionId));
            return;
        }

        ActionRequested?.Invoke(
            this,
            new SettingsWorkspaceShellActionRequestedEventArgs(
                navigate ? SettingsWorkspaceShellActionKind.Navigate : SettingsWorkspaceShellActionKind.OpenPane,
                actionId,
                ResolveDockAction(actionId)));
    }

    private void ApplyLoadingState()
    {
        HeroBadgeText = "Loading";
        HeroBadgeTone = WorkspaceTone.Info;
        HeroFocusText = "Loading Settings workspace";
        HeroSummaryText = "Refreshing preferences, credentials, diagnostics, services, alerts, and support routes.";
        HeroDetailText = "The shell is collecting current workstation settings posture.";
        OperationsItems = Array.Empty<WorkspaceQueueItem>();
        SupportItems = Array.Empty<WorkspaceQueueItem>();
        QuickLinks = Array.Empty<WorkspaceRecentItem>();
    }

    private void ApplyErrorState()
    {
        HeroBadgeText = "Review";
        HeroBadgeTone = WorkspaceTone.Warning;
        HeroFocusText = "Settings posture unavailable";
        HeroSummaryText = "The Settings workspace could not refresh its local presentation snapshot.";
        HeroDetailText = "Open Settings, Diagnostics, or System Health directly while the shell snapshot recovers.";
        CommandGroup = new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "Settings", Label = "Preferences", Description = "Open workstation preferences.", Glyph = "\uE713" },
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics.", Glyph = "\uE90F" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Refresh", Label = "Refresh", Description = "Refresh Settings workspace posture.", Glyph = "\uE72C" }
            ]
        };
        OperationsItems = Array.Empty<WorkspaceQueueItem>();
        SupportItems = Array.Empty<WorkspaceQueueItem>();
        QuickLinks = Array.Empty<WorkspaceRecentItem>();
    }

    private void ApplyPresentation(SettingsWorkspaceShellPresentation presentation, WorkspaceShellContext shellContext)
    {
        ShellContext = shellContext ?? new WorkspaceShellContext();
        CommandGroup = presentation.CommandGroup;
        HeroBadgeText = presentation.HeroBadgeText;
        HeroBadgeTone = presentation.HeroBadgeTone;
        HeroFocusText = presentation.HeroFocusText;
        HeroSummaryText = presentation.HeroSummaryText;
        HeroDetailText = presentation.HeroDetailText;
        OperationsItems = presentation.OperationsItems;
        SupportItems = presentation.SupportItems;
        QuickLinks = presentation.QuickLinks;
    }

    private static PaneDropAction ResolveDockAction(string actionId) => actionId switch
    {
        "Settings" => PaneDropAction.Replace,
        "CredentialManagement" => PaneDropAction.SplitRight,
        "Diagnostics" => PaneDropAction.SplitRight,
        "SystemHealth" => PaneDropAction.SplitBelow,
        "NotificationCenter" => PaneDropAction.OpenTab,
        "ActivityLog" => PaneDropAction.OpenTab,
        "Help" => PaneDropAction.OpenTab,
        "SetupWizard" => PaneDropAction.OpenTab,
        "Workspaces" => PaneDropAction.OpenTab,
        "WorkflowLibrary" => PaneDropAction.OpenTab,
        "ServiceManager" => PaneDropAction.OpenTab,
        _ => PaneDropAction.OpenTab
    };
}
