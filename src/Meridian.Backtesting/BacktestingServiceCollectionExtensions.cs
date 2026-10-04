using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Services;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Backtesting;

/// <summary>
/// Extensions for registering the Meridian backtesting engine with a host's DI container.
/// </summary>
/// <remarks>
/// Backtesting previously had no registration extension of its own, so hosts wired the preflight
/// service and the engine factory inline. That put the engine's composition in a UI-layer file and
/// left no single place to keep correct as the engine's dependencies change. This mirrors
/// <c>AddMeridianQuantScript</c> so both research modules compose the same way.
/// </remarks>
public static class BacktestingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the backtesting preflight service and the per-request engine factory.
    /// </summary>
    /// <remarks>
    /// Uses <c>TryAdd</c> throughout, so a host that has already registered its own preflight
    /// service or engine factory keeps it. The engine factory is per-request rather than a single
    /// engine instance because each run's data root determines its storage catalog.
    /// </remarks>
    public static IServiceCollection AddMeridianBacktesting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IBacktestPreflightService, BacktestPreflightService>();
        services.TryAddSingleton<Func<BacktestRequest, BacktestEngine>>(static sp =>
        {
            BacktestEngine CreateEngine(BacktestRequest request)
            {
                var storageOptions = new StorageOptions { RootPath = request.DataRoot };
                var catalogService = new StorageCatalogService(request.DataRoot, storageOptions);
                return new BacktestEngine(
                    sp.GetRequiredService<ILogger<BacktestEngine>>(),
                    catalogService,
                    sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                    sp.GetService<ICorporateActionAdjustmentService>(),
                    sp.GetService<IBacktestPreflightService>());
            }

            return CreateEngine;
        });

        return services;
    }
}
