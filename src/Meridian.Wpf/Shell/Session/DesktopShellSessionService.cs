using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Shell.Session;

public sealed record DesktopShellSessionRestorePlan(
    string? DeferredPageTag,
    string TargetPageTag,
    IReadOnlyList<ShellWorkspaceTab> OpenTabs);

public sealed class ShellWorkspaceTab : BindableBase
{
    private bool _isActive;
    private bool _isDirty;
    private bool _requiresAttention;

    public ShellWorkspaceTab(string pageTag, string title, string? workspaceId, bool canClose = true)
    {
        PageTag = string.IsNullOrWhiteSpace(pageTag)
            ? throw new ArgumentException("A page tag is required.", nameof(pageTag))
            : ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        Title = string.IsNullOrWhiteSpace(title) ? PageTag : title.Trim();
        WorkspaceId = string.IsNullOrWhiteSpace(workspaceId)
            ? ShellNavigationCatalog.InferWorkspaceIdForPageTag(PageTag)
            : workspaceId.Trim();
        CanClose = canClose;
    }

    public string PageTag { get; }

    public string Title { get; }

    public string? WorkspaceId { get; }

    public bool CanClose { get; }

    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public bool RequiresAttention
    {
        get => _requiresAttention;
        set => SetProperty(ref _requiresAttention, value);
    }
}

public sealed class DesktopShellSessionService
{
    private readonly WorkspaceService _workspaceService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService _operatingContextService;
    private readonly NavigationService _navigationService;
    private readonly Meridian.Wpf.Services.LoggingService _loggingService;
    private readonly ObservableCollection<ShellWorkspaceTab> _openTabs = [];
    private readonly ReadOnlyObservableCollection<ShellWorkspaceTab> _openTabsView;
    private ShellWorkspaceTab? _activeTab;

    public DesktopShellSessionService(
        WorkspaceService workspaceService,
        FundContextService fundContextService,
        WorkstationOperatingContextService operatingContextService,
        NavigationService navigationService,
        Meridian.Wpf.Services.LoggingService loggingService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService ?? throw new ArgumentNullException(nameof(operatingContextService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _openTabsView = new ReadOnlyObservableCollection<ShellWorkspaceTab>(_openTabs);
    }

    public event EventHandler? TabsChanged;

    public ReadOnlyObservableCollection<ShellWorkspaceTab> OpenTabs => _openTabsView;

    public ShellWorkspaceTab? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (ReferenceEquals(_activeTab, value))
            {
                return;
            }

            if (_activeTab is not null)
            {
                _activeTab.IsActive = false;
            }

            _activeTab = value;
            if (_activeTab is not null)
            {
                _activeTab.IsActive = true;
            }

            TabsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ShellWorkspaceTab ActivateOrOpenTab(string pageTag)
    {
        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        var existing = _openTabs.FirstOrDefault(tab =>
            string.Equals(tab.PageTag, canonicalPageTag, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveTab = existing;
            return existing;
        }

        var tab = CreateTab(canonicalPageTag);
        _openTabs.Add(tab);
        ActiveTab = tab;
        TabsChanged?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    public bool NavigateToTab(string pageTag)
    {
        var tab = ActivateOrOpenTab(pageTag);
        return _navigationService.NavigateTo(tab.PageTag);
    }

    public bool ActivateTab(ShellWorkspaceTab? tab)
    {
        if (tab is null || !_openTabs.Contains(tab))
        {
            return false;
        }

        ActiveTab = tab;
        return _navigationService.NavigateTo(tab.PageTag);
    }

    public bool CloseTab(ShellWorkspaceTab? tab)
    {
        if (tab is null || !tab.CanClose || !_openTabs.Contains(tab))
        {
            return false;
        }

        var closedIndex = _openTabs.IndexOf(tab);
        var wasActive = ReferenceEquals(tab, ActiveTab);
        _openTabs.Remove(tab);
        if (wasActive)
        {
            ActiveTab = _openTabs.Count == 0
                ? null
                : _openTabs[Math.Min(closedIndex, _openTabs.Count - 1)];
            if (ActiveTab is not null)
            {
                _navigationService.NavigateTo(ActiveTab.PageTag);
            }
        }

        TabsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RestoreTabs(IEnumerable<WorkspacePage>? pages, string? activePageTag)
    {
        _openTabs.Clear();

        foreach (var page in pages ?? [])
        {
            var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(page.PageTag);
            if (string.IsNullOrWhiteSpace(page.PageTag) ||
                _openTabs.Any(tab => string.Equals(tab.PageTag, canonicalPageTag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _openTabs.Add(CreateTab(canonicalPageTag, page.Title));
        }

        var targetPageTag = string.IsNullOrWhiteSpace(activePageTag)
            ? _openTabs.FirstOrDefault()?.PageTag
            : ShellNavigationCatalog.GetCanonicalPageTag(activePageTag);
        ActiveTab = _openTabs.FirstOrDefault(tab => string.Equals(tab.PageTag, targetPageTag, StringComparison.OrdinalIgnoreCase));
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveWorkspaceSession(DesktopWindowBounds bounds, bool shellIsOpen)
    {
        try
        {
            if (_operatingContextService.CurrentContext is null &&
                _fundContextService.CurrentFundProfile is null &&
                !shellIsOpen)
            {
                return;
            }

            var operatingContextKey = _operatingContextService.CurrentContext?.ContextKey
                ?? _fundContextService.CurrentFundProfile?.FundProfileId;
            var currentPage = ActiveTab?.PageTag ?? _navigationService.GetCurrentPageTag();
            var activeWorkspace = _workspaceService.ActiveWorkspace;
            var existing = _workspaceService.GetLastSessionStateForContext(operatingContextKey);

            var session = new SessionState
            {
                ActivePageTag = currentPage ?? "Dashboard",
                ActiveWorkspaceId = activeWorkspace?.Id,
                ActiveFilters = existing?.ActiveFilters ?? new Dictionary<string, string>(),
                OpenPages = _openTabs.Count > 0
                    ? _openTabs.Select(ToWorkspacePage).ToList()
                    : existing?.OpenPages ?? new List<WorkspacePage>(),
                WindowBounds = new WindowBounds
                {
                    X = bounds.Left,
                    Y = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    IsMaximized = bounds.IsMaximized
                }
            };

            _ = _workspaceService.SaveSessionStateAsync(session, operatingContextKey);
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning(
                "Failed to save desktop workspace session",
                ("Error", ex.Message));
        }
    }

    public async Task SynchronizeLastSelectedFundAsync(CancellationToken ct = default)
    {
        var workspaceContextKey = _workspaceService.LastSelectedOperatingContextKey;
        var compatibilityFundProfileId = _operatingContextService.CurrentContext?.CompatibilityFundProfileId;
        if (string.IsNullOrWhiteSpace(compatibilityFundProfileId) &&
            _operatingContextService.CurrentContext?.ScopeKind == OperatingContextScopeKind.Fund)
        {
            compatibilityFundProfileId = _operatingContextService.CurrentContext.ScopeId;
        }

        if (string.IsNullOrWhiteSpace(compatibilityFundProfileId) &&
            WorkstationOperatingContext.TryGetFundScopeId(workspaceContextKey, out var workspaceFundScopeId))
        {
            compatibilityFundProfileId = workspaceFundScopeId;
        }

        if (string.IsNullOrWhiteSpace(_fundContextService.LastSelectedFundProfileId) &&
            !string.IsNullOrWhiteSpace(compatibilityFundProfileId))
        {
            await _fundContextService.SetLastSelectedFundProfileIdAsync(compatibilityFundProfileId, ct)
                .ConfigureAwait(false);
        }

        var targetContextKey = _operatingContextService.CurrentContext?.ContextKey;
        if (string.IsNullOrWhiteSpace(targetContextKey))
        {
            if (WorkstationOperatingContext.TryParseContextKey(workspaceContextKey, out _, out _))
            {
                targetContextKey = workspaceContextKey;
            }
            else if (!string.IsNullOrWhiteSpace(_fundContextService.LastSelectedFundProfileId))
            {
                targetContextKey = WorkstationOperatingContext.CreateContextKey(
                    OperatingContextScopeKind.Fund,
                    _fundContextService.LastSelectedFundProfileId!);
            }
        }

        if (!string.IsNullOrWhiteSpace(targetContextKey) &&
            !string.Equals(_workspaceService.LastSelectedOperatingContextKey, targetContextKey, StringComparison.OrdinalIgnoreCase))
        {
            await _workspaceService.SetLastSelectedOperatingContextKeyAsync(targetContextKey, ct)
                .ConfigureAwait(false);
        }
    }

    public async Task<DesktopShellSessionRestorePlan?> BuildRestorePlanForContextAsync(
        WorkstationOperatingContext context,
        Func<string?, string> resolveDefaultPageTag,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolveDefaultPageTag);

        try
        {
            await _workspaceService.LoadWorkspacesAsync(ct).ConfigureAwait(false);

            var session = _workspaceService.GetLastSessionStateForContext(context.ContextKey);
            var targetWorkspaceId = !string.IsNullOrWhiteSpace(session?.ActiveWorkspaceId)
                ? session!.ActiveWorkspaceId
                : context.DefaultWorkspaceId;

            if (!string.IsNullOrWhiteSpace(session?.ActiveWorkspaceId))
            {
                await _workspaceService.ActivateWorkspaceAsync(session.ActiveWorkspaceId, ct)
                    .ConfigureAwait(false);
            }

            var targetPageTag = !string.IsNullOrWhiteSpace(session?.ActivePageTag)
                ? session!.ActivePageTag
                : context.DefaultLandingPageTag;

            if (string.IsNullOrWhiteSpace(targetPageTag))
            {
                targetPageTag = resolveDefaultPageTag(targetWorkspaceId);
            }

            var deferredPageTag = !string.IsNullOrWhiteSpace(session?.ActivePageTag) &&
                                  !string.Equals(session.ActivePageTag, "Dashboard", StringComparison.OrdinalIgnoreCase)
                ? session.ActivePageTag
                : null;

            var restoredPages = session?.OpenPages?.Count > 0
                ? session.OpenPages
                : new List<WorkspacePage> { new() { PageTag = targetPageTag, Title = ShellNavigationCatalog.GetPageTitle(targetPageTag) } };
            RestoreTabs(restoredPages, targetPageTag);

            return new DesktopShellSessionRestorePlan(deferredPageTag, targetPageTag, OpenTabs.ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogWarning(
                "Failed to build desktop workspace restore plan",
                ("ContextKey", context.ContextKey),
                ("Error", ex.Message));
            return null;
        }
    }

    public Task SetLastSelectedOperatingContextAsync(WorkstationOperatingContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _workspaceService.SetLastSelectedOperatingContextKeyAsync(context.ContextKey, ct);
    }

    public Task LoadWorkspacesAsync(CancellationToken ct = default)
        => _workspaceService.LoadWorkspacesAsync(ct);

    private static ShellWorkspaceTab CreateTab(string pageTag, string? title = null)
    {
        var canonicalPageTag = ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        return new ShellWorkspaceTab(
            canonicalPageTag,
            string.IsNullOrWhiteSpace(title) ? ShellNavigationCatalog.GetPageTitle(canonicalPageTag) : title,
            ShellNavigationCatalog.InferWorkspaceIdForPageTag(canonicalPageTag),
            canClose: true);
    }

    private static WorkspacePage ToWorkspacePage(ShellWorkspaceTab tab)
        => new()
        {
            PageTag = tab.PageTag,
            Title = tab.Title,
            IsDefault = false
        };
}
