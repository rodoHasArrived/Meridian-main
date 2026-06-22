using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class SetupWizardStateServiceTests
{
    [Fact]
    public async Task CheckBackendAsync_UsesRemoteWorkstationClientForHealthProbe()
    {
        var remoteClient = new FakeRemoteWorkstationClient { HealthEndpointResult = true };
        var service = CreateService(remoteClient);

        var result = await service.CheckBackendAsync();

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Be("Healthy");
        result.ServiceUrl.Should().Be("http://remote.example.com:9100");
        remoteClient.HealthEndpointCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckBackendAsync_WhenRemoteHealthFails_ReportsUnhealthy()
    {
        var remoteClient = new FakeRemoteWorkstationClient { HealthEndpointResult = false };
        var service = CreateService(remoteClient);

        var result = await service.CheckBackendAsync();

        result.IsHealthy.Should().BeFalse();
        result.Message.Should().Be("Health check failed");
        remoteClient.HealthEndpointCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckBackendAsync_ForwardsCancellationTokenToRemoteHealthProbe()
    {
        var remoteClient = new FakeRemoteWorkstationClient { HealthEndpointResult = true };
        var service = CreateService(remoteClient);
        using var cts = new CancellationTokenSource();

        await service.CheckBackendAsync(cts.Token);

        remoteClient.LastHealthToken.CanBeCanceled.Should().BeTrue();
    }

    private static SetupWizardStateService CreateService(FakeRemoteWorkstationClient setupRemoteClient)
    {
        var connectionRemoteClient = new FakeRemoteWorkstationClient();
        var connectionService = new ConnectionService(connectionRemoteClient);
        connectionService.ConfigureServiceUrl("http://remote.example.com:9100");

        var backendRemoteClient = new FakeRemoteWorkstationClient();
        var backendManager = new BackendServiceManager(
            backendRemoteClient,
            Path.Combine(Path.GetTempPath(), "Meridian.Wpf.Tests", "SetupWizard", Guid.NewGuid().ToString("N")));

        return new SetupWizardStateService(
            connectionService,
            FirstRunService.Instance,
            backendManager,
            new SetupWizardService(),
            setupRemoteClient);
    }

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://localhost:8080";
        public bool HealthEndpointResult { get; init; }
        public int HealthEndpointCallCount { get; private set; }
        public CancellationToken LastHealthToken { get; private set; }

        public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
            => BaseUrl = serviceUrl;

        public Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default)
        {
            LastHealthToken = ct;
            HealthEndpointCallCount++;
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

        public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public void Dispose()
        {
        }
    }
}
