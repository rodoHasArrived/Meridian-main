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
                Quote = CreateQuotesResponse("SPY", bid: 502.10m, ask: 502.15m, sessionLast: 502.12m)
            };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.PollSymbolAsync("SPY");

            remoteClient.LastGetEndpoint.Should().Be("/api/data/quotes/SPY");
            viewModel.Items.Should().ContainSingle();
            viewModel.Items[0].Symbol.Should().Be("SPY");
            viewModel.Items[0].BidText.Should().Be("502.10");
            viewModel.Items[0].AskText.Should().Be("502.15");
            viewModel.Items[0].LastText.Should().Be("502.12");
        });
    }

    [Fact]
    public void PollSymbolAsync_UsesMidPriceFallbackWhenNoSessionTradeIsAvailable()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient
            {
                Quote = CreateQuotesResponse("MSFT", bid: 185.02m, ask: 185.04m, sessionLast: null)
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

            remoteClient.LastGetEndpoint.Should().Be("/api/data/quotes/SPY");
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

    [Fact]
    public void PollSymbolAsync_WhenServerHasNoQuoteYet_DoesNotMutateTickerItems()
    {
        WpfTestThread.Run(async () =>
        {
            var remoteClient = new FakeRemoteWorkstationClient
            {
                Quote = new QuotesResponse("SPY", Quote: null, Timestamp: DateTimeOffset.UtcNow)
            };
            using var viewModel = CreateViewModel(remoteClient);

            await viewModel.PollSymbolAsync("SPY");

            remoteClient.LastGetEndpoint.Should().Be("/api/data/quotes/SPY");
            viewModel.Items.Should().BeEmpty();
        });
    }

    /// <summary>
    /// Builds the payload shape <c>GET /api/data/quotes/{symbol}</c> returns
    /// (<see cref="QuotesResponse"/> wrapping a <see cref="QuoteDataResponse"/>).
    /// </summary>
    private static QuotesResponse CreateQuotesResponse(string symbol, decimal bid, decimal ask, decimal? sessionLast)
    {
        var timestamp = new DateTimeOffset(2026, 1, 5, 15, 30, 0, TimeSpan.Zero);
        var session = sessionLast is { } last
            ? new SessionStatsDto(
                SessionDate: "2026-01-05",
                Open: last,
                High: last,
                Low: last,
                Last: last,
                Volume: 100,
                Vwap: last,
                TradeCount: 1,
                Change: 0m,
                ChangePercent: 0m,
                FirstTradeAt: timestamp,
                LastTradeAt: timestamp)
            : null;

        return new QuotesResponse(
            Symbol: symbol,
            Quote: new QuoteDataResponse(
                Symbol: symbol,
                Timestamp: timestamp,
                BidPrice: bid,
                BidSize: 100,
                AskPrice: ask,
                AskSize: 100,
                MidPrice: (bid + ask) / 2m,
                Spread: ask - bid,
                SequenceNumber: 1,
                StreamId: null,
                Venue: null,
                Session: session),
            Timestamp: timestamp);
    }

    private static TickerStripViewModel CreateViewModel(IRemoteWorkstationClient remoteClient)
        => new(
            WpfServices.MessagingService.Instance,
            Meridian.Ui.Services.SymbolManagementService.Instance,
            WpfServices.ConfigService.Instance,
            remoteClient);

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://remote.test";
        public bool IsSuccess { get; init; } = true;
        public QuotesResponse? Quote { get; init; }
        public string? LastGetEndpoint { get; private set; }
        public bool LastCancellationWasRequested { get; private set; }

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

        public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
        {
            LastGetEndpoint = endpoint;
            LastCancellationWasRequested = ct.IsCancellationRequested;
            ct.ThrowIfCancellationRequested();

            if (!IsSuccess || Quote is null || Quote is not T typedQuote)
            {
                return Task.FromResult(ApiResponse<T>.Fail("Remote unavailable", statusCode: 503));
            }

            return Task.FromResult(ApiResponse<T>.Ok(typedQuote));
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
