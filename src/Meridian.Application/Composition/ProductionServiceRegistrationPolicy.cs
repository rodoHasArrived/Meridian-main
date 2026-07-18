using System.Reflection;
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

        throw new InvalidOperationException(
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
}
