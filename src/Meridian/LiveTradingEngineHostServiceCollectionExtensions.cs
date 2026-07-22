using Meridian.Backtesting.Plugins;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Events.Publishers;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Live;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian;

/// <summary>
/// Wires the live trading engine into the hosted app: a market-event tap feeds the pipeline's
/// live events into the in-process feed hub and last-price cache, and the engine activates
/// promoted paper/live runs (on promotion approval and via the startup resume sweep).
/// </summary>
internal static class LiveTradingEngineHostServiceCollectionExtensions
{
    internal static IServiceCollection AddLiveTradingEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(
            configuration.GetSection(LiveTradingEngineOptions.SectionKey).Get<LiveTradingEngineOptions>()
            ?? new LiveTradingEngineOptions());

        services.TryAddSingleton<LiveMarketDataCache>();
        services.TryAddSingleton<ILiveFeedAdapter>(static sp => sp.GetRequiredService<LiveMarketDataCache>());
        services.TryAddSingleton<LiveMarketEventHub>(static sp =>
            new LiveMarketEventHub(sp.GetService<ILogger<LiveMarketEventHub>>()));
        services.TryAddSingleton<ILiveMarketEventFeed>(static sp => sp.GetRequiredService<LiveMarketEventHub>());
        // User-authored strategies: plugin assemblies resolve through this source and reach
        // live execution wrapped in BacktestStrategyLiveAdapter via the catalog fallback below.
        services.AddSingleton<IBacktestStrategyLiveSource>(static sp => new PluginBacktestStrategyLiveSource(
            sp.GetRequiredService<LiveTradingEngineOptions>().StrategyPluginDirectory,
            sp.GetService<ILogger<PluginBacktestStrategyLiveSource>>()));

        services.TryAddSingleton<ILiveStrategyCatalog>(static sp =>
        {
            var catalog = LiveStrategyCatalog.CreateDefault();
            foreach (var source in sp.GetServices<IBacktestStrategyLiveSource>())
            {
                var capturedSource = source;
                catalog.RegisterFallback((LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
                {
                    if (!capturedSource.TryCreate(context, out var inner, out failureReason) || inner is null)
                    {
                        strategy = null;
                        return false;
                    }

                    strategy = new BacktestStrategyLiveAdapter(context.StrategyId, inner);
                    return true;
                });
            }

            return catalog;
        });

        services.TryAddSingleton<LiveTradingEngine>(static sp => new LiveTradingEngine(
            sp.GetRequiredService<ILiveStrategyCatalog>(),
            sp.GetRequiredService<ILiveMarketEventFeed>(),
            sp.GetRequiredService<ILiveFeedAdapter>(),
            sp.GetRequiredService<IOrderGateway>(),
            sp.GetRequiredService<IOrderManager>(),
            sp.GetRequiredService<Meridian.Execution.Models.IPortfolioState>(),
            sp.GetRequiredService<IStrategyRepository>(),
            sp.GetRequiredService<LiveTradingEngineOptions>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetService<StrategyLifecycleManager>(),
            sp.GetService<ExecutionAuditTrailService>(),
            sp.GetService<BrokerageConfiguration>()));
        services.TryAddSingleton<IPromotedRunLauncher>(static sp => sp.GetRequiredService<LiveTradingEngine>());
        services.AddHostedService<LiveTradingEngineHostedService>();

        AddLiveTradingMarketEventTap(services);
        return services;
    }

    /// <summary>
    /// Decorates the registered <see cref="IMarketEventPublisher"/> with a
    /// <see cref="CompositePublisher"/> so every published live event also reaches the trading
    /// engine's feed hub and last-price cache. Publishing stays fire-and-forget: a slow or
    /// failing tap can never stall the market data pipeline.
    /// </summary>
    private static void AddLiveTradingMarketEventTap(IServiceCollection services)
    {
        var existing = services.LastOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(IMarketEventPublisher));
        if (existing is null)
        {
            return;
        }

        services.Remove(existing);
        services.AddSingleton<IMarketEventPublisher>(sp =>
        {
            var inner = CreatePublisher(sp, existing);
            var tap = new LiveTradingMarketEventTap(
                sp.GetRequiredService<LiveMarketDataCache>(),
                sp.GetRequiredService<LiveMarketEventHub>());
            return new CompositePublisher(sp.GetService<ILogger<CompositePublisher>>(), inner, tap);
        });
    }

    private static IMarketEventPublisher CreatePublisher(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IMarketEventPublisher instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (IMarketEventPublisher)factory(sp);
        }

        return (IMarketEventPublisher)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}

/// <summary>
/// <see cref="IMarketEventPublisher"/> tap that forwards live pipeline events into the trading
/// engine's in-process feed hub and last-known-price cache.
/// </summary>
internal sealed class LiveTradingMarketEventTap : IMarketEventPublisher
{
    private readonly LiveMarketDataCache _cache;
    private readonly LiveMarketEventHub _hub;

    public LiveTradingMarketEventTap(LiveMarketDataCache cache, LiveMarketEventHub hub)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    public bool TryPublish(in MarketEvent evt)
    {
        var symbol = evt.EffectiveSymbol;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return true;
        }

        switch (evt.Payload)
        {
            case Trade trade:
                _cache.RecordTrade(symbol, trade);
                break;
            case BboQuotePayload quote:
                _cache.RecordQuote(symbol, quote);
                break;
            case LOBSnapshot orderBook:
                _cache.RecordOrderBook(symbol, orderBook);
                break;
            case HistoricalBar:
                break;
            default:
                // Heartbeats, integrity events, and other payloads carry no strategy signal.
                return true;
        }

        _hub.Publish(new LiveMarketEvent(evt.Timestamp, symbol, evt.Payload));
        return true;
    }
}

/// <summary>
/// Hosts the live trading engine: resumes still-open promoted runs after startup and stops all
/// sessions gracefully (leaving run entries open for resume) at shutdown.
/// </summary>
internal sealed class LiveTradingEngineHostedService : IHostedService
{
    private readonly LiveTradingEngine _engine;
    private readonly ILogger<LiveTradingEngineHostedService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _resumeTask;

    public LiveTradingEngineHostedService(
        LiveTradingEngine engine,
        ILogger<LiveTradingEngineHostedService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _resumeTask = Task.Run(
            async () =>
            {
                try
                {
                    await _engine.ResumePendingRunsAsync(_stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Host shut down before the sweep finished; runs stay open for next start.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Live trading engine startup resume sweep failed.");
                }
            },
            CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        if (_resumeTask is { } resume)
        {
            try
            {
                await resume.ConfigureAwait(false);
            }
            catch
            {
                // Resume failures were already logged by the sweep task.
            }
        }

        await _engine.DisposeAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }
}
