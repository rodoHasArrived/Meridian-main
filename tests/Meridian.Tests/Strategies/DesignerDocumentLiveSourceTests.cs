using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Live.Strategies;
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
        var document = TradableDocument();

        var handled = CreateSource(document).TryCreate(Context(document), out var strategy, out var failureReason);

        handled.Should().BeTrue(failureReason);
        failureReason.Should().BeNull();
        strategy.Should().NotBeNull();
        strategy!.Name.Should().Contain("Momentum design");
    }

    [Fact]
    public void TryCreate_defers_when_the_document_does_not_exist()
    {
        var handled = CreateSource().TryCreate(Context(), out var strategy, out var failureReason);

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
    public void TryCreate_refuses_a_run_that_carries_no_approved_revision()
    {
        var document = TradableDocument();
        var context = new LiveStrategyCreationContext(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId
            });

        var handled = CreateSource(document).TryCreate(context, out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain(DesignerDocumentRevision.ParameterKey)
            .And.Contain("backtested and approved");
    }

    [Fact]
    public void TryCreate_refuses_a_document_edited_since_promotion()
    {
        // The approved revision is the tradable document; the repository now holds an edited one.
        var approved = TradableDocument();
        var edited = DocumentWithGate("PRICE > 999");

        var handled = CreateSource(edited).TryCreate(Context(approved), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("has changed since this run was promoted");
    }

    [Fact]
    public void TryCreate_refuses_a_document_with_a_code_cell()
    {
        var document = WithExtraCell(new StrategyDesignCell(
            "rebalance-loop",
            "Weekly rebalance loop",
            "code",
            "backtest",
            "rebalance every Friday with max 20 names",
            ["PRICE"]));

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("code cell").And.Contain("plugin").And.Contain("QuantLab");
    }

    [Theory]
    [InlineData("CUSIP")]
    [InlineData("OPTION_DELTA")]
    [InlineData("DURATION")]
    [InlineData("LEDGER_CASH")]
    [InlineData("VOLUME_AVG_20D")]
    public void TryCreate_refuses_fields_with_no_live_source(string fieldRef)
    {
        var document = WithExtraCell(
            new StrategyDesignCell("ref-cell", "Reference screen", "formula", "filter", "PRICE > 1", [fieldRef]));

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain(fieldRef).And.Contain("cannot be resolved during live execution");
    }

    [Fact]
    public void TryCreate_refuses_a_document_with_no_trade_cell()
    {
        var document = TradableDocument();
        document = document with
        {
            Cells = document.Cells
                .Where(cell => !string.Equals(cell.Kind, "trade", StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("no trade cell").And.Contain("no executable trade intent");
    }

    [Fact]
    public void TryCreate_refuses_a_non_equity_trade_intent()
    {
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Option",
            ["direction"] = "Buy",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "10"
        });

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("Option").And.Contain("equity and ETF orders only");
    }

    [Fact]
    public void TryCreate_refuses_an_exit_only_trade_direction()
    {
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Sell",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "10"
        });

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("closes a position rather than opening one");
    }

    [Fact]
    public void TryCreate_refuses_a_price_constraint_the_engine_cannot_honour()
    {
        // The shipped trade-intent template asks for VWAP. Downgrading that to a market order
        // would substitute a different execution instruction for the operator's.
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Buy",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "10",
            ["priceConstraint"] = "VWAP"
        });

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("VWAP").And.Contain("no route that honours that instruction");
    }

    [Fact]
    public void TryCreate_refuses_a_fractional_share_count()
    {
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Buy",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "1.9"
        });

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("whole numbers").And.Contain("will not round");
    }

    [Fact]
    public void TryCreate_refuses_a_branching_transition()
    {
        var document = TradableDocument();
        document = document with
        {
            Transitions =
            [
                new StrategyDesignTransition("t1", "liquid-universe", "buy-equities", "loop", "weekly", 52, "bounded")
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("transition 't1'").And.Contain("branching or looping");
    }

    [Fact]
    public void TryCreate_refuses_a_transition_carrying_an_evaluable_condition()
    {
        // Nothing evaluates transition conditions, so an edge conditioned on a real expression
        // would let its downstream cells run unconditionally. Prose labels stay allowed.
        var document = TradableDocument();
        document = document with
        {
            Transitions =
            [
                new StrategyDesignTransition("t1", "liquid-universe", "buy-equities", "next", "PRICE > 100")
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("carrying the condition").And.Contain("never be applied");
    }

    [Fact]
    public void TryCreate_refuses_a_prose_transition_condition_too()
    {
        // There is no reliable way to tell a label from a constraint the operator meant, and
        // nothing evaluates either, so both are refused rather than one being silently dropped.
        var document = TradableDocument();
        document = document with
        {
            Transitions =
            [
                new StrategyDesignTransition("t1", "liquid-universe", "buy-equities", "next", "universe ready")
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("carrying the condition");
    }

    [Fact]
    public void TryCreate_allows_a_transition_with_no_condition()
    {
        var document = TradableDocument();
        document = document with
        {
            Transitions =
            [
                new StrategyDesignTransition("t1", "liquid-universe", "buy-equities", "next", string.Empty)
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeTrue(failureReason);
    }

    [Fact]
    public void TryCreate_contains_a_malformed_document_instead_of_throwing()
    {
        // Duplicate cell ids make the shared design service throw rather than report. Letting that
        // escape would abort the engine's startup resume sweep and strand every other retained run.
        var document = TradableDocument();
        document = document with { Cells = [.. document.Cells, document.Cells[0]] };

        var handled = CreateSource(document).TryCreate(Context(document), out var strategy, out var failureReason);

        handled.Should().BeFalse();
        strategy.Should().BeNull();
        failureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreate_refuses_a_non_equity_universe_builder()
    {
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "FixedIncome",
                ["includeRules"] = "PRICE > 20"
            },
            ["SPY"]);

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("FixedIncome").And.Contain("equity and ETF orders only");
    }

    [Fact]
    public void TryCreate_refuses_a_filter_that_is_not_a_condition()
    {
        // "PRICE" parses, but it is a number. Activating it would produce a run that looks live
        // and silently matches nothing on every event.
        var document = DocumentWithGate("PRICE");

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("Expected a true/false condition");
    }

    [Fact]
    public void TryCreate_refuses_a_rank_that_is_not_a_score()
    {
        var document = WithExtraCell(
            new StrategyDesignCell("rank-cell", "Rank", "formula", "rank", "PRICE > 20", ["PRICE"]));

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("Expected a numeric score");
    }

    [Fact]
    public void TryCreate_refuses_an_unparseable_risk_guard()
    {
        var document = WithExtraCell(new StrategyDesignCell(
            "risk-prose", "Risk guard", "governance", "risk", "keep drawdown sensible", []));

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        // A stated risk limit that the engine cannot evaluate must block activation. Dropping it
        // would run the strategy without the control the operator promoted it with.
        handled.Should().BeFalse();
        failureReason.Should().Contain("Risk guard cell").And.Contain("not an executable condition");
    }

    [Fact]
    public void TryCreate_refuses_a_document_that_fails_designer_validation()
    {
        var document = TradableDocument() with { DatasetReference = string.Empty };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("does not pass designer validation").And.Contain("DatasetRequired");
    }

    [Theory]
    [InlineData("buy-and-hold")]
    [InlineData("moving-average-crossover")]
    public void TryCreate_refuses_a_document_id_that_collides_with_a_built_in_strategy(string reservedId)
    {
        // LiveStrategyCatalog resolves an exact factory id before any fallback, so a document saved
        // under a built-in id would trade the built-in strategy and skip this source entirely --
        // bypassing the approved revision, gates, sizing, and risk guards.
        var document = TradableDocument() with { DocumentId = reservedId };
        var context = new LiveStrategyCreationContext(
            reservedId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = reservedId,
                [DesignerDocumentRevision.ParameterKey] =
                    DesignerDocumentRevision.ComputeHash(new StrategyDesignService().Normalize(document))
            });

        var handled = new DesignerDocumentLiveSource(new FakeDesignRepository(document), new StrategyDesignService())
            .TryCreate(context, out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("collides with a built-in live strategy").And.Contain("Rename the document");
    }

    [Fact]
    public void TryCreate_compiles_a_rank_cell_by_purpose_regardless_of_kind()
    {
        // Designer validation does not constrain kind/purpose pairs. Classifying by kind first
        // would drop this as governance documentation and trade an unranked universe.
        var document = WithExtraCell(new StrategyDesignCell(
            "odd-rank", "Rank", "governance", "rank", "PRICE > 20", ["PRICE"]));

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("Expected a numeric score");
    }

    [Fact]
    public void TryCreate_refuses_an_unsupported_governance_purpose()
    {
        var document = WithExtraCell(new StrategyDesignCell(
            "odd-governance", "Odd", "governance", "universe", "PRICE > 1", ["PRICE"]));

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("no semantics for any other combination");
    }

    [Fact]
    public void TryCreate_refuses_equal_weight_sizing()
    {
        // EqualWeight only means anything if holdings are resized as the target set changes, and
        // this engine enters and exits once rather than trading toward a target weight.
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Buy",
            ["sizingMethod"] = "EqualWeight"
        });

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("EqualWeight").And.Contain("do not rebalance to a target weight");
    }

    [Fact]
    public void Activated_document_will_not_trade_inventory_it_cannot_account_for()
    {
        // A host restart leaves the ownership map empty while the broker still holds the run's
        // earlier fills. Entering again would double the position.
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(
            ["SPY"], portfolioValue: 100_000m, lastPrice: 50m, positions: [("SPY", 10L)]);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty("the position predates this strategy instance and is not its to double or unwind");
    }

    [Fact]
    public void Cross_section_is_abandoned_when_a_rank_score_cannot_be_computed()
    {
        // PRICE / (PRICE - 50) faults for a symbol trading at exactly 50. Ranking the rest against
        // an incomplete cross-section is the defect the cold-field guard already refuses.
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["maxSize"] = "2"
            },
            ["AAA", "BBB"],
            rankSource: "PRICE / (PRICE - 50)");

        var strategy = Activate(document);
        var ctx = new RecordingContext(["AAA", "BBB"], portfolioValue: 100_000m, lastPrice: 60m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("AAA", close: 60m, volume: 1L, day: 5), ctx);
        strategy.OnBar(Bar("BBB", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty("one unscoreable name makes the whole ranked selection indeterminate");
    }

    [Fact]
    public void Activated_document_enters_a_position_once_its_conditions_hold()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 5_000_000L, day: 5), ctx);

        // FixedShares 10 on a document whose only gate is PRICE > 20.
        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 10L));
    }

    [Fact]
    public void Activated_document_places_no_order_while_a_gate_fails()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 5m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 5m, volume: 5_000_000L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty();
    }

    [Fact]
    public void Activated_document_exits_the_position_it_opened()
    {
        var strategy = Activate(TradableDocument());
        // Portfolio and ownership agree: the ten shares are this run's own fill.
        var ctx = new RecordingContext(
            ["SPY"], portfolioValue: 100_000m, lastPrice: 50m, positions: [("SPY", 10L)]);

        strategy.Initialize(ctx);
        strategy.OnOrderFill(Fill("SPY", 10L), ctx);

        strategy.OnBar(Bar("SPY", close: 5m, volume: 1L, day: 5), ctx);

        // Sized from what this run filled, not from the shared portfolio quantity.
        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", -10L));
    }

    [Fact]
    public void Activated_document_does_not_exit_a_holding_whose_fields_are_cold()
    {
        // A restart leaves the rolling window empty. Treating "no data yet" as a failed condition
        // would liquidate a valid position merely because history has not warmed.
        var strategy = Activate(DocumentWithGate("VOLATILITY_20D < 0.30"));
        var ctx = new RecordingContext(
            ["SPY"], portfolioValue: 100_000m, lastPrice: 50m, positions: [("SPY", 10L)]);

        strategy.Initialize(ctx);
        strategy.OnOrderFill(Fill("SPY", 10L), ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 1), ctx);

        ctx.Orders.Should().BeEmpty("a cold window is neither an entry nor an exit signal");

        // Warm and calm: the guard passes, so the holding stays.
        for (var day = 2; day <= 25; day++)
        {
            strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: day), ctx);
        }

        ctx.Orders.Should().BeEmpty("a flat price series is well inside the 30% volatility guard");

        // Warm and violent: the guard now fails, so the run exits its own position.
        for (var day = 26; day <= 60; day++)
        {
            strategy.OnBar(Bar("SPY", close: day % 2 == 0 ? 50m : 100m, volume: 1L, day: day), ctx);
        }

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", -10L));
    }

    [Fact]
    public void Session_windows_advance_per_session_not_per_event()
    {
        // VOLATILITY_20D is a 20-session metric. The live strategy hub carries trades and quotes
        // but never bars, so sessions are keyed off the event timestamp: many trades inside one
        // date must count as one session, not twenty.
        var strategy = Activate(DocumentWithGate("VOLATILITY_20D < 0.30"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        for (var i = 0; i < 40; i++)
        {
            strategy.OnTrade(TradeAt("SPY", 50m, day: 1, sequence: i), ctx);
        }

        ctx.Orders.Should().BeEmpty("forty trades within one session are one session, not forty");

        // Distinct dates roll the session window, and the document activates off ticks alone.
        for (var day = 2; day <= 25; day++)
        {
            strategy.OnTrade(TradeAt("SPY", 50m, day: day, sequence: day), ctx);
        }

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 10L));
    }

    [Fact]
    public void Repeated_bars_for_one_session_do_not_double_count()
    {
        var strategy = Activate(DocumentWithGate("VOLATILITY_20D < 0.30"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        for (var i = 0; i < 30; i++)
        {
            strategy.OnBar(Bar("SPY", close: 50m, volume: 5_000_000L, day: 5), ctx);
        }

        ctx.Orders.Should().BeEmpty("a restated bar for the same session is one session, not thirty");
    }

    [Fact]
    public void Gate_fields_resolve_lazily_so_short_circuiting_still_works()
    {
        // A flat symbol satisfies the left branch, so momentum is never read. Resolving every
        // mentioned field up front would strand this document for sixty-four sessions.
        var strategy = Activate(DocumentWithGate("PORTFOLIO_WEIGHT == 0 || MOMENTUM_63D > 0"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 10L));
    }

    [Fact]
    public void A_gate_evaluation_fault_does_not_liquidate_a_holding()
    {
        // PRICE / (PRICE - 50) divides by zero exactly at 50. That is an arithmetic accident, not
        // a signal that the entry condition stopped holding.
        var strategy = Activate(DocumentWithGate("PRICE / (PRICE - 50) > 1"));
        var ctx = new RecordingContext(
            ["SPY"], portfolioValue: 100_000m, lastPrice: 60m, positions: [("SPY", 10L)]);

        strategy.Initialize(ctx);
        strategy.OnOrderFill(Fill("SPY", 10L), ctx);
        strategy.OnBar(Bar("SPY", close: 60m, volume: 1L, day: 5), ctx);
        ctx.Orders.Should().BeEmpty("the position is already held and the gate still passes");

        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 6), ctx);

        ctx.Orders.Should().BeEmpty("a division by zero is indeterminate, not an exit signal");
    }

    [Fact]
    public void TryCreate_refuses_concurrent_cells_that_share_branches()
    {
        // An any-pass cell replaces its branches' gates with their disjunction; a second cell over
        // the same branches would silently inherit those semantics.
        var document = TradableDocument();
        document = document with
        {
            Cells =
            [
                new StrategyDesignCell("branch-a", "A", "formula", "filter", "PRICE > 20", ["PRICE"]),
                new StrategyDesignCell("branch-b", "B", "formula", "filter", "PRICE < 500", ["PRICE"]),
                new StrategyDesignCell("gate-any", "Any", "concurrent", "concurrent", "any", [],
                    new Dictionary<string, string> { ["branchIds"] = "branch-a,branch-b", ["semantics"] = "any-pass" }),
                new StrategyDesignCell("gate-all", "All", "concurrent", "concurrent", "all", [],
                    new Dictionary<string, string> { ["branchIds"] = "branch-a,branch-b", ["semantics"] = "all-pass" }),
                .. document.Cells.Where(cell => cell.CellId != "liquid-universe")
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("shares branch cell(s)").And.Contain("only one concurrent gate");
    }

    [Fact]
    public void TryCreate_refuses_a_minimum_the_universe_can_never_reach()
    {
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["minSize"] = "20"
            },
            ["AAA", "BBB"]);

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("could never trade");
    }

    [Fact]
    public void Momentum_warms_once_enough_completed_sessions_exist()
    {
        // The window has to retain the in-progress session on top of the 64 completed closes the
        // 63-day comparison needs, or the field is indeterminate forever.
        var strategy = Activate(DocumentWithGate("MOMENTUM_63D > -1"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        for (var day = 1; day <= 70; day++)
        {
            strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: day), ctx);
        }

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 10L));
    }

    [Fact]
    public void An_unranked_cap_follows_declared_universe_order()
    {
        // Without a rank cell every score ties, and an alphabetical tie-break would trade a subset
        // the operator never chose.
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["maxSize"] = "2"
            },
            ["ZZZ", "MMM", "AAA"]);

        var strategy = Activate(document);
        var ctx = new RecordingContext(["ZZZ", "MMM", "AAA"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("ZZZ", close: 50m, volume: 1L, day: 5), ctx);
        strategy.OnBar(Bar("MMM", close: 50m, volume: 1L, day: 5), ctx);
        strategy.OnBar(Bar("AAA", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Select(order => order.Symbol).Should().BeEquivalentTo(["ZZZ", "MMM"]);
    }

    [Fact]
    public void TryCreate_refuses_a_universe_symbol_containing_a_separator()
    {
        // The promoted run carries the universe as a delimited parameter the engine splits.
        var document = TradableDocument() with { Universe = ["AAA,BBB"] };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("comma, semicolon, or space");
    }

    [Fact]
    public void Revision_hash_distinguishes_collection_boundaries()
    {
        // ["AAA,BBB"] and ["AAA","BBB"] must not hash alike, or an edit between those shapes would
        // pass revision verification while changing which symbols trade.
        var service = new StrategyDesignService();
        var joined = service.Normalize(TradableDocument() with { Universe = ["AAA,BBB"] });
        var split = service.Normalize(TradableDocument() with { Universe = ["AAA", "BBB"] });

        DesignerDocumentRevision.ComputeHash(joined)
            .Should().NotBe(DesignerDocumentRevision.ComputeHash(split));
    }

    [Fact]
    public void A_foreign_fractional_holding_blocks_the_symbol()
    {
        // Position.Quantity rounds 0.9 shares to zero. Without the unrounded size the ownership
        // guard would read "no position" and enter on top of someone else's holding.
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(
            ["SPY"],
            portfolioValue: 100_000m,
            lastPrice: 50m,
            positions: [("SPY", 0L)],
            exactQuantity: 0.9m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty("a fractional holding this run does not own is still not its inventory");
    }

    [Fact]
    public void Activated_document_respects_a_universe_builder_max_size()
    {
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["maxSize"] = "2"
            },
            ["AAA", "BBB", "CCC"],
            rankSource: "PRICE");

        var strategy = Activate(document);
        var ctx = new RecordingContext(["AAA", "BBB", "CCC"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("AAA", close: 30m, volume: 1L, day: 5), ctx);
        strategy.OnBar(Bar("BBB", close: 40m, volume: 1L, day: 5), ctx);
        strategy.OnBar(Bar("CCC", close: 50m, volume: 1L, day: 5), ctx);

        // Ranked on PRICE descending, capped at two names: CCC and BBB, never AAA.
        ctx.Orders.Select(order => order.Symbol).Should().BeEquivalentTo(["CCC", "BBB"]);
    }

    [Fact]
    public void Activated_document_holds_off_until_minimum_universe_breadth_exists()
    {
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["minSize"] = "2"
            },
            ["AAA", "BBB"]);

        var strategy = Activate(document);
        var ctx = new RecordingContext(["AAA", "BBB"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("AAA", close: 30m, volume: 1L, day: 5), ctx);
        ctx.Orders.Should().BeEmpty("one qualifying name is below the document's minSize of 2");

        strategy.OnBar(Bar("BBB", close: 30m, volume: 1L, day: 5), ctx);
        ctx.Orders.Select(order => order.Symbol).Should().BeEquivalentTo(["AAA", "BBB"]);
    }

    [Fact]
    public void Activated_document_sizes_a_percent_aum_trade_from_portfolio_value()
    {
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Buy",
            ["sizingMethod"] = "PercentAUM",
            ["sizingValue"] = "0.05"
        });
        var strategy = Activate(document);
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        // 5% of 100,000 is 5,000; at 50 a share that is 100 shares.
        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", 100L));
    }

    [Fact]
    public void Activated_short_document_opens_a_negative_position()
    {
        var document = DocumentWithTradeParameters(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "SellShort",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "7"
        });
        var strategy = Activate(document);
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().ContainSingle().Which.Should().Be(("SPY", -7L));
    }

    [Fact]
    public void Risk_guard_binds_against_the_position_the_order_would_create()
    {
        // PORTFOLIO_WEIGHT is zero while flat, so the cap passes trivially on current state; the
        // 50%-of-AUM order it would authorise has to be measured against the cap, not around it.
        var document = TradableDocument();
        document = document with
        {
            Cells =
            [
                .. document.Cells.Where(cell => cell.CellId != "risk-guard"),
                new StrategyDesignCell(
                    "weight-cap",
                    "Exposure cap",
                    "governance",
                    "risk",
                    "PORTFOLIO_WEIGHT <= 0.10",
                    ["PORTFOLIO_WEIGHT"])
            ]
        };
        document = DocumentWithTradeParameters(
            new Dictionary<string, string>
            {
                ["instrument"] = "Equity",
                ["direction"] = "Buy",
                ["sizingMethod"] = "PercentAUM",
                ["sizingValue"] = "0.5"
            },
            document);

        var strategy = Activate(document);
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty("a 50% position breaches the document's own 10% exposure cap");
    }

    [Fact]
    public void Risk_purpose_binds_as_a_guard_whatever_kind_the_cell_declares()
    {
        // Designer validation does not constrain kind against purpose, so an operator can write a
        // risk limit on a formula cell. Compiled as an entry gate it would pass trivially on a flat
        // symbol and never be measured against the order it governs.
        var document = TradableDocument();
        document = document with
        {
            Cells =
            [
                .. document.Cells.Where(cell => cell.CellId != "risk-guard"),
                new StrategyDesignCell(
                    "weight-cap",
                    "Exposure cap",
                    "formula",
                    "risk",
                    "PORTFOLIO_WEIGHT <= 0.10",
                    ["PORTFOLIO_WEIGHT"])
            ]
        };
        document = DocumentWithTradeParameters(
            new Dictionary<string, string>
            {
                ["instrument"] = "Equity",
                ["direction"] = "Buy",
                ["sizingMethod"] = "PercentAUM",
                ["sizingValue"] = "0.5"
            },
            document);

        var strategy = Activate(document);
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty("a risk-purpose cell is a risk guard even when its kind is 'formula'");
    }

    [Fact]
    public void A_terminal_order_outcome_releases_the_symbol()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);
        ctx.Orders.Should().ContainSingle();

        // While the order is unresolved the symbol stays blocked: it could still fill, and a
        // replacement would double the position.
        strategy.OnBar(Bar("SPY", close: 51m, volume: 1L, day: 6), ctx);
        ctx.Orders.Should().ContainSingle("an unresolved order must not be replaced");

        strategy.Should().BeAssignableTo<ILiveOrderOutcomeObserver>();
        ((ILiveOrderOutcomeObserver)strategy).OnOrderTerminated(ctx.OrderIds[0], LiveOrderOutcome.Rejected);

        strategy.OnBar(Bar("SPY", close: 52m, volume: 1L, day: 7), ctx);
        ctx.Orders.Should().HaveCount(2, "a rejected order must not block the symbol for the life of the run");
    }

    [Fact]
    public void A_terminal_outcome_for_an_unknown_order_changes_nothing()
    {
        var strategy = Activate(TradableDocument());
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ((ILiveOrderOutcomeObserver)strategy).OnOrderTerminated(Guid.NewGuid(), LiveOrderOutcome.Cancelled);

        strategy.OnBar(Bar("SPY", close: 51m, volume: 1L, day: 6), ctx);
        ctx.Orders.Should().ContainSingle("another run's order id must not release this run's marker");
    }

    [Fact]
    public void The_live_adapter_forwards_terminal_outcomes_to_an_observing_strategy()
    {
        var inner = new RecordingObserverStrategy();
        var adapter = new BacktestStrategyLiveAdapter("designer-doc", inner);
        var orderId = Guid.NewGuid();

        adapter.OnOrderTerminated(orderId, LiveOrderOutcome.Expired);

        inner.Terminated.Should().ContainSingle().Which.Should().Be((orderId, LiveOrderOutcome.Expired));
    }

    [Fact]
    public void A_session_only_plan_still_re_decides_once_its_window_is_full()
    {
        // The window holds MaxWindow sessions, so past that point a roll enqueues one close and
        // dequeues another and the session count stops changing. A plan reading only session
        // fields would then never re-decide from market events again.
        var document = DocumentWithGate("MOMENTUM_63D > 0");
        var strategy = Activate(document);
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);
        strategy.Initialize(ctx);

        // Sessions 1-64 flat, so momentum is exactly zero on the first session it can be computed.
        for (var day = 0; day < 64; day++)
        {
            strategy.OnTrade(TradeAt("SPY", 100m, day, day), ctx);
        }

        strategy.OnTrade(TradeAt("SPY", 90m, 64, 64), ctx);
        ctx.Orders.Should().BeEmpty("momentum over a flat window is zero, which does not pass '> 0'");

        // Session 66 drops session 1, so the comparison window shifts even though the count cannot.
        strategy.OnTrade(TradeAt("SPY", 200m, 65, 65), ctx);
        strategy.OnTrade(TradeAt("SPY", 200m, 66, 66), ctx);

        ctx.Orders.Should().ContainSingle("a session roll past MaxWindow still changes what momentum reads");
    }

    [Fact]
    public void A_late_observation_from_an_earlier_session_is_ignored()
    {
        // Nothing upstream guarantees timestamp monotonicity. Treating an older date as a forward
        // roll would append it as a new session and warm a session metric on sessions that never
        // happened -- and then append the current date a second time on the next in-session event.
        var strategy = Activate(DocumentWithGate("VOLATILITY_20D < 5"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 100m);
        strategy.Initialize(ctx);

        // 21 sessions: one short of the 22 the 20-session metric needs.
        for (var day = 0; day < 21; day++)
        {
            strategy.OnBar(Bar("SPY", close: 100m, volume: 1L, day: day), ctx);
        }

        ctx.Orders.Should().BeEmpty("21 recorded sessions cannot compute a 20-session metric");

        for (var repeat = 0; repeat < 5; repeat++)
        {
            strategy.OnBar(Bar("SPY", close: 100m, volume: 1L, day: 3), ctx);
        }

        ctx.Orders.Should().BeEmpty("replaying an earlier session must not manufacture new sessions");

        strategy.OnBar(Bar("SPY", close: 100m, volume: 1L, day: 21), ctx);
        ctx.Orders.Should().ContainSingle("the 22nd real session completes the window");
    }

    [Fact]
    public void An_extended_hours_event_stays_in_its_own_exchange_session()
    {
        // 19:30 Eastern is already the next day in UTC. Dating sessions by the UTC calendar would
        // close the session mid-flight and merge the late quote into the following one.
        var strategy = Activate(DocumentWithGate("VOLATILITY_20D < 5"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 100m);
        strategy.Initialize(ctx);

        // 21 sessions, each dated 2026-01-01 + day at 12:00 UTC (07:00 Eastern, same date).
        for (var day = 0; day < 21; day++)
        {
            strategy.OnTrade(TradeAt("SPY", 100m, day, day), ctx);
        }

        ctx.Orders.Should().BeEmpty("21 recorded sessions cannot compute a 20-session metric");

        // 2026-01-22T00:30Z is 19:30 Eastern on 2026-01-21 — the session already in progress.
        var afterHours = new Trade(
            new DateTimeOffset(2026, 1, 22, 0, 30, 0, TimeSpan.Zero),
            "SPY",
            100m,
            100L,
            AggressorSide.Buy,
            999);
        strategy.OnTrade(afterHours, ctx);

        ctx.Orders.Should().BeEmpty("an after-hours print belongs to the session it traded in");
    }

    [Fact]
    public void TryCreate_refuses_first_wins_concurrent_semantics()
    {
        // The designer schema offers first-wins, but nothing defines its runtime meaning. Running
        // it as any-pass would admit symbols an earlier branch rejected.
        var document = TradableDocument();
        document = document with
        {
            Cells =
            [
                .. document.Cells,
                new StrategyDesignCell("branch-a", "Branch A", "formula", "filter", "PRICE > 10", ["PRICE"]),
                new StrategyDesignCell("branch-b", "Branch B", "formula", "filter", "PRICE > 90", ["PRICE"]),
                new StrategyDesignCell(
                    "concurrent",
                    "Either branch",
                    "concurrent",
                    "filter",
                    "evaluate both branches",
                    [],
                    new Dictionary<string, string> { ["branchIds"] = "branch-a,branch-b", ["semantics"] = "first-wins" })
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("first-wins").And.Contain("no ordered first-result policy");
    }

    [Fact]
    public void A_spot_cross_section_waits_for_every_candidate_to_trade_this_session()
    {
        // A window keeps its last observation for ever. Without a common as-of boundary, a symbol
        // last quoted yesterday still enters today's ranking at yesterday's price.
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["maxSize"] = "2"
            },
            ["AAA", "BBB"],
            rankSource: "PRICE");

        var strategy = Activate(document);
        var ctx = new RecordingContext(["AAA", "BBB"], portfolioValue: 100_000m, lastPrice: 50m);
        strategy.Initialize(ctx);

        strategy.OnBar(Bar("AAA", close: 200m, volume: 1L, day: 5), ctx);
        strategy.OnBar(Bar("BBB", close: 150m, volume: 1L, day: 6), ctx);

        ctx.Orders.Should().BeEmpty("AAA's price is from the previous session, so the cross-section is not comparable");

        strategy.OnBar(Bar("AAA", close: 100m, volume: 1L, day: 6), ctx);
        ctx.Orders.Select(order => order.Symbol).Should().BeEquivalentTo(["BBB", "AAA"]);
    }

    [Fact]
    public void A_session_plan_does_not_decide_at_day_end()
    {
        // The live session raises OnDayEnd before dispatching the new date's first event, so every
        // session metric there still excludes the session that just closed.
        var strategy = Activate(DocumentWithGate("VOLATILITY_20D < 5"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 100m);
        strategy.Initialize(ctx);

        for (var day = 0; day < 22; day++)
        {
            strategy.OnBar(Bar("SPY", close: 100m, volume: 1L, day: day), ctx);
        }

        ctx.Orders.Should().ContainSingle();
        ((ILiveOrderOutcomeObserver)strategy).OnOrderTerminated(ctx.OrderIds[0], LiveOrderOutcome.Rejected);

        strategy.OnDayEnd(new DateOnly(2026, 1, 23), ctx);

        ctx.Orders.Should().ContainSingle("a session-window plan decides on the roll, not on the day-end callback");
    }

    [Theory]
    [InlineData("VOD.L")]
    [InlineData("7203")]
    [InlineData("BRK.B")]
    [InlineData("TOOLONG")]
    public void TryCreate_refuses_a_universe_symbol_that_is_not_a_plain_us_listing(string symbol)
    {
        // Session boundaries are resolved on one exchange calendar. A listing on another venue
        // trades across midnight Eastern inside a single local session, and a class suffix is the
        // same shape as a venue suffix, so neither can be admitted without a per-venue session map.
        var document = TradableDocument() with { Universe = [symbol] };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain(symbol).And.Contain("US exchange calendar");
    }

    [Fact]
    public void A_position_this_run_owns_is_not_exited_once_external_flow_offsets_it()
    {
        // The run holds +10 and someone else holds -10, so the shared book is flat. Reading that as
        // "nothing here" and selling 10 would open a short belonging to the other owner.
        var strategy = Activate(DocumentWithGate("PRICE > 100"));
        var ctx = new RecordingContext(["SPY"], portfolioValue: 100_000m, lastPrice: 50m);

        strategy.Initialize(ctx);
        strategy.OnOrderFill(Fill("SPY", 10L), ctx);
        strategy.OnBar(Bar("SPY", close: 50m, volume: 1L, day: 5), ctx);

        ctx.Orders.Should().BeEmpty("a flat portfolio against a non-zero owned quantity is offset, not closed");
    }

    [Fact]
    public void TryCreate_refuses_a_universe_builder_cell_with_risk_purpose()
    {
        // A universe-builder cell carries include rules and size bounds, not one condition, so it
        // cannot be re-evaluated against a projected position. Compiling it under its own kind
        // would file its rules as ordinary entry gates, which pass trivially on a flat symbol.
        var document = TradableDocument();
        document = document with
        {
            Cells =
            [
                .. document.Cells,
                new StrategyDesignCell(
                    "universe-risk",
                    "Universe risk",
                    "universe-builder",
                    "risk",
                    "build the universe",
                    ["PORTFOLIO_WEIGHT"],
                    new Dictionary<string, string>
                    {
                        ["assetClass"] = "Equity",
                        ["includeRules"] = "PORTFOLIO_WEIGHT <= 0.10"
                    })
            ]
        };

        var handled = CreateSource(document).TryCreate(Context(document), out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("universe-builder").And.Contain("purpose 'risk'");
    }

    [Fact]
    public void TryCreate_refuses_a_run_whose_symbols_differ_from_the_approved_universe()
    {
        // The revision hash pins what the document says; the symbols parameter decides what the
        // engine actually feeds the strategy. A run carrying a valid hash against a narrowed list
        // would select from a smaller universe than the one that was approved.
        var document = UniverseBuilderDocument(
            new Dictionary<string, string>
            {
                ["assetClass"] = "Equity",
                ["includeRules"] = "PRICE > 20",
                ["maxSize"] = "2"
            },
            ["AAA", "BBB", "CCC"]);

        var context = new LiveStrategyCreationContext(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId,
                [DesignerDocumentRevision.ParameterKey] =
                    DesignerDocumentRevision.ComputeHash(new StrategyDesignService().Normalize(document)),
                ["symbols"] = "AAA,BBB"
            });

        var handled = CreateSource(document).TryCreate(context, out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("CCC").And.Contain("different universe");
    }

    [Fact]
    public void TryCreate_accepts_a_run_whose_symbols_match_the_approved_universe()
    {
        var document = TradableDocument();
        var context = new LiveStrategyCreationContext(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId,
                [DesignerDocumentRevision.ParameterKey] =
                    DesignerDocumentRevision.ComputeHash(new StrategyDesignService().Normalize(document)),
                ["symbols"] = "SPY"
            });

        var handled = CreateSource(document).TryCreate(context, out _, out var failureReason);

        handled.Should().BeTrue(failureReason);
    }

    [Fact]
    public void TryCreate_refuses_a_designer_run_that_names_no_universe()
    {
        // LiveTradingEngine.ResolveUniverse falls back to the host's DefaultSymbols and defers only
        // when that is empty too, so a run without its own universe would trade whatever the host
        // is configured with, under this document's revision and risk guards.
        var document = TradableDocument();
        var context = new LiveStrategyCreationContext(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId,
                [DesignerDocumentRevision.ParameterKey] =
                    DesignerDocumentRevision.ComputeHash(new StrategyDesignService().Normalize(document))
            });

        var handled = CreateSource(document).TryCreate(context, out _, out var failureReason);

        handled.Should().BeFalse();
        failureReason.Should().Contain("names no universe").And.Contain("default symbols");
    }

    [Fact]
    public void Catalog_refuses_a_run_that_names_both_a_selector_and_a_factory_alias()
    {
        // The alias resolves before any fallback is consulted, so a designer run aliased to a
        // built-in factory would trade that strategy under its own run id, without the revision,
        // gates, sizing, or risk guards it was approved with.
        var catalog = LiveStrategyCatalog.CreateDefault();
        var sourceConsulted = false;
        catalog.RegisterFallback(
            DesignerDocumentLiveSource.DesignerDocumentParameterKey,
            (LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
            {
                sourceConsulted = true;
                strategy = null;
                failureReason = null;
                return false;
            });

        var resolved = catalog.TryCreate(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId,
                ["liveStrategyId"] = BuyAndHoldLiveStrategy.CatalogId
            },
            out var strategy,
            out var reason);

        resolved.Should().BeFalse();
        strategy.Should().BeNull();
        reason.Should().Contain(DesignerDocumentLiveSource.DesignerDocumentParameterKey).And.Contain("alias");
        sourceConsulted.Should().BeFalse("the contradiction is refused before any source is consulted");
    }

    [Fact]
    public void Designer_document_reaches_live_execution_through_the_catalog()
    {
        // The end-to-end PRD-020 seam: the catalog resolves a designer run that CreateDefault
        // alone could not, and hands back an ILiveStrategy the engine can launch.
        var document = TradableDocument();
        var catalog = LiveStrategyCatalog.CreateDefault();
        var designerSource = CreateSource(document);
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
            Context(document).Parameters,
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

        var resolved = catalog.TryCreate(DocumentId, Context().Parameters, out _, out var reason);

        resolved.Should().BeFalse();
        reason.Should().Contain("not found");
    }

    [Fact]
    public void Catalog_stops_at_the_source_that_claimed_the_run()
    {
        // A source states a reason only when it recognised the run and refused it. Continuing past
        // that would let a later source execute a different implementation under the same run id
        // -- a plugin-backed run whose assembly is missing silently becoming a designer run.
        var catalog = LiveStrategyCatalog.CreateDefault();
        var laterSourceConsulted = false;

        catalog.RegisterFallback(
            "pluginAssembly",
            (LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
            {
                strategy = null;
                failureReason = "plugin assembly 'strategy.dll' could not be loaded";
                return false;
            });
        catalog.RegisterFallback((LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
        {
            laterSourceConsulted = true;
            strategy = null;
            failureReason = null;
            return false;
        });

        var resolved = catalog.TryCreate(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["pluginAssembly"] = "strategy.dll" },
            out _,
            out var reason);

        resolved.Should().BeFalse();
        reason.Should().Contain("plugin assembly 'strategy.dll' could not be loaded").And.Contain(DocumentId);
        laterSourceConsulted.Should().BeFalse("a claimed run must not fall through to another implementation");
    }

    [Fact]
    public void Catalog_consults_later_sources_past_an_unclaimed_diagnostic()
    {
        // The resolver contract lets any source decline with a diagnostic and expect the next one
        // to be tried. Only a source whose selector the run actually carries ends resolution.
        var catalog = LiveStrategyCatalog.CreateDefault();
        catalog.RegisterFallback(
            "pluginAssembly",
            (LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
            {
                strategy = null;
                failureReason = "no plugin directory is configured";
                return false;
            });
        catalog.RegisterFallback((LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
        {
            strategy = new BuyAndHoldLiveStrategy(context.StrategyId);
            failureReason = null;
            return true;
        });

        var resolved = catalog.TryCreate(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            out var strategy,
            out var reason);

        resolved.Should().BeTrue(reason);
        strategy.Should().NotBeNull();
    }

    [Fact]
    public void Catalog_refuses_a_selector_run_whose_own_id_is_a_builtin_strategy()
    {
        // No alias is needed to reach a built-in factory: a run whose own id is a registered
        // strategy id resolves to it before any fallback, so the selected source never runs.
        var catalog = LiveStrategyCatalog.CreateDefault();
        var sourceConsulted = false;
        catalog.RegisterFallback(
            DesignerDocumentLiveSource.DesignerDocumentParameterKey,
            (LiveStrategyCreationContext context, out ILiveStrategy? strategy, out string? failureReason) =>
            {
                sourceConsulted = true;
                strategy = null;
                failureReason = null;
                return false;
            });

        var resolved = catalog.TryCreate(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId
            },
            out var strategy,
            out var reason);

        resolved.Should().BeFalse();
        strategy.Should().BeNull();
        reason.Should().Contain("built-in").And.Contain(BuyAndHoldLiveStrategy.CatalogId);
        sourceConsulted.Should().BeFalse("the contradiction is refused before resolution begins");
    }

    private static LiveStrategyCreationContext Context(StrategyDesignDocument? document = null)
    {
        var normalized = new StrategyDesignService().Normalize(document ?? TradableDocument());
        return new LiveStrategyCreationContext(
            DocumentId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId,
                [DesignerDocumentRevision.ParameterKey] = DesignerDocumentRevision.ComputeHash(normalized),

                // Promotion copies the document's universe into the run's parameter set, and
                // activation refuses a run without one, so the harness has to carry it too.
                ["symbols"] = string.Join(",", normalized.Universe)
            });
    }

    private static DesignerDocumentLiveSource CreateSource(StrategyDesignDocument? document = null) =>
        new(new FakeDesignRepository(document), new StrategyDesignService());

    private static IBacktestStrategy Activate(StrategyDesignDocument document)
    {
        var source = CreateSource(document);
        source.TryCreate(Context(document), out var strategy, out var failureReason).Should().BeTrue(failureReason);
        return strategy!;
    }

    private static HistoricalBar Bar(string symbol, decimal close, long volume, int day) =>
        new(symbol, new DateOnly(2026, 1, 1).AddDays(day), close, close, close, close, volume, "test", 0L, null);

    private static Trade TradeAt(string symbol, decimal price, int day, int sequence) => new(
        new DateTimeOffset(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)).AddDays(day),
        symbol,
        price,
        100L,
        AggressorSide.Buy,
        sequence);

    private static FillEvent Fill(string symbol, long quantity) => new(
        FillId: Guid.NewGuid(),
        OrderId: Guid.NewGuid(),
        Symbol: symbol,
        FilledQuantity: quantity,
        FillPrice: 50m,
        Commission: 0m,
        FilledAt: DateTimeOffset.UnixEpoch);

    private static StrategyDesignCell TradeCell(IReadOnlyDictionary<string, string> parameters) =>
        new("buy-equities", "Buy equities", "trade", "trade", "execute buy order", [], parameters);

    private static StrategyDesignCell RiskCell() =>
        new("risk-guard", "Risk guard", "governance", "control", "attach run trace to the review packet", []);

    private static IReadOnlyDictionary<string, string> DefaultTradeParameters() => new Dictionary<string, string>
    {
        ["instrument"] = "Equity",
        ["direction"] = "Buy",
        ["sizingMethod"] = "FixedShares",
        ["sizingValue"] = "10"
    };

    private static StrategyDesignDocument TradableDocument() => DocumentWithGate("PRICE > 20");

    private static StrategyDesignDocument DocumentWithGate(string gateSource) => new(
        DocumentId,
        "Momentum design",
        "Designer document used by the PRD-020 activation tests.",
        "1",
        "provider-bars/equities/daily",
        ["SPY"],
        [
            new StrategyDesignCell("liquid-universe", "Liquid equity universe", "visual", "universe", gateSource, []),
            TradeCell(DefaultTradeParameters()),
            RiskCell()
        ],
        [],
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static StrategyDesignDocument WithExtraCell(StrategyDesignCell cell)
    {
        var document = TradableDocument();
        return document with { Cells = [.. document.Cells, cell] };
    }

    private static StrategyDesignDocument UniverseBuilderDocument(
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> universe,
        string? rankSource = null)
    {
        var cells = new List<StrategyDesignCell>
        {
            new(
                "equity-universe",
                "Structured universe",
                "universe-builder",
                "universe",
                "structured universe definition",
                ["PRICE"],
                parameters)
        };

        if (rankSource is not null)
        {
            cells.Add(new StrategyDesignCell("rank", "Rank", "formula", "rank", rankSource, ["PRICE"]));
        }

        cells.Add(TradeCell(new Dictionary<string, string>
        {
            ["instrument"] = "Equity",
            ["direction"] = "Buy",
            ["sizingMethod"] = "FixedShares",
            ["sizingValue"] = "1"
        }));
        cells.Add(RiskCell());

        return TradableDocument() with { Universe = universe, Cells = cells };
    }

    private static StrategyDesignDocument DocumentWithTradeParameters(
        IReadOnlyDictionary<string, string> parameters,
        StrategyDesignDocument? baseDocument = null)
    {
        var document = baseDocument ?? TradableDocument();
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

    /// <summary>
    /// Minimal strategy that records the terminal outcomes forwarded to it, so the adapter's
    /// forwarding can be asserted without a live session.
    /// </summary>
    private sealed class RecordingObserverStrategy : IBacktestStrategy, ILiveOrderOutcomeObserver
    {
        public List<(Guid OrderId, LiveOrderOutcome Outcome)> Terminated { get; } = [];

        public string Name => "recording-observer";

        public void Initialize(IBacktestContext ctx)
        {
        }

        public void OnTrade(Trade trade, IBacktestContext ctx)
        {
        }

        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx)
        {
        }

        public void OnBar(HistoricalBar bar, IBacktestContext ctx)
        {
        }

        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx)
        {
        }

        public void OnOrderFill(FillEvent fill, IBacktestContext ctx)
        {
        }

        public void OnDayEnd(DateOnly date, IBacktestContext ctx)
        {
        }

        public void OnFinished(IBacktestContext ctx)
        {
        }

        public void OnOrderTerminated(Guid orderId, LiveOrderOutcome outcome) =>
            Terminated.Add((orderId, outcome));
    }

    private sealed class RecordingContext : IBacktestContext
    {
        private readonly Dictionary<string, Position> _positions;
        private readonly decimal _lastPrice;

        public RecordingContext(
            IEnumerable<string> universe,
            decimal portfolioValue,
            decimal lastPrice,
            IEnumerable<(string Symbol, long Quantity)>? positions = null,
            decimal? exactQuantity = null)
        {
            Universe = universe.ToHashSet(StringComparer.OrdinalIgnoreCase);
            PortfolioValue = portfolioValue;
            Cash = portfolioValue;
            _lastPrice = lastPrice;
            _positions = (positions ?? Enumerable.Empty<(string Symbol, long Quantity)>()).ToDictionary(
                item => item.Symbol,
                item => exactQuantity is { } exact
                    ? new Position(item.Symbol, item.Quantity, 100m, 0m, 0m) { ExactQuantity = exact }
                    : new Position(item.Symbol, item.Quantity, 100m, 0m, 0m),
                StringComparer.OrdinalIgnoreCase);
        }

        public List<(string Symbol, long Quantity)> Orders { get; } = [];

        public List<Guid> OrderIds { get; } = [];

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
            var orderId = Guid.NewGuid();
            OrderIds.Add(orderId);
            return orderId;
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
