using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NonProductionOnlyImplementationAttribute : Attribute;

public interface INonProductionOnlyService;

public static class ProductionServiceRegistrationPolicy
{
    public static void Validate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!IsProductionEnvironment())
        {
            return;
        }

        var violations = services
            .Select(descriptor => descriptor.ImplementationType)
            .Where(type => type is not null)
            .Distinct()
            .Where(IsNonProductionOnlyImplementation)
            .Select(type => type!.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Production startup policy rejected non-production DI registrations: {string.Join(", ", violations)}");
    }

    internal static bool IsNonProductionOnlyImplementation(Type implementationType)
    {
        if (implementationType is null)
        {
            return false;
        }

        if (implementationType.Name.StartsWith("InMemory", StringComparison.Ordinal) &&
            implementationType.Name.EndsWith("Service", StringComparison.Ordinal))
        {
            return true;
        }

        if (typeof(INonProductionOnlyService).IsAssignableFrom(implementationType))
        {
            return true;
        }

        return implementationType.GetCustomAttribute<NonProductionOnlyImplementationAttribute>() is not null;
    }

    internal static bool IsProductionEnvironment()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("MERIDIAN_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("MERIDIAN_DEPLOYMENT_ENVIRONMENT");

        var mode = Environment.GetEnvironmentVariable("MERIDIAN_MODE");

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase);
    }
}
