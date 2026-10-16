using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Resilience;
using Meridian.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaConnectionReadinessTests
{
    [Fact]
    public async Task Connect_WaitsForAuthenticationBeforeInitialSubscriptionReplayAndReadiness()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await connection.SendAsync("[{\"T\":\"success\",\"msg\":\"connected\"}]", timeout.Token);
        AssertAction(await connection.ReadAsync(timeout.Token), "auth");

        connecting.IsCompleted.Should().BeFalse();
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
        connection.HasMessages.Should().BeFalse("subscriptions must wait for authentication acknowledgment");

        await connection.SendAsync("[{\"T\":\"success\",\"msg\":\"authenticated\"}]", timeout.Token);
        var replay = await connection.ReadAsync(timeout.Token);
        AssertSymbols(replay, "subscribe", "trades", "AAPL");
        await connecting;
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_RejectedAuthenticationFailsWithoutPublishingReadiness()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var snapshots = new List<WebSocketConnectionDiagnostics>();
        client.ConnectionDiagnosticsChanged += snapshot => snapshots.Add(snapshot);

        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        AssertAction(await connection.ReadAsync(timeout.Token), "auth");
        await connection.SendAsync("[{\"T\":\"error\",\"code\":402,\"msg\":\"auth failed\"}]", timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<AuthenticationException>();
        snapshots.Should().NotContain(snapshot => snapshot.IsConnected);
        client.GetConnectionDiagnosticsSnapshot().LastFailureKind.Should()
            .Be(ProviderFailureKind.AuthenticationOrAuthorizationFailure);
        connection.HasMessages.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_SubscriptionSendFailureFailsInitialization()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.FailReplayOnAttempt = 1;
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<WebSocketException>()
            .WithMessage("subscription replay failed");
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_CancellationWhileAuthenticationIsDelayedRemainsCancellation()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();

        var connecting = client.ConnectAsync(cancellation.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        AssertAction(await connection.ReadAsync(timeout.Token), "auth");
        cancellation.Cancel();

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<OperationCanceledException>();
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_SubscriptionsChangedDuringReplayAreReconciledBeforeReadiness()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        var original = client.SubscribeTrades(new("AAPL"));
        client.DuringReplay = () =>
        {
            client.UnsubscribeTrades(original);
            client.SubscribeTrades(new("MSFT"));
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL");
        AssertSymbols(await connection.ReadAsync(timeout.Token), "unsubscribe", "trades", "AAPL");
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
        await connecting;
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Disconnect_CancelsPendingAuthenticationAndPreventsLaterReadiness()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        AssertAction(await connection.ReadAsync(timeout.Token), "auth");
        var disconnecting = client.DisconnectAsync(timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<OperationCanceledException>();
        await disconnecting;
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Unsubscribe_SendsRemovalOnlyAfterLastReferenceAndReplaysRemainingSubscriptions()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        var first = client.SubscribeTrades(new("AAPL"));
        var second = client.SubscribeTrades(new("AAPL"));
        var quote = client.SubscribeMarketDepth(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        var replay = await connection.ReadAsync(timeout.Token);
        AssertSymbols(replay, "subscribe", "trades", "AAPL");
        AssertSymbols(replay, "subscribe", "quotes", "AAPL");
        await connecting;

        client.UnsubscribeTrades(first);
        client.SubscribeTrades(new("MSFT"));
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
        client.UnsubscribeTrades(second);
        var unsubscribe = await connection.ReadAsync(timeout.Token);
        AssertSymbols(unsubscribe, "unsubscribe", "trades", "AAPL");
        using (var document = JsonDocument.Parse(unsubscribe))
            document.RootElement.TryGetProperty("quotes", out _).Should().BeFalse();
        client.UnsubscribeMarketDepth(quote);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "unsubscribe", "quotes", "AAPL");

        await connection.CloseAsync(timeout.Token);
        var reconnected = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(reconnected, timeout.Token);
        var remaining = await reconnected.ReadAsync(timeout.Token);
        AssertSymbols(remaining, "subscribe", "trades", "MSFT");
        using var remainingDocument = JsonDocument.Parse(remaining);
        remainingDocument.RootElement.TryGetProperty("quotes", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Reconnect_ReplayFailureDoesNotPublishConnectedOrSuccessfulRecovery()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.FailReplayOnAttempt = 2;
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await connecting;
        var recoveryFinished = new TaskCompletionSource<WebSocketConnectionDiagnostics>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectedAfterLoss = false;
        client.ConnectionDiagnosticsChanged += snapshot =>
        {
            if (snapshot.IsConnected)
                connectedAfterLoss = true;
            if (snapshot.LifecycleState == ProviderConnectionLifecycleState.Failed)
                recoveryFinished.TrySetResult(snapshot);
        };

        await connection.CloseAsync(timeout.Token);
        var reconnected = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(reconnected, timeout.Token);
        var failed = await recoveryFinished.Task.WaitAsync(timeout.Token);

        connectedAfterLoss.Should().BeFalse();
        failed.IsConnected.Should().BeFalse();
        failed.LastError.Should().Contain("subscription replay failed");
        client.ReplayAttempts.Should().Be(2);
    }

    private static async Task AuthenticateAsync(LoopbackConnection connection, CancellationToken ct)
    {
        AssertAction(await connection.ReadAsync(ct), "auth");
        await connection.SendAsync("[{\"T\":\"success\",\"msg\":\"authenticated\"}]", ct);
    }

    private static void AssertAction(string payload, string action)
    {
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("action").GetString().Should().Be(action);
    }

    private static void AssertSymbols(string payload, string action, string channel, params string[] symbols)
    {
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("action").GetString().Should().Be(action);
        document.RootElement.GetProperty(channel).EnumerateArray().Select(value => value.GetString())
            .Should().BeEquivalentTo(symbols);
    }

    private static TestAlpacaClient CreateClient(Uri uri)
    {
        var publisher = new TestMarketEventPublisher();
        return new(new TradeDataCollector(publisher), new QuoteCollector(publisher), uri);
    }

    private sealed class TestAlpacaClient(TradeDataCollector trades, QuoteCollector quotes, Uri uri)
        : AlpacaMarketDataClient(trades, quotes, new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", SubscribeQuotes: true),
            WebSocketConnectionConfig.Default with
            {
                MaxRetries = 1,
                MaxReconnectAttempts = 1,
                RetryBaseDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero
            })
    {
        public int ReplayAttempts { get; private set; }
        public int? FailReplayOnAttempt { get; set; }
        public Action? DuringReplay { get; set; }
        protected override Uri BuildWebSocketUri() => uri;
        protected override string BuildSubscriptionPayload()
        {
            var payload = base.BuildSubscriptionPayload();
            var duringReplay = DuringReplay;
            DuringReplay = null;
            duringReplay?.Invoke();
            return payload;
        }
        protected override Task ResubscribeAsync(CancellationToken ct)
        {
            ReplayAttempts++;
            return base.ResubscribeAsync(ct);
        }
        protected override Task SendAsync(string message, CancellationToken ct = default)
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.GetProperty("action").GetString() == "subscribe" &&
                ReplayAttempts == FailReplayOnAttempt)
                throw new WebSocketException("subscription replay failed");
            return base.SendAsync(message, ct);
        }
    }

    private sealed class LoopbackServer(WebApplication application) : IAsyncDisposable
    {
        private readonly Channel<LoopbackConnection> _connections = Channel.CreateUnbounded<LoopbackConnection>();
        public Uri Uri => new(application.Urls.Single().Replace("http://", "ws://", StringComparison.Ordinal));
        public ValueTask<LoopbackConnection> AcceptAsync(CancellationToken ct) => _connections.Reader.ReadAsync(ct);

        public static async Task<LoopbackServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var server = new LoopbackServer(app);
            app.UseWebSockets();
            app.Run(async context =>
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var connection = new LoopbackConnection(socket);
                await server._connections.Writer.WriteAsync(connection, context.RequestAborted);
                await connection.ReceiveAsync(context.RequestAborted);
            });
            await app.StartAsync();
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await application.StopAsync(shutdown.Token);
            await application.DisposeAsync();
        }
    }

    private sealed class LoopbackConnection(WebSocket socket)
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
        public bool HasMessages => _messages.Reader.TryPeek(out _);
        public ValueTask<string> ReadAsync(CancellationToken ct) => _messages.Reader.ReadAsync(ct);
        public Task SendAsync(string message, CancellationToken ct)
            => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, ct);
        public Task CloseAsync(CancellationToken ct)
            => socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnect test", ct);

        public async Task ReceiveAsync(CancellationToken ct)
        {
            try
            {
                var buffer = new byte[4096];
                var message = new StringBuilder();
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.CloseReceived)
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", ct);
                        return;
                    }
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        await _messages.Writer.WriteAsync(message.ToString(), ct);
                        message.Clear();
                    }
                }
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                // Expected when an authentication failure or a test closes the transport.
            }
        }
    }
}
