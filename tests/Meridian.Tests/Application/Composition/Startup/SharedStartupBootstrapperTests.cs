using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Commands;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Config;
using Meridian.Core.Config;
using Meridian.Application.Services;
using Meridian.Platform.Runtime;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup;

[Collection("Sequential")]
public sealed class SharedStartupBootstrapperTests : IDisposable
{
    private static readonly string[] StartupEnvironmentVariables =
    [
        "DOTNET_ENVIRONMENT",
        "ASPNETCORE_ENVIRONMENT",
        "MERIDIAN_USE_INMEMORY_GOVERNANCE",
        SecurityMasterStartup.ConnectionStringVariable,
        SecurityMasterStartup.SchemaVariable,
        DirectLendingStartup.ConnectionStringVariable,
        DirectLendingStartup.SchemaVariable,
        LedgerStartup.ConnectionStringVariable,
        LedgerStartup.SchemaVariable,
        FundAccountsStartup.ConnectionStringVariable,
        FundAccountsStartup.SchemaVariable,
        FundStructureStartup.ConnectionStringVariable,
        FundStructureStartup.SchemaVariable,
        BankingStartup.ConnectionStringVariable,
        BankingStartup.SchemaVariable,
        MoneyMarketStartup.ConnectionStringVariable,
        MoneyMarketStartup.SchemaVariable,
        "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING",
        "MERIDIAN_SCOPED_ACCESS_SCHEMA"
    ];

    private readonly string? _originalConfigPath = Environment.GetEnvironmentVariable("MDC_CONFIG_PATH");
    private readonly Dictionary<string, string?> _originalEnvironment = CaptureEnvironment(StartupEnvironmentVariables);
    private readonly string _originalCurrentDirectory = Environment.CurrentDirectory;
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();
    private readonly List<string> _tempDirectories = [];

    public SharedStartupBootstrapperTests()
    {
        ConfigureLocalInMemoryGovernanceFixture();
    }

    [Fact]
    public void ResolveConfigPath_PrefersCliArgumentOverEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("MDC_CONFIG_PATH", "/env/config.json");
        var cliArgs = CliArguments.Parse(["--config", "/cli/config.json"]);

        var resolved = SharedStartupHelpers.ResolveConfigPath(cliArgs);

        resolved.Should().Be("/cli/config.json");
    }

    [Fact]
    public void ResolveConfigPath_UsesEnvironmentVariableWhenCliArgumentMissing()
    {
        Environment.SetEnvironmentVariable("MDC_CONFIG_PATH", "/env/config.json");
        var cliArgs = CliArguments.Parse([]);

        var resolved = SharedStartupHelpers.ResolveConfigPath(cliArgs);

        resolved.Should().Be("/env/config.json");
    }

    [Fact]
    public void ResolveConfigPath_FindsConfigDirectoryInAncestorDirectory()
    {
        Environment.SetEnvironmentVariable("MDC_CONFIG_PATH", null);

        var repositoryRoot = CreateTempDirectory();
        var configDirectory = Path.Combine(repositoryRoot, "config");
        Directory.CreateDirectory(configDirectory);

        File.WriteAllText(Path.Combine(configDirectory, "appsettings.json"), "{}");

        var nestedWorkingDirectory = Path.Combine(repositoryRoot, "src", "Meridian.Ui");
        Directory.CreateDirectory(nestedWorkingDirectory);
        Environment.CurrentDirectory = nestedWorkingDirectory;
        var expectedPath = Path.Combine(
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "..")),
            "config",
            "appsettings.json");

        var resolved = SharedStartupHelpers.ResolveConfigPath(CliArguments.Parse([]));

        resolved.Should().Be(expectedPath);
    }

    [Fact]
    public async Task RunAsync_DesktopModeCancellation_StillStopsDashboardServerGracefully()
    {
        var cfg = new AppConfig { DataRoot = CreateTempDirectory() };
        var configPath = CreateConfigFile(cfg);
        var cliArgs = CliArguments.Parse([]);
        var deployment = DeploymentContext.ForDesktop(configPath, port: 4321);
        using var cts = new CancellationTokenSource();
        await using var configService = new ConfigurationService(_log);

        FakeDashboardServer? server = null;
        var orchestrator = new HostModeOrchestrator(
            _log,
            (configPath, port, _) =>
            {
                server = new FakeDashboardServer(configPath, port, cts);
                return server;
            });

        Func<Task<int>> act = () => orchestrator.RunAsync(cliArgs, cfg, configPath, configService, deployment, cts.Token);

        var exitCode = await act();
        exitCode.Should().Be(0);
        server.Should().NotBeNull();
        server!.ConfigPath.Should().Be(configPath);
        server.Port.Should().Be(4321);
        server.StartCallCount.Should().Be(1);
        server.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
        server.StopCancellationToken.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_DesktopMode_RemainsAliveUntilCancellation()
    {
        var cfg = new AppConfig { DataRoot = CreateTempDirectory() };
        var configPath = CreateConfigFile(cfg);
        var cliArgs = CliArguments.Parse([]);
        var deployment = DeploymentContext.ForDesktop(configPath, port: 4321);
        using var cts = new CancellationTokenSource();
        await using var configService = new ConfigurationService(_log);

        FakeDashboardServer? server = null;
        var orchestrator = new HostModeOrchestrator(
            _log,
            (configPath, port, _) =>
            {
                server = new FakeDashboardServer(configPath, port, waitForStartCancellation: true);
                return server;
            });

        var runTask = orchestrator.RunAsync(cliArgs, cfg, configPath, configService, deployment, cts.Token);

        await Task.Delay(200);
        runTask.IsCompleted.Should().BeFalse("desktop mode should keep the collector alive until shutdown is requested");

        cts.Cancel();

        var exitCode = await runTask;
        exitCode.Should().Be(0);
        server.Should().NotBeNull();
        server!.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WorkstationMode_RemainsAliveUntilLifecycleShutdown()
    {
        var cfg = new AppConfig { DataRoot = CreateTempDirectory() };
        var configPath = CreateConfigFile(cfg);
        var cliArgs = CliArguments.Parse(["--mode", "workstation"]);
        var deployment = DeploymentContext.ForWorkstation(configPath, port: 4321);
        await using var configService = new ConfigurationService(_log);

        FakeDashboardServer? server = null;
        var orchestrator = new HostModeOrchestrator(
            _log,
            (configPath, port, lifecycle) =>
            {
                server = new FakeDashboardServer(configPath, port, lifecycle: lifecycle);
                return server;
            });

        var runTask = orchestrator.RunAsync(cliArgs, cfg, configPath, configService, deployment);

        await Task.Delay(200);
        runTask.IsCompleted.Should().BeFalse("workstation mode should serve the browser workstation until the lifecycle requests shutdown");

        server.Should().NotBeNull();
        await server!.Lifecycle!.RequestShutdownAsync("test-shutdown");

        var exitCode = await runTask;
        exitCode.Should().Be(0);
        server.StartCallCount.Should().Be(1);
        server.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_EmitsCorrelatedStartupPhaseDiagnostics()
    {
        var cfg = new AppConfig { DataRoot = CreateTempDirectory() };
        var cliArgs = CliArguments.Parse(["--help"]);
        var deployment = DeploymentContext.ForCommand("help", "accountNumber=ACCT-123456-test.json");
        await using var configService = new ConfigurationService(_log);
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var orchestrator = new HostModeOrchestrator(
            logger,
            (_, _, _) => throw new InvalidOperationException("Command dispatch should not start the dashboard server."));

        var exitCode = await orchestrator.RunAsync(cliArgs, cfg, deployment.ConfigPath, configService, deployment)
            .WaitAsync(TimeSpan.FromSeconds(10));

        exitCode.Should().Be(0);
        var events = sink.Events;
        var startupStart = events.Should().ContainSingle(evt =>
            evt.MessageTemplate.Text.Contains("Startup sequence started for {OperationName}", StringComparison.Ordinal))
            .Subject;
        var correlationId = startupStart.Properties["CorrelationId"].ToString();

        startupStart.Properties["OperationName"].ToString().Should().Contain("runtime.startup.sequence");
        startupStart.Properties["ComponentName"].ToString().Should().Contain("StartupOrchestrator");
        startupStart.Properties["ConfigPath"].ToString().Should().Contain("[REDACTED]");
        startupStart.Properties["ConfigPath"].ToString().Should().NotContain("ACCT-123456");

        events.Should().Contain(evt =>
            evt.MessageTemplate.Text.Contains("Startup phase completed for {OperationName}", StringComparison.Ordinal)
            && evt.Properties["CorrelationId"].ToString() == correlationId
            && evt.Properties["StartupPhase"].ToString().Contains("command-dispatch", StringComparison.Ordinal));
        events.Should().Contain(evt =>
            evt.MessageTemplate.Text.Contains("Startup sequence completed for {OperationName}", StringComparison.Ordinal)
            && evt.Properties["CorrelationId"].ToString() == correlationId
            && evt.Properties["ExitCode"].ToString().Contains("0", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MDC_CONFIG_PATH", _originalConfigPath);
        RestoreEnvironment(_originalEnvironment);
        Environment.CurrentDirectory = _originalCurrentDirectory;
        (_log as IDisposable)?.Dispose();

        foreach (var path in _tempDirectories.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private string CreateConfigFile(AppConfig cfg)
    {
        var path = Path.Combine(CreateTempDirectory(), "appsettings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, AppConfigJsonOptions.Write));
        return path;
    }

    private static Dictionary<string, string?> CaptureEnvironment(IEnumerable<string> names)
        => names.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (name, value) in values)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void ConfigureLocalInMemoryGovernanceFixture()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(FundAccountsStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(FundStructureStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(BankingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(BankingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING", null);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA", null);
    }

    private sealed class FakeDashboardServer : IHostDashboardServer
    {
        private readonly CancellationTokenSource? _cts;
        private readonly bool _waitForStartCancellation;

        public FakeDashboardServer(
            string configPath,
            int port,
            CancellationTokenSource? cts = null,
            IApplicationLifecycleCoordinator? lifecycle = null,
            bool waitForStartCancellation = false)
        {
            ConfigPath = configPath;
            Port = port;
            _cts = cts;
            Lifecycle = lifecycle;
            _waitForStartCancellation = waitForStartCancellation;
        }

        public string ConfigPath { get; }
        public int Port { get; }
        public IApplicationLifecycleCoordinator? Lifecycle { get; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public CancellationToken StopCancellationToken { get; private set; }

        public async Task StartAsync(CancellationToken ct = default)
        {
            StartCallCount++;
            if (_waitForStartCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
            }

            _cts?.Cancel();
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            StopCancellationToken = ct;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly object _gate = new();
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToArray();
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_gate)
            {
                _events.Add(logEvent);
            }
        }
    }
}
