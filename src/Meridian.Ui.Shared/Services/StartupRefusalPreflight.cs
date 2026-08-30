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
    /// </summary>
    /// <remarks>
    /// Nothing is caught here. The caller decides what a refusal means for its own lifetime, and
    /// swallowing one in a shared helper is how a guard comes to have no effect at all.
    /// </remarks>
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var guard in services.GetServices<IStartupRefusalGuard>())
        {
            ct.ThrowIfCancellationRequested();
            await guard.StartAsync(ct).ConfigureAwait(false);
        }
    }
}
