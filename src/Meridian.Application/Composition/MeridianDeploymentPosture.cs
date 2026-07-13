using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition;

/// <summary>
/// The single typed deployment posture a host declares before feature composition (ADR-019).
/// <see cref="ProductionServiceRegistrationPolicy"/> treats <see cref="ProductionApi"/> as a
/// production posture; every other value defers to environment-based resolution so packaged
/// local-workstation builds keep their current composition behavior.
/// </summary>
public enum MeridianDeploymentPosture
{
    Unspecified = 0,
    LocalWorkstation,
    ProductionApi,
    Worker,
    Migration
}

/// <summary>
/// Immutable posture declaration registered on the service collection so the registration policy
/// and the final-graph guard resolve the same production answer as the host that composed them.
/// </summary>
public sealed record MeridianDeploymentPostureDeclaration(MeridianDeploymentPosture Posture);

public static class MeridianDeploymentPostureServiceCollectionExtensions
{
    /// <summary>
    /// Declares the host's deployment posture. Call before <c>AddMarketDataServices</c> so
    /// composition-time validation already sees the declared posture. The last declaration wins.
    /// </summary>
    public static IServiceCollection DeclareMeridianDeploymentPosture(
        this IServiceCollection services,
        MeridianDeploymentPosture posture)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new MeridianDeploymentPostureDeclaration(posture));
        return services;
    }
}
