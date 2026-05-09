using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Shell.Session;

public sealed record DesktopShellSessionRestorePlan(
    string? DeferredPageTag,
    string TargetPageTag);

public sealed class DesktopShellSessionService
{
    private readonly WorkspaceService _workspaceService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService _operatingContextService;
    private readonly NavigationService _navigationService;
    private readonly Meridian.Wpf.Services.LoggingService _loggingService;

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
            var currentPage = _navigationService.GetCurrentPageTag();
            var activeWorkspace = _workspaceService.ActiveWorkspace;
            var existing = _workspaceService.GetLastSessionStateForContext(operatingContextKey);

            var session = new SessionState
            {
                ActivePageTag = currentPage ?? "Dashboard",
                ActiveWorkspaceId = activeWorkspace?.Id,
                ActiveFilters = existing?.ActiveFilters ?? new Dictionary<string, string>(),
                OpenPages = existing?.OpenPages ?? new List<WorkspacePage>(),
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

            return new DesktopShellSessionRestorePlan(deferredPageTag, targetPageTag);
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
}
