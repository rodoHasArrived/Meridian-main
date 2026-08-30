using Microsoft.Extensions.Hosting;

namespace Meridian.Application.Composition;

/// <summary>
/// A startup guard that can <b>refuse</b> a composition outright, as opposed to a hosted service
/// whose failure a host may degrade past.
/// </summary>
/// <remarks>
/// <para><b>Why a marker rather than a list in the shell.</b> A host that must not show any UI until
/// the refusals have been decided needs to run exactly these services and not the rest, and
/// <see cref="IHostedService"/> alone cannot say which those are. Naming them in the shell instead
/// would put the list somewhere no guard author looks: a third guard added later would be silently
/// uncovered, and silence is the failure mode governance guards exist to remove.</para>
///
/// <para><b>Implementations must be safe to run twice.</b> Hosts that pre-run these guards still
/// start them again as ordinary hosted services, so a guard has to be a question about the
/// composition rather than an action on it. Both current implementations are: one reads the
/// configured accounts and compares a count, the other validates the service collection and
/// resolves singletons the provider has already cached.</para>
/// </remarks>
public interface IStartupRefusalGuard : IHostedService;
