using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class BackendServiceManagerTests
{
    [Fact]
    public void BuildProcessArguments_StartsDesktopModeOnConfiguredPort()
    {
        var args = BackendServiceManager.BuildProcessArguments(
            @"C:\config\appsettings.json",
            "http://localhost:9105");

        args.Should().Equal(
            "--mode",
            "desktop",
            "--config",
            @"C:\config\appsettings.json",
            "--http-port",
            "9105");
    }

    [Fact]
    public void ResolveHttpPort_FallsBackToDefaultPortForInvalidServiceUrl()
    {
        var port = BackendServiceManager.ResolveHttpPort("not-a-valid-url");

        port.Should().Be(8080);
    }

    [Fact]
    public async Task GetStatusAsync_UsesRemoteWorkstationClientForHealth()
    {
        var remoteClient = new FakeRemoteWorkstationClient { HealthEndpointResult = true };
        var manager = new BackendServiceManager(remoteClient, CreateAppDataDirectory());

        var status = await manager.GetStatusAsync();

        status.IsHealthy.Should().BeTrue();
        status.IsRunning.Should().BeTrue("a reachable remote host can be managed externally");
        remoteClient.HealthEndpointCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_WhenRemoteHealthFailsWithoutManagedInstall_ReportsUnhealthy()
    {
        var remoteClient = new FakeRemoteWorkstationClient { HealthEndpointResult = false };
        var manager = new BackendServiceManager(remoteClient, CreateAppDataDirectory());

        var status = await manager.GetStatusAsync();

        status.IsHealthy.Should().BeFalse();
        status.IsRunning.Should().BeFalse();
        status.StatusMessage.Should().Be("Backend is not installed for lifecycle management yet.");
        remoteClient.HealthEndpointCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_WhenRemoteHealthProbeTimesOut_ReportsUnhealthy()
    {
        var remoteClient = new FakeRemoteWorkstationClient
        {
            HealthEndpointExceptionFactory = _ => new TaskCanceledException("Health probe timed out.")
        };
        var manager = new BackendServiceManager(remoteClient, CreateAppDataDirectory());

        var status = await manager.GetStatusAsync();

        status.IsHealthy.Should().BeFalse();
        status.IsRunning.Should().BeFalse();
        status.StatusMessage.Should().Be("Backend is not installed for lifecycle management yet.");
        remoteClient.HealthEndpointCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_WhenCallerCancelsRemoteHealthProbe_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var remoteClient = new FakeRemoteWorkstationClient
        {
            HealthEndpointExceptionFactory = ct =>
            {
                cts.Cancel();
                return new OperationCanceledException(ct);
            }
        };
        var manager = new BackendServiceManager(remoteClient, CreateAppDataDirectory());

        Func<Task> act = () => manager.GetStatusAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        remoteClient.HealthEndpointCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_ForwardsCancellationTokenToRemoteHealthProbe()
    {
        var remoteClient = new FakeRemoteWorkstationClient { HealthEndpointResult = true };
        var manager = new BackendServiceManager(remoteClient, CreateAppDataDirectory());
        using var cts = new CancellationTokenSource();

        await manager.GetStatusAsync(cts.Token);

        remoteClient.LastHealthToken.CanBeCanceled.Should().BeTrue();
    }

    private static string CreateAppDataDirectory()
        => Path.Combine(Path.GetTempPath(), "Meridian.Wpf.Tests", "BackendServiceManager", Guid.NewGuid().ToString("N"));

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://localhost:8080";
        public bool HealthEndpointResult { get; init; }
        public Func<CancellationToken, Exception>? HealthEndpointExceptionFactory { get; init; }
        public int HealthEndpointCallCount { get; private set; }
        public CancellationToken LastHealthToken { get; private set; }

        public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
            => BaseUrl = serviceUrl;

        public Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default)
        {
            LastHealthToken = ct;
            HealthEndpointCallCount++;
            if (HealthEndpointExceptionFactory is { } exceptionFactory)
            {
                return Task.FromException<bool>(exceptionFactory(ct));
            }

            return Task.FromResult(HealthEndpointResult);
        }

        public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new ServiceHealthResult { IsReachable = HealthEndpointResult });

        public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult<StatusResponse?>(null);

        public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiResponse<StatusResponse> { Success = HealthEndpointResult });

        public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> PostWithResponseAsync<T>(string endpoint, object? body = null, CancellationToken ct = default)
            where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public void Dispose()
        {
        }
    }
}
