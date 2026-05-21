using Meridian.Application.Services;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers market data collector services.
/// </summary>
internal sealed class CollectorFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // QuoteCollector - BBO state tracking
        services.AddSingleton<QuoteCollector>(sp =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            return new QuoteCollector(publisher);
        });

        // SessionStatsCollector - per-symbol intraday open/high/low/last/volume/VWAP
        services.AddSingleton<SessionStatsCollector>();

        // TradeDataCollector - tick-by-tick trade processing
        services.AddSingleton<TradeDataCollector>(sp =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            var quoteCollector = sp.GetRequiredService<QuoteCollector>();
            var sessionStats = sp.GetRequiredService<SessionStatsCollector>();
            return new TradeDataCollector(publisher, quoteCollector, sessionStats);
        });

        // MarketDepthCollector - L2 order book maintenance
        services.AddSingleton<MarketDepthCollector>(sp =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            return new MarketDepthCollector(publisher, requireExplicitSubscription: true);
        });

        // OptionDataCollector - option quotes, trades, greeks, chains
        services.AddSingleton<OptionDataCollector>(sp =>
        {
            var publisher = sp.GetRequiredService<IMarketEventPublisher>();
            return new OptionDataCollector(publisher);
        });

        // OptionsChainService - orchestrates option chain discovery and filtering
        services.AddSingleton<OptionsChainService>(sp =>
        {
            var collector = sp.GetRequiredService<OptionDataCollector>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OptionsChainService>>();
            var providers = sp.GetServices<Infrastructure.Adapters.Core.IOptionsChainProvider>();
            return new OptionsChainService(collector, logger, providers);
        });

        return services;
    }
}
