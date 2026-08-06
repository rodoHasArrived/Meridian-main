using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Guards the Alpaca execution-stream interruption scenario in which delivery, downstream
/// processing, or state persistence fails and the normalized provider event must remain durable.
/// </summary>
public sealed class AlpacaTradeUpdatesClientTests
{
    [Fact]
    public async Task ProcessMessageAsync_MalformedEventThenCorrectedReplay_DoesNotPoisonEventId()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5R";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Func<Task> malformed = () => sut.ProcessMessageAsync(
            CreateTradeUpdate(eventId, includeOrderId: false));

        await malformed.Should().ThrowAsync<JsonException>()
            .WithMessage("*order.id*");
        store.SaveAttempts.Should().Be(0);
        sut.Watermark.Should().BeNull();

        await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));
        store.SaveAttempts.Should().Be(1, "the normalized envelope is persisted before channel admission");
        store.PendingEventIds.Should().Equal(eventId);

        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await reports.MoveNextAsync()).Should().BeTrue();
        reports.Current.OrderId.Should().Be("alpaca-order-001");
        store.EventIds.Should().BeEmpty("yielding is delivery, not acknowledgement");
    }

    [Fact]
    public async Task Reports_ConsumerFailure_LeavesDurableEnvelopeForLaterConsumerWithoutBrokerReplay()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5S";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));

        async Task ConsumeAndFailAsync()
        {
            await foreach (var _ in sut.Reports.WithCancellation(timeout.Token))
                throw new InvalidOperationException("downstream execution handling failed");
        }

        Func<Task> consume = ConsumeAndFailAsync;
        await consume.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("downstream execution handling failed");
        store.SaveAttempts.Should().Be(1);
        store.PendingEventIds.Should().Equal(eventId);
        sut.Watermark.Should().BeNull();

        await using var replay = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await replay.MoveNextAsync()).Should().BeTrue(
            "a new consumer explicitly retries the durable pending envelope without broker reinjection");
    }

    [Fact]
    public async Task Reports_ConsumerBreak_LeavesDurableEnvelopeForLaterConsumerWithoutBrokerReplay()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5Z";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));

        await foreach (var _ in sut.Reports.WithCancellation(timeout.Token))
            break;

        store.SaveAttempts.Should().Be(1);
        store.PendingEventIds.Should().Equal(eventId);
        sut.Watermark.Should().BeNull();

        await using var replay = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await replay.MoveNextAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Reports_CancelledBeforeNextMove_LeavesDurableEnvelopeForLaterConsumer()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5T";
        await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = sut.Reports.GetAsyncEnumerator(cancelled.Token);

        (await reports.MoveNextAsync()).Should().BeTrue();
        cancelled.Cancel();
        Func<Task> moveNext = async () => { _ = await reports.MoveNextAsync(); };

        await moveNext.Should().ThrowAsync<OperationCanceledException>();
        store.SaveAttempts.Should().Be(1);
        store.PendingEventIds.Should().Equal(eventId);
        sut.Watermark.Should().BeNull();

        using var replayTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var replay = sut.Reports.GetAsyncEnumerator(replayTimeout.Token);
        (await replay.MoveNextAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Reports_SuccessfulConsumerResume_AtomicallyRemovesPendingAndAdvancesCursor()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5V";
        const string nextEventId = "01JJ3WE73W5PG672TC4XACXH5Y";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));
        await sut.ProcessMessageAsync(CreateTradeUpdate(nextEventId));
        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);

        (await reports.MoveNextAsync()).Should().BeTrue();
        store.SaveAttempts.Should().Be(2);
        sut.Watermark.Should().BeNull();

        (await reports.MoveNextAsync()).Should().BeTrue();

        store.SaveAttempts.Should().Be(3);
        store.EventIds.Should().Equal(eventId);
        store.PendingEventIds.Should().Equal(nextEventId);
        store.Watermark.Should().Be(DateTimeOffset.Parse("2026-08-05T14:30:00.123456789Z"));
        sut.Watermark.Should().Be(store.Watermark);
    }

    [Fact]
    public async Task Reports_AcknowledgementSaveFailure_ReplaysFromSharedStoreAfterRestartWithoutBrokerMessage()
    {
        var durableState = new CursorState();
        var failingStore = new TestCursorStore(durableState) { FailOnSaveAttempt = 2 };
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5W";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await using (var first = CreateSut(failingStore))
        {
            await first.ProcessMessageAsync(CreateTradeUpdate(eventId));
            await using var reports = first.Reports.GetAsyncEnumerator(timeout.Token);
            (await reports.MoveNextAsync()).Should().BeTrue();

            await first.DisposeAsync();
            (await reports.MoveNextAsync()).Should().BeFalse();

            failingStore.SaveAttempts.Should().Be(2);
            failingStore.EventIds.Should().BeEmpty();
            failingStore.PendingEventIds.Should().Equal(eventId);
            failingStore.Watermark.Should().BeNull();
            first.Watermark.Should().BeNull();
        }

        var restartedStore = new TestCursorStore(durableState);
        await using var restarted = CreateSut(restartedStore);
        using var stoppedRun = new CancellationTokenSource();
        stoppedRun.Cancel();
        await restarted.StartAsync(stoppedRun.Token);
        await using var replay = restarted.Reports.GetAsyncEnumerator(timeout.Token);

        (await replay.MoveNextAsync()).Should().BeTrue(
            "StartAsync must replay the normalized durable envelope without ProcessMessageAsync");
        replay.Current.OrderId.Should().Be("alpaca-order-001");
        restartedStore.LoadStateCalls.Should().Be(1);
        restartedStore.SaveAttempts.Should().Be(0);
    }

    [Fact]
    public async Task ProcessMessageAsync_DuplicatePendingContent_QueuesOnlyOneReport()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH5X";
        var payload = CreateTradeUpdate(eventId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ProcessMessageAsync(payload);
        await sut.ProcessMessageAsync(payload);
        store.SaveAttempts.Should().Be(1);
        store.PendingEventIds.Should().Equal(eventId);

        var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await reports.MoveNextAsync()).Should().BeTrue();
        await reports.DisposeAsync();

        await using var replay = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await replay.MoveNextAsync()).Should().BeTrue(
            "the second broker message was deduplicated and the later delivery is the one durable envelope");
    }

    [Fact]
    public async Task ProcessMessageAsync_ReusedPendingEventIdWithConflictingContent_FailsClosed()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH60";
        await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));

        Func<Task> conflict = () => sut.ProcessMessageAsync(CreateTradeUpdate(eventId, price: "214.00"));

        await conflict.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflicting normalized execution content*");
        store.SaveAttempts.Should().Be(1);
        store.PendingEventIds.Should().Equal(eventId);
    }

    [Fact]
    public async Task ProcessMessageAsync_CommittedEventId_IsIdempotentOnlyForMatchingContent()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH65";
        const string nextEventId = "01JJ3WE73W5PG672TC4XACXH66";
        var payload = CreateTradeUpdate(eventId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ProcessMessageAsync(payload);
        await sut.ProcessMessageAsync(CreateTradeUpdate(nextEventId));
        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await reports.MoveNextAsync()).Should().BeTrue();
        (await reports.MoveNextAsync()).Should().BeTrue("resuming commits the first event");
        store.SaveAttempts.Should().Be(3);

        await sut.ProcessMessageAsync(payload);
        store.SaveAttempts.Should().Be(3, "matching committed content is idempotent");

        Func<Task> conflict = () => sut.ProcessMessageAsync(CreateTradeUpdate(eventId, price: "214.00"));
        await conflict.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflicting normalized execution content*");
        store.SaveAttempts.Should().Be(3);
    }

    [Fact]
    public async Task ProcessMessageAsync_InboxWriteFailure_DoesNotAdmitUnpersistedReport()
    {
        var store = new TestCursorStore { FailOnSaveAttempt = 1 };
        var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH61";

        Func<Task> process = () => sut.ProcessMessageAsync(CreateTradeUpdate(eventId));

        await process.Should().ThrowAsync<IOException>()
            .WithMessage("simulated durable-state persistence failure");
        store.SaveAttempts.Should().Be(1);
        store.PendingEventIds.Should().BeEmpty();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        var waiting = reports.MoveNextAsync().AsTask();
        await sut.DisposeAsync();
        (await waiting.WaitAsync(timeout.Token)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessMessageAsync_InboxWriteFailureAfterCommit_RemainsDurablyReplayable()
    {
        var store = new TestCursorStore { FailAfterSaveAttempt = 1 };
        await using var sut = CreateSut(store);
        const string eventId = "01JJ3WE73W5PG672TC4XACXH64";

        Func<Task> process = () => sut.ProcessMessageAsync(CreateTradeUpdate(eventId));

        await process.Should().ThrowAsync<IOException>()
            .WithMessage("simulated post-commit persistence failure");
        store.PendingEventIds.Should().Equal(eventId);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var replay = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await replay.MoveNextAsync()).Should().BeTrue(
            "reloading after an ambiguous write must discover the atomically committed envelope");
        replay.Current.OrderId.Should().Be("alpaca-order-001");
    }

    [Fact]
    public async Task ProcessMessageAsync_TerminalUpdateWithoutBrokerEventId_UsesScopedStableIdentity()
    {
        var store = new TestCursorStore();
        await using var sut = CreateSut(store);
        sut.ConfigureDurableStateScope("paper-account-42", AlpacaCredentialEnvironment.PaperEnvironment);

        await sut.ProcessMessageAsync(CreateTradeUpdate(
            "unused",
            status: "canceled",
            includeBrokerEventIdentity: false));

        store.PendingEventIds.Should().ContainSingle()
            .Which.Should().MatchRegex("^alpaca:paper:[0-9a-f]{64}:stream:[0-9A-F]{64}$");
        store.PendingReports.Should().ContainSingle()
            .Which.ReportType.Should().Be(ExecutionReportType.Cancelled);
    }

    [Fact]
    public async Task ReconcileAfterConnectAsync_RepeatedSnapshot_DurablyAdmitsOneStableEnvelope()
    {
        var store = new TestCursorStore();
        var report = CreateReconciledReport();
        await using var sut = new AlpacaTradeUpdatesClient(
            new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true),
            NullLogger<AlpacaTradeUpdatesClient>.Instance,
            _ => Task.FromResult<IReadOnlyList<ExecutionReport>>([report]),
            cursorStore: store);
        sut.ConfigureDurableStateScope("paper-account-42", AlpacaCredentialEnvironment.PaperEnvironment);

        await sut.ReconcileAfterConnectAsync();
        await sut.ReconcileAfterConnectAsync();

        store.SaveAttempts.Should().Be(1, "the same REST snapshot has one stable durable event identity");
        store.PendingEventIds.Should().ContainSingle()
            .Which.Should().Contain(":rest-reconciliation:");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        (await reports.MoveNextAsync()).Should().BeTrue();
        reports.Current.Should().BeEquivalentTo(report);
    }

    [Fact]
    public async Task ReconcileAfterConnectAsync_InboxWriteFailure_DoesNotAdmitTransientReport()
    {
        var store = new TestCursorStore { FailOnSaveAttempt = 1 };
        var sut = new AlpacaTradeUpdatesClient(
            new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true),
            NullLogger<AlpacaTradeUpdatesClient>.Instance,
            _ => Task.FromResult<IReadOnlyList<ExecutionReport>>([CreateReconciledReport()]),
            cursorStore: store);
        sut.ConfigureDurableStateScope("paper-account-42", AlpacaCredentialEnvironment.PaperEnvironment);

        Func<Task> reconcile = () => sut.ReconcileAfterConnectAsync();

        await reconcile.Should().ThrowAsync<IOException>()
            .WithMessage("simulated durable-state persistence failure");
        store.PendingEventIds.Should().BeEmpty();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        var waiting = reports.MoveNextAsync().AsTask();
        await sut.DisposeAsync();
        (await waiting.WaitAsync(timeout.Token)).Should().BeFalse();
    }

    [Fact]
    public async Task FileCursorStore_TwoClientsWritingSamePath_PreserveBothPendingEnvelopes()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "alpaca-trade-updates.cursor");

        try
        {
            var firstStore = new FileAlpacaTradeUpdateCursorStore(path);
            var secondStore = new FileAlpacaTradeUpdateCursorStore(path);
            await using var first = CreateSut(firstStore);
            await using var second = CreateSut(secondStore);

            await Task.WhenAll(
                first.ProcessMessageAsync(CreateTradeUpdate("multi-client-event-1", price: "213.45")),
                second.ProcessMessageAsync(CreateTradeUpdate("multi-client-event-2", price: "214.45")));

            var state = firstStore.LoadState();
            state.PendingEnvelopes.Select(envelope => envelope.EventId)
                .Should().BeEquivalentTo(["multi-client-event-1", "multi-client-event-2"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileCursorStore_DefaultPath_IsSeparatedByAccountAndPaperLiveEnvironment()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var paper = new FileAlpacaTradeUpdateCursorStore(
                "provider-account-1",
                AlpacaCredentialEnvironment.PaperEnvironment,
                directory);
            var live = new FileAlpacaTradeUpdateCursorStore(
                "provider-account-1",
                AlpacaCredentialEnvironment.LiveEnvironment,
                directory);
            var anotherPaperAccount = new FileAlpacaTradeUpdateCursorStore(
                "provider-account-2",
                AlpacaCredentialEnvironment.PaperEnvironment,
                directory);

            paper.StatePath.Should().NotBe(live.StatePath);
            paper.StatePath.Should().NotBe(anotherPaperAccount.StatePath);
            paper.StatePath.Should().Contain($"{Path.DirectorySeparatorChar}paper{Path.DirectorySeparatorChar}");
            live.StatePath.Should().Contain($"{Path.DirectorySeparatorChar}live{Path.DirectorySeparatorChar}");
            paper.StatePath.Should().NotContain("provider-account-1");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GatewayReconciliation_StatusAll_MapsOpenAndTerminalOrdersWithFillPrices()
    {
        var handler = new RecordingHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("/account/activities/FILL", StringComparison.Ordinal)
                    ? CreateFillActivitiesResponse()
                    : CreateReconciliationOrdersResponse(),
                Encoding.UTF8,
                "application/json")
        });
        await using var gateway = new AlpacaBrokerageGateway(
            new StubHttpClientFactory(handler),
            new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true),
            NullLogger<AlpacaBrokerageGateway>.Instance);

        var reports = await gateway.ReconcileExecutionSnapshotsAsync(null, CancellationToken.None);

        var orderRequest = handler.RequestUris.Single(uri => uri.AbsolutePath.EndsWith("/v2/orders", StringComparison.Ordinal));
        orderRequest.Query.Should().Contain("status=all");
        orderRequest.Query.Should().Contain("limit=500");
        orderRequest.Query.Should().Contain("direction=desc");
        handler.RequestUris.Should().Contain(uri =>
            uri.AbsolutePath.EndsWith("/account/activities/FILL", StringComparison.Ordinal) &&
            uri.Query.Contains("direction=asc", StringComparison.Ordinal));
        reports.Should().HaveCount(6);
        reports.Single(report => report.Report.OrderId == "partial").Report.Should().Match<ExecutionReport>(report =>
            report.ReportType == ExecutionReportType.PartialFill &&
            report.OrderStatus == OrderStatus.PartiallyFilled &&
            report.FillPrice == 101.25m);
        reports.Single(report => report.Report.OrderId == "filled").Report.ReportType.Should().Be(ExecutionReportType.Fill);
        reports.Where(report => report.Report.OrderId == "canceled").Select(report => report.Report.ReportType)
            .Should().Equal(ExecutionReportType.PartialFill, ExecutionReportType.Cancelled);
        reports.Single(report => report.Report.OrderId == "canceled" &&
                                 report.Report.ReportType == ExecutionReportType.PartialFill)
            .Report.Should().Match<ExecutionReport>(report =>
                report.FilledQuantity == 2m && report.FillPrice == 99.50m);
        reports.Single(report => report.Report.OrderId == "canceled" &&
                                 report.Report.ReportType == ExecutionReportType.Cancelled)
            .Report.Should().Match<ExecutionReport>(report =>
            report.ReportType == ExecutionReportType.Cancelled && report.FillPrice == 99.50m);
        reports.Single(report => report.Report.OrderId == "expired").Report.ReportType.Should().Be(ExecutionReportType.Expired);
        reports.Single(report => report.Report.OrderId == "rejected").Report.ReportType.Should().Be(ExecutionReportType.Rejected);
    }

    [Fact]
    public async Task LegacyCursorStore_NewEnvelope_FailsClosedUntilVersionedStateIsImplemented()
    {
        await using var sut = CreateSut(new LegacyCursorStore());

        Func<Task> process = () => sut.ProcessMessageAsync(
            CreateTradeUpdate("01JJ3WE73W5PG672TC4XACXH62"));

        await process.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*implement SaveState*");
    }

    [Fact]
    public void FileCursorStore_LegacyJson_DualReadsAndUpgradesToVersionedShape()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "alpaca-trade-updates.cursor");

        try
        {
            File.WriteAllText(
                path,
                """{"Watermark":"2026-08-04T21:00:00+00:00","EventIds":["legacy-event-1","legacy-event-2"]}""");
            var sut = new FileAlpacaTradeUpdateCursorStore(path);

            var loaded = sut.LoadState();
            loaded.Version.Should().Be(AlpacaTradeUpdateCursorState.CurrentVersion);
            loaded.Watermark.Should().Be(DateTimeOffset.Parse("2026-08-04T21:00:00+00:00"));
            loaded.EventIds.Should().Equal("legacy-event-1", "legacy-event-2");
            loaded.EventHashes.Should().BeEmpty();
            loaded.PendingEnvelopes.Should().BeEmpty();

            sut.Save(
                DateTimeOffset.Parse("2026-08-05T21:00:00+00:00"),
                ["legacy-event-2", "new-event-3"]);
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            saved.RootElement.GetProperty("Version").GetInt32()
                .Should().Be(AlpacaTradeUpdateCursorState.CurrentVersion);
            saved.RootElement.GetProperty("EventIds").EnumerateArray()
                .Select(element => element.GetString())
                .Should().Equal("legacy-event-2", "new-event-3");
            saved.RootElement.GetProperty("PendingEnvelopes").GetArrayLength().Should().Be(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileCursorStore_PendingEnvelope_IsNormalizedBoundedAndContainsNoCredentials()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "alpaca-trade-updates.cursor");

        try
        {
            var store = new FileAlpacaTradeUpdateCursorStore(path);
            await using var sut = CreateSut(store);
            const string eventId = "01JJ3WE73W5PG672TC4XACXH63";

            await sut.ProcessMessageAsync(CreateTradeUpdate(eventId));

            var serialized = File.ReadAllText(path);
            serialized.Should().Contain(eventId);
            serialized.Should().Contain("alpaca-order-001");
            serialized.Should().NotContain("test-key");
            serialized.Should().NotContain("test-secret");
            serialized.Should().NotContain("execution_id");
            store.LoadState().PendingEnvelopes.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FileCursorStore_CorruptJson_StartFailsClosedInsteadOfDiscardingPendingState()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "alpaca-trade-updates.cursor");

        try
        {
            File.WriteAllText(path, "{\"Version\":2,\"PendingEnvelopes\":[");
            var store = new FileAlpacaTradeUpdateCursorStore(path);
            await using var sut = CreateSut(store);

            Action load = () => store.LoadState();
            load.Should().Throw<InvalidDataException>().WithMessage("*corrupt JSON*");

            Func<Task> start = () => sut.StartAsync(new CancellationToken(canceled: true));
            await start.Should().ThrowAsync<InvalidDataException>().WithMessage("*corrupt JSON*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileCursorStore_TooManyPendingEnvelopes_FailsClosedBeforeMaterializingEntries()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, "alpaca-trade-updates.cursor");

        try
        {
            var pending = string.Join(",", Enumerable.Repeat("null", AlpacaTradeUpdateStateCodec.MaxPendingEnvelopes + 1));
            File.WriteAllText(
                path,
                $"{{\"Version\":2,\"Watermark\":null,\"EventIds\":[],\"EventHashes\":{{}},\"PendingEnvelopes\":[{pending}]}}");
            var sut = new FileAlpacaTradeUpdateCursorStore(path);

            Action load = () => sut.LoadState();
            load.Should().Throw<InvalidDataException>()
                .WithMessage($"*exceeds {AlpacaTradeUpdateStateCodec.MaxPendingEnvelopes} pending envelopes*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_WaitingReader_CompletesAndLifecycleIsIdempotent()
    {
        var sut = CreateSut(new TestCursorStore());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reports = sut.Reports.GetAsyncEnumerator(timeout.Token);
        var waiting = reports.MoveNextAsync().AsTask();

        await sut.DisposeAsync();

        (await waiting.WaitAsync(timeout.Token)).Should().BeFalse();
        await sut.DisposeAsync();
        Func<Task> restart = () => sut.StartAsync();
        await restart.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static AlpacaTradeUpdatesClient CreateSut(IAlpacaTradeUpdateCursorStore cursorStore) =>
        new(
            new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret", UseSandbox: true),
            NullLogger<AlpacaTradeUpdatesClient>.Instance,
            cursorStore: cursorStore);

    private static string CreateTradeUpdate(
        string eventId,
        bool includeOrderId = true,
        string price = "213.45",
        string status = "filled",
        bool includeBrokerEventIdentity = true)
    {
        var order = new Dictionary<string, object?>
        {
            ["client_order_id"] = "meridian-client-order-001",
            ["created_at"] = "2026-08-05T14:29:59.900000000Z",
            ["updated_at"] = "2026-08-05T14:30:00.123456789Z",
            ["submitted_at"] = "2026-08-05T14:29:59.950000000Z",
            ["filled_at"] = "2026-08-05T14:30:00.123456789Z",
            ["asset_class"] = "us_equity",
            ["symbol"] = "AAPL",
            ["qty"] = "100",
            ["filled_qty"] = "100",
            ["filled_avg_price"] = price,
            ["side"] = "buy",
            ["order_type"] = "market",
            ["type"] = "market",
            ["time_in_force"] = "day",
            ["status"] = status,
            ["extended_hours"] = false
        };
        if (includeOrderId)
            order["id"] = "alpaca-order-001";

        var data = new Dictionary<string, object?>
        {
            ["at"] = "2026-08-05T14:30:00.124000000Z",
            ["event"] = status,
            ["timestamp"] = "2026-08-05T14:30:00.123456789Z",
            ["order"] = order,
            ["position_qty"] = "100",
            ["price"] = price,
            ["qty"] = "100"
        };
        if (includeBrokerEventIdentity)
        {
            data["event_id"] = eventId;
            data["execution_id"] = "2f63ea93-423d-4169-b3f6-3fdafc10c418";
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["stream"] = "trade_updates",
            ["data"] = data
        });
    }

    private static ExecutionReport CreateReconciledReport() => new()
    {
        OrderId = "reconciled-order-1",
        GatewayOrderId = "reconciled-order-1",
        ClientOrderId = "client-reconciled-order-1",
        Symbol = "MSFT",
        Side = OrderSide.Buy,
        OrderQuantity = 10m,
        FilledQuantity = 4m,
        FillPrice = 401.25m,
        OrderStatus = OrderStatus.PartiallyFilled,
        ReportType = ExecutionReportType.PartialFill,
        Timestamp = DateTimeOffset.Parse("2026-08-05T15:00:00.1234567Z"),
        Diagnostics = new ExecutionDiagnostics
        {
            BrokerStatus = "partially_filled",
            Category = "alpaca-rest-reconciliation"
        }
    };

    private static string CreateReconciliationOrdersResponse() =>
        """
        [
          {
            "id":"partial","client_order_id":"client-partial","symbol":"AAPL","side":"buy","type":"limit",
            "qty":"10","filled_qty":"4","filled_avg_price":"101.25","status":"partially_filled",
            "created_at":"2026-08-05T14:00:00Z","updated_at":"2026-08-05T14:01:00Z"
          },
          {
            "id":"filled","client_order_id":"client-filled","symbol":"MSFT","side":"sell","type":"market",
            "qty":"5","filled_qty":"5","filled_avg_price":"401.75","status":"filled",
            "created_at":"2026-08-05T14:02:00Z","updated_at":"2026-08-05T14:03:00Z","filled_at":"2026-08-05T14:03:00Z"
          },
          {
            "id":"canceled","client_order_id":"client-canceled","symbol":"NVDA","side":"buy","type":"limit",
            "qty":"8","filled_qty":"2","filled_avg_price":"99.50","status":"canceled",
            "created_at":"2026-08-05T14:04:00Z","updated_at":"2026-08-05T14:06:00Z","canceled_at":"2026-08-05T14:06:00Z"
          },
          {
            "id":"expired","client_order_id":"client-expired","symbol":"AMD","side":"buy","type":"limit",
            "qty":"3","filled_qty":"0","filled_avg_price":null,"status":"expired",
            "created_at":"2026-08-05T14:07:00Z","updated_at":"2026-08-05T20:00:00Z","expired_at":"2026-08-05T20:00:00Z"
          },
          {
            "id":"rejected","client_order_id":"client-rejected","symbol":"TSLA","side":"sell","type":"market",
            "qty":"1","filled_qty":"0","filled_avg_price":null,"status":"rejected",
            "created_at":"2026-08-05T14:08:00Z","updated_at":"2026-08-05T14:08:01Z","failed_at":"2026-08-05T14:08:01Z"
          }
        ]
        """;

    private static string CreateFillActivitiesResponse() =>
        """
        [
          {
            "id":"fill-activity-partial","activity_type":"FILL","transaction_time":"2026-08-05T14:01:00Z",
            "symbol":"AAPL","qty":"4","cum_qty":"4","leaves_qty":"6","price":"101.25","side":"buy",
            "type":"partial_fill","order_id":"partial"
          },
          {
            "id":"fill-activity-filled","activity_type":"FILL","transaction_time":"2026-08-05T14:03:00Z",
            "symbol":"MSFT","qty":"5","cum_qty":"5","leaves_qty":"0","price":"401.75","side":"sell",
            "type":"fill","order_id":"filled"
          },
          {
            "id":"fill-activity-before-cancel","activity_type":"FILL","transaction_time":"2026-08-05T14:05:00Z",
            "symbol":"NVDA","qty":"2","cum_qty":"2","leaves_qty":"6","price":"99.50","side":"buy",
            "type":"partial_fill","order_id":"canceled"
          }
        ]
        """;

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-cursor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class CursorState
    {
        public object Gate { get; } = new();
        public AlpacaTradeUpdateCursorState Value { get; set; } = AlpacaTradeUpdateCursorState.Empty;
    }

    private sealed class TestCursorStore : IAlpacaTradeUpdateCursorStore
    {
        private readonly CursorState _state;

        public TestCursorStore(CursorState? state = null) =>
            _state = state ?? new CursorState();

        public int? FailOnSaveAttempt { get; init; }
        public int? FailAfterSaveAttempt { get; init; }
        public int SaveAttempts { get; private set; }
        public int LoadStateCalls { get; private set; }

        public DateTimeOffset? Watermark
        {
            get
            {
                lock (_state.Gate)
                    return _state.Value.Watermark;
            }
        }

        public IReadOnlyList<string> EventIds
        {
            get
            {
                lock (_state.Gate)
                    return _state.Value.EventIds.ToArray();
            }
        }

        public IReadOnlyList<string> PendingEventIds
        {
            get
            {
                lock (_state.Gate)
                    return _state.Value.PendingEnvelopes.Select(envelope => envelope.EventId).ToArray();
            }
        }

        public IReadOnlyList<ExecutionReport> PendingReports
        {
            get
            {
                lock (_state.Gate)
                    return _state.Value.PendingEnvelopes.Select(envelope => envelope.Report).ToArray();
            }
        }

        public DateTimeOffset? Load() => LoadState().Watermark;

        public IReadOnlyList<string> LoadRecentEventIds() => LoadState().EventIds;

        public AlpacaTradeUpdateCursorState LoadState()
        {
            lock (_state.Gate)
            {
                LoadStateCalls++;
                return Clone(_state.Value);
            }
        }

        public void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds)
        {
            lock (_state.Gate)
            {
                var ids = recentEventIds.ToArray();
                var hashes = _state.Value.EventHashes
                    .Where(pair => ids.Contains(pair.Key, StringComparer.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                SaveStateLocked(new AlpacaTradeUpdateCursorState(
                    AlpacaTradeUpdateCursorState.CurrentVersion,
                    watermark,
                    ids,
                    hashes,
                    _state.Value.PendingEnvelopes));
            }
        }

        public void SaveState(AlpacaTradeUpdateCursorState state)
        {
            lock (_state.Gate)
                SaveStateLocked(state);
        }

        private void SaveStateLocked(AlpacaTradeUpdateCursorState state)
        {
            SaveAttempts++;
            if (FailOnSaveAttempt == SaveAttempts)
                throw new IOException("simulated durable-state persistence failure");
            _state.Value = Clone(state);
            if (FailAfterSaveAttempt == SaveAttempts)
                throw new IOException("simulated post-commit persistence failure");
        }

        private static AlpacaTradeUpdateCursorState Clone(AlpacaTradeUpdateCursorState state) =>
            new(
                state.Version,
                state.Watermark,
                state.EventIds.ToArray(),
                state.EventHashes.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal),
                state.PendingEnvelopes.ToArray());
    }

    private sealed class LegacyCursorStore : IAlpacaTradeUpdateCursorStore
    {
        public DateTimeOffset? Load() => null;
        public IReadOnlyList<string> LoadRecentEventIds() => [];
        public void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds) { }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }
    }
}
