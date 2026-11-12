using Meridian.Contracts.Api;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class TickerStripViewModelTests
{
    [Fact]
    public void PollSymbolAsync_UsesRemoteWorkstationClientForQuotePolling()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient
            {
                Quote = new TickerStripViewModel.TickerStripQuoteDto
                {
                    Bid = 502.10m,
                    Ask = 502.15m,
                    Last = 502.12m
                }
            };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.PollSymbolAsync("SPY");

            remoteClient.LastGetEndpoint.Should().Be("/api/live/SPY/quote");
            viewModel.Items.Should().ContainSingle();
            viewModel.Items[0].Symbol.Should().Be("SPY");
            viewModel.Items[0].BidText.Should().Be("502.10");
            viewModel.Items[0].AskText.Should().Be("502.15");
            viewModel.Items[0].LastText.Should().Be("502.12");
        });
    }

    [Fact]
    public void PollSymbolAsync_UsesPriceFallbackWhenLastIsMissing()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient
            {
                Quote = new TickerStripViewModel.TickerStripQuoteDto
                {
                    Bid = 185.01m,
                    Ask = 185.04m,
                    Price = 185.03m
                }
            };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.PollSymbolAsync("MSFT");

            viewModel.Items.Should().ContainSingle();
            viewModel.Items[0].LastText.Should().Be("185.03");
        });
    }

    [Fact]
    public void PollSymbolAsync_WhenRemoteReturnsNonSuccess_DoesNotMutateTickerItems()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient { IsSuccess = false };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.PollSymbolAsync("SPY");

            remoteClient.LastGetEndpoint.Should().Be("/api/live/SPY/quote");
            viewModel.Items.Should().BeEmpty();
        });
    }

    [Fact]
    public void PollSymbolAsync_ForwardsCancellationToRemoteWorkstationClient()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient();
            using var viewModel = CreateViewModel(remoteClient);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => viewModel.PollSymbolAsync("SPY", cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            remoteClient.LastCancellationWasRequested.Should().BeTrue();
        });
    }

    private static TickerStripViewModel CreateViewModel(IRemoteWorkstationClient remoteClient)
        => new(
            WpfServices.MessagingService.Instance,
            Meridian.Ui.Services.SymbolManagementService.Instance,
            WpfServices.ConfigService.Instance,
            remoteClient);

    [Fact]
    public void PollAllSymbolsAsync_DoesNotOverlapAndReleasesGuardAfterCompletion()
    {
        WpfTestThread.Run(async () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var remoteClient = new FakeRemoteWorkstationClient
            {
                ResponseGate = gate,
                Quote = new TickerStripViewModel.TickerStripQuoteDto { Last = 100m }
            };
            using var viewModel = CreateViewModel(remoteClient);
            viewModel.UpdateSymbol("SPY", 99m, 101m, 100m, true);

            var firstPoll = viewModel.PollAllSymbolsAsync();
            firstPoll.IsCompleted.Should().BeFalse();
            await viewModel.PollAllSymbolsAsync();
            remoteClient.GetInvocationCount.Should().Be(1, "a timer tick must not overlap the pending read");

            gate.SetResult();
            await firstPoll;
            await viewModel.PollAllSymbolsAsync();
            remoteClient.GetInvocationCount.Should().Be(2, "completed polling must release its guard");
        });
    }

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://remote.test";
        public bool IsSuccess { get; init; } = true;
        public TickerStripViewModel.TickerStripQuoteDto? Quote { get; init; }
        public string? LastGetEndpoint { get; private set; }
        public bool LastCancellationWasRequested { get; private set; }
        public TaskCompletionSource? ResponseGate { get; init; }
        public int GetInvocationCount { get; private set; }

        public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
            => BaseUrl = serviceUrl;

        public Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceHealthResult.Healthy(isConnected: true, latencyMs: 1));

        public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult<StatusResponse?>(null);

        public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<StatusResponse>.Fail("Not configured"));

        public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
            => Task.FromResult<T?>(null);

        public async Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
        {
            LastGetEndpoint = endpoint;
            GetInvocationCount++;
            LastCancellationWasRequested = ct.IsCancellationRequested;
            ct.ThrowIfCancellationRequested();
            if (ResponseGate is not null)
                await ResponseGate.Task.WaitAsync(ct);

            if (!IsSuccess || Quote is null || Quote is not T typedQuote)
            {
                return ApiResponse<T>.Fail("Remote unavailable", statusCode: 503);
            }

            return ApiResponse<T>.Ok(typedQuote);
        }

        public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default)
            where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> PostWithResponseAsync<T>(
            string endpoint,
            object? body = null,
            CancellationToken ct = default) where T : class
            => Task.FromResult(ApiResponse<T>.Fail("Not configured"));

        public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
            => Task.FromResult(ApiResponse<T>.Fail("Not configured"));

        public void Dispose()
        {
        }
    }
}
