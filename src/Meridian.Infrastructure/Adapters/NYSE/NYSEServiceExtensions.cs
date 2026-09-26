using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Meridian.Infrastructure.Adapters.NYSE;

/// <summary>
/// Service registration extensions for NYSE Direct data source.
/// </summary>
public static class NYSEServiceExtensions
{
    /// <summary>
    /// Adds NYSE Direct using the configuration registered by the final host composition.
    /// </summary>
    public static IServiceCollection AddNYSEDataSource(this IServiceCollection services)
        => AddNYSEDataSource(services, static provider => provider.GetRequiredService<IConfiguration>());

    /// <summary>
    /// Adds NYSE Direct data source to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNYSEDataSource(
        this IServiceCollection services,
        IConfiguration configuration)
        => AddNYSEDataSource(services, _ => configuration);

    private static IServiceCollection AddNYSEDataSource(
        IServiceCollection services,
        Func<IServiceProvider, IConfiguration> resolveConfiguration)
    {
        services.AddSingleton(provider =>
        {
            var configuration = resolveConfiguration(provider);
            var nyseOptions = new NYSEOptions();
            configuration.GetSection("DataSources:Sources:nyse:NYSE").Bind(nyseOptions);

            // Fall back to root NYSE section if not in DataSources.
            if (string.IsNullOrEmpty(nyseOptions.ResolveApiKey()))
            {
                configuration.GetSection("NYSE").Bind(nyseOptions);
            }
            return nyseOptions;
        });

        services.AddSingleton<IDataSource>(sp =>
        {
            var dataSourcesConfig = new UnifiedDataSourcesConfig();
            resolveConfiguration(sp).GetSection("DataSources").Bind(dataSourcesConfig);
            var sourceOptions = dataSourcesConfig.GetOptionsForSource("nyse");
            var opts = sp.GetRequiredService<NYSEOptions>();
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var log = sp.GetService<ILogger>()?.ForContext<NYSEDataSource>();
            return new NYSEDataSource(opts, factory, sourceOptions, log);
        });

        services.AddSingleton<IRealtimeDataSource>(sp =>
        {
            var sources = sp.GetServices<IDataSource>();
            return sources.OfType<NYSEDataSource>().First();
        });

        services.AddSingleton<IHistoricalDataSource>(sp =>
        {
            var sources = sp.GetServices<IDataSource>();
            return sources.OfType<NYSEDataSource>().First();
        });

        return services;
    }
}
