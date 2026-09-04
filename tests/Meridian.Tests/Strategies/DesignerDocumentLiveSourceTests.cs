using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
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
        reason.Should().Contain("Fallback sources:").And.Contain("not found");
    }

    private static LiveStrategyCreationContext Context(StrategyDesignDocument? document = null) => new(
        DocumentId,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = DocumentId,
            [DesignerDocumentRevision.ParameterKey] = DesignerDocumentRevision.ComputeHash(
                new StrategyDesignService().Normalize(document ?? TradableDocument()))
        });

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
