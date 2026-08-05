using System.Runtime.ExceptionServices;
using Meridian.Core.Config;
using Meridian.Contracts.Coordination;
using Meridian.Core.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions;
using Meridian.Application.UI;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Meridian.Platform.Monitoring;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using DeploymentContext = Meridian.Platform.Runtime.DeploymentContext;
using DeploymentMode = Meridian.Platform.Runtime.DeploymentMode;

namespace Meridian.Application.Composition;

/// <summary>
/// Unified host graph surface used by the shared startup/orchestration layer.
/// All host modes flow through this class, which delegates to <see cref="ServiceCompositionRoot"/>.
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <list type="bullet">
/// <item><description>Single host graph construction surface for console, desktop, and utility flows</description></item>
/// <item><description>Uses <see cref="ServiceCompositionRoot"/> for all DI registration</description></item>
/// <item><description>Shared startup orchestrators choose canonical <see cref="CompositionOptions"/> presets</description></item>
/// <item><description>Eliminates duplicated service wiring across hosts</description></item>
/// </list>
/// </remarks>
[ImplementsAdr("ADR-001", "Unified host startup for all deployment modes")]
public sealed class HostStartup : IAsyncDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(30);

    private readonly IHost _host;
    private readonly CompositionOptions _options;
    private readonly Serilog.ILogger _log;
    private readonly TimeSpan _stopTimeout;
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    private bool _disposed;

    private HostStartup(
        IHost host,
        CompositionOptions options,
        Serilog.ILogger log,
        TimeSpan stopTimeout)
    {
        _host = host;
        _options = options;
        _log = log;
        _stopTimeout = stopTimeout;
    }

    internal static async Task<HostStartup> CreateStartedHostAsync(
        CompositionOptions options,
        bool enableProcessWideHostedServices,
        Action<IServiceCollection>? configureServices,
        CancellationToken cancellationToken,
        bool enableDatabaseInitialization = true,
        TimeSpan? stopTimeout = null)
    {
        var effectiveStopTimeout = stopTimeout ?? DefaultStopTimeout;
        if (effectiveStopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(stopTimeout), "Stop timeout must be positive.");

        Meridian.Storage.MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        var log = LoggingSetup.ForContext<HostStartup>();
        var aspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var dotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var environmentName = !string.IsNullOrWhiteSpace(aspNetCoreEnvironment)
            ? aspNetCoreEnvironment
            : !string.IsNullOrWhiteSpace(dotnetEnvironment)
                ? dotnetEnvironment
                : Environments.Production;
        var builder = new Microsoft.Extensions.Hosting.HostBuilder()
            .UseEnvironment(environmentName)
            // Preserve the former raw-provider validation behavior. The Meridian final-graph guard
            // remains authoritative; enabling Generic Host's Development-only ValidateOnBuild
            // would reject unrelated lazy registrations that these executable profiles never use.
            .UseDefaultServiceProvider((_, providerOptions) =>
            {
                providerOptions.ValidateOnBuild = false;
                providerOptions.ValidateScopes = false;
            })
            .ConfigureServices(services =>
            {
                services.AddLogging(logging => logging.AddSerilog());
                var effectiveOptions = options with
                {
                    EnableProcessWideHostedServices = enableProcessWideHostedServices
                };
                services.AddMarketDataServices(effectiveOptions);

                // AddMarketDataServices inserts the final-graph production guard at index 0.
                // Database initialization must run immediately after that guard and before
                // coordination or any other hosted service starts. Desktop child graphs retain
                // their local storage/symbol initialization while the parent owns process-wide
                // coordinators and accounting workers.
                if (enableDatabaseInitialization)
                {
                    services.Insert(
                        1,
                        ServiceDescriptor.Singleton<IHostedService>(
                            serviceProvider => new DatabaseInitializationHostedService(serviceProvider)));
                }

                configureServices?.Invoke(services);
            });

        var host = builder.Build();
        try
        {
            // The production guard resolves factory-backed singletons while validating the final
            // graph. Initialize these static routers before StartAsync so those factories cannot
            // permanently capture fallback HTTP clients.
            InitializeHttpClientFactory(host.Services, log);
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return new HostStartup(host, options, log, effectiveStopTimeout);
        }
        catch
        {
            await StopAndDisposeFailedHostAsync(host, log, effectiveStopTimeout).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates a host startup for streaming data collection (CLI headless mode).
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup instance.</returns>
    public static Task<HostStartup> CreateForStreamingAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Streaming with { ConfigPath = configPath },
            enableProcessWideHostedServices: true,
            configureServices: null,
            cancellationToken);

    internal static Task<HostStartup> CreateForStreamingAsync(
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Streaming with { ConfigPath = configPath },
            enableProcessWideHostedServices: true,
            configureServices,
            cancellationToken);

    /// <summary>
    /// Creates a host startup for the default/full host profile.
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup instance.</returns>
    public static Task<HostStartup> CreateDefaultAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Default with { ConfigPath = configPath },
            enableProcessWideHostedServices: true,
            configureServices: null,
            cancellationToken);

    internal static Task<HostStartup> CreateDefaultAsync(
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Default with { ConfigPath = configPath },
            enableProcessWideHostedServices: true,
            configureServices,
            cancellationToken);

    /// <summary>
    /// Creates a default-profile host for a one-shot ETL command without unrelated process-wide
    /// coordination, reconciliation, polling, or accounting workers.
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup instance.</returns>
    public static Task<HostStartup> CreateForEtlAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Default with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            configureServices: null,
            cancellationToken);

    internal static Task<HostStartup> CreateForEtlAsync(
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Default with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            configureServices,
            cancellationToken);

    /// <summary>
    /// Creates a host startup for backfill-only operation.
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup instance.</returns>
    public static Task<HostStartup> CreateForBackfillAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.BackfillOnly with { ConfigPath = configPath },
            enableProcessWideHostedServices: true,
            configureServices: null,
            cancellationToken);

    internal static Task<HostStartup> CreateForBackfillAsync(
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.BackfillOnly with { ConfigPath = configPath },
            enableProcessWideHostedServices: true,
            configureServices,
            cancellationToken);

    /// <summary>
    /// Creates a host startup for minimal utility commands (validation, config checks, etc.).
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup instance.</returns>
    public static Task<HostStartup> CreateForUtilityAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Minimal with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            configureServices: null,
            cancellationToken);

    internal static Task<HostStartup> CreateForUtilityAsync(
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateStartedHostAsync(
            CompositionOptions.Minimal with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            configureServices,
            cancellationToken);

    /// <summary>
    /// Gets a required service from the DI container.
    /// </summary>
    public T GetRequiredService<T>() where T : notnull
        => _host.Services.GetRequiredService<T>();

    /// <summary>
    /// Gets a service from the DI container, or null if not registered.
    /// </summary>
    public T? GetService<T>() where T : class
        => _host.Services.GetService<T>();

    /// <summary>
    /// Gets the service provider for advanced scenarios.
    /// </summary>
    public IServiceProvider ServiceProvider => _host.Services;

    /// <summary>
    /// Gets the ConfigStore from the DI container.
    /// </summary>
    public ConfigStore ConfigStore => GetRequiredService<ConfigStore>();

    /// <summary>
    /// Gets the ConfigurationService from the DI container.
    /// </summary>
    public ConfigurationService ConfigurationService => GetRequiredService<ConfigurationService>();

    /// <summary>
    /// Gets the ProviderFactory from the DI container.
    /// </summary>
    public ProviderFactory ProviderFactory => GetRequiredService<ProviderFactory>();

    /// <summary>
    /// Gets the EventPipeline from the DI container.
    /// </summary>
    public EventPipeline Pipeline => GetRequiredService<EventPipeline>();

    /// <summary>
    /// Gets the StorageOptions from the DI container.
    /// </summary>
    public StorageOptions StorageOptions => GetRequiredService<StorageOptions>();

    /// <summary>
    /// Creates the streaming market data client based on configuration.
    /// Delegates to <see cref="ProviderRegistry.CreateStreamingClient(string)"/> which uses
    /// dictionary-based factory lookup instead of switch statements.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <returns>Configured market data client.</returns>
    public IMarketDataClient CreateStreamingClient(AppConfig config)
    {
        var registry = GetRequiredService<ProviderRegistry>();
        return registry.CreateStreamingClient(config.DataSource);
    }

    /// <summary>
    /// Creates a subscription manager for managing symbol subscriptions.
    /// </summary>
    /// <param name="dataClient">The market data client.</param>
    /// <param name="providerId">Provider identifier used for cross-instance coordination ownership.</param>
    /// <returns>Configured subscription manager.</returns>
    public SubscriptionOrchestrator CreateSubscriptionOrchestrator(IMarketDataClient dataClient, string providerId)
    {
        var depthCollector = GetRequiredService<MarketDepthCollector>();
        var tradeCollector = GetRequiredService<TradeDataCollector>();
        var log = LoggingSetup.ForContext<SubscriptionOrchestrator>();

        var optionCollector = GetService<OptionDataCollector>();
        var ownershipService = GetService<ISubscriptionOwnershipService>();

        return new SubscriptionOrchestrator(
            depthCollector,
            tradeCollector,
            dataClient,
            providerId,
            ownershipService,
            log,
            optionCollector);
    }

    /// <summary>
    /// Creates the backfill providers using the ProviderFactory.
    /// Uses unified credential resolution through ConfigurationService.
    /// </summary>
    /// <returns>List of configured backfill providers.</returns>
    public IReadOnlyList<IHistoricalDataProvider> CreateBackfillProviders()
    {
        var factory = GetRequiredService<ProviderFactory>();
        return factory.CreateBackfillProviders();
    }

    /// <summary>
    /// Creates a composite backfill provider with automatic failover.
    /// </summary>
    /// <param name="providers">Individual backfill providers.</param>
    /// <returns>Composite provider with failover support.</returns>
    public CompositeHistoricalDataProvider CreateCompositeBackfillProvider(
        IReadOnlyList<IHistoricalDataProvider> providers)
    {
        var factory = GetRequiredService<ProviderFactory>();
        return factory.CreateCompositeBackfillProvider(providers);
    }

    /// <summary>
    /// Creates a StatusWriter for persisting status information.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <param name="configPath">Path to configuration file.</param>
    /// <returns>Configured StatusWriter.</returns>
    public StatusWriter CreateStatusWriter(AppConfig config, string configPath)
    {
        var configService = GetRequiredService<ConfigurationService>();
        var statusPath = Path.Combine(config.DataRoot, "_status", "status.json");
        return new StatusWriter(statusPath, () => configService.LoadAndPrepareConfig(configPath));
    }

    /// <summary>
    /// Starts hot-reload configuration watching.
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="onReload">Callback when configuration changes.</param>
    /// <param name="onError">Callback for errors.</param>
    /// <returns>ConfigWatcher instance.</returns>
    public ConfigWatcher? StartHotReload(
        string configPath,
        Action<AppConfig> onReload,
        Action<Exception> onError)
    {
        var configService = GetRequiredService<ConfigurationService>();
        return configService.StartHotReload(configPath, onReload, onError);
    }

    /// <summary>
    /// Initializes HttpClientFactory for proper HTTP client lifecycle management.
    /// Also wires the CircuitBreakerCallbackRouter so that Polly state-change callbacks
    /// can forward to CircuitBreakerStatusService at request time.
    /// </summary>
    private static void InitializeHttpClientFactory(IServiceProvider serviceProvider, Serilog.ILogger log)
    {
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        if (httpClientFactory != null)
        {
            HttpClientFactoryProvider.Initialize(serviceProvider);
            log.Debug("HttpClientFactory initialized with named clients for all data providers");
        }

        // Wire the circuit breaker callback router if the service is registered.
        var cbService = serviceProvider.GetService<CircuitBreakerStatusService>();
        if (cbService != null)
        {
            CircuitBreakerCallbackRouter.Initialize(cbService);
            log.Debug("CircuitBreakerCallbackRouter initialized - circuit breaker states will be tracked");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            var cleanupFailures = new List<Exception>();

            var stopFailure = await RunBoundedCleanupAsync(
                cancellationToken => _host.StopAsync(cancellationToken),
                _stopTimeout,
                "Generic Host stop",
                _log).ConfigureAwait(false);
            if (stopFailure is not null)
                cleanupFailures.Add(stopFailure);

            if (_options.EnablePipelineServices)
            {
                var pipeline = GetService<EventPipeline>();
                if (pipeline != null)
                {
                    var flushFailure = await RunBoundedCleanupAsync(
                        pipeline.FlushAsync,
                        _stopTimeout,
                        "Event pipeline flush",
                        _log).ConfigureAwait(false);
                    if (flushFailure is not null)
                        cleanupFailures.Add(flushFailure);
                }
            }

            var disposalFailure = await RunBoundedCleanupAsync(
                _ => DisposeHostAsync(_host).AsTask(),
                _stopTimeout,
                "Generic Host disposal",
                _log).ConfigureAwait(false);
            if (disposalFailure is not null)
                cleanupFailures.Add(disposalFailure);

            ThrowCleanupFailures(cleanupFailures);
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    private static void ThrowCleanupFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0)
            return;

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
            return;
        }

        throw new AggregateException(
            "Generic Host shutdown encountered multiple cleanup failures.",
            failures);
    }

    private static async Task StopAndDisposeFailedHostAsync(
        IHost host,
        Serilog.ILogger log,
        TimeSpan stopTimeout)
    {
        _ = await RunBoundedCleanupAsync(
            cancellationToken => host.StopAsync(cancellationToken),
            stopTimeout,
            "Generic Host cleanup after startup failure",
            log).ConfigureAwait(false);
        _ = await RunBoundedCleanupAsync(
            _ => DisposeHostAsync(host).AsTask(),
            stopTimeout,
            "Generic Host disposal after startup failure",
            log).ConfigureAwait(false);
    }

    private static async Task<Exception?> RunBoundedCleanupAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        string operationName,
        Serilog.ILogger log)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        Task? operationTask = null;
        try
        {
            operationTask = operation(timeoutCts.Token);
            await operationTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            log.Warning(
                "{OperationName} did not complete within {TimeoutSeconds} seconds",
                operationName,
                timeout.TotalSeconds);
            ObserveLateFailure(operationTask, operationName, log);
            return new TimeoutException(
                $"{operationName} did not complete within {timeout.TotalSeconds} seconds.",
                ex);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "{OperationName} raised an error", operationName);
            return ex;
        }
    }

    private static void ObserveLateFailure(
        Task? operationTask,
        string operationName,
        Serilog.ILogger log)
    {
        if (operationTask is null || operationTask.IsCompleted)
            return;

        _ = operationTask.ContinueWith(
            completedTask =>
                log.Warning(
                    completedTask.Exception?.GetBaseException(),
                    "{OperationName} faulted after its shutdown deadline",
                    operationName),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();

        host.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class DatabaseInitializationHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public DatabaseInitializationHostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LedgerStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await SecurityMasterStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await DirectLendingStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await AssetOperationsStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await FundAccountsStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await FundStructureStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await BankingStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
            await MoneyMarketStartup.EnsureDatabaseReadyAsync(_serviceProvider, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

/// <summary>
/// Static entry point for selecting canonical <see cref="CompositionOptions"/> presets
/// and creating <see cref="HostStartup"/> instances for the shared startup layer.
/// </summary>
public static class HostStartupFactory
{
    /// <summary>
    /// Resolves the canonical host profile for the supplied deployment context.
    /// </summary>
    public static CompositionOptions ResolveProfile(DeploymentContext deployment)
    {
        return deployment.Mode switch
        {
            DeploymentMode.Desktop => CompositionOptions.Default,
            _ => CompositionOptions.Streaming
        };
    }

    /// <summary>
    /// Creates the appropriate HostStartup based on deployment context.
    /// </summary>
    /// <param name="deployment">Deployment context from command line arguments.</param>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup instance.</returns>
    public static Task<HostStartup> CreateAsync(
        DeploymentContext deployment,
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(deployment, configPath, configureServices: null, cancellationToken);

    internal static Task<HostStartup> CreateAsync(
        DeploymentContext deployment,
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(deployment, configPath, configureServices, cancellationToken);

    private static Task<HostStartup> CreateCoreAsync(
        DeploymentContext deployment,
        string configPath,
        Action<IServiceCollection>? configureServices,
        CancellationToken cancellationToken)
        => HostStartup.CreateStartedHostAsync(
            ResolveProfile(deployment) with { ConfigPath = configPath },
            enableProcessWideHostedServices: deployment.Mode != DeploymentMode.Desktop,
            configureServices,
            cancellationToken,
            enableDatabaseInitialization: deployment.Mode != DeploymentMode.Desktop);

    /// <summary>
    /// Creates a HostStartup for backfill operations.
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup for backfill.</returns>
    public static Task<HostStartup> CreateForBackfillAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => HostStartup.CreateForBackfillAsync(configPath, cancellationToken);

    /// <summary>
    /// Creates a backfill HostStartup while respecting background-service ownership for the
    /// supplied deployment context.
    /// </summary>
    /// <param name="deployment">Deployment context that identifies the parent host.</param>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup for backfill.</returns>
    public static Task<HostStartup> CreateForBackfillAsync(
        DeploymentContext deployment,
        string configPath,
        CancellationToken cancellationToken = default)
        => CreateForBackfillCoreAsync(
            deployment,
            configPath,
            configureServices: null,
            cancellationToken);

    internal static Task<HostStartup> CreateForBackfillAsync(
        DeploymentContext deployment,
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken = default)
        => CreateForBackfillCoreAsync(
            deployment,
            configPath,
            configureServices,
            cancellationToken);

    private static Task<HostStartup> CreateForBackfillCoreAsync(
        DeploymentContext deployment,
        string configPath,
        Action<IServiceCollection>? configureServices,
        CancellationToken cancellationToken)
        => HostStartup.CreateStartedHostAsync(
            CompositionOptions.BackfillOnly with { ConfigPath = configPath },
            enableProcessWideHostedServices: deployment.Mode != DeploymentMode.Desktop,
            configureServices,
            cancellationToken,
            enableDatabaseInitialization: deployment.Mode != DeploymentMode.Desktop);

    /// <summary>
    /// Creates a HostStartup for utility commands (validation, config checks, etc.).
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <param name="cancellationToken">Token that cancels host startup.</param>
    /// <returns>Configured HostStartup for utilities.</returns>
    public static Task<HostStartup> CreateForUtilityAsync(
        string configPath,
        CancellationToken cancellationToken = default)
        => HostStartup.CreateForUtilityAsync(configPath, cancellationToken);
}
