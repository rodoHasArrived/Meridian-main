using Meridian.Application.Composition;
using Meridian.DataIntegration.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.ProviderRouting;

/// <summary>Production and distributed workstations require reviewed connection ownership for every secret.</summary>
public sealed record ProviderCredentialOwnershipPolicy(bool RequireExplicitOwnership)
{
    public bool ProhibitsLegacy(string providerId)
        => RequireExplicitOwnership && ProviderCredentialCatalog.Find(providerId)?.RequiresCredentials != false;

    /// <summary>Directly constructed compatibility services still honor production environment flags.</summary>
    public static ProviderCredentialOwnershipPolicy ForCurrentEnvironment(CompositionConfiguration? configuration = null)
    {
        configuration ??= new CompositionConfiguration();
        return new(ProductionServiceRegistrationPolicy.IsProductionEnvironment(configuration) ||
            IsEnabled(configuration["MDC_PACKAGED_BUILD"]) || IsEnabled(configuration["MERIDIAN_CUSTOMER_BUILD"]));
    }

    private static bool IsEnabled(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1" ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
}

public static class ProviderCredentialOwnershipPolicyRegistration
{
    public static IServiceCollection AddProviderCredentialOwnershipPolicy(this IServiceCollection services)
    {
        // Evaluate the final composition when resolved: a posture declaration added after feature
        // registration must not leave an earlier development-only fallback enabled.
        services.TryAddSingleton(_ => new ProviderCredentialOwnershipPolicy(
            ProductionServiceRegistrationPolicy.IsProductionComposition(services) ||
            ProviderCredentialOwnershipPolicy.ForCurrentEnvironment(CompositionConfiguration.Resolve(services)).RequireExplicitOwnership));
        return services;
    }
}
