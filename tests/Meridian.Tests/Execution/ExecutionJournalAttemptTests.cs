using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Execution;

public sealed class ExecutionJournalAttemptTests
{
    private static readonly Guid Account = Guid.Parse("8c6b0029-9422-4961-ad61-a0ab6cbb242d");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-10T15:00:00Z");
    private const string PaperScope = "local-paper:paper";

    [Fact]
    public async Task ReusedClientIdKeepsRefusalAndTerminalAttemptsSeparateAfterReplay()
    {
        var root = NewRoot();
        await using (var journal = new ExecutionJournal(root)
        { OpeningEvidenceSource = (_, _) => new(true, true, At, "test opening evidence") })
        {
            var gateway = new Mock<IExecutionGateway>();
            gateway.SetupGet(value => value.GatewayId).Returns("paper");
            gateway.Setup(value => value.StreamExecutionReportsAsync(It.IsAny<CancellationToken>())).Returns(EmptyReports());
            gateway.Setup(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OrderRequest request, CancellationToken _) => FillReport(request, request.Side == OrderSide.Buy ? 100m : 120m));
            var coverage = new Mock<IOrderInstrumentCoverageSource>();
            coverage.Setup(source => source.IsUsdEquityAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var prices = new Mock<IOrderMarketReferenceSource>();
            prices.Setup(source => source.GetReference("SPY", It.IsAny<OrderSide>()))
                .Returns(() => new OrderMarketReference(100m, DateTimeOffset.UtcNow, "test quote"));
            await using var oms = new OrderManagementSystem(gateway.Object, NullLogger<OrderManagementSystem>.Instance,
                journal: journal, instrumentCoverage: coverage.Object, marketReferences: prices.Object);
            await oms.StartAsync();

            (await oms.PlaceOrderAsync(Request("reused", "AAPL", "refused") with { Quantity = 0m })).Success.Should().BeFalse();
            (await oms.PlaceOrderAsync(Request("reused", "SPY", "entered"))).Success.Should().BeTrue();
            (await oms.PlaceOrderAsync(Request("reused", "SPY", "entered"))).Success.Should().BeTrue();
            (await oms.PlaceOrderAsync(Request("exit", "SPY", "exit") with { Side = OrderSide.Sell, Quantity = 2m })).Success.Should().BeTrue();
        }

        await using var replay = new ExecutionJournal(root);
        var attempts = await replay.OrdersAsync(Account, scope: PaperScope, orderId: "reused");
        attempts.Should().HaveCount(3);
        attempts.Select(order => order.AttemptId).Should().OnlyHaveUniqueItems();
        attempts.First().Should().Match<JournalOrder>(order => order.Symbol == "SPY" && order.Status == "Filled");
        var refused = attempts.Single(order => order.Status == "Rejected");
        refused.Symbol.Should().Be("AAPL");
        refused.Tag.Should().Be("refused");
        var detail = await replay.OrdersAsync(Account, scope: PaperScope, orderId: "reused", attemptId: refused.AttemptId);
        detail.Should().ContainSingle().Which.AttemptId.Should().Be(refused.AttemptId);
        var summary = await replay.SummaryAsync(Account, scope: PaperScope);
        summary.Fills.Should().Be(3);
        summary.RealizedPnl.Should().Be(40m);
        summary.Attributions.Should().ContainSingle().Which.Tag.Should().Be("entered");
        var alerts = new ExecutionAlertService(replay);
        await alerts.ScanAsync(Account, PaperScope, (_, _) => 0d);
        await alerts.ScanAsync(Account, PaperScope, (_, _) => 0d);
        var notifications = await alerts.NotificationsAsync(Account, PaperScope);
        notifications.Count(item => item.Title == "Order execution observed" && item.OrderId == "reused").Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedReplayAndDuplicateRefusalCannotChangeTheCurrentAttempt(bool reuseAcrossAccounts)
    {
        await using var journal = new ExecutionJournal(NewRoot());
        var reports = Channel.CreateUnbounded<ExecutionReport>();
        var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<ExecutionReport> ReadReports([EnumeratorCancellation] CancellationToken ct)
        {
            yield return await reports.Reader.ReadAsync(ct);
            consumed.SetResult();
        }

        var request = Request("reused", "SPY", "first");
        var oldFill = FillReport(request, 100m) with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(-1) };
        var gateway = new Mock<IExecutionGateway>();
        gateway.SetupGet(value => value.GatewayId).Returns("paper");
        gateway.Setup(value => value.StreamExecutionReportsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => ReadReports(ct));
        gateway.Setup(value => value.SubmitOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderRequest submitted, CancellationToken _) => submitted.Symbol == "SPY" ? oldFill : new ExecutionReport
            {
                OrderId = submitted.ClientOrderId!,
                ClientOrderId = submitted.ClientOrderId,
                Symbol = submitted.Symbol,
                Side = submitted.Side,
                ReportType = ExecutionReportType.New,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = submitted.Quantity
            });
        await using var oms = new OrderManagementSystem(gateway.Object, NullLogger<OrderManagementSystem>.Instance, journal: journal);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(request)).Success.Should().BeTrue();
        var currentAccount = reuseAcrossAccounts ? Guid.NewGuid() : Account;
        var current = request with { Symbol = "AAPL", FundAccountId = currentAccount };
        (await oms.PlaceOrderAsync(current)).Success.Should().BeTrue();
        (await oms.PlaceOrderAsync(current)).Success.Should().BeFalse();

        await reports.Writer.WriteAsync(oldFill);
        await consumed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        oms.GetOpenOrders().Should().ContainSingle().Which.Status.Should().Be(OrderStatus.Accepted);
        var orders = await journal.OrdersAsync(currentAccount, scope: PaperScope, orderId: "reused");
        orders.Should().HaveCount(reuseAcrossAccounts ? 2 : 3);
        orders[0].Symbol.Should().Be("AAPL");
        orders[0].Status.Should().Be("Accepted");
        orders[0].FilledQuantity.Should().Be(0m);
        orders.Single(order => order.Status == "Rejected").Events.Should().ContainSingle(entry => entry.Kind == "DuplicateSubmission");
        var originalOrders = await journal.OrdersAsync(Account, scope: PaperScope, orderId: "reused");
        originalOrders.Single(order => order.Status == "Filled").FilledQuantity.Should().Be(1m);
    }

    [Fact]
    public async Task LateStreamIdentityJoinsExternalActivityWithoutRewritingOrDoubleCountingFill()
    {
        var root = NewRoot();
        var request = Request("client-id", "SPY", "tracked");
        var attempt = Guid.NewGuid();
        var fill = new BrokerageFillSnapshotDto("fill-id", "broker-id", "SPY", OrderSide.Buy, 1m, 100m, At, Commission: 2m);
        var activity = new ExecutionJournalEntry(1, "activity:fill-id", PaperScope, Account, "external:broker-id", "ActivityFill", At, ActivityFill: fill);
        await using (var journal = new ExecutionJournal(root))
        {
            await journal.AppendAsync(new(1, "dispatch", PaperScope, Account, "client-id", "Dispatch", At.AddMinutes(-1),
                Request: request, AttemptId: attempt));
            await journal.AppendAsync(activity);
            (await journal.OrdersAsync(Account, scope: PaperScope)).Should().Contain(order => order.OrderId == "external:broker-id");
            await journal.AppendAsync(new(1, "stream-fill", PaperScope, Account, "client-id", "Fill", At.AddSeconds(5),
                Report: FillReport(request, 100m) with { GatewayOrderId = "broker-id" }, AttemptId: attempt));
            await journal.AppendAsync(activity with { Scope = "live:other-scope" });
        }

        await using var replay = new ExecutionJournal(root);
        var order = (await replay.OrdersAsync(Account, scope: PaperScope)).Should().ContainSingle().Which;
        order.OrderId.Should().Be("client-id");
        order.Tag.Should().Be("tracked");
        order.FilledQuantity.Should().Be(1m);
        order.Events.Should().Contain(activity);
        var summary = await replay.SummaryAsync(Account, scope: PaperScope);
        summary.Orders.Should().Be(1);
        summary.Fills.Should().Be(1);
        summary.Commissions.Should().Be(2m);
        (await replay.OrdersAsync(Account, scope: "live:other-scope")).Should().ContainSingle()
            .Which.OrderId.Should().Be("external:broker-id");
    }

    [Fact]
    public async Task LegacyRetainedIntentsKeepRefusalSeparateFromLaterDispatch()
    {
        await using var journal = new ExecutionJournal(NewRoot());
        var refused = Request("reused", "AAPL", "refused");
        var routed = Request("reused", "SPY", "routed");
        await journal.AppendAsync(new(1, "intent:1", PaperScope, Account, "reused", "Intent", At, Request: refused));
        await journal.AppendAsync(new(1, "refusal:1", PaperScope, Account, "reused", "Rejected", At, Request: refused));
        await journal.AppendAsync(new(1, "intent:2", PaperScope, Account, "reused", "Intent", At, Request: routed));
        await journal.AppendAsync(new(1, "dispatch:2", PaperScope, Account, "reused", "Dispatch", At, Request: routed));
        await journal.AppendAsync(new(1, "fill:2", PaperScope, Account, "reused", "Fill", At, Report: FillReport(routed, 100m)));

        var orders = await journal.OrdersAsync(Account, scope: PaperScope, orderId: "reused");
        orders.Should().HaveCount(2);
        orders[0].Symbol.Should().Be("SPY");
        orders[0].FilledQuantity.Should().Be(1m);
        orders[1].Symbol.Should().Be("AAPL");
        orders[1].FilledQuantity.Should().Be(0m);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PaperBookReplayRequiresEconomicContinuity(bool retainSameBook, bool legacyJournal)
    {
        var root = NewRoot();
        var book = new PaperTradingPortfolio(1000m);
        await using (var journal = CreateJournal(legacyJournal ? null : book.BookId))
            await RetainFill(journal, "before-restart", OrderSide.Buy, 100m, At);

        var currentBook = retainSameBook ? book : new PaperTradingPortfolio(1000m);
        await using (var journal = CreateJournal(currentBook.BookId))
        {
            await RetainFill(journal, "after-restart-buy", OrderSide.Buy, 120m, At.AddMinutes(1));
            await RetainFill(journal, "after-restart-sell", OrderSide.Sell, 120m, At.AddMinutes(2));
        }

        await using var replay = CreateJournal(currentBook.BookId);
        var summary = await replay.SummaryAsync(Account, scope: PaperScope);
        (await replay.OrdersAsync(Account, scope: PaperScope)).Should().HaveCount(3);
        var gaps = (await replay.ReadAsync(Account)).Where(entry => entry.Kind == "EconomicCoverageGap");
        if (retainSameBook)
        {
            gaps.Should().BeEmpty();
            summary.RealizedPnl.Should().Be(20m);
        }
        else
        {
            gaps.Should().ContainSingle();
            summary.RealizedPnl.Should().BeNull();
            summary.NetRealizedPnl.Should().BeNull();
            summary.Attributions.Should().BeEmpty();
        }

        ExecutionJournal CreateJournal(Guid? bookId) => new(root)
        {
            OpeningEvidenceSource = (_, _) => new(true, true, At, "test opening evidence"),
            PaperBookIdentitySource = () => bookId
        };

        static async Task RetainFill(ExecutionJournal journal, string id, OrderSide side, decimal price, DateTimeOffset at)
        {
            var request = Request(id, "SPY", "test") with { Side = side };
            await journal.AppendAsync(new(1, $"dispatch:{id}", PaperScope, Account, id, "Dispatch", at,
                Request: request, ReportingAssetClass: "USD_US_EQUITY"));
            await journal.AppendAsync(new(1, $"fill:{id}", PaperScope, Account, id, "Fill", at,
                Report: FillReport(request, price) with { Timestamp = at }));
        }
    }

    private static OrderRequest Request(string id, string symbol, string tag) => new()
    {
        ClientOrderId = id,
        FundAccountId = Account,
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 1m,
        LimitPrice = 100m,
        Metadata = new Dictionary<string, string> { ["tag"] = tag }
    };

    private static ExecutionReport FillReport(OrderRequest request, decimal price) => new()
    {
        OrderId = request.ClientOrderId!,
        ClientOrderId = request.ClientOrderId,
        Symbol = request.Symbol,
        Side = request.Side,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = request.Quantity,
        FilledQuantity = request.Quantity,
        FillPrice = price,
        Commission = 0m,
        Fees = 0m,
        Timestamp = request.Side == OrderSide.Buy ? At : At.AddMinutes(1)
    };

    private static async IAsyncEnumerable<ExecutionReport> EmptyReports()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "Meridian.Tests", "journal-attempts", Guid.NewGuid().ToString("N"));
}
