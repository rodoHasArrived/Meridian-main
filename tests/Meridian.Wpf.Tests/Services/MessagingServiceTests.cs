using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="MessagingService"/> — pub/sub messaging,
/// typed/named messages, weak references, subscription management.
/// </summary>
public sealed class MessagingServiceTests
{
    private static MessagingService Svc => MessagingService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = MessagingService.Instance;
        var b = MessagingService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── Send (string) ────────────────────────────────────────────────

    [Fact]
    public void Send_ShouldRaiseMessageReceivedEvent()
    {
        var svc = Svc;
        string? received = null;
        svc.MessageReceived += (_, msg) => received = msg;

        svc.Send("test-message");

        received.Should().Be("test-message");

        // Cleanup
        svc.MessageReceived -= (_, msg) => received = msg;
    }

    [Fact]
    public void Send_NullOrEmpty_ShouldNotRaiseEvent()
    {
        var svc = Svc;
        bool eventRaised = false;
        void Handler(object? sender, string msg) { eventRaised = true; }
        svc.MessageReceived += Handler;

        svc.Send("");
        eventRaised.Should().BeFalse();

        svc.MessageReceived -= Handler;
    }

    // ── SendNamed ────────────────────────────────────────────────────

    [Fact]
    public void SendNamed_ShouldDeliverToSubscriber()
    {
        var svc = Svc;
        var messageName = "TestMsg-" + Guid.NewGuid();
        object? receivedPayload = null;

        using var sub = svc.Subscribe(messageName, payload => receivedPayload = payload);

        svc.SendNamed(messageName, "hello");

        receivedPayload.Should().Be("hello");
    }

    [Fact]
    public void SendNamed_NullOrEmpty_ShouldNotDeliver()
    {
        var svc = Svc;
        bool delivered = false;

        using var sub = svc.Subscribe("anything", _ => delivered = true);

        svc.SendNamed("");
        delivered.Should().BeFalse();
    }

    [Fact]
    public void SendNamed_ShouldAlsoRaiseMessageReceivedEvent()
    {
        var svc = Svc;
        var messageName = "AlsoEvent-" + Guid.NewGuid();
        string? receivedMsg = null;
        void Handler(object? sender, string msg) { receivedMsg = msg; }
        svc.MessageReceived += Handler;

        svc.SendNamed(messageName);

        receivedMsg.Should().Be(messageName);

        svc.MessageReceived -= Handler;
    }

    // ── Subscribe / Unsubscribe ──────────────────────────────────────

    [Fact]
    public void Subscribe_ShouldReceiveMessages()
    {
        var svc = Svc;
        var msgName = "SubTest-" + Guid.NewGuid();
        int callCount = 0;

        using var sub = svc.Subscribe(msgName, _ => callCount++);

        svc.SendNamed(msgName);
        svc.SendNamed(msgName);

        callCount.Should().Be(2);
    }

    [Fact]
    public void Unsubscribe_ViaDispose_ShouldStopReceivingMessages()
    {
        var svc = Svc;
        var msgName = "UnsubTest-" + Guid.NewGuid();
        int callCount = 0;

        var sub = svc.Subscribe(msgName, _ => callCount++);
        svc.SendNamed(msgName);
        callCount.Should().Be(1);

        sub.Dispose();
        svc.SendNamed(msgName);
        callCount.Should().Be(1); // no additional call
    }

    [Fact]
    public void Subscribe_MultipleHandlers_ShouldAllReceive()
    {
        var svc = Svc;
        var msgName = "Multi-" + Guid.NewGuid();
        int count1 = 0, count2 = 0;

        using var sub1 = svc.Subscribe(msgName, _ => count1++);
        using var sub2 = svc.Subscribe(msgName, _ => count2++);

        svc.SendNamed(msgName);

        count1.Should().Be(1);
        count2.Should().Be(1);
    }

    // ── SubscribeAll ─────────────────────────────────────────────────

    [Fact]
    public void SubscribeAll_ShouldReceiveAllStringMessages()
    {
        var svc = Svc;
        var messages = new List<string>();

        using var sub = svc.SubscribeAll(msg => messages.Add(msg));

        svc.Send("msg-a");
        svc.Send("msg-b");

        messages.Should().Contain("msg-a");
        messages.Should().Contain("msg-b");
    }

    // ── GetSubscriptionCount ─────────────────────────────────────────

    [Fact]
    public void GetSubscriptionCount_ShouldReflectActiveSubscriptions()
    {
        var svc = Svc;
        var msgName = "CountTest-" + Guid.NewGuid();

        var sub1 = svc.Subscribe(msgName, _ => { });
        var sub2 = svc.Subscribe(msgName, _ => { });

        svc.GetSubscriptionCount(msgName).Should().Be(2);

        sub1.Dispose();
        svc.GetSubscriptionCount(msgName).Should().Be(1);

        sub2.Dispose();
    }

    [Fact]
    public void GetSubscriptionCount_UnknownType_ShouldReturnZero()
    {
        var svc = Svc;
        svc.GetSubscriptionCount("non-existent-type-" + Guid.NewGuid()).Should().Be(0);
    }

    // ── ClearSubscriptions ───────────────────────────────────────────

    [Fact]
    public void ClearSubscriptions_ShouldRemoveAllSubscriptions()
    {
        var svc = Svc;
        var msgName = "ClearTest-" + Guid.NewGuid();

        using var sub = svc.Subscribe(msgName, _ => { });
        svc.GetSubscriptionCount(msgName).Should().Be(1);

        svc.ClearSubscriptions();

        svc.GetSubscriptionCount(msgName).Should().Be(0);
    }

    // ── Typed message Send<T> ────────────────────────────────────────

    [Fact]
    public void SendTyped_ShouldDeliverToTypedSubscriber()
    {
        var svc = Svc;
        NavigationMessage? received = null;

        using var sub = svc.Subscribe<NavigationMessage>(msg => received = msg);

        svc.Send(new NavigationMessage { PageTag = "dashboard", Parameter = 42 });

        received.Should().NotBeNull();
        received!.PageTag.Should().Be("dashboard");
        received.Parameter.Should().Be(42);
    }

    // ── Error handling in handlers ───────────────────────────────────

    [Fact]
    public void Send_HandlerThrows_ShouldNotAffectOtherHandlers()
    {
        var svc = Svc;
        var msgName = "ErrorHandler-" + Guid.NewGuid();
        bool secondCalled = false;

        using var sub1 = svc.Subscribe(msgName, _ => throw new InvalidOperationException("oops"));
        using var sub2 = svc.Subscribe(msgName, _ => secondCalled = true);

        svc.SendNamed(msgName);

        secondCalled.Should().BeTrue();
    }

    // ── MessageTypes constants ───────────────────────────────────────

    [Fact]
    public void MessageTypes_ShouldDefineExpectedConstants()
    {
        MessageTypes.SymbolsUpdated.Should().Be("SymbolsUpdated");
        MessageTypes.ConfigurationChanged.Should().Be("ConfigurationChanged");
        MessageTypes.ConnectionStatusChanged.Should().Be("ConnectionStatusChanged");
        MessageTypes.BackfillStarted.Should().Be("BackfillStarted");
        MessageTypes.BackfillCompleted.Should().Be("BackfillCompleted");
        MessageTypes.ThemeChanged.Should().Be("ThemeChanged");
        MessageTypes.RefreshRequested.Should().Be("RefreshRequested");
        MessageTypes.WatchlistUpdated.Should().Be("WatchlistUpdated");
    }

    // ── Message payload models ───────────────────────────────────────

    [Fact]
    public void NavigationMessage_ShouldHaveDefaults()
    {
        var msg = new NavigationMessage();
        msg.PageTag.Should().BeEmpty();
        msg.Parameter.Should().BeNull();
    }

    [Fact]
    public void BackfillProgressMessage_ShouldStoreValues()
    {
        var msg = new BackfillProgressMessage
        {
            Symbol = "SPY",
            CurrentSymbol = 3,
            TotalSymbols = 10,
            BarsDownloaded = 500,
            PercentComplete = 30.5,
            Status = "Downloading"
        };

        msg.Symbol.Should().Be("SPY");
        msg.CurrentSymbol.Should().Be(3);
        msg.TotalSymbols.Should().Be(10);
        msg.BarsDownloaded.Should().Be(500);
        msg.PercentComplete.Should().Be(30.5);
        msg.Status.Should().Be("Downloading");
    }

    [Fact]
    public void ConfigurationChangedMessage_ShouldStoreValues()
    {
        var msg = new ConfigurationChangedMessage
        {
            Section = "DataSource",
            Key = "ActiveProvider",
            OldValue = "Alpaca",
            NewValue = "Polygon"
        };

        msg.Section.Should().Be("DataSource");
        msg.Key.Should().Be("ActiveProvider");
        msg.OldValue.Should().Be("Alpaca");
        msg.NewValue.Should().Be("Polygon");
    }
}
