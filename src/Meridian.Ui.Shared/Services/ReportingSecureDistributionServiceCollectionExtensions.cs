using Meridian.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Registers the application-level secure distribution graph. Durable governance, artifact,
/// catalog, audit, grant, and delivery stores remain deployment-owned prerequisites.
/// </summary>
public static class ReportingSecureDistributionServiceCollectionExtensions
{
    public static IServiceCollection AddSecureReportingDistribution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(BuildOptions());
        services.TryAddSingleton<IReportingReleaseAuthorizationVerifier,
            GovernanceReportingReleaseAuthorizationVerifier>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IReportingDeliveryTransport, SecurePortalReportingDeliveryTransport>());
        services.TryAddSingleton<ReportingDeliveryDispatcher>();
        services.TryAddSingleton<ReportingSecureDistributionApplicationService>();
        return services;
    }

    private static SecureReportingDistributionOptions BuildOptions()
    {
        var defaults = SecureReportingDistributionOptions.Default;
        return defaults with
        {
            ExternalAccessBaseUri = NormalizeOptional(
                Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_EXTERNAL_ACCESS_BASE_URI")),
            WorkerId = NormalizeOptional(Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_DELIVERY_WORKER_ID"))
                       ?? defaults.WorkerId
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
