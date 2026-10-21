using Meridian.Infrastructure.Http;
using Meridian.Platform.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers HttpClientFactory for proper HTTP client lifecycle management.
/// Implements ADR-010: HttpClient Factory pattern.
/// </summary>
internal sealed class HttpClientFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // Register all named HttpClient configurations with Polly policies.
        services.AddMarketDataHttpClientsTracked(serviceProvider =>
        {
            var statusService = serviceProvider.GetService<CircuitBreakerStatusService>();
            return (name, state, error) =>
            {
                var circuitBreakerState = state switch
                {
                    "Open" => CircuitBreakerState.Open,
                    "HalfOpen" => CircuitBreakerState.HalfOpen,
                    _ => CircuitBreakerState.Closed
                };

                statusService?.RecordStateTransition(name, circuitBreakerState, error);
            };
        });

        return services;
    }
}
