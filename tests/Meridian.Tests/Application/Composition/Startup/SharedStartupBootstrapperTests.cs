using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Serilog;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup;

[Collection("Sequential")]
public sealed class SharedStartupBootstrapperTests : IDisposable
{
    private readonly string? _originalConfigPath = Environment.GetEnvironmentVariable("MDC_CONFIG_PATH");
    private readonly string _originalCurrentDirectory = Environment.CurrentDirectory;
    private readonly ILogger _log = new LoggerConfiguration().CreateLogger();
    private readonly List<string> _tempDirectories = [];

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

        var expectedPath = Path.Combine(configDirectory, "appsettings.json");
        File.WriteAllText(expectedPath, "{}");

        var nestedWorkingDirectory = Path.Combine(repositoryRoot, "src", "Meridian.Ui");
        Directory.CreateDirectory(nestedWorkingDirectory);
        Environment.CurrentDirectory = nestedWorkingDirectory;

        var resolved = SharedStartupHelpers.ResolveConfigPath(CliArguments.Parse([]));

        resolved.Should().Be(expectedPath);
    }

    [Fact]
    public async Task RunAsync_DesktopModeCancellation_StillStopsDashboardServerGracefully()
    {
        var cfg = new AppConfig { DataRoot = CreateTempDirectory() };
        var cliArgs = CliArguments.Parse([]);
        var deployment = DeploymentContext.ForDesktop("test.json", port: 4321);
        using var cts = new CancellationTokenSource();
        await using var configService = new ConfigurationService(_log);

        FakeDashboardServer? server = null;
        var orchestrator = new HostModeOrchestrator(
            _log,
            (configPath, port) =>
            {
                server = new FakeDashboardServer(configPath, port, cts);
                return server;
            });

        Func<Task<int>> act = () => orchestrator.RunAsync(cliArgs, cfg, "test.json", configService, deployment, cts.Token);

        var exitCode = await act();
        exitCode.Should().Be(0);
        server.Should().NotBeNull();
        server!.ConfigPath.Should().Be("test.json");
        server.Port.Should().Be(4321);
        server.StartCallCount.Should().Be(1);
        server.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
        server.StopCancellationToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_DesktopMode_RemainsAliveUntilCancellation()
    {
        var cfg = new AppConfig { DataRoot = CreateTempDirectory() };
        var cliArgs = CliArguments.Parse([]);
        var deployment = DeploymentContext.ForDesktop("test.json", port: 4321);
        using var cts = new CancellationTokenSource();
        await using var configService = new ConfigurationService(_log);

        FakeDashboardServer? server = null;
        var orchestrator = new HostModeOrchestrator(
            _log,
            (configPath, port) =>
            {
                server = new FakeDashboardServer(configPath, port);
                return server;
            });

        var runTask = orchestrator.RunAsync(cliArgs, cfg, "test.json", configService, deployment, cts.Token);

        await Task.Delay(200);
        runTask.IsCompleted.Should().BeFalse("desktop mode should keep the collector alive until shutdown is requested");

        cts.Cancel();

        var exitCode = await runTask;
        exitCode.Should().Be(0);
        server.Should().NotBeNull();
        server!.StopCallCount.Should().Be(1);
        server.DisposeCallCount.Should().Be(1);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MDC_CONFIG_PATH", _originalConfigPath);
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

    private sealed class FakeDashboardServer : IHostDashboardServer
    {
        private readonly CancellationTokenSource? _cts;

        public FakeDashboardServer(string configPath, int port, CancellationTokenSource? cts = null)
        {
            ConfigPath = configPath;
            Port = port;
            _cts = cts;
        }

        public string ConfigPath { get; }
        public int Port { get; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public CancellationToken StopCancellationToken { get; private set; }

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCallCount++;
            _cts?.Cancel();
            return Task.CompletedTask;
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
}
