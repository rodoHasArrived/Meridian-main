using Meridian.Application.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meridian.Application.Composition;

/// <summary>
/// Refuses to start a host whose configuration contradicts its Security Master governance profile:
/// an Institutional profile with one of the three requirements explicitly set to false, or a
/// production composition with <c>SecurityMasterWorkbench:GovernanceProfile</c> explicitly set to
/// <c>Standard</c>. The refusal names each contradicted key.
/// </summary>
/// <remarks>
/// An <see cref="IStartupRefusalGuard"/>: it asks <see cref="ISecurityMasterGovernanceRequirements"/>
/// one question, which reads bound options and a posture fact decided from the service descriptors,
/// so it answers without constructing the graph and is safe to run twice. The requirements stay in
/// force regardless of this guard; it exists so a contradictory deployment is corrected rather than
/// run with settings that do not mean what they say.
/// </remarks>
public sealed class SecurityMasterGovernanceProfileGuard : IStartupRefusalGuard
{
    private readonly ISecurityMasterGovernanceRequirements _requirements;

    public SecurityMasterGovernanceProfileGuard(ISecurityMasterGovernanceRequirements requirements)
    {
        _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SecurityMasterGovernanceSnapshot current;
        try
        {
            current = _requirements.Current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not StartupRefusedException)
        {
            // An unbindable section (for example an unknown GovernanceProfile value) leaves the
            // profile undecided; that is a refusal, not a degraded feature.
            throw new StartupRefusedException(
                $"Security Master governance settings under '{SecurityMasterWorkbenchOptions.SectionName}' could not be read, "
                + "so the governance profile is undecided. Correct the section "
                + $"('{SecurityMasterGovernanceRequirements.GovernanceProfileKey}' accepts Standard or Institutional) and start again.",
                ex);
        }

        if (DescribeContradiction(current) is { } refusal)
        {
            throw new StartupRefusedException(refusal);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The startup refusal for <paramref name="snapshot"/>, or null when nothing is contradicted.</summary>
    internal static string? DescribeContradiction(SecurityMasterGovernanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ContradictedKeys.Count == 0)
        {
            return null;
        }

        var reason = snapshot.ProfileSource == SecurityMasterGovernanceProfileSource.ProductionComposition
            ? "this host is a production composition"
            : $"'{SecurityMasterGovernanceRequirements.GovernanceProfileKey}' is Institutional";
        var findings = snapshot.ContradictedKeys.Select(static key =>
            string.Equals(key, SecurityMasterGovernanceRequirements.GovernanceProfileKey, StringComparison.Ordinal)
                ? $"'{key}' is explicitly Standard, but a production composition always runs the Institutional profile; remove the key or set it to Institutional."
                : $"'{key}' is explicitly false; remove the key or set it to true.");

        return
            "Security Master governance configuration contradicts the Institutional governance profile, "
            + $"which is in force because {reason}. {string.Join(" ", findings)} "
            + "Under the Institutional profile governed term amendments, independent field-edit review and "
            + "independent profile approval are always in force and cannot be turned off.";
    }
}

/// <summary>Registers the Security Master governance requirements seam.</summary>
public static class SecurityMasterGovernanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISecurityMasterGovernanceRequirements"/>, the
    /// <see cref="IGovernedTermAmendmentPolicy"/> that reads it, and the startup guard that refuses a
    /// configuration contradicting the governance profile. Idempotent; every host that enforces a
    /// Security Master governance requirement calls it (<c>StorageFeatureRegistration</c>, the
    /// workstation shared services and the WPF desktop).
    /// </summary>
    /// <remarks>
    /// <para>The production fact is decided when the seam is first resolved, from the same collection
    /// and by the same <see cref="ProductionServiceRegistrationPolicy.IsProductionComposition"/> the
    /// ADR-019 final-graph guard uses, so a posture declared after this call is still honoured.</para>
    /// <para>This does not bind the <c>SecurityMasterWorkbench</c> section: the host does, once,
    /// because binding a section twice appends its list values (<c>SourcePrecedence</c>) twice. A
    /// host that binds nothing reads the code defaults, so <c>GovernanceProfile</c> in its
    /// configuration is not read there; a production composition is Institutional regardless.</para>
    /// </remarks>
    public static IServiceCollection AddSecurityMasterGovernanceRequirements(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Keeps IOptionsMonitor resolvable in hosts that never bind the section; it then reads the
        // code defaults (Standard profile, every requirement off).
        services.AddOptions<SecurityMasterWorkbenchOptions>();
        services.TryAddSingleton(_ => new SecurityMasterGovernanceHostPosture(
            ProductionServiceRegistrationPolicy.IsProductionComposition(services)));
        services.TryAddSingleton<ISecurityMasterGovernanceRequirements, SecurityMasterGovernanceRequirements>();
        services.TryAddSingleton<IGovernedTermAmendmentPolicy, GovernedTermAmendmentPolicy>();

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(SecurityMasterGovernanceProfileGuard)))
        {
            return services;
        }

        // One instance in both roles, so the copy a shell pre-runs and the copy host start runs are
        // the same object. Appended: the ADR-019 guard must stay the first IHostedService.
        services.AddSingleton<SecurityMasterGovernanceProfileGuard>();
        services.AddSingleton<IStartupRefusalGuard>(
            static sp => sp.GetRequiredService<SecurityMasterGovernanceProfileGuard>());
        services.AddSingleton<IHostedService>(
            static sp => sp.GetRequiredService<SecurityMasterGovernanceProfileGuard>());
        return services;
    }
}
