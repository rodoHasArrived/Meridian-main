using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Session;

namespace Meridian.Wpf.Shell.Root;

public sealed class DesktopShellCoordinator
{
    private readonly DesktopShellSessionService _sessionService;

    public DesktopShellCoordinator(DesktopShellSessionService sessionService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
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
}
