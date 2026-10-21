using Meridian.Contracts.Operations;
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

/// <summary>
/// Explicit acknowledgement that a composition runs on non-real data and must render the persistent
/// provenance label. A host declares it when it knowingly binds fabricated or seeded stores/sources
/// (e.g. demo mode) instead of durable ones, so the label is a deliberate, retained decision rather
/// than a silent default. The last declaration wins.
/// </summary>
public sealed record MeridianDataProvenanceDeclaration(DataProvenance Provenance);

/// <summary>
/// Explicit override of the detected host platform consulted by the ADR-019 platform fail-closed
/// (<see cref="ProductionServiceRegistrationPolicy.ResolveHostPlatform"/>). Hosts never declare it;
/// tests do, so the production-posture refusal is deterministic on every CI operating system. The
/// last declaration wins.
/// </summary>
internal sealed record MeridianHostPlatformDeclaration(
    bool IsWindows,
    System.Runtime.InteropServices.Architecture Architecture,
    string Description);

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

    /// <summary>
    /// Forces the persistent, non-dismissable provenance label for this composition. Used when a
    /// host deliberately runs on simulated, seeded, or sample data so the signal can never be
    /// silently lost. The last declaration wins.
    /// </summary>
    public static IServiceCollection ForceDataProvenanceLabel(
        this IServiceCollection services,
        DataProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new MeridianDataProvenanceDeclaration(provenance));
        return services;
    }

    /// <summary>
    /// Overrides the host platform the ADR-019 platform fail-closed evaluates. Test seam only: a
    /// declared platform replaces <see cref="OperatingSystem.IsWindows"/> and
    /// <see cref="System.Runtime.InteropServices.RuntimeInformation.OSArchitecture"/> for this
    /// collection, so a production posture can be proven to refuse on Linux and compose on Windows
    /// regardless of the machine running the test. The last declaration wins.
    /// </summary>
    internal static IServiceCollection DeclareMeridianHostPlatform(
        this IServiceCollection services,
        bool isWindows,
        System.Runtime.InteropServices.Architecture architecture,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new MeridianHostPlatformDeclaration(
            isWindows,
            architecture,
            description ?? (isWindows ? "Windows (declared)" : "non-Windows (declared)")));
        return services;
    }
}
