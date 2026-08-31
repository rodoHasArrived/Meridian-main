using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Composition.Startup.ModeRunners;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Contracts.Lifecycle;
using Meridian.Core.Config;
using Meridian.Platform.Runtime;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup;

public sealed class WorkstationModeRunnerTests : IAsyncDisposable
{
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();
    private readonly ConfigurationService _configurationService;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "meridian-lifecycle-mode-runner-tests",
        Guid.NewGuid().ToString("N"));

    public WorkstationModeRunnerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _configurationService = new ConfigurationService(_log);
    }

    [Fact]
    public async Task RunAsync_ShutdownRequestWaitsForTerminationReceiptBoundary()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var server = new RecordingDashboardServer();
        var runner = new WorkstationModeRunner(_log, (_, _, _) => server);
        var context = new StartupContext
        {
            CliArgs = CliArguments.Parse(["--mode", "workstation"]),
            ConfigPath = Path.Combine(_tempDirectory, "appsettings.json"),
            Config = new AppConfig { DataRoot = _tempDirectory },
            Deployment = DeploymentContext.ForWorkstation(Path.Combine(_tempDirectory, "appsettings.json"), 4321),
            ConfigurationService = _configurationService,
            DashboardServerFactory = (_, _, _) => server,
            Lifecycle = lifecycle,
            Log = _log,
            CancellationToken = lifecycle.StopWorkToken
        };

        var runTask = runner.RunAsync(context);
        await server.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await lifecycle.RequestShutdownAsync(new LifecycleShutdownRequestDto
        {
            Reason = LifecycleShutdownReason.Operator
        });

        await Task.Delay(50);
        runTask.IsCompleted.Should().BeFalse("the host must remain alive until shutdown receipt persistence releases termination");

        lifecycle.SignalTermination();
        (await runTask.WaitAsync(TimeSpan.FromSeconds(2))).Should().Be(0);
        server.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ServerStartFailure_StillStopsAndDisposesWithoutMaskingFailure()
    {
        using var lifecycle = ApplicationLifecycleCoordinator.Create(_log);
        var startupFailure = new InvalidOperationException("dashboard-start-failure-sentinel");
        var server = new RecordingDashboardServer(startupFailure);
        var runner = new WorkstationModeRunner(_log, (_, _, _) => server);
        var configPath = Path.Combine(_tempDirectory, "appsettings.json");
        var context = new StartupContext
        {
            CliArgs = CliArguments.Parse(["--mode", "workstation"]),
            ConfigPath = configPath,
            Config = new AppConfig { DataRoot = _tempDirectory },
            Deployment = DeploymentContext.ForWorkstation(configPath, 4321),
            ConfigurationService = _configurationService,
            DashboardServerFactory = (_, _, _) => server,
            Lifecycle = lifecycle,
            Log = _log,
            CancellationToken = lifecycle.StopWorkToken
        };

        var act = () => runner.RunAsync(context);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(startupFailure);
        server.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
    }

    public async ValueTask DisposeAsync()
    {
        await _configurationService.DisposeAsync();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingDashboardServer : IHostDashboardServer
    {
        private readonly Exception? _startFailure;

        public RecordingDashboardServer(Exception? startFailure = null)
        {
            _startFailure = startFailure;
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public Task StartAsync(CancellationToken ct = default)
        {
            Started.TrySetResult();
            if (_startFailure is not null)
                return Task.FromException(_startFailure);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
