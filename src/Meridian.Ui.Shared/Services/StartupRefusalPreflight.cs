using Meridian.Application.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Runs the composition's <see cref="IStartupRefusalGuard"/>s on their own, ahead of whatever the
/// host would otherwise start alongside them.
/// </summary>
/// <remarks>
/// <para><b>Why this is separate from starting the host.</b> A shell must not be shown while a
/// refusal is still undecided — the operator can use it, and using it is what the guard forbade.
/// But <see cref="Microsoft.Extensions.Hosting.IHost.StartAsync"/> does not return until
/// <i>every</i> hosted service has started, and an ordinary one may take arbitrarily long: the
/// desktop composition starts a symbol-registry initializer and a registry migration that read the
/// configured data root, which can be slow or unreachable. Waiting on the whole host to decide a
/// refusal therefore trades a shell that appears too early for one that may never appear at all.
/// The guards themselves are cheap and answer immediately.</para>
///
/// <para>Running them here does not remove them from the host. They start again as ordinary hosted
/// services once the shell is up, which is what keeps hosts that do not pre-run them — the web
/// lane — covered by exactly the same guards. <see cref="IStartupRefusalGuard"/> requires
/// implementations to be safe to run twice for this reason.</para>
/// </remarks>
public static class StartupRefusalPreflight
{
    /// <summary>
    /// Starts every registered refusal guard, in registration order, and lets a refusal propagate.
    /// A guard that <b>fails</b> is reported as a refusal too.
    /// </summary>
    /// <remarks>
    /// <para>No refusal is caught here. The caller decides what a refusal means for its own
    /// lifetime, and swallowing one in a shared helper is how a guard comes to have no effect at
    /// all.</para>
    ///
    /// <para><b>An inconclusive guard is a refusal, not a worker failure.</b> A guard that throws
    /// for some other reason — <c>InMemoryFundStructureTenancyGuard</c> failing to read the account
    /// store, say — has not said the composition is safe; it has said it cannot tell. Letting that
    /// surface as an ordinary exception meant every caller applied its ordinary tolerance to it:
    /// the WPF shell reported a recoverable startup error and showed the window, and the
    /// hosted-service retry behind it tolerates non-refusals too, so a persistent read failure left
    /// the unpartitioned fund structure serving indefinitely (Codex review finding on PR #2871).
    /// Wrapping here rather than in each caller is what makes the fail-closed reading the default
    /// for every host that pre-runs the guards.</para>
    ///
    /// <para>Cancellation is not a refusal: it means the startup this was part of is being torn
    /// down, so it propagates unchanged.</para>
    /// </remarks>
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var guard in services.GetServices<IStartupRefusalGuard>())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await guard.StartAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException && !HostStartupEscalation.IsRefusal(ex))
            {
                throw new StartupRefusedException(
                    $"The startup refusal guard '{guard.GetType().Name}' could not determine whether "
                    + "this composition is safe to serve, so it is treated as a refusal. Resolve the "
                    + "underlying fault and start again.",
                    ex);
            }
        }
    }
}
