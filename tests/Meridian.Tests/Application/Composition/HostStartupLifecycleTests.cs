using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Composition.Startup.ModeRunners;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Services;
using Meridian.Core.Config;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Platform.Runtime;
using Meridian.Storage;
using Meridian.Storage.Banking;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.MoneyMarket;
using Meridian.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Serilog.Core;
using Xunit;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Guards the executable-host failure mode where a built service graph never enters the
/// Generic Host lifecycle, leaving production guards and background coordination dormant.
/// </summary>
[Collection("Sequential")]
public sealed class HostStartupLifecycleTests
{
    [Theory]
    [InlineData(ExecutableProfile.HeadlessCollector)]
    [InlineData(ExecutableProfile.HeadlessBackfill)]
    [InlineData(ExecutableProfile.DesktopCollector)]
    [InlineData(ExecutableProfile.DesktopBackfill)]
    [InlineData(ExecutableProfile.EtlCommand)]
    [InlineData(ExecutableProfile.UtilityCommand)]
    public async Task CreateAsync_ExecutableProfile_StartsAndStopsHostedService(
        ExecutableProfile profile)
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create($"{nameof(HostStartupLifecycleTests)}-{profile}");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new RecordingHostedService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var startup = await CreateAsync(
            profile,
            configPath,
            services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton<IHostedService>(
                    serviceProvider => serviceProvider.GetRequiredService<RecordingHostedService>());
            },
            timeout.Token);
        try
        {
            probe.StartCount.Should().Be(1, $"{profile} must enter the Generic Host lifecycle");
            probe.StopCount.Should().Be(0);
            probe.Events.Should().Equal("start");
            startup.ServiceProvider.GetRequiredService<RecordingHostedService>()
                .Should().BeSameAs(probe);
            startup.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName
                .Should().Be(Environments.Development);

            var hostedServices = startup.ServiceProvider.GetServices<IHostedService>().ToArray();
            hostedServices[0].Should().BeOfType<ProductionRegistrationGuardService>();
            hostedServices.Count(service => ReferenceEquals(service, probe)).Should().Be(1);
            var hostedServiceNames = hostedServices
                .Select(service => service.GetType().Name)
                .ToArray();
            if (profile is ExecutableProfile.DesktopCollector or ExecutableProfile.DesktopBackfill)
            {
                hostedServiceNames.Should().NotContain(
                    "DatabaseInitializationHostedService",
                    "the desktop parent owns database startup migrations");
            }
            else
            {
                hostedServiceNames.Should().Contain(
                    "DatabaseInitializationHostedService",
                    "headless and one-shot hosts own every configured database startup migration");
            }

            if (profile is ExecutableProfile.DesktopCollector
                or ExecutableProfile.DesktopBackfill
                or ExecutableProfile.EtlCommand
                or ExecutableProfile.UtilityCommand)
            {
                hostedServiceNames.Should().NotContain(
                    "ClusterCoordinatorService",
                    "desktop parents own coordination and one-shot ETL must not activate it");
                hostedServiceNames.Should().NotContain(
                    "SplitBrainDetector",
                    "desktop parents own detection and one-shot ETL must not activate it");
                hostedServiceNames.Should().Contain(
                    "StorageCatalogInitializationHostedService",
                    "the child graph has its own storage catalog instance");
                hostedServiceNames.Should().Contain(
                    "SymbolRegistryInitializationHostedService",
                    "the child graph has its own symbol registry instance");
                if (profile == ExecutableProfile.UtilityCommand)
                {
                    hostedServiceNames.Should().NotContain(
                        "CanonicalSymbolRegistryMigrationService",
                        "the minimal utility profile does not enable symbol management");
                }
                else
                {
                    hostedServiceNames.Should().Contain(
                        "CanonicalSymbolRegistryMigrationService",
                        "desktop, backfill, collector, and ETL providers need initialized canonical aliases");
                }
            }
            else
            {
                hostedServiceNames.Should().Contain("ClusterCoordinatorService");
                hostedServiceNames.Should().Contain("SplitBrainDetector");
            }
        }
        finally
        {
            await startup.DisposeAsync();
        }

        await startup.DisposeAsync();
        probe.StartCount.Should().Be(1);
        probe.StopCount.Should().Be(
            1,
            $"{profile} must stop hosted services exactly once during disposal");
        probe.Events.Should().Equal("start", "stop");
    }

    [Fact]
    public async Task CreateStartedHostAsync_ConfiguredFundOperationStores_AreResolvableByGenericHost()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-fund-operation-stores");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        const string connectionString =
            "Host=composition-test.invalid;Port=5432;Database=meridian;Username=test;Password=test";
        Environment.SetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable, connectionString);
        Environment.SetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable, connectionString);
        Environment.SetEnvironmentVariable(BankingStartup.ConnectionStringVariable, connectionString);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.ConnectionStringVariable, connectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var startup = await HostStartup.CreateStartedHostAsync(
            CompositionOptions.Minimal with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            configureServices: null,
            timeout.Token,
            enableDatabaseInitialization: false);

        startup.ServiceProvider.GetRequiredService<FundAccountStoreOptions>()
            .ConnectionString.Should().Be(connectionString);
        startup.ServiceProvider.GetRequiredService<IFundAccountStore>()
            .Should().BeOfType<PostgresFundAccountStore>();
        startup.ServiceProvider.GetRequiredService<FundStructureStoreOptions>()
            .ConnectionString.Should().Be(connectionString);
        startup.ServiceProvider.GetRequiredService<IFundStructureStore>()
            .Should().BeOfType<PostgresFundStructureStore>();
        startup.ServiceProvider.GetRequiredService<BankingStoreOptions>()
            .ConnectionString.Should().Be(connectionString);
        startup.ServiceProvider.GetRequiredService<IBankingStore>()
            .Should().BeOfType<PostgresBankingStore>();
        startup.ServiceProvider.GetRequiredService<MoneyMarketStoreOptions>()
            .ConnectionString.Should().Be(connectionString);
        startup.ServiceProvider.GetRequiredService<IMoneyMarketFundAuxStore>()
            .Should().BeOfType<PostgresMoneyMarketFundStore>();
    }

    [Theory]
    [InlineData(FundAccountsStartup.ConnectionStringVariable)]
    [InlineData(FundStructureStartup.ConnectionStringVariable)]
    [InlineData(BankingStartup.ConnectionStringVariable)]
    [InlineData(MoneyMarketStartup.ConnectionStringVariable)]
    public async Task CreateStartedHostAsync_ConfiguredFundOperationDomain_RunsOwnedMigration(
        string connectionStringVariable)
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-owned-migration-{connectionStringVariable}");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        Environment.SetEnvironmentVariable(
            connectionStringVariable,
            "not-a-valid-postgres-connection-string");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Func<Task> act = async () =>
        {
            await using var _ = await HostStartup.CreateStartedHostAsync(
                CompositionOptions.Minimal with { ConfigPath = configPath },
                enableProcessWideHostedServices: false,
                configureServices: null,
                timeout.Token);
        };

        await act.Should().ThrowAsync<ArgumentException>(
            "the generic host must execute every configured fund-operation migration");
    }

    [Fact]
    public async Task CreateAsync_FinalProductionGuardRunsBeforeProbeStarts()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-production-guard");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new RecordingHostedService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Func<Task> act = async () =>
        {
            await using var _ = await HostStartup.CreateForUtilityAsync(
                configPath,
                services =>
                {
                    services.DeclareMeridianDeploymentPosture(MeridianDeploymentPosture.ProductionApi);
                    services.AddSingleton<NonProductionProbeService>();
                    services.AddSingleton(probe);
                    services.AddSingleton<IHostedService>(
                        serviceProvider => serviceProvider.GetRequiredService<RecordingHostedService>());
                },
                timeout.Token);
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Production startup policy*");
        probe.StartCount.Should().Be(0, "the final production guard is the first hosted service");
        probe.StopCount.Should().Be(
            1,
            "failed Generic Host startup must run one bounded cleanup pass");
        probe.Events.Should().Equal("stop");
    }

    [Fact]
    public async Task CreateAsync_DisposalFailureDoesNotMaskStartupFailure()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-startup-failure");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new RecordingHostedService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Func<Task> act = async () =>
        {
            await using var _ = await HostStartup.CreateForUtilityAsync(
                configPath,
                services =>
                {
                    services.AddSingleton(probe);
                    services.AddSingleton<IHostedService>(
                        serviceProvider =>
                            serviceProvider.GetRequiredService<RecordingHostedService>());
                    services.AddSingleton<IHostedService, ThrowOnStartAndDisposeHostedService>();
                },
                timeout.Token);
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage(ThrowOnStartAndDisposeHostedService.StartupFailureMessage);
        probe.StartCount.Should().Be(1);
        probe.StopCount.Should().Be(1);
        probe.Events.Should().Equal("start", "stop");
    }

    [Fact]
    public async Task DisposeAsync_HostDisposalFailure_PropagatesOriginalFailure()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-disposal-failure");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new FailingCleanupHostedService(failStop: false, failDispose: true);
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startup = await HostStartup.CreateStartedHostAsync(
            CompositionOptions.Minimal with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton<IHostedService>(
                    serviceProvider => serviceProvider.GetRequiredService<FailingCleanupHostedService>());
            },
            startupTimeout.Token);

        var thrown = await Assert.ThrowsAsync<ApplicationException>(
            () => startup.DisposeAsync().AsTask());

        thrown.Should().BeSameAs(probe.DisposalFailure);
        probe.StopCount.Should().Be(1);
        probe.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_StopAndHostDisposalFailures_AreAggregatedAfterBothRun()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-aggregate-cleanup-failure");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new FailingCleanupHostedService(failStop: true, failDispose: true);
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startup = await HostStartup.CreateStartedHostAsync(
            CompositionOptions.Minimal with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton<IHostedService>(
                    serviceProvider => serviceProvider.GetRequiredService<FailingCleanupHostedService>());
            },
            startupTimeout.Token);

        var thrown = await Assert.ThrowsAsync<AggregateException>(
            () => startup.DisposeAsync().AsTask());

        thrown.Flatten().InnerExceptions.Should().ContainSingle(
            failure => ReferenceEquals(failure, probe.StopFailure));
        thrown.Flatten().InnerExceptions.Should().ContainSingle(
            failure => ReferenceEquals(failure, probe.DisposalFailure));
        probe.StopCount.Should().Be(1);
        probe.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_NonCooperativeHostedServiceStop_RemainsBoundedAndDisposesHost()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-bounded-stop");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new NonCooperativeStopHostedService();
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startup = await HostStartup.CreateStartedHostAsync(
            CompositionOptions.Minimal with { ConfigPath = configPath },
            enableProcessWideHostedServices: false,
            services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton<IHostedService>(
                    serviceProvider => serviceProvider.GetRequiredService<NonCooperativeStopHostedService>());
            },
            startupTimeout.Token,
            enableDatabaseInitialization: true,
            stopTimeout: TimeSpan.FromMilliseconds(75));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var thrown = await Assert.ThrowsAsync<TimeoutException>(
            async () => await startup.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        stopwatch.Stop();

        thrown.Message.Should().Contain("Generic Host stop");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        probe.StopCount.Should().Be(1);
        probe.DisposeCount.Should().Be(1);

        probe.ReleaseStop();
        await probe.StopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(DeploymentMode.Headless)]
    [InlineData(DeploymentMode.Desktop)]
    public async Task BackfillRunner_UsesOneStartedHostForPipelineAndExecution(
        DeploymentMode deploymentMode)
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-backfill-runner-{deploymentMode}");
        var backfillDate = new DateOnly(2025, 1, 2);
        var config = CreateConfig(artifacts.RootPath) with
        {
            Backfill = new BackfillConfig(
                Enabled: true,
                Provider: "synthetic",
                Symbols: ["SPY"],
                From: backfillDate,
                To: backfillDate,
                EnableFallback: false,
                Providers: new BackfillProvidersConfig(
                    Synthetic: new SyntheticMarketDataConfig(
                        Enabled: true,
                        Scenario: new SyntheticScenarioConfig(
                            Enabled: true,
                            BackfillBarsLimit: 1))))
        };
        var configPath = await WriteConfigAsync(config);
        await using var configurationService = new ConfigurationService(Logger.None);
        using var lifecycle = ApplicationLifecycleCoordinator.Create(Logger.None);
        var deployment = CreateDeployment(deploymentMode, configPath);
        var context = new StartupContext
        {
            CliArgs = CliArguments.Parse(["--backfill"]),
            ConfigPath = configPath,
            Config = config,
            Deployment = deployment,
            ConfigurationService = configurationService,
            DashboardServerFactory = static (_, _, _) =>
                throw new NotSupportedException("Backfill runner must not create a dashboard server."),
            Lifecycle = lifecycle,
            Log = Logger.None,
            CancellationToken = CancellationToken.None
        };
        var probe = new RecordingHostedService();
        var hostFactoryCalls = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runner = new BackfillModeRunner(
            Logger.None,
            async (requestedDeployment, requestedConfigPath, cancellationToken) =>
            {
                Interlocked.Increment(ref hostFactoryCalls);
                return await HostStartupFactory.CreateForBackfillAsync(
                    requestedDeployment,
                    requestedConfigPath,
                    services =>
                    {
                        services.AddSingleton(probe);
                        services.AddSingleton<IHostedService>(
                            serviceProvider =>
                                serviceProvider.GetRequiredService<RecordingHostedService>());
                    },
                    cancellationToken);
            });

        var exitCode = await runner.RunAsync(context, timeout.Token);

        exitCode.Should().Be(0);
        hostFactoryCalls.Should().Be(1, "backfill must not create separate pipeline and provider hosts");
        probe.StartCount.Should().Be(1);
        probe.StopCount.Should().Be(1);
        probe.Events.Should().Equal("start", "stop");
    }

    [Fact]
    public async Task BackfillRunner_CancellationDuringProviderCall_DisposesProviderExactlyOnce()
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-backfill-provider-cancellation");
        var backfillDate = new DateOnly(2025, 1, 2);
        var config = CreateConfig(artifacts.RootPath) with
        {
            Backfill = new BackfillConfig(
                Enabled: true,
                Provider: "synthetic",
                Symbols: ["SPY"],
                From: backfillDate,
                To: backfillDate,
                EnableFallback: false)
        };
        var configPath = await WriteConfigAsync(config);
        await using var configurationService = new ConfigurationService(Logger.None);
        using var lifecycle = ApplicationLifecycleCoordinator.Create(Logger.None);
        var context = new StartupContext
        {
            CliArgs = CliArguments.Parse(["--backfill"]),
            ConfigPath = configPath,
            Config = config,
            Deployment = CreateDeployment(DeploymentMode.Headless, configPath),
            ConfigurationService = configurationService,
            DashboardServerFactory = static (_, _, _) =>
                throw new NotSupportedException("Backfill runner must not create a dashboard server."),
            Lifecycle = lifecycle,
            Log = Logger.None,
            CancellationToken = CancellationToken.None
        };

        var provider = new Mock<IHistoricalDataProvider>();
        var providerCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCount = 0;
        provider.SetupGet(item => item.Name).Returns("synthetic");
        provider.SetupGet(item => item.DisplayName).Returns("Synthetic cancellation probe");
        provider.SetupGet(item => item.Description).Returns("Blocks until cancellation.");
        provider
            .Setup(item => item.GetDailyBarsAsync(
                It.IsAny<string>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                async (string _, DateOnly? _, DateOnly? _, CancellationToken token) =>
                {
                    providerCallStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Array.Empty<Meridian.Contracts.Domain.Models.HistoricalBar>();
                });
        provider.Setup(item => item.Dispose()).Callback(() => Interlocked.Increment(ref disposeCount));

        var runner = new BackfillModeRunner(
            Logger.None,
            static (deployment, requestedConfigPath, cancellationToken) =>
                HostStartupFactory.CreateForBackfillAsync(
                    deployment,
                    requestedConfigPath,
                    cancellationToken),
            _ => [provider.Object]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var runTask = runner.RunAsync(context, cancellation.Token);
        await providerCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        disposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData("--etl-resume")]
    [InlineData("--etl-import")]
    public async Task EtlCommandRoute_UsesStartedOneShotHostAndDisposesIt(string route)
    {
        using var environment = HostStartupTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(HostStartupLifecycleTests)}-etl-route-{route.TrimStart('-')}");
        var configPath = await WriteConfigAsync(artifacts.RootPath);
        var probe = new RecordingHostedService();
        var hostFactoryCalls = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var command = new EtlCommands(
            configPath,
            Logger.None,
            async (requestedConfigPath, cancellationToken) =>
            {
                Interlocked.Increment(ref hostFactoryCalls);
                requestedConfigPath.Should().Be(configPath);
                return await HostStartup.CreateForEtlAsync(
                    requestedConfigPath,
                    services =>
                    {
                        services.AddSingleton(probe);
                        services.AddSingleton<IHostedService>(
                            serviceProvider =>
                                serviceProvider.GetRequiredService<RecordingHostedService>());
                    },
                    cancellationToken);
            });

        _ = await command.ExecuteAsync([route], timeout.Token);

        hostFactoryCalls.Should().Be(1);
        probe.StartCount.Should().Be(1);
        probe.StopCount.Should().Be(1);
        probe.Events.Should().Equal("start", "stop");
    }

    private static Task<HostStartup> CreateAsync(
        ExecutableProfile profile,
        string configPath,
        Action<IServiceCollection> configureServices,
        CancellationToken cancellationToken)
        => profile switch
        {
            ExecutableProfile.HeadlessCollector => HostStartupFactory.CreateAsync(
                CreateDeployment(DeploymentMode.Headless, configPath),
                configPath,
                configureServices,
                cancellationToken),
            ExecutableProfile.HeadlessBackfill => HostStartupFactory.CreateForBackfillAsync(
                CreateDeployment(DeploymentMode.Headless, configPath),
                configPath,
                configureServices,
                cancellationToken),
            ExecutableProfile.DesktopCollector => HostStartupFactory.CreateAsync(
                CreateDeployment(DeploymentMode.Desktop, configPath),
                configPath,
                configureServices,
                cancellationToken),
            ExecutableProfile.DesktopBackfill => HostStartupFactory.CreateForBackfillAsync(
                CreateDeployment(DeploymentMode.Desktop, configPath),
                configPath,
                configureServices,
                cancellationToken),
            ExecutableProfile.EtlCommand => HostStartup.CreateForEtlAsync(
                configPath,
                configureServices,
                cancellationToken),
            ExecutableProfile.UtilityCommand => HostStartup.CreateForUtilityAsync(
                configPath,
                configureServices,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };

    private static DeploymentContext CreateDeployment(DeploymentMode mode, string configPath)
        => new()
        {
            Mode = mode,
            ConfigPath = configPath
        };

    private static async Task<string> WriteConfigAsync(string dataRoot)
        => await WriteConfigAsync(CreateConfig(dataRoot));

    private static async Task<string> WriteConfigAsync(AppConfig config)
    {
        var configPath = Path.Combine(config.DataRoot, "appsettings.json");
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(config, AppConfigJsonOptions.Write));
        return configPath;
    }

    private static AppConfig CreateConfig(string dataRoot)
        => new(
            DataRoot: dataRoot,
            Compress: false,
            Storage: new StorageConfig(),
            Backfill: new BackfillConfig(Enabled: false),
            Coordination: new CoordinationConfig(Enabled: false));

    public enum ExecutableProfile : byte
    {
        HeadlessCollector,
        HeadlessBackfill,
        DesktopCollector,
        DesktopBackfill,
        EtlCommand,
        UtilityCommand
    }

    private sealed class RecordingHostedService : IHostedService
    {
        private readonly object _gate = new();
        private readonly List<string> _events = [];
        private int _startCount;
        private int _stopCount;

        public int StartCount => Volatile.Read(ref _startCount);
        public int StopCount => Volatile.Read(ref _stopCount);

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToArray();
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCount);
            lock (_gate)
            {
                _events.Add("start");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);
            lock (_gate)
            {
                _events.Add("stop");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NonProductionProbeService : INonProductionOnlyService;

    private sealed class ThrowOnStartAndDisposeHostedService : IHostedService, IAsyncDisposable
    {
        public const string StartupFailureMessage = "hosted-service startup sentinel";

        public Task StartAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException(StartupFailureMessage);

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.FromException(new ApplicationException("hosted-service disposal sentinel"));
    }

    private sealed class FailingCleanupHostedService : IHostedService, IAsyncDisposable
    {
        private readonly bool _failStop;
        private readonly bool _failDispose;
        private int _stopCount;
        private int _disposeCount;

        public FailingCleanupHostedService(bool failStop, bool failDispose)
        {
            _failStop = failStop;
            _failDispose = failDispose;
        }

        public ApplicationException StopFailure { get; } =
            new("hosted-service stop sentinel");
        public ApplicationException DisposalFailure { get; } =
            new("hosted-service disposal sentinel");
        public int StopCount => Volatile.Read(ref _stopCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);
            return _failStop ? Task.FromException(StopFailure) : Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return _failDispose
                ? ValueTask.FromException(DisposalFailure)
                : ValueTask.CompletedTask;
        }
    }

    private sealed class NonCooperativeStopHostedService : IHostedService, IAsyncDisposable
    {
        private readonly TaskCompletionSource _releaseStop =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopCount;
        private int _disposeCount;

        public int StopCount => Volatile.Read(ref _stopCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public TaskCompletionSource StopCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);
            await _releaseStop.Task.ConfigureAwait(false);
            StopCompleted.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }

        public void ReleaseStop() => _releaseStop.TrySetResult();
    }

    private sealed class HostStartupTestEnvironment : IDisposable
    {
        private static readonly string[] VariableNames =
        [
            "DOTNET_ENVIRONMENT",
            "ASPNETCORE_ENVIRONMENT",
            "MERIDIAN_ENVIRONMENT",
            "MERIDIAN_DEPLOYMENT_ENVIRONMENT",
            "MERIDIAN_MODE",
            "MERIDIAN_API_DEPLOYMENT_MODE",
            "MERIDIAN_USE_INMEMORY_GOVERNANCE",
            MeridianDatabaseEnvironment.UnifiedVariable,
            .. MeridianDatabaseEnvironment.PropagatedConnectionStringVariables,
            SecurityMasterStartup.SchemaVariable,
            AssetOperationsStartup.SchemaVariable,
            DirectLendingStartup.SchemaVariable,
            LedgerStartup.SchemaVariable,
            FundAccountsStartup.SchemaVariable,
            FundStructureStartup.SchemaVariable,
            BankingStartup.SchemaVariable,
            MoneyMarketStartup.SchemaVariable,
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING",
            "MERIDIAN_SCOPED_ACCESS_SCHEMA"
        ];

        private readonly IReadOnlyDictionary<string, string?> _originalValues;

        private HostStartupTestEnvironment()
        {
            _originalValues = VariableNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    name => name,
                    Environment.GetEnvironmentVariable,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var name in _originalValues.Keys)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        }

        public static HostStartupTestEnvironment Enable() => new();

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
