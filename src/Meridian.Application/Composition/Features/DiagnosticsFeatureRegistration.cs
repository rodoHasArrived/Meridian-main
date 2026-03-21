using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers diagnostic and error tracking services.
/// </summary>
internal sealed class DiagnosticsFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // Historical data query
        services.AddSingleton<HistoricalDataQueryService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new HistoricalDataQueryService(config.DataRoot);
        });

        // Unified market data read abstraction (IMarketDataStore)
        services.AddSingleton<IMarketDataStore>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new JsonlMarketDataStore(config.DataRoot);
        });

        // Diagnostic bundle generator
        services.AddSingleton<DiagnosticBundleService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new DiagnosticBundleService(config.DataRoot, null, () => configStore.Load());
        });

        // Sample data generator
        services.AddSingleton<SampleDataGenerator>();

        // Error tracker
        services.AddSingleton<ErrorTracker>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new ErrorTracker(config.DataRoot);
        });

        // API documentation service
        services.AddSingleton<ApiDocumentationService>();

        // Circuit breaker status service - tracks Polly circuit breaker state transitions
        services.AddSingleton<CircuitBreakerStatusService>();

        return services;
    }
}
