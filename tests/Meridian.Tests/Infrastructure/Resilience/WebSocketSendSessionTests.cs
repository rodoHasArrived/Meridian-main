using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meridian.Tests.Infrastructure.Resilience;

public sealed class WebSocketSendSessionTests
{
    [Fact]
    public async Task Connect_WhenInitializerCancelsButCompletes_DetachesTransport()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var application = builder.Build();
        application.UseWebSockets();
        application.Run(async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                await socket.ReceiveAsync(new byte[1], context.RequestAborted);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                // The canceled initialization aborts its transport before readiness.
            }
        });
        await application.StartAsync();
        await using var manager = new WebSocketConnectionManager("test");
        using var cancellation = new CancellationTokenSource();
        var uri = new Uri(application.Urls.Single().Replace("http://", "ws://", StringComparison.Ordinal));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ConnectAsync(
            uri, configureSocket: null, initializeConnection: _ =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            }, ct: cancellation.Token));

        manager.State.Should().Be(WebSocketState.None);
        manager.IsTransportConnected.Should().BeFalse();
        await Assert.ThrowsAsync<WebSocketException>(() => manager.SendAsync("after cancellation"));
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await application.StopAsync(shutdown.Token);
    }

    [Fact]
    public async Task ManagerSendAsync_ConcurrentMessages_UsesOneTransportSendAtATime()
    {
        await using var manager = new WebSocketConnectionManager("test");
        using var lifetime = new CancellationTokenSource();
        using var socket = new ControlledWebSocket();
        AttachSession(manager, socket, lifetime);

        var sends = Enumerable.Range(0, 8).Select(i => manager.SendAsync($"message-{i}")).ToArray();

        socket.Payloads.Should().HaveCount(1);
        socket.ReleaseSends();
        await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(2));

        socket.Payloads.Should().HaveCount(8);
        socket.InFlightCounts.Should().OnlyContain(count => count == 1);
    }

    [Fact]
    public async Task ManagerSendAsync_QueuedCancellation_DoesNotWriteOrBlockLaterMessages()
    {
        await using var manager = new WebSocketConnectionManager("test");
        using var lifetime = new CancellationTokenSource();
        using var socket = new ControlledWebSocket();
        AttachSession(manager, socket, lifetime);
        var first = manager.SendAsync("first");
        using var queuedCts = new CancellationTokenSource();
        var cancelled = manager.SendAsync("cancelled", queuedCts.Token);

        queuedCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        socket.Payloads.Should().Equal("first");

        socket.ReleaseSends();
        await first;
        await manager.SendAsync("last");
        socket.Payloads.Should().Equal("first", "last");
    }

    [Fact]
    public async Task ManagerDisconnect_CancelsActiveAndQueuedWrites()
    {
        await using var manager = new WebSocketConnectionManager("test");
        using var lifetime = new CancellationTokenSource();
        using var socket = new ControlledWebSocket();
        AttachSession(manager, socket, lifetime);
        var first = manager.SendAsync("first");
        var queued = manager.SendAsync("queued");

        await manager.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        socket.Payloads.Should().Equal("first");
        await Assert.ThrowsAsync<WebSocketException>(() => manager.SendAsync("after shutdown"));
    }

    [Fact]
    public async Task ManagerDispose_WhenActiveWriteIgnoresCancellation_DoesNotStrandQueuedWrites()
    {
        var manager = new WebSocketConnectionManager(
            "test", config: null, logger: null, shutdownTimeout: TimeSpan.FromMilliseconds(40));
        using var lifetime = new CancellationTokenSource();
        using var socket = new ControlledWebSocket(respectCancellation: false);
        AttachSession(manager, socket, lifetime);
        var first = manager.SendAsync("first");
        var queued = manager.SendAsync("queued");

        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        first.IsCompleted.Should().BeFalse();
        socket.CloseCalls.Should().Be(0, "shutdown must not overlap the active transport write");
        socket.ReleaseSends();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task CleanupAndReplacement_RejectsOldQueue_WithoutBlockingNewTransport()
    {
        await using var manager = new WebSocketConnectionManager("test");
        using var oldLifetime = new CancellationTokenSource();
        using var oldSocket = new ControlledWebSocket(respectCancellation: false);
        AttachSession(manager, oldSocket, oldLifetime);
        var oldActive = manager.SendAsync("old-active");
        var oldQueued = manager.SendAsync("old-queued");

        // Reconnect uses this same cleanup transaction before installing a new socket.
        var cleanup = typeof(WebSocketConnectionManager).GetMethod(
            "CleanupConnectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)cleanup.Invoke(manager, new object[] { CancellationToken.None })!)
            .WaitAsync(TimeSpan.FromSeconds(2));

        using var newLifetime = new CancellationTokenSource();
        using var newSocket = new ControlledWebSocket();
        newSocket.ReleaseSends();
        AttachSession(manager, newSocket, newLifetime);
        await manager.SendAsync("new").WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldQueued);
        oldSocket.Payloads.Should().Equal("old-active");
        newSocket.Payloads.Should().Equal("new");
        oldActive.IsCompleted.Should().BeFalse();
        oldSocket.ReleaseSends();
        await oldActive;
    }

    [Fact]
    public async Task SendAsync_HeartbeatAndApplicationMessages_ShareTheSameGate()
    {
        using var socket = new ControlledWebSocket();
        var session = new WebSocketSendSession(socket, CancellationToken.None);
        var applicationSend = session.SendAsync(
            Encoding.UTF8.GetBytes("subscription"), WebSocketMessageType.Text, CancellationToken.None).AsTask();
        var heartbeatSend = session.SendAsync(
            new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, CancellationToken.None).AsTask();

        socket.MessageTypes.Should().Equal(WebSocketMessageType.Text);
        socket.ReleaseSends();
        await Task.WhenAll(applicationSend, heartbeatSend);

        socket.MessageTypes.Should().Equal(WebSocketMessageType.Text, WebSocketMessageType.Binary);
        socket.InFlightCounts.Should().OnlyContain(count => count == 1);
    }

    [Fact]
    public async Task CloseAsync_WaitsForTheActiveSend_AndHonorsShutdownCancellation()
    {
        using var socket = new ControlledWebSocket(respectCancellation: false);
        var session = new WebSocketSendSession(socket, CancellationToken.None);
        var activeSend = session.SendAsync(
            new byte[] { 1 }, WebSocketMessageType.Text, CancellationToken.None).AsTask();
        using var shutdownCts = new CancellationTokenSource();
        var cancelledClose = session.CloseAsync(shutdownCts.Token);

        socket.CloseCalls.Should().Be(0);
        shutdownCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledClose);
        socket.CloseCalls.Should().Be(0);

        var close = session.CloseAsync(CancellationToken.None);
        socket.ReleaseSends();
        await Task.WhenAll(activeSend, close);
        socket.CloseCalls.Should().Be(1);
        socket.InFlightAtClose.Should().Be(0);
    }

    private static void AttachSession(
        WebSocketConnectionManager manager, WebSocket socket, CancellationTokenSource lifetime)
    {
        typeof(WebSocketConnectionManager).GetField("_connectionCts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, lifetime);
        // The manager owns its concrete ClientWebSocket; the controlled sender supplies
        // deterministic write blocking while preserving the manager's close/dispose path.
        typeof(WebSocketConnectionManager).GetField("_webSocket", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new ClientWebSocket());
        typeof(WebSocketConnectionManager).GetField("_sendSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new WebSocketSendSession(socket, lifetime.Token));
    }

    private sealed class ControlledWebSocket(bool respectCancellation = true) : WebSocket
    {
        private readonly TaskCompletionSource<bool> _sendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState _state = WebSocketState.Open;
        private int _inFlight;

        internal ConcurrentQueue<string> Payloads { get; } = new();
        internal ConcurrentQueue<WebSocketMessageType> MessageTypes { get; } = new();
        internal ConcurrentQueue<int> InFlightCounts { get; } = new();
        internal int CloseCalls { get; private set; }
        internal int InFlightAtClose { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        internal void ReleaseSends() => _sendRelease.TrySetResult(true);

        public override async ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
        {
            InFlightCounts.Enqueue(Interlocked.Increment(ref _inFlight));
            Payloads.Enqueue(Encoding.UTF8.GetString(buffer.Span));
            MessageTypes.Enqueue(messageType);
            try
            {
                if (respectCancellation)
                    await _sendRelease.Task.WaitAsync(ct);
                else
                    await _sendRelease.Task;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
            => SendAsync(buffer.AsMemory(), messageType, endOfMessage, ct).AsTask();

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
        {
            CloseCalls++;
            InFlightAtClose = Volatile.Read(ref _inFlight);
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken ct)
            => CloseAsync(closeStatus, statusDescription, ct);

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            => throw new NotSupportedException();

        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => _state = WebSocketState.Closed;
    }
}
