using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features;

internal static class ServiceCollectionRegistrationAssertions
{
    public static ServiceDescriptor SingleDescriptor<TService>(this IServiceCollection services)
        => services.Single(descriptor => descriptor.ServiceType == typeof(TService));
}
