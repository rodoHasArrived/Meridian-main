using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Concrete test implementation of BackendServiceManagerBase.
/// Uses a temp directory for state files and in-memory process tracking.
/// </summary>
internal sealed class TestBackendServiceManager : BackendServiceManagerBase
{
    private readonly string _appDataDir;
    private bool _processRunning;
    private int _lastPid;
    private bool _healthy;
    public string? LastResolvedPath { get; private set; }
    public string? LastLogInfoMessage { get; private set; }
    public string? LastLogErrorMessage { get; private set; }
    public string? ResolvePathReturn { get; set; }
    public bool StartProcessShouldFail { get; set; }

    public TestBackendServiceManager(string appDataDir) : base(appDataDir)
    {
        _appDataDir = appDataDir;
    }

    // Override to use temp dir instead of actual AppData
    protected override string GetAppDataDirectory() => _appDataDir;

    protected override string? ResolveExecutablePath(string? preferredPath)
    {
        LastResolvedPath = preferredPath;
        return ResolvePathReturn ?? preferredPath;
    }

    protected override int? StartProcess(string executablePath, string workingDirectory)
    {
        if (StartProcessShouldFail) return null;
        _lastPid = 12345;
        _processRunning = true;
        return _lastPid;
    }

    protected override Task<bool> KillProcessAsync(int processId, CancellationToken ct)
    {
        if (processId == _lastPid) _processRunning = false;
        return Task.FromResult(true);
    }

    protected override bool IsProcessRunning(int processId) => _processRunning && processId == _lastPid;

    protected override Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(_healthy);

    protected override void LogInfo(string message, params (string key, string value)[] properties)
    {
        LastLogInfoMessage = message;
    }

    protected override void LogError(string message, Exception? exception = null)
    {
        LastLogErrorMessage = message;
    }

    public void SetHealthy(bool healthy) => _healthy = healthy;
}

public sealed class BackendServiceManagerBaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestBackendServiceManager _sut;

    public BackendServiceManagerBaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mdc-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _sut = new TestBackendServiceManager(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task InstallAsync_Succeeds_WhenPathResolvable()
    {
        // Create a fake executable file
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;

        var result = await _sut.InstallAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("registered");
    }

    [Fact]
    public async Task InstallAsync_Fails_WhenPathNotResolvable()
    {
        _sut.ResolvePathReturn = null;

        var result = await _sut.InstallAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task InstallAsync_WithExplicitPath_UsesPreferredPath()
    {
        var exePath = Path.Combine(_tempDir, "custom-backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;

        await _sut.InstallAsync(exePath);

        _sut.LastResolvedPath.Should().Be(exePath);
    }

    [Fact]
    public async Task StartAsync_Succeeds_WhenExecutableExists()
    {
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;

        // Install first
        await _sut.InstallAsync();

        _sut.SetHealthy(true);
        var result = await _sut.StartAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("started");
    }

    [Fact]
    public async Task StartAsync_Fails_WhenProcessStartFails()
    {
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;

        await _sut.InstallAsync();

        _sut.StartProcessShouldFail = true;
        var result = await _sut.StartAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Failed");
    }

    [Fact]
    public async Task StartAsync_Fails_WhenNoInstallation()
    {
        _sut.ResolvePathReturn = null;

        var result = await _sut.StartAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No backend installation");
    }

    [Fact]
    public async Task StopAsync_Succeeds_WhenProcessRunning()
    {
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;
        _sut.SetHealthy(true);

        await _sut.InstallAsync();
        await _sut.StartAsync();

        var result = await _sut.StopAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("stopped");
    }

    [Fact]
    public async Task StopAsync_Succeeds_WhenNoRuntimeTracked()
    {
        var result = await _sut.StopAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("not tracked");
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsNotInstalled_WhenClean()
    {
        var status = await _sut.GetStatusAsync();

        status.IsInstalled.Should().BeFalse();
        status.IsRunning.Should().BeFalse();
        status.StatusMessage.Should().Contain("not installed");
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsInstalled_AfterInstall()
    {
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;

        await _sut.InstallAsync();

        var status = await _sut.GetStatusAsync();

        status.IsInstalled.Should().BeTrue();
        status.ExecutablePath.Should().Be(exePath);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsRunning_WhenStarted()
    {
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;
        _sut.SetHealthy(true);

        await _sut.InstallAsync();
        await _sut.StartAsync();

        var status = await _sut.GetStatusAsync();

        status.IsRunning.Should().BeTrue();
        status.IsHealthy.Should().BeTrue();
        status.ProcessId.Should().Be(12345);
    }

    [Fact]
    public async Task RestartAsync_StopsAndStarts()
    {
        var exePath = Path.Combine(_tempDir, "backend.exe");
        await File.WriteAllTextAsync(exePath, "fake");
        _sut.ResolvePathReturn = exePath;
        _sut.SetHealthy(true);

        await _sut.InstallAsync();
        await _sut.StartAsync();

        var result = await _sut.RestartAsync();

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void BackendServiceOperationResult_SuccessResult_HasCorrectProperties()
    {
        var result = BackendServiceOperationResult.SuccessResult("All good");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("All good");
    }

    [Fact]
    public void BackendServiceOperationResult_Failed_HasCorrectProperties()
    {
        var result = BackendServiceOperationResult.Failed("Something broke");

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Something broke");
    }
}
