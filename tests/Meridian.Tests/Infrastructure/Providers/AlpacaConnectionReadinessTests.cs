using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
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
        connecting.IsCompleted.Should().BeFalse("provider subscription acknowledgment is required for readiness");
        await FluentActions.Awaiting(() => connecting.WaitAsync(TimeSpan.FromMilliseconds(50)))
            .Should().ThrowAsync<TimeoutException>();
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
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

    [Theory]
    [InlineData(400, ProviderFailureKind.LocalConfigurationError)]
    [InlineData(403, ProviderFailureKind.LocalConfigurationError)]
    [InlineData(405, ProviderFailureKind.LocalConfigurationError)]
    [InlineData(410, ProviderFailureKind.LocalConfigurationError)]
    [InlineData(401, ProviderFailureKind.AuthenticationOrAuthorizationFailure)]
    [InlineData(402, ProviderFailureKind.AuthenticationOrAuthorizationFailure)]
    [InlineData(409, ProviderFailureKind.AuthenticationOrAuthorizationFailure)]
    [InlineData(404, ProviderFailureKind.TransientNetworkFailure)]
    [InlineData(406, ProviderFailureKind.ProviderRateLimit)]
    [InlineData(407, ProviderFailureKind.TransientNetworkFailure)]
    [InlineData(500, ProviderFailureKind.ProviderOutage)]
    public async Task Connect_AuthenticationControlErrorPreservesFailureClassification(int code, ProviderFailureKind expected)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        AssertAction(await connection.ReadAsync(timeout.Token), "auth");
        await connection.SendAsync($$"""[{"T":"error","code":{{code}},"msg":"provider error"}]""", timeout.Token);

        var failure = await Record.ExceptionAsync(() => connecting);
        failure.Should().NotBeNull();
        ProviderFailureClassifier.Classify(failure!).Should().Be(expected);
        ProviderFailureClassifier.IsRetryable(expected).Should().Be(expected is not
            (ProviderFailureKind.AuthenticationOrAuthorizationFailure or ProviderFailureKind.LocalConfigurationError));
        client.GetConnectionDiagnosticsSnapshot().LastFailureKind.Should().Be(expected);
    }

    [Fact]
    public async Task Connect_ClosedBeforeAuthenticationAcknowledgmentIsRetryableTransportFailure()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        AssertAction(await connection.ReadAsync(timeout.Token), "auth");
        await connection.CloseAsync(timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<WebSocketException>();
        client.GetConnectionDiagnosticsSnapshot().LastFailureKind.Should().Be(ProviderFailureKind.TransientNetworkFailure);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(405)]
    [InlineData(409)]
    [InlineData(410)]
    public async Task Connect_RejectedSubscriptionAcknowledgmentFailsWithoutReadiness(int code)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL");
        await connection.SendAsync($$"""[{"T":"error","code":{{code}}}]""", timeout.Token);

        var failure = await Record.ExceptionAsync(() => connecting);
        failure.Should().NotBeNull();
        if (code == 409)
            failure.Should().BeOfType<AuthenticationException>();
        else
            failure.Should().BeOfType<ConfigurationException>();
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connect_ErrorTakesPriorityOverSubscriptionAcknowledgmentInSameBatch(bool errorFirst)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        const string acknowledgment = """{"T":"subscription","trades":["AAPL"]}""";
        const string error = """{"T":"error","code":405}""";
        await connection.SendAsync(errorFirst ? $"[{error},{acknowledgment}]" : $"[{acknowledgment},{error}]", timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<ConfigurationException>().WithMessage("*405*");
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"T\":\"subscription\",\"trades\":[]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"quotes\":[\"AAPL\"]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":\"AAPL\"}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"corrections\":[\"MSFT\"]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"cancelErrors\":[\"MSFT\"]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"corrections\":[\"*\"]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"cancelErrors\":[\"*\"]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"corrections\":\"AAPL\"}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"cancelErrors\":[null]}")]
    [InlineData("{\"T\":\"subscription\",\"trades\":[\"AAPL\"],\"corrections\":[\"AAPL\"],\"bars\":[\"AAPL\"]}")]
    public async Task Connect_MismatchedOrMalformedSubscriptionStateFailsInitialization(string acknowledgment)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await connection.SendAsync(acknowledgment, timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different subscription state*");
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Theory]
    [InlineData("AAPL", "AAPL")]
    [InlineData("*", "*")]
    [InlineData("*", "AAPL")]
    public async Task Connect_AutomaticTradeChannelsRespectRequestedSymbolsAndWildcard(string requested, string automaticSymbol)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new(requested));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", requested);
        await AcknowledgeAsync(connection, timeout.Token, trades: [requested],
            corrections: [automaticSymbol], cancelErrors: [automaticSymbol]);
        await connecting;

        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeTrue();
    }

    [Theory]
    [InlineData("corrections")]
    [InlineData("cancelErrors")]
    public async Task Connect_AutomaticTradeChannelsCannotAddTradeCoverageToQuoteSubscription(string channel)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeMarketDepth(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "quotes", "AAPL");
        await connection.SendAsync($$"""{"T":"subscription","quotes":["AAPL"],"{{channel}}":["AAPL"]}""", timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different subscription state*");
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_SubscriptionAcknowledgmentTimeoutFailsInitialization()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri, TimeSpan.FromMilliseconds(500));
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<TimeoutException>()
            .WithMessage("*subscription state*");
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connect_CancellationOrShutdownDuringSubscriptionAcknowledgmentPreventsReadiness(bool disconnect)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var connecting = client.ConnectAsync(cancellation.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        var stopping = disconnect ? client.DisconnectAsync(timeout.Token) : Task.CompletedTask;
        if (!disconnect)
            cancellation.Cancel();

        await FluentActions.Awaiting(() => connecting).Should().ThrowAsync<OperationCanceledException>();
        await stopping;
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Connect_NewsControlsAreHandledBeforeVirtualDataDispatch()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.NewsOnly = true;
        client.SubscribeNews("AAPL");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "news", "AAPL");
        await connection.SendAsync("""[{"T":"n"},{"T":"subscription","news":["AAPL"]}]""", timeout.Token);
        await connecting;

        client.NewsMessages.Should().Be(1);
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeTrue();
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
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "unsubscribe", "trades", "AAPL");
        await AcknowledgeAsync(connection, timeout.Token);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
        await AcknowledgeAsync(connection, timeout.Token, trades: ["MSFT"]);
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
    public async Task Unsubscribe_AutomaticTradeChannelsFollowSharedReferencesAndRemainingReconnectSubscriptions()
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
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"], quotes: ["AAPL"],
            corrections: ["AAPL"], cancelErrors: ["AAPL"]);
        await connecting;

        client.UnsubscribeTrades(first);
        client.SubscribeTrades(new("MSFT"));
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL", "MSFT"], quotes: ["AAPL"],
            corrections: ["AAPL", "MSFT"], cancelErrors: ["AAPL", "MSFT"]);
        client.UnsubscribeTrades(second);
        var unsubscribe = await connection.ReadAsync(timeout.Token);
        AssertSymbols(unsubscribe, "unsubscribe", "trades", "AAPL");
        using (var document = JsonDocument.Parse(unsubscribe))
            document.RootElement.TryGetProperty("quotes", out _).Should().BeFalse();
        await AcknowledgeAsync(connection, timeout.Token, trades: ["MSFT"], quotes: ["AAPL"],
            corrections: ["MSFT"], cancelErrors: ["MSFT"]);
        client.UnsubscribeMarketDepth(quote);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "unsubscribe", "quotes", "AAPL");
        await AcknowledgeAsync(connection, timeout.Token, trades: ["MSFT"], corrections: ["MSFT"], cancelErrors: ["MSFT"]);

        await connection.CloseAsync(timeout.Token);
        var reconnected = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(reconnected, timeout.Token);
        var remaining = await reconnected.ReadAsync(timeout.Token);
        AssertSymbols(remaining, "subscribe", "trades", "MSFT");
        using var remainingDocument = JsonDocument.Parse(remaining);
        remainingDocument.RootElement.TryGetProperty("quotes", out _).Should().BeFalse();
        await AcknowledgeAsync(reconnected, timeout.Token, trades: ["MSFT"], corrections: ["MSFT"], cancelErrors: ["MSFT"]);
        await WaitForReadinessAsync(client, timeout.Token);
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
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
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

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(405)]
    [InlineData(410)]
    public async Task Reconnect_PermanentReplayRejectionStopsWithoutSpendingRemainingAttempts(int code)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri, reconnectAttempts: 3);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;

        var recoveryFailed = new TaskCompletionSource<WebSocketConnectionDiagnostics>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionDiagnosticsChanged += snapshot =>
        {
            if (snapshot.LifecycleState == ProviderConnectionLifecycleState.Failed)
                recoveryFailed.TrySetResult(snapshot);
        };
        await connection.CloseAsync(timeout.Token);
        var reconnected = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(reconnected, timeout.Token);
        AssertSymbols(await reconnected.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL");
        await reconnected.SendAsync($$"""[{"T":"error","code":{{code}}}]""", timeout.Token);
        var failed = await recoveryFailed.Task.WaitAsync(timeout.Token);

        failed.IsConnected.Should().BeFalse();
        failed.LastFailureKind.Should().Be(ProviderFailureKind.LocalConfigurationError);
        failed.ReconnectAttempts.Should().Be(1);
        client.ReplayAttempts.Should().Be(2, "the rejected replay must not spend the other two reconnect attempts");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveSubscription_RejectionOrTimeoutInvalidatesReadinessAndReplaysDesiredState(bool letTimeout)
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri, letTimeout ? TimeSpan.FromMilliseconds(500) : null);
        var original = client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;

        client.UnsubscribeTrades(original);
        AssertSymbols(await connection.ReadAsync(timeout.Token), "unsubscribe", "trades", "AAPL");
        client.SubscribeTrades(new("MSFT"));
        using (var noSecondCommand = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await FluentActions.Awaiting(async () => { await connection.ReadAsync(noSecondCommand.Token); })
                .Should().ThrowAsync<OperationCanceledException>("the next command must await the first acknowledgment");
        if (!letTimeout)
            await connection.SendAsync("""[{"T":"error","code":500}]""", timeout.Token);

        var reconnected = await server.AcceptAsync(timeout.Token);
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
        await AuthenticateAsync(reconnected, timeout.Token);
        AssertSymbols(await reconnected.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
        await AcknowledgeAsync(reconnected, timeout.Token, trades: ["MSFT"]);
        await WaitForReadinessAsync(client, timeout.Token);
        client.ReplayAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Reconnect_TransientAuthenticationErrorRetriesAndRestoresSubscriptions()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri, reconnectAttempts: 2);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;
        await connection.CloseAsync(timeout.Token);

        var rejected = await server.AcceptAsync(timeout.Token);
        AssertAction(await rejected.ReadAsync(timeout.Token), "auth");
        await rejected.SendAsync("""[{"T":"error","code":500}]""", timeout.Token);
        var retry = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(retry, timeout.Token);
        AssertSymbols(await retry.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL");
        await AcknowledgeAsync(retry, timeout.Token, trades: ["AAPL"]);
        await WaitForReadinessAsync(client, timeout.Token);
        client.GetConnectionDiagnosticsSnapshot().ReconnectAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Reconnect_CancelsLiveAcknowledgmentWaitBeforeReplayingReplacementTransport()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;
        client.SubscribeTrades(new("MSFT"));
        AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
        await connection.CloseAsync(timeout.Token);

        var reconnected = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(reconnected, timeout.Token);
        AssertSymbols(await reconnected.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL", "MSFT");
        await AcknowledgeAsync(reconnected, timeout.Token, trades: ["AAPL", "MSFT"]);
        await WaitForReadinessAsync(client, timeout.Token);
        client.ReplayAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Reconnect_FaultedOldControlCannotInvalidateReplacementInitialization()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;

        var sendPaused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AfterSubscriptionSend = async () =>
        {
            sendPaused.TrySetResult();
            await releaseSend.Task.WaitAsync(timeout.Token);
        };
        try
        {
            client.SubscribeTrades(new("MSFT"));
            AssertSymbols(await connection.ReadAsync(timeout.Token), "subscribe", "trades", "MSFT");
            await sendPaused.Task.WaitAsync(timeout.Token);
            await connection.SendAsync("""[{"T":"error","code":500}]""", timeout.Token);
            await client.ErrorObserved.Task.WaitAsync(timeout.Token);
            await connection.CloseAsync(timeout.Token);

            // Hold the original sender until the replacement has authenticated. Its
            // already-faulted waiter then resumes while the new transport is replaying.
            var replacement = await server.AcceptAsync(timeout.Token);
            await AuthenticateAsync(replacement, timeout.Token);
            releaseSend.TrySetResult();
            AssertSymbols(await replacement.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL", "MSFT");
            await AcknowledgeAsync(replacement, timeout.Token, trades: ["AAPL", "MSFT"]);
            await WaitForReadinessAsync(client, timeout.Token);
            client.ReplayAttempts.Should().Be(2);
        }
        finally
        {
            releaseSend.TrySetResult();
        }
    }

    [Fact]
    public async Task Disconnect_CancelsLiveAcknowledgmentWaitAndAllowsFreshConnection()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;
        client.SubscribeTrades(new("MSFT"));
        await connection.ReadAsync(timeout.Token);
        await client.DisconnectAsync(timeout.Token);
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();

        client.SubscribeTrades(new("SPY"));
        var freshConnect = client.ConnectAsync(timeout.Token);
        var fresh = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(fresh, timeout.Token);
        AssertSymbols(await fresh.ReadAsync(timeout.Token), "subscribe", "trades", "SPY");
        await AcknowledgeAsync(fresh, timeout.Token, trades: ["SPY"]);
        await freshConnect;
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_UnsolicitedControlFailureAfterReplayCannotPublishReadiness()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        client.AfterReplay = () => client.ReceiveForTestAsync("""[{"T":"error","code":500}]""");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var snapshots = new List<WebSocketConnectionDiagnostics>();
        client.ConnectionDiagnosticsChanged += snapshot => snapshots.Add(snapshot);
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);

        var failure = await Record.ExceptionAsync(() => connecting);
        failure.Should().NotBeNull();
        ProviderFailureClassifier.Classify(failure!).Should().Be(ProviderFailureKind.ProviderOutage);
        snapshots.Should().NotContain(snapshot => snapshot.IsConnected);
        client.GetConnectionDiagnosticsSnapshot().IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task LiveUnsolicitedControlFailureInvalidatesReadinessAndReconnects()
    {
        await using var server = await LoopbackServer.StartAsync();
        await using var client = CreateClient(server.Uri);
        client.SubscribeTrades(new("AAPL"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connecting = client.ConnectAsync(timeout.Token);
        var connection = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(connection, timeout.Token);
        await connection.ReadAsync(timeout.Token);
        await AcknowledgeAsync(connection, timeout.Token, trades: ["AAPL"]);
        await connecting;
        await connection.SendAsync("""[{"T":"error","code":500}]""", timeout.Token);

        var reconnected = await server.AcceptAsync(timeout.Token);
        await AuthenticateAsync(reconnected, timeout.Token);
        AssertSymbols(await reconnected.ReadAsync(timeout.Token), "subscribe", "trades", "AAPL");
        await AcknowledgeAsync(reconnected, timeout.Token, trades: ["AAPL"]);
        await WaitForReadinessAsync(client, timeout.Token);
        client.ReplayAttempts.Should().Be(2);
    }

    private static async Task WaitForReadinessAsync(TestAlpacaClient client, CancellationToken ct)
    {
        if (client.GetConnectionDiagnosticsSnapshot().IsConnected)
            return;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionDiagnosticsChanged += OnDiagnostics;
        try
        {
            if (!client.GetConnectionDiagnosticsSnapshot().IsConnected)
                await ready.Task.WaitAsync(ct);
        }
        finally
        {
            client.ConnectionDiagnosticsChanged -= OnDiagnostics;
        }
        void OnDiagnostics(WebSocketConnectionDiagnostics snapshot)
        {
            if (snapshot.IsConnected)
                ready.TrySetResult();
        }
    }

    private static Task AcknowledgeAsync(LoopbackConnection connection, CancellationToken ct,
        string[]? trades = null, string[]? quotes = null, string[]? news = null,
        string[]? corrections = null, string[]? cancelErrors = null)
        => connection.SendAsync(JsonSerializer.Serialize(new
        {
            T = "subscription",
            trades = trades ?? [],
            quotes = quotes ?? [],
            news = news ?? [],
            corrections = corrections ?? [],
            cancelErrors = cancelErrors ?? []
        }), ct);

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

    private static TestAlpacaClient CreateClient(Uri uri, TimeSpan? controlTimeout = null, int reconnectAttempts = 1)
    {
        var publisher = new TestMarketEventPublisher();
        return new(new TradeDataCollector(publisher), new QuoteCollector(publisher), uri, reconnectAttempts)
        { ControlAcknowledgmentTimeout = controlTimeout ?? TimeSpan.FromSeconds(10) };
    }

    private sealed class TestAlpacaClient(TradeDataCollector trades, QuoteCollector quotes, Uri uri, int reconnectAttempts)
        : AlpacaMarketDataClient(trades, quotes, new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", SubscribeQuotes: true),
            WebSocketConnectionConfig.Default with
            {
                MaxRetries = 1,
                MaxReconnectAttempts = reconnectAttempts,
                RetryBaseDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero
            })
    {
        public int ReplayAttempts { get; private set; }
        public int? FailReplayOnAttempt { get; set; }
        public Action? DuringReplay { get; set; }
        public Func<Task>? AfterReplay { get; set; }
        public Func<Task>? AfterSubscriptionSend { get; set; }
        public TaskCompletionSource ErrorObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool NewsOnly { get; set; }
        public int NewsMessages { get; private set; }
        public int SubscribeNews(string symbol) => Subscribe(new(symbol), "news");
        public Task ReceiveForTestAsync(string message) => HandleMessageAsync(message);
        protected override Uri BuildWebSocketUri() => uri;
        protected override string BuildSubscriptionPayload()
        {
            var payload = NewsOnly ? BuildNewsSubscriptionMessage(Subscriptions.GetSymbolsByKind("news")) : base.BuildSubscriptionPayload();
            var duringReplay = DuringReplay;
            DuringReplay = null;
            duringReplay?.Invoke();
            return payload;
        }
        protected override void HandleMessage(JsonElement element)
        {
            if (element.TryGetProperty("T", out var messageType) && messageType.ValueEquals("error"))
                ErrorObserved.TrySetResult();
            // Dedicated news adapters intentionally ignore every non-news element here.
            if (NewsOnly)
            {
                if (element.TryGetProperty("T", out var type) && type.GetString() == "n")
                    NewsMessages++;
                return;
            }
            base.HandleMessage(element);
        }
        protected override async Task ResubscribeAsync(CancellationToken ct)
        {
            ReplayAttempts++;
            await base.ResubscribeAsync(ct);
            var afterReplay = AfterReplay;
            AfterReplay = null;
            if (afterReplay is not null)
                await afterReplay();
        }
        protected override async Task SendAsync(string message, CancellationToken ct = default)
        {
            using var document = JsonDocument.Parse(message);
            var subscribe = document.RootElement.GetProperty("action").ValueEquals("subscribe");
            if (subscribe &&
                ReplayAttempts == FailReplayOnAttempt)
                throw new WebSocketException("subscription replay failed");
            await base.SendAsync(message, ct);
            if (subscribe && AfterSubscriptionSend is { } afterSend)
            {
                AfterSubscriptionSend = null;
                await afterSend();
            }
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
