module Meridian.FSharp.Tests.RiskPolicyTests

open System
open System.Collections.Generic
open Xunit
open FsCheck
open FsUnit.Xunit
open Meridian.Execution.Sdk
open Meridian.Backtesting.Sdk
open Meridian.Ledger
open Meridian.FSharp.Interop

let private createOrder side quantity =
    Meridian.Execution.Sdk.OrderRequest(
        Symbol = "AAPL",
        Side = side,
        Type = Meridian.Execution.Sdk.OrderType.Market,
        Quantity = quantity,
        StrategyId = "strategy")

let private boundedInt minInclusive maxInclusive seed =
    let width = int64 maxInclusive - int64 minInclusive + 1L
    minInclusive + int (abs (int64 seed) % width)

let private createBacktestResult sharpe drawdown totalReturn =
    let attribution = Dictionary<string, SymbolAttribution>() :> IReadOnlyDictionary<string, SymbolAttribution>
    let metrics =
        BacktestMetrics(
            InitialCapital = 100000m,
            FinalEquity = 110000m,
            GrossPnl = 10000m,
            NetPnl = 9000m,
            TotalReturn = totalReturn,
            AnnualizedReturn = totalReturn,
            SharpeRatio = sharpe,
            SortinoRatio = 1.2,
            CalmarRatio = 1.1,
            MaxDrawdown = 5000m,
            MaxDrawdownPercent = drawdown,
            MaxDrawdownRecoveryDays = 10,
            ProfitFactor = 1.4,
            WinRate = 0.55,
            TotalTrades = 20,
            WinningTrades = 11,
            LosingTrades = 9,
            TotalCommissions = 25m,
            TotalMarginInterest = 0m,
            TotalShortRebates = 0m,
            Xirr = 0.12,
            SymbolAttribution = attribution)

    let universe = HashSet<string>() :> IReadOnlySet<string>
    let ledger = Ledger() :> IReadOnlyLedger

    BacktestResult(
        Request = null,
        Universe = universe,
        Snapshots = [||],
        CashFlows = [||],
        Fills = [||],
        Metrics = metrics,
        Ledger = ledger,
        ElapsedTime = TimeSpan.Zero,
        TotalEventsProcessed = 0L)

[<Fact>]
let ``Position limit rejects projected quantity above max`` () =
    let ctx = RiskInterop.CreateContext(createOrder OrderSide.Buy 20m, 90m, 100m, Nullable(), Nullable(), Nullable())
    let result = RiskInterop.EvaluatePositionLimit(ctx)

    result.Approved |> should equal false
    result.Reasons[0].Contains("Position limit exceeded") |> should equal true

[<Fact>]
let ``Scenario_RiskPositionLimit_GeneratedLargerExposureNeverTurnsRejectIntoApprove`` () =
    let property currentSeed maxSeed excessSeed extraSeed =
        let maxPosition = boundedInt 1 10_000 maxSeed |> decimal
        let currentPosition = boundedInt 0 (int maxPosition) currentSeed |> decimal
        let rejectedQuantity = (maxPosition - currentPosition) + (boundedInt 1 5_000 excessSeed |> decimal)
        let largerQuantity = rejectedQuantity + (boundedInt 0 5_000 extraSeed |> decimal)

        let rejected =
            RiskInterop.CreateContext(createOrder OrderSide.Buy rejectedQuantity, currentPosition, maxPosition, Nullable(), Nullable(), Nullable())
            |> RiskInterop.EvaluatePositionLimit

        let larger =
            RiskInterop.CreateContext(createOrder OrderSide.Buy largerQuantity, currentPosition, maxPosition, Nullable(), Nullable(), Nullable())
            |> RiskInterop.EvaluatePositionLimit

        (not rejected.Approved) && (not larger.Approved)

    Check.One(Config.QuickThrowOnFailure.WithMaxTest(200), property)

let private createPortfolioContextSigned order portfolioExposure symbolExposure signedSymbolExposure portfolioValue orderNotional signedOrderNotional maxGross maxConcentration maxNotional escalateNotional =
    RiskInterop.CreatePortfolioContext(
        order,
        portfolioExposure,
        symbolExposure,
        signedSymbolExposure,
        portfolioValue,
        orderNotional,
        signedOrderNotional,
        maxGross,
        maxConcentration,
        maxNotional,
        escalateNotional)

let private createPortfolioContext order portfolioExposure symbolExposure portfolioValue orderNotional maxGross maxConcentration maxNotional escalateNotional =
    createPortfolioContextSigned order portfolioExposure symbolExposure (Nullable()) portfolioValue orderNotional (Nullable()) maxGross maxConcentration maxNotional escalateNotional

[<Fact>]
let ``Gross exposure rejects projected breach of the ceiling`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 10m) (Nullable 95_000m) (Nullable()) (Nullable()) (Nullable 10_000m) (Nullable 100_000m) (Nullable()) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateGrossExposure(ctx)

    result.Approved |> should equal false
    result.Reasons[0].Contains("Gross exposure limit") |> should equal true

[<Fact>]
let ``Gross exposure approves when unconfigured`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 10m) (Nullable 95_000m) (Nullable()) (Nullable()) (Nullable 10_000m) (Nullable()) (Nullable()) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateGrossExposure(ctx)

    result.Approved |> should equal true

[<Fact>]
let ``Gross exposure approves a de-risking sell near the ceiling`` () =
    // 95k gross, all in this symbol long; selling 50k reduces projected gross to 45k.
    let ctx = createPortfolioContextSigned (createOrder OrderSide.Sell 500m) (Nullable 95_000m) (Nullable 95_000m) (Nullable 95_000m) (Nullable()) (Nullable 50_000m) (Nullable -50_000m) (Nullable 100_000m) (Nullable()) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateGrossExposure(ctx)

    result.Approved |> should equal true

[<Fact>]
let ``Gross exposure preserves offsetting contribution gross in projections`` () =
    // Symbol gross 28k from offsetting lots (net -8k). A 100 buy moves only the net
    // component: projected symbol gross = 28k - 8k + 7.9k = 27.9k. Collapsing to
    // |net + order| = 7.9k would slip under this 20k ceiling; preserving the offsetting
    // gross must reject.
    let ctx = createPortfolioContextSigned (createOrder OrderSide.Buy 1m) (Nullable 28_000m) (Nullable 28_000m) (Nullable -8_000m) (Nullable()) (Nullable 100m) (Nullable 100m) (Nullable 20_000m) (Nullable()) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateGrossExposure(ctx)

    result.Approved |> should equal false

[<Fact>]
let ``Gross exposure handles an order crossing through zero`` () =
    // Long 30k in symbol; selling 80k notional crosses to short 50k: projected gross
    // = 60k existing-other + 50k = 110k > 100k ceiling.
    let ctx = createPortfolioContextSigned (createOrder OrderSide.Sell 800m) (Nullable 90_000m) (Nullable 30_000m) (Nullable 30_000m) (Nullable()) (Nullable 80_000m) (Nullable -80_000m) (Nullable 100_000m) (Nullable()) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateGrossExposure(ctx)

    result.Approved |> should equal false

[<Fact>]
let ``Symbol concentration rejects breach of the portfolio-value cap`` () =
    // 20k existing + 10k order = 30% of a 100k portfolio > 25% cap.
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 100m) (Nullable 20_000m) (Nullable 20_000m) (Nullable 100_000m) (Nullable 10_000m) (Nullable()) (Nullable 25m) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateSymbolConcentration(ctx)

    result.Approved |> should equal false
    result.Reasons[0].Contains("Concentration limit") |> should equal true

[<Fact>]
let ``Symbol concentration approves without a positive portfolio value`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 100m) (Nullable 20_000m) (Nullable 20_000m) (Nullable 0m) (Nullable 10_000m) (Nullable()) (Nullable 25m) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateSymbolConcentration(ctx)

    result.Approved |> should equal true

[<Fact>]
let ``Symbol concentration approves a reducing sell above the cap`` () =
    // 30% long concentration; selling 5% of portfolio value projects 25% — not a breach
    // of the 28% cap even though the position already exceeds it.
    let ctx = createPortfolioContextSigned (createOrder OrderSide.Sell 50m) (Nullable 30_000m) (Nullable 30_000m) (Nullable 30_000m) (Nullable 100_000m) (Nullable 5_000m) (Nullable -5_000m) (Nullable()) (Nullable 28m) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateSymbolConcentration(ctx)

    result.Approved |> should equal true

[<Fact>]
let ``Symbol concentration rejects a short crossing past the cap`` () =
    // Net short 10k; selling another 20k notional projects |−30k| = 30% > 25% cap.
    let ctx = createPortfolioContextSigned (createOrder OrderSide.Sell 200m) (Nullable 10_000m) (Nullable 10_000m) (Nullable -10_000m) (Nullable 100_000m) (Nullable 20_000m) (Nullable -20_000m) (Nullable()) (Nullable 25m) (Nullable()) (Nullable())
    let result = RiskInterop.EvaluateSymbolConcentration(ctx)

    result.Approved |> should equal false

[<Fact>]
let ``Order notional rejects above the hard ceiling`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 100m) (Nullable()) (Nullable()) (Nullable()) (Nullable 60_000m) (Nullable()) (Nullable()) (Nullable 50_000m) (Nullable 10_000m)
    let result = RiskInterop.EvaluateOrderNotional(ctx)

    result.Approved |> should equal false
    result.DecisionKind |> should equal "reject"

[<Fact>]
let ``Order notional escalates inside the governed-approval band`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 100m) (Nullable()) (Nullable()) (Nullable()) (Nullable 20_000m) (Nullable()) (Nullable()) (Nullable 50_000m) (Nullable 10_000m)
    let result = RiskInterop.EvaluateOrderNotional(ctx)

    result.Approved |> should equal false
    result.DecisionKind |> should equal "escalate"
    result.Reasons[0].Contains("governed-approval band") |> should equal true

[<Fact>]
let ``Order notional approves below the escalation band`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 100m) (Nullable()) (Nullable()) (Nullable()) (Nullable 5_000m) (Nullable()) (Nullable()) (Nullable 50_000m) (Nullable 10_000m)
    let result = RiskInterop.EvaluateOrderNotional(ctx)

    result.Approved |> should equal true

[<Fact>]
let ``Order notional approves when no notional is resolvable`` () =
    let ctx = createPortfolioContext (createOrder OrderSide.Buy 100m) (Nullable()) (Nullable()) (Nullable()) (Nullable()) (Nullable()) (Nullable()) (Nullable 50_000m) (Nullable 10_000m)
    let result = RiskInterop.EvaluateOrderNotional(ctx)

    result.Approved |> should equal true

[<Fact>]
let ``Risk aggregation surfaces escalate decisions`` () =
    let result =
        RiskInterop.Aggregate(
            [|
                { Approved = true; DecisionKind = "approve"; Reasons = [||] }
                { Approved = false; DecisionKind = "escalate"; Reasons = [| "governed approval required" |] }
            |])

    result.Approved |> should equal false
    result.DecisionKind |> should equal "escalate"

[<Fact>]
let ``Risk aggregation returns approve when all decisions approve`` () =
    let result =
        RiskInterop.Aggregate(
            [|
                { Approved = true; DecisionKind = "approve"; Reasons = [||] }
                { Approved = true; DecisionKind = "approve"; Reasons = [||] }
            |])

    result.Approved |> should equal true

[<Fact>]
let ``Promotion policy returns eligible for qualifying backtest`` () =
    let decision = PromotionInterop.EvaluateBacktestPromotion(createBacktestResult 0.8 0.10m 0.15m, 0.5, 0.25m, 0.0m)

    decision.Eligible |> should equal true
    decision.Outcome |> should equal "approved"

[<Fact>]
let ``Promotion policy returns ineligible for material threshold miss`` () =
    let decision = PromotionInterop.EvaluateBacktestPromotion(createBacktestResult 0.2 0.40m -0.05m, 0.5, 0.25m, 0.0m)

    decision.Eligible |> should equal false
    decision.Outcome |> should equal "requires_human_review"
    decision.Reasons.Length |> should equal 3
