module Meridian.FSharp.Tests.RiskPolicyTests

open System
open System.Collections.Generic
open Xunit
open FsCheck.Xunit
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

[<Property(MaxTest = 200)>]
let ``Scenario_RiskPositionLimit_GeneratedLargerExposureNeverTurnsRejectIntoApprove`` currentSeed maxSeed excessSeed extraSeed =
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
