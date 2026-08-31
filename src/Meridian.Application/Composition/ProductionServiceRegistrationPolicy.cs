using System.Reflection;
using Meridian.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NonProductionOnlyImplementationAttribute : Attribute;

/// <summary>
/// Explicit, auditable opt-out for implementations whose type name matches a prohibited
/// production prefix (<c>InMemory*</c>, <c>Null*</c>, <c>NoOp*</c>, <c>Fake*</c>, <c>Stub*</c>,
/// <c>Sample*</c>) but are genuinely safe for a production role (ADR-019). Matched by attribute
/// type name so lower layers can declare an identically named attribute without an upward
/// project reference. Marker-based prohibitions (<see cref="INonProductionOnlyService"/> and
/// <see cref="NonProductionOnlyImplementationAttribute"/>) cannot be overridden.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProductionSafeImplementationAttribute(string justification) : Attribute
{
    public string Justification { get; } = justification;
}

public interface INonProductionOnlyService;

/// <summary>
/// The single production composition policy (ADR-019). A composition is production when the host
/// declared <see cref="MeridianDeploymentPosture.ProductionApi"/> on the collection or the
/// environment resolves to production; both paths share one prohibited-implementation matcher.
/// <see cref="ProductionRegistrationGuardService"/> re-runs this policy over the final graph at
/// host start so registrations added after the composition root cannot bypass it.
/// </summary>
public static class ProductionServiceRegistrationPolicy
{
    private static readonly string[] ProhibitedNamePrefixes =
        ["InMemory", "Null", "NoOp", "Fake", "Stub", "Sample"];

    public static void Validate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!IsProductionComposition(services))
        {
            return;
        }

        ThrowIfViolations(CollectStaticViolations(services));
    }

    internal static string[] CollectStaticViolations(IServiceCollection services)
        => services
            .Select(GetRegisteredImplementationType)
            .Where(type => type is not null)
            .Distinct()
            .Where(type => IsNonProductionOnlyImplementation(type!))
            .Select(type => type!.FullName ?? type!.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    internal static void ThrowIfViolations(IReadOnlyCollection<string> violations)
    {
        if (violations.Count == 0)
        {
            return;
        }

        throw new StartupRefusedException(
            "Production startup policy rejected non-production DI registrations: " +
            $"{string.Join(", ", violations)}. Replace each binding with a production implementation, " +
            "or scope the capability out of the supported envelope per ADR-019.");
    }

    internal static Type? GetRegisteredImplementationType(ServiceDescriptor descriptor)
        => descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
            : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    internal static bool IsNonProductionOnlyImplementation(Type implementationType)
    {
        if (implementationType is null)
        {
            return false;
        }

        if (typeof(INonProductionOnlyService).IsAssignableFrom(implementationType))
        {
            return true;
        }

        if (implementationType.GetCustomAttribute<NonProductionOnlyImplementationAttribute>() is not null)
        {
            return true;
        }

        return HasProhibitedNamePrefix(implementationType.Name)
               && !HasProductionSafeOptOut(implementationType);
    }

    private static bool HasProhibitedNamePrefix(string typeName)
        => ProhibitedNamePrefixes.Any(prefix =>
            typeName.Length > prefix.Length &&
            typeName.StartsWith(prefix, StringComparison.Ordinal) &&
            char.IsUpper(typeName[prefix.Length]));

    private static bool HasProductionSafeOptOut(Type implementationType)
        => implementationType
            .GetCustomAttributes(inherit: false)
            .Any(attribute => string.Equals(
                attribute.GetType().Name,
                nameof(ProductionSafeImplementationAttribute),
                StringComparison.Ordinal));

    /// <summary>
    /// Returns whether registrations added to <paramref name="services"/> are being composed for a
    /// production environment or an explicitly production API posture. Downstream feature modules use
    /// this to omit unsupported fallback capabilities before the final graph guard runs.
    /// </summary>
    public static bool IsProductionComposition(IServiceCollection services)
        => ResolveDeclaredPosture(services) == MeridianDeploymentPosture.ProductionApi
           || IsProductionEnvironment();

    /// <summary>
    /// Rejects Quant Lab in every supported production or customer-distribution posture until the
    /// isolated worker's OS controls and deployment profile have production certification. The
    /// worker now provides killable process/resource containment, but it deliberately retains the
    /// launching identity's file and network permissions and is not a hostile-code sandbox.
    /// </summary>
    public static void EnsureIsolatedQuantLabIsAllowed(IServiceCollection services, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!enabled)
        {
            return;
        }

        if (IsProductionComposition(services) ||
            IsTruthy(Environment.GetEnvironmentVariable("MDC_PACKAGED_BUILD")) ||
            IsTruthy(Environment.GetEnvironmentVariable("MERIDIAN_CUSTOMER_BUILD")))
        {
            throw new StartupRefusedException(
                "QuantLab:Enabled cannot be used in a production, packaged, or customer build. " +
                "The isolated worker's current OS controls do not remove the launching identity's " +
                "file/network permissions and are not certified as a hostile-code sandbox under ADR-019.");
        }
    }

    // ── Supported local-workstation data posture (ADR-019) ──────────────────────────────────────
    //
    // Production rejects every non-production binding outright. The supported *local* posture is
    // softer but still fail-closed about one thing: a money-path durable store must never run
    // in-memory while the operator is led to believe the data is real. So under the local posture we
    // either fail closed on an in-memory durable binding (when the host asserts durability), or force
    // the persistent simulated label so nothing fabricated is ever mistaken for real.

    private static readonly string[] DurableStoreServiceNameMarkers =
        ["Store", "Repository", "Sink", "Journal", "Ledger", "Archive", "Wal", "Persistence"];

    /// <summary>
    /// Whether registrations are being composed for the supported single-operator local-workstation
    /// posture. Distinct from <see cref="IsProductionComposition"/>: production owns its own outright
    /// rejection, so this returns <c>false</c> in a production composition.
    /// </summary>
    public static bool IsSupportedLocalComposition(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (IsProductionComposition(services))
        {
            return false;
        }

        return ResolveDeclaredPosture(services) == MeridianDeploymentPosture.LocalWorkstation
               || IsLocalWorkstationEnvironment();
    }

    internal static bool IsLocalWorkstationEnvironment()
        => string.Equals(
            Environment.GetEnvironmentVariable("MERIDIAN_API_DEPLOYMENT_MODE"),
            nameof(MeridianDeploymentPosture.LocalWorkstation),
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsDurableStoreServiceType(Type? serviceType)
    {
        if (serviceType is null)
        {
            return false;
        }

        var name = serviceType.Name;
        return DurableStoreServiceNameMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// Durable-store service contracts bound to a non-production (in-memory/null/no-op/…)
    /// implementation, formatted as <c>Service -> Implementation</c> for diagnostics.
    /// </summary>
    internal static IReadOnlyList<string> CollectNonDurableStoreBindings(IServiceCollection services)
        => services
            .Where(descriptor => IsDurableStoreServiceType(descriptor.ServiceType))
            .Select(descriptor => (Service: descriptor.ServiceType, Implementation: GetRegisteredImplementationType(descriptor)))
            .Where(pair => pair.Implementation is not null && IsNonProductionOnlyImplementation(pair.Implementation!))
            .Select(pair => $"{pair.Service.FullName ?? pair.Service.Name} -> {pair.Implementation!.FullName ?? pair.Implementation!.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The provenance a composed graph must surface. An explicit <see cref="MeridianDataProvenanceDeclaration"/>
    /// wins; otherwise a supported-local composition that bound any in-memory durable store forces
    /// <see cref="DataProvenance.Simulated"/> so fabricated data cannot pass for real. Real data (the
    /// only unlabeled state) requires either durable stores or no local fabrication at all.
    /// </summary>
    public static DataProvenance ResolveComposedDataProvenance(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (TryResolveForcedProvenance(services, out var forced))
        {
            return forced;
        }

        return IsSupportedLocalComposition(services) && CollectNonDurableStoreBindings(services).Count > 0
            ? DataProvenance.Simulated
            : DataProvenance.Real;
    }

    internal static bool TryResolveForcedProvenance(IServiceCollection services, out DataProvenance provenance)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService &&
                services[i].ImplementationInstance is MeridianDataProvenanceDeclaration declaration)
            {
                provenance = declaration.Provenance;
                return true;
            }
        }

        provenance = DataProvenance.Real;
        return false;
    }

    /// <summary>
    /// Fail-closed variant for the supported local posture. When <paramref name="requireDurableStores"/>
    /// is set (the host asserts real durability) any in-memory money-path store binding aborts
    /// composition with the offending bindings named. When durability is not required, callers force
    /// the persistent label via <see cref="ResolveComposedDataProvenance"/> instead of throwing.
    /// </summary>
    public static void ValidateSupportedLocal(IServiceCollection services, bool requireDurableStores)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!requireDurableStores || !IsSupportedLocalComposition(services))
        {
            return;
        }

        var bindings = CollectNonDurableStoreBindings(services);
        if (bindings.Count == 0)
        {
            return;
        }

        throw new StartupRefusedException(
            "Supported local-workstation posture requires durable money-path stores but found in-memory " +
            $"bindings: {string.Join(", ", bindings)}. Bind a durable store, or drop the durability requirement " +
            "and force the persistent simulated-data label so fabricated data can never be mistaken for real.");
    }

    internal static MeridianDeploymentPosture ResolveDeclaredPosture(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService &&
                services[i].ImplementationInstance is MeridianDeploymentPostureDeclaration declaration)
            {
                return declaration.Posture;
            }
        }

        return MeridianDeploymentPosture.Unspecified;
    }

    internal static bool IsProductionEnvironment()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("MERIDIAN_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("MERIDIAN_DEPLOYMENT_ENVIRONMENT");

        var mode = Environment.GetEnvironmentVariable("MERIDIAN_MODE");
        var apiDeploymentMode = Environment.GetEnvironmentVariable("MERIDIAN_API_DEPLOYMENT_MODE");

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase)
               || string.Equals(apiDeploymentMode, nameof(MeridianDeploymentPosture.ProductionApi), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
