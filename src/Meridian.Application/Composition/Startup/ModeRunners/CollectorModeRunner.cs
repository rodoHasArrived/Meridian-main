using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the headless streaming data collector.
/// Handles provider creation (with optional failover), symbol subscriptions, hot-reload, and simulate-feed.
/// Does not start a UI server — desktop mode uses <see cref="DesktopModeRunner"/> which wraps this runner.
/// </summary>
public sealed class CollectorModeRunner
{
    private readonly ILogger _log;

    public CollectorModeRunner(ILogger log) => _log = log;

    /// <summary>
    /// Executes the streaming collector loop.
    /// Creates and configures the host, connects to the data provider, applies symbol subscriptions,
    /// and runs until the process is cancelled or the provider disconnects.
    /// </summary>
    /// <param name="ctx">Resolved startup context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exit code: 0 on clean shutdown, non-zero on error.</returns>
    public async Task<int> RunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        var statusPath = Path.Combine(ctx.Config.DataRoot, "_status", "status.json");
        await using var statusWriter = new StatusWriter(
            statusPath,
            () => ctx.ConfigurationService.LoadAndPrepareConfig(ctx.ConfigPath));

        ConfigWatcher? watcher = null;

        await using var hostStartup = HostStartupFactory.Create(ctx.Deployment, ctx.ConfigPath);
        var storageOpt = hostStartup.StorageOptions;
        var pipeline = hostStartup.Pipeline;

        await pipeline.RecoverAsync();
        _log.Information("WAL enabled for pipeline durability");

        var policy = hostStartup.GetRequiredService<JsonlStoragePolicy>();
        _log.Information("Storage path: {RootPath}", storageOpt.RootPath);
        _log.Information("Naming convention: {NamingConvention}", storageOpt.NamingConvention);
        _log.Information("Date partitioning: {DatePartition}", storageOpt.DatePartition);
        _log.Information("Compression: {CompressionEnabled}", storageOpt.Compress ? "enabled" : "disabled");
        _log.Debug("Example path: {ExamplePath}", policy.GetPathPreview());

        var quoteCollector = hostStartup.GetRequiredService<QuoteCollector>();
        var tradeCollector = hostStartup.GetRequiredService<TradeDataCollector>();
        var depthCollector = hostStartup.GetRequiredService<MarketDepthCollector>();

        if (!string.IsNullOrWhiteSpace(ctx.CliArgs.Replay))
        {
            _log.Information("Replaying events from {ReplayPath}...", ctx.CliArgs.Replay);
            var replayer = new JsonlReplayer(ctx.CliArgs.Replay);
            await foreach (var evt in replayer.ReadEventsAsync(ct))
            {
                await pipeline.PublishAsync(evt);
            }

            await pipeline.FlushAsync();
            await statusWriter.WriteOnceAsync();
            return 0;
        }

        var providerRegistry = hostStartup.GetRequiredService<ProviderRegistry>();
        var failoverCfg = ctx.Config.DataSources;
        var failoverRules = failoverCfg?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
        var useFailover = failoverCfg?.EnableFailover == true && failoverRules.Length > 0;

        ConnectionHealthMonitor? healthMonitor = null;
        StreamingFailoverService? failoverService = null;
        IMarketDataClient dataClient;

        if (useFailover)
        {
            _log.Information("Streaming failover enabled with {RuleCount} rules", failoverRules.Length);

            healthMonitor = new ConnectionHealthMonitor();
            failoverService = new StreamingFailoverService(healthMonitor);

            var rule = failoverRules[0];
            var providerMap = new Dictionary<string, IMarketDataClient>(StringComparer.OrdinalIgnoreCase);
            var allProviderIds = new[] { rule.PrimaryProviderId }
                .Concat(rule.BackupProviderIds)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var sources = failoverCfg!.Sources ?? Array.Empty<DataSourceConfig>();
            var providerIds = allProviderIds.ToList();
            var creationTasks = providerIds.Select(providerId =>
            {
                var source = sources.FirstOrDefault(
                    s => string.Equals(s.Id, providerId, StringComparison.OrdinalIgnoreCase));
                var providerKind = source?.Provider ?? ctx.Config.DataSource;
                return Task.Run(() =>
                {
                    try
                    {
                        var client = providerRegistry.CreateStreamingClient(providerKind);
                        return (providerId, client: (IMarketDataClient?)client, providerKind, error: (Exception?)null);
                    }
                    catch (Exception ex)
                    {
                        return (providerId, client: (IMarketDataClient?)null, providerKind, error: (Exception?)ex);
                    }
                }, ct);
            });

            var results = await Task.WhenAll(creationTasks);
            foreach (var (providerId, client, providerKind, error) in results)
            {
                if (client != null)
                {
                    providerMap[providerId] = client;
                    failoverService.RegisterProvider(providerId);
                    _log.Information(
                        "Created streaming client for failover provider {ProviderId} ({Kind})",
                        providerId,
                        providerKind);
                }
                else
                {
                    _log.Warning(
                        error,
                        "Failed to create streaming client for provider {ProviderId}; skipping",
                        providerId);
                }
            }

            if (providerMap.Count == 0)
            {
                _log.Error("No streaming providers could be created for failover; falling back to single provider");
                dataClient = providerRegistry.CreateStreamingClient(ctx.Config.DataSource);
            }
            else
            {
                var initialProvider = providerMap.ContainsKey(rule.PrimaryProviderId)
                    ? rule.PrimaryProviderId
                    : providerMap.Keys.First();

                dataClient = new FailoverAwareMarketDataClient(providerMap, failoverService, rule.Id, initialProvider);
                failoverService.Start(failoverCfg);
            }
        }
        else
        {
            dataClient = providerRegistry.CreateStreamingClient(ctx.Config.DataSource);
        }

        await using var dataClientDisposable = dataClient;

        try
        {
            var leaseManager = hostStartup.GetService<ILeaseManager>();
            if (leaseManager is not null)
            {
                var coordinationSnapshot = await leaseManager.GetSnapshotAsync(ct);
                _log.Information(
                    "Coordination initialized: enabled={Enabled}, mode={Mode}, instance={InstanceId}, root={RootPath}",
                    coordinationSnapshot.Enabled,
                    coordinationSnapshot.Mode,
                    coordinationSnapshot.InstanceId,
                    coordinationSnapshot.RootPath);
            }

            using var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await dataClient.ConnectAsync(connectTimeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _log.Error(
                "Connection to {DataSource} timed out after 30 seconds. " +
                "Check network connectivity, firewall rules, and provider credentials. " +
                "Use --dry-run to validate configuration without connecting.",
                ctx.Config.DataSource);
            return ErrorCode.ConnectionTimeout.ToExitCode();
        }
        catch (Exception ex)
        {
            var errorCode = ErrorCodeExtensions.FromException(ex);
            if (errorCode == ErrorCode.Unknown)
                errorCode = ErrorCode.ConnectionFailed;

            _log.Error(
                ex,
                "Failed to connect to {DataSource} data provider (ErrorCode={ErrorCode}, ExitCode={ExitCode}). " +
                "Check credentials and connectivity.",
                ctx.Config.DataSource,
                errorCode,
                errorCode.ToExitCode());

            return errorCode.ToExitCode();
        }

        try
        {
            var subscriptionManager = hostStartup.CreateSubscriptionOrchestrator(dataClient, ctx.Config.DataSource.ToString());
            var runtimeCfg = SharedStartupHelpers.EnsureDefaultSymbols(ctx.Config);
            await subscriptionManager.ApplyAsync(runtimeCfg, ct);
            var symbols = runtimeCfg.Symbols ?? Array.Empty<SymbolConfig>();

            if (ctx.Deployment.HotReloadEnabled)
            {
                watcher = ctx.ConfigurationService.StartHotReload(ctx.ConfigPath, newCfg =>
                {
                    try
                    {
                        var nextCfg = SharedStartupHelpers.EnsureDefaultSymbols(newCfg);
                        subscriptionManager.ApplyAsync(nextCfg).GetAwaiter().GetResult();
                        _ = statusWriter.WriteOnceAsync();
                        _log.Information("Applied hot-reloaded configuration: {Count} symbols", nextCfg.Symbols?.Length ?? 0);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to apply hot-reloaded configuration");
                    }
                }, ex => _log.Error(ex, "Configuration watcher error"));

                _log.Information("Watching {ConfigPath} for subscription changes", ctx.ConfigPath);
            }

            if (ctx.CliArgs.SimulateFeed)
            {
                var now = DateTimeOffset.UtcNow;
                var sym = symbols[0].Symbol;

                depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 500.24m, 300m, "MM1"));
                depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Ask, 500.26m, 250m, "MM2"));
                depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Update, OrderBookSide.Bid, 500.24m, 350m, "MM1"));
                depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 3, DepthOperation.Update, OrderBookSide.Ask, 500.30m, 100m, "MMX"));
                depthCollector.ResetSymbolStream(sym);
                depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 500.20m, 100m, "MM3"));
                depthCollector.OnDepth(new MarketDepthUpdate(now, sym, 0, DepthOperation.Insert, OrderBookSide.Ask, 500.22m, 90m, "MM4"));

                tradeCollector.OnTrade(new MarketTradeUpdate(now, sym, 500.21m, 100, AggressorSide.Buy, SequenceNumber: 1, StreamId: "SIM", Venue: "TEST"));

                await Task.Delay(200, ct);
            }

            await statusWriter.WriteOnceAsync();
            _log.Information("Collector ready at {StoragePath}; waiting for shutdown", storageOpt.RootPath);

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Information("Shutdown requested; disconnecting from data provider...");
        }
        finally
        {
            watcher?.Dispose();

            try
            {
                await dataClient.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Data provider disconnect raised an error during shutdown");
            }

            failoverService?.Dispose();
            healthMonitor?.Dispose();

            var pipelineMetrics = pipeline.EventMetrics;
            _log.Information(
                "Shutdown complete. Metrics: published={Published}, integrity={Integrity}, dropped={Dropped}",
                pipelineMetrics.Published,
                pipelineMetrics.Integrity,
                pipelineMetrics.Dropped);
        }

        return 0;
    }
}
