using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Covers <c>PRD-020</c>: a promoted Strategy Designer document either activates and trades with
/// real orders, or is refused with a reason an operator can act on. Nothing in between — no
/// silent default, no partially-honoured document, no fabricated activation.
/// </summary>
public sealed class DesignerDocumentLiveSourceTests
{
    private const string DocumentId = "strategy-design-momentum";

    [Fact]
    public void TryCreate_ignores_runs_that_name_no_designer_document()
    {
        var source = CreateSource();

        var handled = source.TryCreate(
            new LiveStrategyCreationContext("buy-and-hold", new Dictionary<string, string>()),
            out var strategy,
            out var failureReason);

        // Staying silent (rather than failing) is what lets the catalog's own "no implementation
        // registered" message survive for genuinely unregistered strategies.
        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().BeNull();
    }

    [Fact]
    public void TryCreate_activates_a_valid_designer_document()
    {
        var source = CreateSource(TradableDocument());

        var handled = source.TryCreate(Context(), out var strategy, out var failureReason);

        handled.Should().BeTrue();
        failureReason.Should().BeNull();
        strategy.Should().NotBeNull();
        strategy!.Name.Should().Contain("Momentum design");
    }

    [Fact]
    public void TryCreate_defers_when_the_document_does_not_exist()
    {
        var source = CreateSource();

        var handled = source.TryCreate(Context(), out var strategy, out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().Contain(DocumentId).And.Contain("not found");
    }

    [Fact]
    public void TryCreate_defers_when_no_design_repository_is_registered()
    {
        var source = new DesignerDocumentLiveSource(repository: null, new StrategyDesignService());

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("no strategy design repository is registered");
    }

    [Fact]
    public void TryCreate_refuses_a_document_with_a_code_cell()
    {
        var document = TradableDocument() with
        {
            Cells =
            [
                .. TradableDocument().Cells,
                new StrategyDesignCell(
                    "rebalance-loop",
                    "Weekly rebalance loop",
                    "code",
                    "backtest",
                    "rebalance every Friday with max 20 names",
                    ["PRICE"])
            ]
        };
        var source = CreateSource(document);

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("code cell")
            .And.Contain("plugin")
            .And.Contain("QuantLab");
    }

    [Theory]
    [InlineData("CUSIP")]
    [InlineData("OPTION_DELTA")]
    [InlineData("DURATION")]
    public void TryCreate_refuses_fields_with_no_live_source(string fieldRef)
    {
        var document = TradableDocument() with
        {
            Cells =
            [
                .. TradableDocument().Cells,
                new StrategyDesignCell("ref-cell", "Reference screen", "formula", "filter", "PRICE > 1", [fieldRef])
            ]
        };
        var source = CreateSource(document);

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain(fieldRef).And.Contain("cannot be resolved during live execution");
    }

    [Fact]
    public void TryCreate_refuses_a_document_with_no_trade_cell()
    {
        var document = TradableDocument() with
        {
            Cells = TradableDocument().Cells
                .Where(cell => !string.Equals(cell.Kind, "trade", StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
        var source = CreateSource(document);

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("no trade cell").And.Contain("no executable trade intent");
    }

    [Fact]
    public void TryCreate_refuses_a_non_equity_trade_intent()
    {
        var source = CreateSource(DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Option",
            ["direction"] = "Buy",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "10"
        }));

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("Option").And.Contain("equity and ETF orders only");
    }

    [Fact]
    public void TryCreate_refuses_an_exit_only_trade_direction()
    {
        var source = CreateSource(DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Sell",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "10"
        }));

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("closes a position rather than opening one");
    }

    [Fact]
    public void TryCreate_refuses_an_unparseable_risk_guard()
    {
        var document = TradableDocument() with
        {
            Cells =
            [
                .. TradableDocument().Cells,
                new StrategyDesignCell(
                    "risk-prose",
                    "Risk guard",
                    "governance",
                    "risk",
                    "keep drawdown sensible",
                    [])
            ]
        };
        var source = CreateSource(document);

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        // A stated risk limit that the engine cannot evaluate must block activation. Dropping it
        // would run the strategy without the control the operator promoted it with.
        handled.Should().BeFalse();
        failureReason.Should().Contain("Risk guard cell").And.Contain("not an executable condition");
    }

    [Fact]
    public void TryCreate_refuses_a_document_that_fails_designer_validation()
    {
        var document = TradableDocument() with { DatasetReference = string.Empty };
        var source = CreateSource(document);

        var handled = source.TryCreate(Context(), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("does not pass designer validation").And.Contain("DatasetRequired");
    }

    [Fact]
    public void Activated_document_enters_a_position_once_its_conditions_hold()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 5_000_000L), ctx);

        // FixedShares 10 on a document whose only gate is PRICE > 20.
        ctx.Orders.Should().ContainSingle();
        ctx.Orders[0].Should().Be(("SPY", 10L));
    }

    [Fact]
    public void Activated_document_places_no_order_while_a_gate_fails()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 5m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 5m, volume: 5_000_000L), ctx);

        ctx.Orders.Should().BeEmpty();
    }

    [Fact]
    public void Activated_document_exits_when_its_conditions_stop_holding()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 5m, positions: [("SPY", 10L)]);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 5m, volume: 5_000_000L), ctx);

        ctx.Orders.Should().ContainSingle();
        ctx.Orders[0].Should().Be(("SPY", -10L));
    }

    [Fact]
    public void Activated_document_does_not_trade_a_symbol_whose_window_is_cold()
    {
        // VOLUME_AVG_20D needs 20 observations; a single bar must not be enough to satisfy it.
        var document = DocumentWithGate("VOLUME_AVG_20D > 1000000");
        var strategy = Activate(document);
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 5_000_000L), ctx);

        ctx.Orders.Should().BeEmpty();

        for (var i = 0; i < 20; i++)
        {
            strategy.OnBar(Bar("SPY", close: 50m, volume: 5_000_000L), ctx);
        }

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 10L));
    }

    [Fact]
    public void Activated_document_respects_a_universe_builder_max_size()
    {
        var document = TradableDocument() with
        {
            Universe = ["AAA", "BBB", "CCC"],
            Cells =
            [
                new StrategyDesignCell(
                    "equity-universe",
                    "Structured universe",
                    "universe-builder",
                    "universe",
                    "structured universe definition",
                    ["PRICE"],
                    new Dictionary<string, string>
                    {
                        ["assetClass"] = "Equity",
                        ["includeRules"] = "PRICE > 20",
                        ["maxSize"] = "2"
                    }),
                new StrategyDesignCell("rank", "Rank", "formula", "rank", "PRICE", ["PRICE"]),
                TradeCell(new Dictionary<string, string>
                {
                    ["instrument"] = "Equity",
                    ["direction"] = "Buy",
                    ["sizingMethod"] = "FixedShares",
                    ["sizingValue"] = "1"
                }),
                RiskCell()
            ]
        };

        var strategy = Activate(document);
        var ctx = new RecordingContext(["AAA", "BBB", "CCC"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("AAA", close: 30m, volume: 1L), ctx);
        strategy.OnBar(Bar("BBB", close: 40m, volume: 1L), ctx);
        strategy.OnBar(Bar("CCC", close: 50m, volume: 1L), ctx);

        // Ranked on PRICE descending, capped at two names: CCC and BBB, never AAA.
        ctx.Orders.Select(order => order.Symbol).Should().BeEquivalentTo(["CCC", "BBB"]);
    }

    [Fact]
    public void Activated_document_holds_off_until_minimum_universe_breadth_exists()
    {
        var document = TradableDocument() with
        {
            Universe = ["AAA", "BBB"],
            Cells =
            [
                new StrategyDesignCell(
                    "equity-universe",
                    "Structured universe",
                    "universe-builder",
                    "universe",
                    "structured universe definition",
                    ["PRICE"],
                    new Dictionary<string, string>
                    {
                        ["assetClass"] = "Equity",
                        ["includeRules"] = "PRICE > 20",
                        ["minSize"] = "2"
                    }),
                TradeCell(new Dictionary<string, string>
                {
                    ["instrument"] = "Equity",
                    ["direction"] = "Buy",
                    ["sizingMethod"] = "FixedShares",
                    ["sizingValue"] = "1"
                }),
                RiskCell()
            ]
        };

        var strategy = Activate(document);
        var ctx = new RecordingContext(["AAA", "BBB"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("AAA", close: 30m, volume: 1L), ctx);
        ctx.Orders.Should().BeEmpty("one qualifying name is below the document's minSize of 2");

        strategy.OnBar(Bar("BBB", close: 30m, volume: 1L), ctx);
        ctx.Orders.Select(order => order.Symbol).Should().BeEquivalentTo(["AAA", "BBB"]);
    }

    [Fact]
    public void Activated_document_sizes_a_percent_aum_trade_from_portfolio_value()
    {
        var source = CreateSource(DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Buy",
            ["sizingMethod"] = "PercentAUM",
            ["sizingValue"] = "0.05"
        }));
        source.TryCreate(Context(), out var strategy, out _).Should().BeTrue();

        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);
        strategy!.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L), ctx);

        // 5% of 100,000 is 5,000; at 50 a share that is 100 shares.
        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 100L));
    }

    [Fact]
    public void Activated_short_document_opens_a_negative_position()
    {
        var source = CreateSource(DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "SellShort",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "7"
        }));
        source.TryCreate(Context(), out var strategy, out _).Should().BeTrue();

        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);
        strategy!.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L), ctx);

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", -7L));
    }

    [Fact]
    public void Designer_document_reaches_live_execution_through_the_catalog()
    {
        // The end-to-end PRD-020 seam: the catalog resolves a designer run that CreateDefault
        // alone could not, and hands back an ILiveStrategy the engine can launch.
        var catalog = LiveStrategyCatalog.CreateDefault();
        var designerSource = CreateSource(TradableDocument());
        catalog.RegisterFallback((LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
        {
            if (!designerSource.TryCreate(context, out var inner, out failureReason) || inner is null)
            {
                strategy = null;
                return false;
            }

            strategy = new BacktestStrategyLiveAdapter(context.StrategyId, inner);
            return true;
        });

        var resolved = catalog.TryCreate(
            DocumentId,
            new Dictionary<string, string> { [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId },
            out var liveStrategy,
            out var reason);

        resolved.Should().BeTrue(reason);
        liveStrategy.Should().BeOfType<BacktestStrategyLiveAdapter>();
        liveStrategy!.StrategyId.Should().Be(DocumentId);
    }

    [Fact]
    public void Catalog_reports_the_designer_failure_reason_when_activation_is_refused()
    {
        var catalog = LiveStrategyCatalog.CreateDefault();
        var designerSource = CreateSource();
        catalog.RegisterFallback((LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
        {
            strategy = null;
            designerSource.TryCreate(context, out _, out failureReason);
            return false;
        });

        var resolved = catalog.TryCreate(
            DocumentId,
            new Dictionary<string, string> { [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId },
            out _,
            out var reason);

        resolved.Should().BeFalse();
        reason.Should().Contain("Fallback sources:").And.Contain("not found");
    }

    private static LiveStrategyCreationContext Context() => new(
        DocumentId,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId
        });

    private static DesignerDocumentLiveSource CreateSource(StrategyDesignDocument? document = null) =>
        new(new FakeDesignRepository(document), new StrategyDesignService());

    private static IBacktestStrategy Activate(StrategyDesignDocument document)
    {
        var source = CreateSource(document);
        source.TryCreate(Context(), out var strategy, out var failureReason).Should().BeTrue(failureReason);
        return strategy!;
    }

    private static HistoricalBar Bar(string symbol, decimal close, long volume) =>
        new(symbol, new DateOnly(2026, 1, 5), close, close, close, close, volume, "test", 0L, null);

    private static StrategyDesignCell TradeCell(IReadOnlyDictionary<string, string> parameters) =>
        new("buy-equities", "Buy equities", "trade", "trade", "execute buy order", [], parameters);

    private static StrategyDesignCell RiskCell() =>
        new("risk-guard", "Risk guard", "governance", "control", "attach run trace to the review packet", []);

    private static StrategyDesignDocument TradableDocument() => DocumentWithGate("PRICE > 20");

    private static StrategyDesignDocument DocumentWithGate(string gateSource) => new(
        DocumentId,
        "Momentum design",
        "Designer document used by the PRD-020 activation tests.",
        "1",
        "provider-bars/equities/daily",
        ["SPY"],
        [
            new StrategyDesignCell(
                "liquid-universe",
                "Liquid equity universe",
                "visual",
                "universe",
                gateSource,
                []),
            TradeCell(new Dictionary<string, string>
            {
                ["instrument"] = "Equity",
                ["direction"] = "Buy",
                ["sizingMethod"] = "FixedShares",
                ["sizingValue"] = "10"
            }),
            RiskCell()
        ],
        [],
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static StrategyDesignDocument DocumentWithTradeParameters(IReadOnlyDictionary<string, string> parameters)
    {
        var document = TradableDocument();
        return document with
        {
            Cells = document.Cells
                .Select(cell => string.Equals(cell.Kind, "trade", StringComparison.OrdinalIgnoreCase)
                    ? TradeCell(parameters)
                    : cell)
                .ToArray()
        };
    }

    private sealed class FakeDesignRepository(StrategyDesignDocument? document) : IStrategyDesignRepository
    {
        public Task SaveAsync(StrategyDesignDocument value, CancellationToken ct = default) => Task.CompletedTask;

        public Task<StrategyDesignDocument?> GetAsync(string documentId, CancellationToken ct = default) =>
            Task.FromResult(document is not null
                && string.Equals(document.DocumentId, documentId, StringComparison.OrdinalIgnoreCase)
                    ? document
                    : null);

        public Task<IReadOnlyList<StrategyDesignDraftSummary>> ListDraftsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StrategyDesignDraftSummary>>([]);
    }

    private sealed class RecordingContext : IBacktestContext
    {
        private readonly Dictionary<string, Position> _positions;
        private readonly decimal _lastPrice;

        public RecordingContext(
            IEnumerable<string> universe,
            decimal portfolioValue,
            decimal lastPrice,
            IEnumerable<(string Symbol, long Quantity)>? positions = null)
        {
            Universe = universe.ToHashSet(StringComparer.OrdinalIgnoreCase);
            PortfolioValue = portfolioValue;
            Cash = portfolioValue;
            _lastPrice = lastPrice;
            _positions = (positions ?? Enumerable.Empty<(string Symbol, long Quantity)>()).ToDictionary(
                item => item.Symbol,
                item => new Position(item.Symbol, item.Quantity, 100m, 0m, 0m),
                StringComparer.OrdinalIgnoreCase);
        }

        public List<(string Symbol, long Quantity)> Orders { get; } = [];

        public IReadOnlySet<string> Universe { get; }

        public DateTimeOffset CurrentTime { get; } = DateTimeOffset.UnixEpoch;

        public DateOnly CurrentDate { get; } = new(2026, 1, 5);

        public decimal Cash { get; }

        public decimal PortfolioValue { get; }

        public IReadOnlyDictionary<string, Position> Positions => _positions;

        public IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts { get; } =
            new Dictionary<string, FinancialAccountSnapshot>();

        public IReadOnlyLedger Ledger => throw new NotSupportedException("Not used by designer strategy tests.");

        public decimal? GetLastPrice(string symbol) => _lastPrice > 0m ? _lastPrice : null;

        public Guid PlaceMarketOrder(string symbol, long quantity)
        {
            Orders.Add((symbol, quantity));
            return Guid.NewGuid();
        }

        public Guid PlaceMarketOrder(string symbol, long quantity, string accountId) =>
            PlaceMarketOrder(symbol, quantity);

        public Guid PlaceOrder(OrderRequest request) => Guid.NewGuid();

        public Guid PlaceBracketOrder(BracketOrderRequest request) => Guid.NewGuid();

        public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice) => Guid.NewGuid();

        public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice, string accountId) => Guid.NewGuid();

        public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice) => Guid.NewGuid();

        public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice, string accountId) => Guid.NewGuid();

        public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice) => Guid.NewGuid();

        public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice, string accountId) =>
            Guid.NewGuid();

        public void CancelOrder(Guid orderId)
        {
        }

        public void CancelContingentOrders(Guid parentOrderId)
        {
        }

        public Task<OptionChainSnapshot?> GetOptionChainAsync(
            string underlyingSymbol,
            DateOnly expiration,
            int? strikeRange = null,
            CancellationToken ct = default) => Task.FromResult<OptionChainSnapshot?>(null);

        public Task<DateOnly?> GetNearestExpirationAsync(
            string underlyingSymbol,
            int minDte = 0,
            CancellationToken ct = default) => Task.FromResult<DateOnly?>(null);
    }
}
