using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.Shell.Root;

public sealed class DesktopShellCoordinator
{
    private readonly DesktopShellSessionService _sessionService;

    public DesktopShellCoordinator(
        DesktopShellSessionService sessionService,
        IShellPageRegistry pageRegistry,
        IServiceProvider serviceProvider,
        ILogger<DesktopShellCoordinator>? logger = null)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        ArgumentNullException.ThrowIfNull(pageRegistry);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        ValidateWorkspaceCapabilityRegistrations(pageRegistry, serviceProvider, logger);
    }

    public async Task<DesktopShellSessionRestorePlan?> PrepareOperatingContextAsync(
        WorkstationOperatingContext context,
        Func<string?, string> resolveDefaultPageTag,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolveDefaultPageTag);

        await _sessionService.SetLastSelectedOperatingContextAsync(context, ct).ConfigureAwait(false);
        return await _sessionService
            .BuildRestorePlanForContextAsync(context, resolveDefaultPageTag, ct)
            .ConfigureAwait(false);
    }

    private static void ValidateWorkspaceCapabilityRegistrations(
        IShellPageRegistry pageRegistry,
        IServiceProvider serviceProvider,
        ILogger<DesktopShellCoordinator>? logger)
    {
        var serviceInspector = serviceProvider.GetService<IServiceProviderIsService>();

        foreach (var capability in pageRegistry.WorkspaceCapabilities)
        {
            WarnIfMissing(serviceInspector, logger, capability.Workspace.Id, "home page", capability.Pages.FirstOrDefault(
                page => string.Equals(page.PageTag, capability.Workspace.HomePageTag, StringComparison.OrdinalIgnoreCase))?.PageType);
            WarnIfMissing(serviceInspector, logger, capability.Workspace.Id, "state provider", capability.ShellDefinition.StateProviderType);
            WarnIfMissing(serviceInspector, logger, capability.Workspace.Id, "view model", capability.ShellDefinition.ViewModelType);
        }
    }

    private static void WarnIfMissing(
        IServiceProviderIsService? serviceInspector,
        ILogger<DesktopShellCoordinator>? logger,
        string workspaceId,
        string registrationKind,
        Type? serviceType)
    {
        if (serviceType is null || serviceInspector?.IsService(serviceType) != false)
        {
            return;
        }

        logger?.LogWarning(
            "Workspace capability '{WorkspaceId}' declares {RegistrationKind} '{ServiceType}' but no matching DI registration was found.",
            workspaceId,
            registrationKind,
            serviceType.FullName);
    }
}
