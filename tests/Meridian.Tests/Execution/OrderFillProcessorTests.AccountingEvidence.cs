using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed partial class OrderFillProcessorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossingZero_PublishesExactClosingBasisAndResidualOpening(bool shortPosition)
    {
        var order = CreateOrder("cross-zero") with
        {
            Side = shortPosition ? OrderSide.Buy : OrderSide.Sell,
            Quantity = 15m,
            FilledQuantity = 15m
        };
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFill(CreateReport("opening") with
        {
            Side = shortPosition ? OrderSide.Sell : OrderSide.Buy,
            FillPrice = 100m
        }, ownerAccountId: order.FundAccountId!.Value.ToString("D"));
        var report = CreateReport(order.OrderId) with
        {
            Side = order.Side,
            OrderQuantity = 15m,
            FilledQuantity = 15m,
            FillPrice = 110m
        };
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(order, portfolio, publisher, null, out _);

        await processor.ProcessAsync(null, report, 0m);

        var trade = Assert.Single(publisher.Events);
        Assert.Equal(1, trade.AccountingEvidenceVersion);
        var evidence = Assert.IsType<TradeFillAccountingEvidence>(trade.AccountingEvidence);
        Assert.Equal(trade.FinancialAccountId, evidence.FinancialAccountId);
        var closing = Assert.IsType<SecurityTradePosting>(evidence.Closing);
        Assert.Equal(shortPosition ? SecurityTradePostingKind.CoverShort : SecurityTradePostingKind.CloseLong,
            closing.Kind);
        Assert.Equal(10m, closing.Quantity);
        Assert.Equal(1_000m, closing.CarryingValue);
        Assert.Equal(1_100m, closing.GrossValue);
        var opening = Assert.IsType<SecurityTradePosting>(evidence.Opening);
        Assert.Equal(shortPosition ? SecurityTradePostingKind.OpenLong : SecurityTradePostingKind.OpenShort,
            opening.Kind);
        Assert.Equal(5m, opening.Quantity);
        Assert.Equal(550m, opening.GrossValue);
        Assert.Equal(shortPosition ? -100m : 100m, trade.RealizedPnl);
        Assert.Equal(evidence.RealizedPnl, trade.RealizedPnl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BreakevenClose_RetainsDirectionEvenWhenRealizedPnlIsZero(bool shortPosition)
    {
        var order = CreateOrder("breakeven") with { Side = shortPosition ? OrderSide.Buy : OrderSide.Sell };
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFill(CreateReport("opening") with
        {
            Side = shortPosition ? OrderSide.Sell : OrderSide.Buy
        }, ownerAccountId: order.FundAccountId!.Value.ToString("D"));
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(order, portfolio, publisher, null, out _);

        await processor.ProcessAsync(null, CreateReport(order.OrderId) with { Side = order.Side }, 0m);

        var trade = Assert.Single(publisher.Events);
        Assert.Equal(0m, trade.RealizedPnl);
        var evidence = Assert.IsType<TradeFillAccountingEvidence>(trade.AccountingEvidence);
        Assert.Null(evidence.Opening);
        Assert.Equal(shortPosition ? SecurityTradePostingKind.CoverShort : SecurityTradePostingKind.CloseLong,
            Assert.IsType<SecurityTradePosting>(evidence.Closing).Kind);
        Assert.Equal(1_500m, evidence.Closing.CarryingValue);
    }

    [Fact]
    public async Task FillEvidence_RetainsSeparateBrokerCommissionFeesAndSlippage()
    {
        var order = CreateOrder("explicit-costs");
        var portfolio = new PaperTradingPortfolio(10_000m);
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(order, portfolio, publisher, null, out _);
        var report = CreateReport(order.OrderId) with { Commission = 1.25m, Fees = 0.25m, SlippageCost = 0.5m };

        await processor.ProcessAsync(null, report, 0m);

        var trade = Assert.Single(publisher.Events);
        Assert.Equal(1.25m, trade.Commission);
        var evidence = Assert.IsType<TradeFillAccountingEvidence>(trade.AccountingEvidence);
        Assert.Equal(0.25m, evidence.Fees);
        Assert.Equal(0.5m, evidence.SlippageCost);
        Assert.Equal(8_498m, trade.NewCash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingLotMutationAuthority_IsExplicitlyVersionOneWithoutEvidence(bool readOnlyPortfolio)
    {
        var order = CreateOrder("missing-authority");
        var publisher = new RecordingPublisher();
        var processor = CreateProcessor(order,
            readOnlyPortfolio ? new ReadOnlyAccountingPortfolio() : null, publisher, null, out _);

        await processor.ProcessAsync(null, CreateReport(order.OrderId), 0m);

        var trade = Assert.Single(publisher.Events);
        Assert.Equal(1, trade.AccountingEvidenceVersion);
        Assert.Null(trade.AccountingEvidence);
        Assert.Equal(0m, trade.RealizedPnl);
    }

    [Fact]
    public async Task SessionClaim_OnlyRetainsVerifiedAccountingVersionWhenFillEvidenceWasCaptured()
    {
        foreach (var hasPaperAuthority in new[] { false, true })
        {
            var order = CreateOrder("session-authority");
            var store = new RecordingSessionStore();
            await using var persistence = new PaperSessionPersistenceService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PaperSessionPersistenceService>.Instance, store);
            var session = await persistence.CreateSessionAsync(new CreatePaperSessionDto("capture", null, 10_000m));
            var publisher = new RecordingPublisher();
            var processor = CreateProcessor(order, hasPaperAuthority ? new PaperTradingPortfolio(10_000m) : null,
                publisher, persistence, out _);

            await processor.ProcessAsync(session.SessionId, CreateReport(order.OrderId), 0m);

            Assert.Equal(1, Assert.Single(publisher.Events).AccountingEvidenceVersion);
            var record = Assert.IsType<PaperSessionFillRecord>(store.StoredFill);
            Assert.Equal(hasPaperAuthority ? 1 : 0, record.AccountingEvidenceVersion);
            Assert.Equal(order.FundAccountId!.Value.ToString("D"), record.FinancialAccountId);
        }
    }

    [Fact]
    public async Task SessionWithoutAccountingPublisher_RestartsWithoutAuthorityToCloseAgainstANewLedger()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-no-publisher-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new JsonlFilePaperSessionStore(root,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonlFilePaperSessionStore>.Instance);
        var order = CreateOrder("unpublished-short") with { Side = OrderSide.Sell };
        var opening = CreateReport(order.OrderId) with { Side = OrderSide.Sell, FillPrice = 100m };
        string sessionId;
        try
        {
            await using (var persistence = new PaperSessionPersistenceService(
                             Microsoft.Extensions.Logging.Abstractions.NullLogger<PaperSessionPersistenceService>.Instance, store))
            {
                sessionId = (await persistence.CreateSessionAsync(new CreatePaperSessionDto("session-only", null, 10_000m))).SessionId;
                var processor = CreateProcessor(order, new PaperTradingPortfolio(10_000m), publisher: null, persistence, out _);

                await processor.ProcessAsync(sessionId, opening, 0m);

                Assert.Equal(0, Assert.Single(await store.LoadFillRecordsAsync(sessionId)).AccountingEvidenceVersion);
            }

            await using var restarted = new PaperSessionPersistenceService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PaperSessionPersistenceService>.Instance, store);
            await restarted.InitialiseAsync();
            var portfolio = Assert.IsType<PaperTradingPortfolio>(restarted.GetActivePortfolio(sessionId));
            var closing = portfolio.ApplyFillWithAccountingEvidence(CreateReport("new-ledger-cover") with
            {
                Side = OrderSide.Buy,
                FillPrice = 90m
            }, ownerAccountId: order.FundAccountId!.Value.ToString("D"));

            Assert.Equal(100m, closing.RealizedPnl);
            Assert.Null(closing.Evidence);
            Assert.Empty(portfolio.Positions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AccountingHandoffRetry_ReusesTheSameCapturedEvidenceWithoutReapplyingFill()
    {
        var order = CreateOrder("evidence-retry");
        var portfolio = new PaperTradingPortfolio(10_000m);
        var publisher = new RecordingPublisher
        {
            OnPublishAsync = _ => Task.FromException(new IOException("Primary handoff unavailable."))
        };
        var processor = CreateProcessor(order, portfolio, publisher, null, out _);
        var report = CreateReport(order.OrderId);

        await Assert.ThrowsAsync<AccountingHandoffException>(() => processor.ProcessAsync(null, report, 0m));
        var acceptedCash = portfolio.Cash;
        publisher.OnPublishAsync = null;
        await processor.ProcessAsync(null, report, report.FilledQuantity);

        Assert.Equal(2, publisher.Events.Count);
        Assert.Same(publisher.Events[0], publisher.Events[1]);
        Assert.NotNull(publisher.Events[1].AccountingEvidence);
        Assert.Equal(acceptedCash, portfolio.Cash);
        Assert.Equal(10m, portfolio.Positions["AAPL"].Quantity);
    }

    private sealed class ReadOnlyAccountingPortfolio : IPortfolioState
    {
        public decimal Cash => 10_000m;
        public decimal PortfolioValue => Cash;
        public decimal UnrealisedPnl => 0m;
        public decimal RealisedPnl => throw new InvalidOperationException("Aggregate P&L is not fill accounting authority.");
        public IReadOnlyDictionary<string, IPosition> Positions { get; } = new Dictionary<string, IPosition>();
    }
}
