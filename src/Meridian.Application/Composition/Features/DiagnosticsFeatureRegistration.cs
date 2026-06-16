using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Monitoring;
using Meridian.Core.Config;
using Meridian.DataIntegration.Historical;
using Meridian.DataIntegration.Testing;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Platform.ApiDocumentation;
using Meridian.Platform.Diagnostics;
using Meridian.Platform.Monitoring;
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

        // Error tracker
        services.AddSingleton<ErrorTracker>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            return new ErrorTracker(config.DataRoot);
        });

        // Shutdown diagnostics
        services.AddSingleton<ShutdownDiagnosticsService>();

        // Diagnostic bundle generator
        services.AddSingleton<DiagnosticBundleService>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var metrics = sp.GetService<IEventMetrics>();
            var errorTracker = sp.GetService<ErrorTracker>();
            var shutdownDiagnostics = sp.GetService<ShutdownDiagnosticsService>();
            var providerRegistry = sp.GetService<ProviderRegistry>();
            return new DiagnosticBundleService(
                config.DataRoot,
                metrics is null ? null : metrics.GetSnapshot,
                () => configStore.Load(),
                errorTracker is null ? null : () => errorTracker.GetLastErrors(10),
                shutdownDiagnostics is null ? null : () => shutdownDiagnostics.GetSnapshot(),
                providerRegistry is null ? null : () => GetProviderConnectionDiagnostics(providerRegistry));
        });

        // Sample data generator
        services.AddSingleton<SampleDataGenerator>();

        // API documentation service
        services.AddSingleton<ApiDocumentationService>();

        // Circuit breaker status service - tracks Polly circuit breaker state transitions
        services.AddSingleton<CircuitBreakerStatusService>();

        return services;
    }

    private static IReadOnlyCollection<ProviderConnectionDiagnosticSnapshot> GetProviderConnectionDiagnostics(
        ProviderRegistry providerRegistry)
    {
        return providerRegistry.GetAllProviderMetadata()
            .OfType<IProviderConnectionDiagnosticsSource>()
            .Select(static source =>
            {
                var snapshot = source.GetConnectionDiagnosticsSnapshot();
                return new ProviderConnectionDiagnosticSnapshot(
                    snapshot.ProviderName,
                    snapshot.LifecycleState.ToString(),
                    snapshot.WebSocketState.ToString(),
                    snapshot.IsConnected,
                    snapshot.IsReconnecting,
                    snapshot.ReconnectAttempts,
                    snapshot.LastConnectedAt,
                    snapshot.LastDisconnectedAt,
                    snapshot.LastHeartbeatReceivedAt,
                    snapshot.LastMessageReceivedAt,
                    snapshot.LastReconnectAttemptAt,
                    snapshot.LastFailureKind?.ToString(),
                    snapshot.ConnectionAge,
                    snapshot.IdleDuration,
                    snapshot.ActiveSubscriptions,
                    snapshot.FailedSubscriptions,
                    snapshot.RecoveringSubscriptions,
                    snapshot.LastSubscriptionMessageAt);
            })
            .ToArray();
    }
}
