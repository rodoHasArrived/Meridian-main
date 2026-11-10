using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Storage.Archival;
using Meridian.Tests.Ui;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in certification of the production Alpaca brokerage gateway against the real paper
/// Trading API, including order submission, amendment, cancellation and a one-share round trip.
/// A transport guard confines every request to the paper host and to order-scoped mutations, and
/// each test cancels or flattens only what it created, so a shared paper account is left alone.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaPaperOrderLifecycleTests(ITestOutputHelper output)
{
    private const string Symbol = "SPY";

    // Far from any plausible price: these orders rest on the book and can never execute.
    private const decimal RestingBuyLimit = 1.00m;

    // Caps for the marketable round trip. Paper executes at its simulated quote, never at the
    // cap, so the caps only guarantee the order is marketable in whichever session is open.
    private const decimal MarketableBuyCap = 5000m;
    private const decimal MarketableSellFloor = 1.00m;

    private static readonly IReadOnlyDictionary<string, string> ExtendedHours =
        new Dictionary<string, string> { ["extended_hours"] = "true" };

    [AlpacaPaperOrderLifecycleFact]
    public async Task RestingLimitOrder_IsAcknowledgedListedRecoveredAndCancelled()
    {
        await using var paper = await PaperOrderSession.ConnectAsync(output);
        var clientOrderId = paper.NextClientOrderId();

        var ack = await paper.SubmitAsync(RestingBuy(clientOrderId));

        ack.ReportType.Should().Be(ExecutionReportType.New);
        ack.OrderStatus.Should().BeOneOf(OrderStatus.Accepted, OrderStatus.PendingNew);
        ack.ClientOrderId.Should().Be(clientOrderId);
        string.IsNullOrWhiteSpace(ack.GatewayOrderId).Should().BeFalse("the broker must assign its own order id");
        (await paper.Gateway.GetOpenOrdersAsync(paper.Token)).Should().Contain(order =>
            order.ClientOrderId == clientOrderId && order.Symbol == Symbol && order.Side == OrderSide.Buy && order.Quantity == 1m);

        var recovered = await paper.Gateway.GetOrderForRecoveryAsync(clientOrderId, paper.Token);
        (recovered is not null).Should().BeTrue("a retained client order id must resolve to the broker order");
        recovered!.OrderId.Should().Be(clientOrderId);
        recovered.GatewayOrderId.Should().Be(ack.GatewayOrderId);
        recovered.OrderLimitPrice.Should().Be(RestingBuyLimit);
        recovered.FilledQuantity.Should().Be(0m);

        var cancelled = await paper.Gateway.CancelOrderAsync(ClientId(clientOrderId), paper.Token);

        cancelled.ReportType.Should().Be(ExecutionReportType.Cancelled);
        cancelled.OrderStatus.Should().Be(OrderStatus.Cancelled);
        cancelled.FilledQuantity.Should().Be(0m);
        (await paper.Gateway.GetOrderForRecoveryAsync(clientOrderId, paper.Token))!.OrderStatus.Should().Be(OrderStatus.Cancelled);
        // Alpaca's open-order listing can trail a cancellation its order lookup already confirms.
        await paper.WaitUntilNotOpenAsync([ack.GatewayOrderId!], "a cancelled order must leave the open book");
    }

    [AlpacaPaperOrderLifecycleFact]
    public async Task ReplacedOrder_CancelledByItsOriginalClientOrderId_LeavesNoWorkingSuccessor()
    {
        await using var paper = await PaperOrderSession.ConnectAsync(output);
        var clientOrderId = paper.NextClientOrderId();
        // Extended-hours orders reach the book outside the regular session, where Alpaca will
        // replace them; a regular-session order submitted while closed stays unreplaceable.
        var ack = await paper.SubmitAsync(RestingBuy(clientOrderId) with { Metadata = ExtendedHours });
        await paper.WaitForAsync(clientOrderId, report => report.OrderStatus is not OrderStatus.PendingNew);

        var amended = await paper.Gateway.ModifyOrderAsync(ClientId(clientOrderId),
            new OrderModification { NewLimitPrice = RestingBuyLimit + 0.02m }, paper.Token);
        paper.Own(amended.GatewayOrderId);
        output.WriteLine("Amendment outcome: {0} (limit {1}).", amended.ReportType, amended.OrderLimitPrice);

        if (amended.ReportType is ExecutionReportType.Modified)
        {
            amended.OrderLimitPrice.Should().Be(RestingBuyLimit + 0.02m);
            amended.ReplacesOrderId.Should().Be(ack.GatewayOrderId);
            amended.GatewayOrderId.Should().NotBe(ack.GatewayOrderId, "Alpaca replaces an order with a new broker order");
            var recovered = await paper.Gateway.GetOrderForRecoveryAsync(clientOrderId, paper.Token);
            recovered!.ReportType.Should().Be(ExecutionReportType.Modified, "recovery follows the broker's replacement chain");
            recovered.OrderLimitPrice.Should().Be(RestingBuyLimit + 0.02m);
        }
        else
        {
            // Alpaca refuses to replace an order it has only queued (for example on a closed
            // day). The original terms must then still be the ones the broker holds.
            amended.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
            amended.OrderLimitPrice.Should().Be(RestingBuyLimit);
        }

        var cancelled = await paper.Gateway.CancelOrderAsync(ClientId(clientOrderId), paper.Token);

        cancelled.OrderStatus.Should().Be(OrderStatus.Cancelled,
            "cancelling the client order id must cancel whichever broker order now holds its terms");
        cancelled.GatewayOrderId.Should().Be(amended.ReportType is ExecutionReportType.Modified
            ? amended.GatewayOrderId
            : ack.GatewayOrderId);
        await paper.WaitUntilNotOpenAsync([ack.GatewayOrderId!, amended.GatewayOrderId!],
            "neither the original nor its replacement may keep working after the cancel");
    }

    [AlpacaPaperOrderLifecycleFact]
    public async Task RefusedSubmissions_AreReportedAsTheBrokerDecidedThem()
    {
        await using var paper = await PaperOrderSession.ConnectAsync(output);

        // A reused client order id is refused with 422. That status cannot be told apart from a
        // refusal that follows an accepted submission, so it is uncertain until recovery reads
        // the retained identity, which here finds the original order untouched.
        var duplicateId = paper.NextClientOrderId();
        var original = await paper.SubmitAsync(RestingBuy(duplicateId));
        var duplicate = await Record.ExceptionAsync(() => paper.Gateway.SubmitOrderAsync(RestingBuy(duplicateId), paper.Token));
        duplicate.Should().BeOfType<HttpRequestException>().Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await paper.Gateway.GetOrderForRecoveryAsync(duplicateId, paper.Token))!.GatewayOrderId.Should().Be(original.GatewayOrderId);
        (await paper.Gateway.CancelOrderAsync(ClientId(duplicateId), paper.Token)).OrderStatus.Should().Be(OrderStatus.Cancelled);

        // An unknown asset is the same 422, and recovery proves no order was created.
        var unknownId = paper.NextClientOrderId();
        var unknown = await Record.ExceptionAsync(() => paper.Gateway.SubmitOrderAsync(
            RestingBuy(unknownId) with { Symbol = "ZZZZNOTREAL" }, paper.Token));
        unknown.Should().BeOfType<HttpRequestException>().Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ((await paper.Gateway.GetOrderForRecoveryAsync(unknownId, paper.Token)) is null).Should().BeTrue();

        // Insufficient buying power is a definite 403 rejection carrying the broker's reason.
        var rejected = await paper.Gateway.SubmitOrderAsync(
            RestingBuy(paper.NextClientOrderId()) with { Quantity = 1_000_000m, LimitPrice = 500m }, paper.Token);
        rejected.ReportType.Should().Be(ExecutionReportType.Rejected);
        rejected.OrderStatus.Should().Be(OrderStatus.Rejected);
        rejected.RejectReason.Should().Contain("insufficient buying power");
    }

    [AlpacaPaperOrderLifecycleFact]
    public async Task ConditionalAndBracketOrders_AreAcceptedWithTheirChildOrdersAndCancelled()
    {
        await using var paper = await PaperOrderSession.ConnectAsync(output);

        var bracketId = paper.NextClientOrderId();
        var bracket = await paper.SubmitAsync(RestingBuy(bracketId) with
        {
            Metadata = new Dictionary<string, string>
            {
                ["order_class"] = "bracket",
                ["take_profit.limit_price"] = "2.00",
                ["stop_loss.stop_price"] = "0.50",
            },
        });
        bracket.ChildOrders.Should().HaveCount(2, "a bracket is acknowledged with its take-profit and stop-loss children");
        bracket.ChildOrders!.Select(child => child.Type).Should().BeEquivalentTo([OrderType.Limit, OrderType.StopMarket]);
        bracket.ChildOrders!.Should().OnlyContain(child => child.Symbol == Symbol && child.Side == OrderSide.Sell && child.Quantity == 1m);
        var open = await paper.Gateway.GetOpenOrdersAsync(paper.Token);
        open.Select(order => order.OrderId).Should().Contain(bracket.ChildOrders!.Select(child => child.OrderId),
            "child orders must be visible to cancellation sweeps while the parent works");
        (await paper.Gateway.CancelOrderAsync(ClientId(bracketId), paper.Token)).OrderStatus.Should().Be(OrderStatus.Cancelled);
        await paper.WaitUntilNotOpenAsync(bracket.ChildOrders!.Select(child => child.OrderId), "cancelling the parent cancels its children");

        // Stops far above the market cannot trigger; each must be accepted and then cancelled.
        OrderRequest[] conditional =
        [
            RestingBuy(paper.NextClientOrderId()) with { Type = OrderType.StopLimit, StopPrice = 9000m, LimitPrice = 9001m },
            RestingBuy(paper.NextClientOrderId()) with { Type = OrderType.StopMarket, StopPrice = 9000m, LimitPrice = null },
            RestingBuy(paper.NextClientOrderId()) with { Type = OrderType.TrailingStop, TrailPercent = 5m, LimitPrice = null },
            RestingBuy(paper.NextClientOrderId()) with { TimeInForce = TimeInForce.GoodTilCancelled },
        ];
        foreach (var request in conditional)
        {
            var ack = await paper.SubmitAsync(request);
            ack.ReportType.Should().Be(ExecutionReportType.New, "{0} {1} must be accepted", request.Type, request.TimeInForce);
            (await paper.Gateway.CancelOrderAsync(ClientId(request.ClientOrderId!), paper.Token))
                .OrderStatus.Should().Be(OrderStatus.Cancelled, "{0} {1} must cancel", request.Type, request.TimeInForce);
        }
    }

    [AlpacaPaperOrderLifecycleFact]
    public async Task OptionAndMultiLegOrders_AreAcceptedAndCancelled_WithoutBreakingReconciliation()
    {
        await using var paper = await PaperOrderSession.ConnectAsync(output);
        var (lowStrike, highStrike) = await paper.FindCallSpreadAsync(Symbol);

        var optionId = paper.NextClientOrderId();
        var option = await paper.SubmitAsync(new OrderRequest
        {
            Symbol = lowStrike,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 0.01m,
            ClientOrderId = optionId,
            PositionIntent = PositionIntent.BuyToOpen,
        });
        option.ReportType.Should().Be(ExecutionReportType.New);
        (await paper.Gateway.GetOpenOrdersAsync(paper.Token)).Should().Contain(order =>
            order.ClientOrderId == optionId && order.Symbol == lowStrike);
        (await paper.Gateway.CancelOrderAsync(ClientId(optionId), paper.Token)).OrderStatus.Should().Be(OrderStatus.Cancelled);

        var spreadId = paper.NextClientOrderId();
        var spread = await paper.SubmitAsync(new OrderRequest
        {
            Symbol = Symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 0.01m,
            ClientOrderId = spreadId,
            Legs =
            [
                new OrderLeg { Symbol = lowStrike, Side = OrderSide.Buy, RatioQuantity = 1m, PositionIntent = PositionIntent.BuyToOpen },
                new OrderLeg { Symbol = highStrike, Side = OrderSide.Sell, RatioQuantity = 1m, PositionIntent = PositionIntent.SellToOpen },
            ],
        });
        spread.ReportType.Should().Be(ExecutionReportType.New);
        spread.ChildOrders.Should().HaveCount(2, "the package is acknowledged with one broker order per leg");
        spread.ChildOrders!.Select(leg => (leg.Symbol, leg.Side)).Should().BeEquivalentTo(
            [(lowStrike, OrderSide.Buy), (highStrike, OrderSide.Sell)]);
        (await paper.Gateway.GetOpenOrdersAsync(paper.Token)).Select(order => order.OrderId)
            .Should().Contain(spread.ChildOrders!.Select(leg => leg.OrderId), "each leg must be visible to cancellation sweeps");

        // The package parent names no instrument (Alpaca returns an empty symbol and side), so
        // it is refused as a single-instrument order instead of being recovered with invented
        // identity, and it must not stop the reconciliation of every other order.
        var recovery = await Record.ExceptionAsync(() => paper.Gateway.GetOrderForRecoveryAsync(spreadId, paper.Token));
        recovery.Should().BeOfType<InvalidDataException>().Which.Message.Should().Contain("multi-leg parent");
        var reconciled = await paper.Gateway.ReconcileExecutionSnapshotsAsync(watermark: null, paper.Token);
        reconciled.Should().OnlyContain(report => !string.IsNullOrWhiteSpace(report.Report.Symbol));
        reconciled.Should().NotContain(report => report.Report.OrderId == spread.GatewayOrderId);

        (await paper.Gateway.CancelOrderAsync(ClientId(spreadId), paper.Token)).OrderStatus.Should().Be(OrderStatus.Cancelled);
        await paper.WaitUntilNotOpenAsync(spread.ChildOrders!.Select(leg => leg.OrderId), "cancelling the package cancels its legs");
    }

    [AlpacaPaperOrderLifecycleFact]
    public async Task MarketableRoundTrip_FillsReconcileExactlyOnce_AndLeaveThePositionAsFound()
    {
        await using var paper = await PaperOrderSession.ConnectAsync(output);
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var buyId = paper.NextClientOrderId();
        var sellId = paper.NextClientOrderId();

        var buyAck = await paper.SubmitAsync(RestingBuy(buyId) with { LimitPrice = MarketableBuyCap, Metadata = ExtendedHours });
        var buy = await paper.WaitForFillAsync(buyId);
        paper.OwnPosition(Symbol, buy.FilledQuantity);

        buy.ReportType.Should().Be(ExecutionReportType.Fill);
        buy.FilledQuantity.Should().Be(1m);
        (buy.FillPrice is > 0m and < MarketableBuyCap).Should().BeTrue("paper executes at its quote, not at the cap");
        (await paper.HeldQuantityAsync(Symbol) >= 1m).Should().BeTrue("the fill must appear in the broker position");

        // The reconnect path: exact FILL activities read over REST are admitted to the durable
        // inbox once, and neither a further reconciliation nor a restart delivers them again.
        var inbox = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-paper-inbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(inbox);
        try
        {
            var delivered = new List<ExecutionReport>();
            async Task<ExecutionReport[]> ExactFillsAsync(string brokerOrderId)
            {
                // The FILL activity can trail the order's filled status by a moment. Until it
                // lands, the order snapshot carries the fill without per-execution detail.
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    delivered.AddRange(await paper.ReconcileIntoInboxAsync(inbox));
                    var exact = delivered.Where(report => report.OrderId == brokerOrderId && report.LastFillQuantity is > 0m).ToArray();
                    if (exact.Length > 0)
                        return exact;
                }

                return [];
            }

            var buyFills = await ExactFillsAsync(buyAck.GatewayOrderId!);
            buyFills.Should().NotBeEmpty("the missed fill must be backfilled from the broker's FILL activities");
            buyFills.Should().OnlyContain(fill => fill.Symbol == Symbol && fill.Side == OrderSide.Buy && fill.FillPrice > 0m
                && fill.ClientOrderId == buyId && fill.ReportType == ExecutionReportType.Fill && fill.FilledQuantity == 1m);

            var sellAck = await paper.SubmitAsync(RestingBuy(sellId) with
            {
                Side = OrderSide.Sell,
                LimitPrice = MarketableSellFloor,
                Metadata = ExtendedHours,
            });
            var sell = await paper.WaitForFillAsync(sellId);
            paper.OwnPosition(Symbol, -sell.FilledQuantity);
            sell.FilledQuantity.Should().Be(1m);

            var sellFills = await ExactFillsAsync(sellAck.GatewayOrderId!);
            sellFills.Should().OnlyContain(fill => fill.Symbol == Symbol && fill.Side == OrderSide.Sell && fill.ClientOrderId == sellId);
            delivered.AddRange(await paper.ReconcileIntoInboxAsync(inbox));
            foreach (var brokerOrderId in new[] { buyAck.GatewayOrderId, sellAck.GatewayOrderId })
            {
                delivered.Where(report => report.OrderId == brokerOrderId).Sum(report => report.LastFillQuantity ?? 0m)
                    .Should().Be(1m, "each execution is delivered exactly once across every reconciliation and restart");
            }
            delivered.Should().OnlyContain(report => !string.IsNullOrWhiteSpace(report.Symbol));
        }
        finally
        {
            Directory.Delete(inbox, recursive: true);
        }

        var activity = await paper.Gateway.GetActivitySnapshotAsync(paper.ExternalAccountId, startedAt, paper.Token);
        activity.Fills.Where(fill => fill.OrderId == buyAck.GatewayOrderId).Sum(fill => fill.Quantity).Should().Be(1m);
        activity.Fills.Should().Contain(fill => fill.OrderId == buyAck.GatewayOrderId && fill.Side == OrderSide.Buy
            && fill.Symbol == Symbol && fill.Price == buy.FillPrice);
        output.WriteLine("Round trip complete: bought and sold one {0} share on paper.", Symbol);
    }

    private static OrderRequest RestingBuy(string clientOrderId) => new()
    {
        Symbol = Symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 1m,
        LimitPrice = RestingBuyLimit,
        ClientOrderId = clientOrderId,
    };

    private static OrderCancellationIdentifier ClientId(string clientOrderId) =>
        new(clientOrderId, OrderCancellationIdentifierKind.ClientOrderId);

    /// <summary>
    /// One connected paper gateway plus the bookkeeping needed to undo exactly what a test did.
    /// </summary>
    private sealed class PaperOrderSession : IAsyncDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(3));
        private readonly PaperOrderHttpClientFactory _clients = new();
        private readonly AlpacaOptions _options;
        private readonly HashSet<string> _ownedBrokerOrderIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _ownedPositions = new(StringComparer.Ordinal);
        private readonly string _runPrefix = $"mrd-it-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30];
        private int _sequence;

        private PaperOrderSession(ITestOutputHelper output, AlpacaOptions options)
        {
            _output = output;
            _options = options;
            Gateway = new AlpacaBrokerageGateway(_clients, options, NullLogger<AlpacaBrokerageGateway>.Instance);
        }

        public AlpacaBrokerageGateway Gateway { get; }
        public CancellationToken Token => _timeout.Token;
        public string ExternalAccountId { get; private set; } = string.Empty;

        public static async Task<PaperOrderSession> ConnectAsync(ITestOutputHelper output)
        {
            var key = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName);
            var secret = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName);
            var environment = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName);
            // Assert presence only: test failures must never print a credential value.
            string.IsNullOrWhiteSpace(key).Should().BeFalse("ALPACA_KEY_ID must be configured for the opted-in paper order run");
            string.IsNullOrWhiteSpace(secret).Should().BeFalse("ALPACA_SECRET_KEY must be configured for the opted-in paper order run");
            string.Equals(environment, "paper", StringComparison.Ordinal).Should().BeTrue(
                "ALPACA_TRADING_ENVIRONMENT must explicitly be paper; live or implicit defaults are refused");

            var session = new PaperOrderSession(output, new AlpacaOptions(KeyId: key!, SecretKey: secret!, UseSandbox: true));
            await session.Gateway.ConnectAsync(session.Token);
            session.ExternalAccountId = (await session.Gateway.GetAccountsAsync(session.Token)).Single().AccountId;
            return session;
        }

        public string NextClientOrderId() => $"{_runPrefix}-{Interlocked.Increment(ref _sequence)}";

        public void Own(string? brokerOrderId)
        {
            if (!string.IsNullOrWhiteSpace(brokerOrderId))
                _ownedBrokerOrderIds.Add(brokerOrderId);
        }

        public void OwnPosition(string symbol, decimal signedQuantity) =>
            _ownedPositions[symbol] = _ownedPositions.GetValueOrDefault(symbol) + signedQuantity;

        public async Task<ExecutionReport> SubmitAsync(OrderRequest request)
        {
            var ack = await Gateway.SubmitOrderAsync(request, Token);
            Own(ack.GatewayOrderId);
            foreach (var child in ack.ChildOrders ?? [])
                Own(child.OrderId);
            return ack;
        }

        public async Task<ExecutionReport> WaitForAsync(string clientOrderId, Func<ExecutionReport, bool> reached)
        {
            ExecutionReport? latest = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                latest = await Gateway.GetOrderForRecoveryAsync(clientOrderId, Token);
                if (latest is not null && reached(latest))
                    return latest;
                await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
            }

            return latest ?? throw new InvalidOperationException("The paper broker never returned the submitted order.");
        }

        public async Task<ExecutionReport> WaitForFillAsync(string clientOrderId)
        {
            var report = await WaitForAsync(clientOrderId, candidate => candidate.OrderStatus is OrderStatus.Filled);
            report.OrderStatus.Should().Be(OrderStatus.Filled,
                "a marketable extended-hours order fills only while an Alpaca session is trading (overnight, pre-market, regular or after-hours); rerun during one");
            return report;
        }

        /// <summary>
        /// Alpaca's open-order listing trails a cancellation its order lookup already confirms,
        /// and child orders are cancelled shortly after their parent rather than atomically.
        /// </summary>
        public async Task WaitUntilNotOpenAsync(IEnumerable<string> brokerOrderIds, string because)
        {
            var expected = brokerOrderIds.ToHashSet(StringComparer.Ordinal);
            string[] stillOpen = [];
            for (var attempt = 0; attempt < 20; attempt++)
            {
                stillOpen = (await Gateway.GetOpenOrdersAsync(Token)).Select(order => order.OrderId).Where(expected.Contains).ToArray();
                if (stillOpen.Length == 0)
                    return;
                await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
            }

            stillOpen.Should().BeEmpty(because);
        }

        public async Task<decimal> HeldQuantityAsync(string symbol) =>
            (await Gateway.GetPositionsAsync(Token)).Where(position => position.Symbol == symbol).Sum(position => position.Quantity);

        /// <summary>
        /// Runs the stream client's post-connect REST reconciliation into a file-backed inbox, the
        /// way a restarted host would, and returns the reports it delivers and acknowledges.
        /// </summary>
        public async Task<IReadOnlyList<ExecutionReport>> ReconcileIntoInboxAsync(string inboxDirectory)
        {
            var writer = new AtomicFileWriterAdapter();
            await using var stream = new AlpacaTradeUpdatesClient(_options, NullLogger<AlpacaTradeUpdatesClient>.Instance, writer,
                cursorStore: new FileAlpacaTradeUpdateCursorStore(Path.Combine(inboxDirectory, "trade-updates.json"), writer));
            // Constructing the gateway binds its REST reconciliation to the stream client. The
            // socket is never started: this exercises the recovery lane, not stream continuity.
            await using var reconciling = new AlpacaBrokerageGateway(_clients, _options, NullLogger<AlpacaBrokerageGateway>.Instance, stream);
            stream.ConfigureDurableStateScope(ExternalAccountId, AlpacaCredentialEnvironment.PaperEnvironment);
            await stream.ReconcileAfterConnectAsync(Token);

            var delivered = new List<ExecutionReport>();
            using var drained = CancellationTokenSource.CreateLinkedTokenSource(Token);
            drained.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await foreach (var report in stream.Reports.WithCancellation(drained.Token))
                    delivered.Add(report);
            }
            catch (OperationCanceledException) when (!Token.IsCancellationRequested)
            {
                // The inbox is drained: nothing further arrives without a socket.
            }

            return delivered;
        }

        /// <summary>Finds two tradable calls on one expiry, the lower strike first.</summary>
        public async Task<(string LowStrike, string HighStrike)> FindCallSpreadAsync(string underlying)
        {
            using var client = _clients.CreateClient("AlpacaPaperOrderLifecycle");
            var earliest = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using var response = await client.GetAsync(
                $"https://paper-api.alpaca.markets/v2/options/contracts?underlying_symbols={underlying}&type=call&status=active&expiration_date_gte={earliest}&limit=50",
                Token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            var calls = document.RootElement.GetProperty("option_contracts").EnumerateArray()
                .Where(contract => contract.GetProperty("tradable").GetBoolean())
                .Select(contract => (
                    Symbol: contract.GetProperty("symbol").GetString()!,
                    Expiry: contract.GetProperty("expiration_date").GetString()!,
                    Strike: decimal.Parse(contract.GetProperty("strike_price").GetString()!, CultureInfo.InvariantCulture)))
                .GroupBy(contract => contract.Expiry)
                .OrderBy(expiry => expiry.Key, StringComparer.Ordinal)
                .Select(expiry => expiry.OrderBy(contract => contract.Strike).Take(2).ToArray())
                .FirstOrDefault(pair => pair.Length == 2);
            (calls is not null).Should().BeTrue("the paper account must list two tradable calls on one expiry for {0}", underlying);
            return (calls![0].Symbol, calls[1].Symbol);
        }

        public async ValueTask DisposeAsync()
        {
            // Undo only what this session created, even when an assertion failed part-way.
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                foreach (var order in await Gateway.GetOpenOrdersAsync(cleanup.Token))
                {
                    if (order.ClientOrderId?.StartsWith(_runPrefix, StringComparison.Ordinal) != true
                        && !_ownedBrokerOrderIds.Contains(order.OrderId))
                        continue;
                    var cancelled = await Gateway.CancelOrderAsync(order.OrderId, cleanup.Token);
                    _output.WriteLine("Cleanup cancelled a leftover order: {0}.", cancelled.OrderStatus);
                }

                foreach (var (symbol, quantity) in _ownedPositions.Where(owned => owned.Value != 0m))
                {
                    var flatten = await Gateway.SubmitOrderAsync(new OrderRequest
                    {
                        Symbol = symbol,
                        Side = quantity > 0m ? OrderSide.Sell : OrderSide.Buy,
                        Type = OrderType.Limit,
                        Quantity = Math.Abs(quantity),
                        LimitPrice = quantity > 0m ? MarketableSellFloor : MarketableBuyCap,
                        ClientOrderId = NextClientOrderId(),
                        Metadata = ExtendedHours,
                    }, cleanup.Token);
                    _output.WriteLine("Cleanup submitted a flattening order for {0} {1}: {2}.", quantity, symbol, flatten.OrderStatus);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _output.WriteLine("Cleanup could not finish ({0}); inspect the paper account for orders prefixed {1}.",
                    ex.GetType().Name, _runPrefix);
            }

            _output.WriteLine("Paper Trading API requests: {0} read, {1} order mutations.", _clients.ReadCount, _clients.MutationCount);
            await Gateway.DisposeAsync();
            _clients.Dispose();
            _timeout.Dispose();
        }
    }

    private sealed class PaperOrderHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly PaperOrderHandler _handler = new();
        public int ReadCount => _handler.ReadCount;
        public int MutationCount => _handler.MutationCount;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        public void Dispose() => _handler.Dispose();
    }

    /// <summary>
    /// Refuses anything but reads and order-scoped mutations on the paper host, before the
    /// request (and its credentials) can leave the process.
    /// </summary>
    private sealed class PaperOrderHandler : DelegatingHandler
    {
        private const string OrderPath = "/v2/orders";
        private int _readCount;
        private int _mutationCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public int MutationCount => Volatile.Read(ref _mutationCount);

        public PaperOrderHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var path = uri?.AbsolutePath ?? string.Empty;
            var paperHost = uri is not null && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443
                && string.Equals(uri.Host, "paper-api.alpaca.markets", StringComparison.OrdinalIgnoreCase);
            var read = request.Method == HttpMethod.Get && path.StartsWith("/v2/", StringComparison.Ordinal);
            // One order at a time: never the cancel-all or close-position routes.
            var singleOrder = path.Length > OrderPath.Length + 1 && path.StartsWith(OrderPath + "/", StringComparison.Ordinal);
            var mutation = request.Method == HttpMethod.Post && path == OrderPath
                || (request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete) && singleOrder;
            if (!paperHost || !(read || mutation))
                throw new InvalidOperationException(
                    "Paper order certification permits only paper Trading API reads and single-order submit, replace and cancel requests.");
            if (read)
                Interlocked.Increment(ref _readCount);
            else
                Interlocked.Increment(ref _mutationCount);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public sealed class AlpacaPaperOrderLifecycleFactAttribute : FactAttribute
{
    public AlpacaPaperOrderLifecycleFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MERIDIAN_RUN_ALPACA_PAPER_ORDERS"), "1", StringComparison.Ordinal))
            Skip = "Real paper-broker order validation is opt-in: set MERIDIAN_RUN_ALPACA_PAPER_ORDERS=1 and the documented paper credentials.";
    }
}
