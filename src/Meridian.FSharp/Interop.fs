/// C# interoperability helpers for the F# domain library.
/// Provides extension methods and adapters for seamless C# consumption.
module Meridian.FSharp.Interop

open System
open System.Runtime.CompilerServices
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Domain.Integrity
open Meridian.FSharp.Validation.ValidationTypes
module Spread = Meridian.FSharp.Calculations.Spread
module Imbalance = Meridian.FSharp.Calculations.Imbalance
module Aggregations = Meridian.FSharp.Calculations.Aggregations
module RiskRules = Meridian.FSharp.Risk.RiskRules
module RiskEvaluation = Meridian.FSharp.Risk.RiskEvaluation
module PromotionPolicy = Meridian.FSharp.Promotion.PromotionPolicy
module PromotionTypes = Meridian.FSharp.Promotion.PromotionTypes
open Meridian.FSharp.Risk.RiskTypes
open Meridian.Backtesting.Sdk

/// Private helper functions for option conversion (for F# internal use).
/// These are needed because [<Extension>] methods only work for C# consumers.
[<AutoOpen>]
module private OptionHelpers =
    /// Convert Option<T> to Nullable<T> for value types.
    let inline toNullable (opt: 'T option) : Nullable<'T> =
        match opt with
        | Some v -> Nullable v
        | None -> Nullable()

    /// Convert Option<T> to T or null/default for reference types.
    let inline toNullableRef (opt: 'T option) : 'T =
        match opt with
        | Some v -> v
        | None -> Unchecked.defaultof<'T>

/// Extension methods for Option types to work with C# nullable types.
[<Extension>]
type OptionExtensions =

    /// Convert Option<T> to Nullable<T> for value types.
    [<Extension>]
    static member ToNullable(opt: 'T option) : Nullable<'T> =
        toNullable opt

    /// Convert Option<T> to T or null for reference types.
    [<Extension>]
    static member ToNullableRef(opt: 'T option) : 'T =
        toNullableRef opt

    /// Convert Option<T> to T or a default value.
    [<Extension>]
    static member GetValueOrDefault(opt: 'T option, defaultValue: 'T) : 'T =
        Option.defaultValue defaultValue opt

    /// Check if Option has a value.
    [<Extension>]
    static member HasValue(opt: 'T option) : bool =
        Option.isSome opt

/// C#-friendly wrapper for trade events.
[<Sealed>]
type TradeEventWrapper(trade: TradeEvent) =

    member _.Symbol = trade.Symbol
    member _.Price = trade.Price
    member _.Quantity = trade.Quantity
    member _.Side = trade.Side.ToInt()
    member _.SequenceNumber = trade.SequenceNumber
    member _.Timestamp = trade.Timestamp
    member _.ExchangeTimestamp = toNullable trade.ExchangeTimestamp
    member _.StreamId = toNullableRef trade.StreamId
    member _.Venue = toNullableRef trade.Venue

    member _.ToFSharpEvent() = trade

    static member FromFSharp(trade: TradeEvent) = TradeEventWrapper(trade)

    static member Create(symbol, price, quantity, side, sequenceNumber, timestamp) =
        TradeEventWrapper({
            Symbol = symbol
            Price = price
            Quantity = quantity
            Side = AggressorSide.FromInt(side)
            SequenceNumber = sequenceNumber
            Timestamp = timestamp
            ExchangeTimestamp = None
            StreamId = None
            Venue = None
        })

/// C#-friendly wrapper for quote events.
[<Sealed>]
type QuoteEventWrapper(quote: QuoteEvent) =

    member _.Symbol = quote.Symbol
    member _.BidPrice = quote.BidPrice
    member _.BidSize = quote.BidSize
    member _.AskPrice = quote.AskPrice
    member _.AskSize = quote.AskSize
    member _.SequenceNumber = quote.SequenceNumber
    member _.Timestamp = quote.Timestamp
    member _.ExchangeTimestamp = toNullable quote.ExchangeTimestamp

    member _.ToFSharpEvent() = quote

    static member FromFSharp(quote: QuoteEvent) = QuoteEventWrapper(quote)

    static member Create(symbol, bidPrice, bidSize, askPrice, askSize, sequenceNumber, timestamp) =
        QuoteEventWrapper({
            Symbol = symbol
            BidPrice = bidPrice
            BidSize = bidSize
            AskPrice = askPrice
            AskSize = askSize
            SequenceNumber = sequenceNumber
            Timestamp = timestamp
            ExchangeTimestamp = None
            StreamId = None
        })

/// C#-friendly validation result.
[<Sealed>]
type ValidationResultWrapper<'T>(result: ValidationResult<'T>) =

    member _.IsSuccess =
        match result with
        | Ok _ -> true
        | Error _ -> false

    member _.Value =
        match result with
        | Ok v -> v
        | Error _ -> Unchecked.defaultof<'T>

    member _.Errors =
        match result with
        | Ok _ -> [||]
        | Error errors -> errors |> List.map (fun e -> e.Description) |> List.toArray

    member _.ErrorDetails =
        match result with
        | Ok _ -> [||]
        | Error errors -> errors |> List.toArray

/// C#-friendly spread calculator.
[<Sealed>]
type SpreadCalculator private () =

    static member Calculate(bidPrice: decimal, askPrice: decimal) : Nullable<decimal> =
        toNullable (Spread.calculate bidPrice askPrice)

    static member MidPrice(bidPrice: decimal, askPrice: decimal) : Nullable<decimal> =
        toNullable (Spread.midPrice bidPrice askPrice)

    static member SpreadBps(bidPrice: decimal, askPrice: decimal) : Nullable<decimal> =
        toNullable (Spread.spreadBps bidPrice askPrice)

    static member FromQuote(quote: QuoteEvent) : Nullable<decimal> =
        toNullable (Spread.fromQuote quote)

    static member MidPriceFromQuote(quote: QuoteEvent) : Nullable<decimal> =
        toNullable (Spread.midPriceFromQuote quote)

/// C#-friendly imbalance calculator.
[<Sealed>]
type ImbalanceCalculator private () =

    static member Calculate(bidQuantity: int64, askQuantity: int64) : Nullable<decimal> =
        toNullable (Imbalance.calculate bidQuantity askQuantity)

    static member FromQuote(quote: QuoteEvent) : Nullable<decimal> =
        toNullable (Imbalance.fromQuote quote)

    static member Microprice(book: OrderBookSnapshot) : Nullable<decimal> =
        toNullable (Imbalance.microprice book)

/// C#-friendly aggregation functions.
[<Sealed>]
type AggregationFunctions private () =

    static member Vwap(trades: TradeEvent seq) : Nullable<decimal> =
        toNullable (Aggregations.vwap trades)

    static member Twap(trades: TradeEvent seq) : Nullable<decimal> =
        toNullable (Aggregations.twap trades)

    static member TotalVolume(trades: TradeEvent seq) : int64 =
        Aggregations.totalVolume trades

    static member OrderFlowImbalance(trades: TradeEvent seq) : int64 =
        Aggregations.orderFlowImbalance trades

    static member VolumeBreakdown(trades: TradeEvent seq) : Aggregations.VolumeBreakdown =
        Aggregations.volumeBreakdown trades

/// C#-friendly trade validator.
[<Sealed>]
type TradeValidator private () =

    static member Validate(trade: TradeEvent) : ValidationResultWrapper<TradeEvent> =
        ValidationResultWrapper(Validation.TradeValidator.validateTradeDefault trade)

    static member IsValid(trade: TradeEvent) : bool =
        Validation.TradeValidator.isValidTrade trade

    static member ValidateWithConfig(trade: TradeEvent, config: Validation.TradeValidator.TradeValidationConfig) =
        ValidationResultWrapper(Validation.TradeValidator.validateTrade config trade)

    static member ValidateHistorical(trade: TradeEvent) : ValidationResultWrapper<TradeEvent> =
        ValidationResultWrapper(Validation.TradeValidator.validateTrade (Validation.TradeValidator.TradeValidationConfig.createHistorical()) trade)

/// C#-friendly quote validator.
[<Sealed>]
type QuoteValidator private () =

    static member Validate(quote: QuoteEvent) : ValidationResultWrapper<QuoteEvent> =
        ValidationResultWrapper(Validation.QuoteValidator.validateQuoteDefault quote)

    static member IsValid(quote: QuoteEvent) : bool =
        Validation.QuoteValidator.isValidQuote quote

    static member HasValidSpread(quote: QuoteEvent) : bool =
        Validation.QuoteValidator.hasValidSpread quote

    static member ValidateWithConfig(quote: QuoteEvent, config: Validation.QuoteValidator.QuoteValidationConfig) =
        ValidationResultWrapper(Validation.QuoteValidator.validateQuote config quote)

    static member ValidateHistorical(quote: QuoteEvent) : ValidationResultWrapper<QuoteEvent> =
        ValidationResultWrapper(Validation.QuoteValidator.validateQuote (Validation.QuoteValidator.QuoteValidationConfig.createHistorical()) quote)

/// C#-friendly aggressor inference.
[<Sealed>]
type AggressorInference private () =

    static member Infer(tradePrice: decimal, bidPrice: Nullable<decimal>, askPrice: Nullable<decimal>) : int =
        let bidOpt = if bidPrice.HasValue then Some bidPrice.Value else None
        let askOpt = if askPrice.HasValue then Some askPrice.Value else None
        (inferAggressor tradePrice bidOpt askOpt).ToInt()

    static member InferFromQuote(tradePrice: decimal, quote: QuoteEvent) : int =
        (inferAggressor tradePrice (Some quote.BidPrice) (Some quote.AskPrice)).ToInt()

[<CLIMutable>]
type RiskDecisionDto = {
    Approved: bool
    DecisionKind: string
    Reasons: string array
}

[<CLIMutable>]
type PromotionDecisionDto = {
    Eligible: bool
    Outcome: string
    RequiredManualOverrideKind: string
    Reasons: string array
}

[<Sealed>]
type RiskInterop private () =

    static member private ToDto(decision: RiskDecision) =
        match decision with
        | Approve ->
            { Approved = true
              DecisionKind = "approve"
              Reasons = [||] }
        | Reject reason ->
            { Approved = false
              DecisionKind = "reject"
              Reasons = [| reason |] }
        | Escalate reason ->
            { Approved = false
              DecisionKind = "escalate"
              Reasons = [| reason |] }

    static member CreateContext(
        request: Meridian.Execution.Sdk.OrderRequest,
        currentPositionQuantity: decimal,
        maxPositionSize: Nullable<decimal>,
        portfolioValue: Nullable<decimal>,
        initialCapital: Nullable<decimal>,
        maxDrawdownPercent: Nullable<decimal>) : RiskContext =
        {
            Request = request
            CurrentPositionQuantity = currentPositionQuantity
            MaxPositionSize = if maxPositionSize.HasValue then Some maxPositionSize.Value else None
            PortfolioValue = if portfolioValue.HasValue then Some portfolioValue.Value else None
            InitialCapital = if initialCapital.HasValue then Some initialCapital.Value else None
            MaxDrawdownPercent = if maxDrawdownPercent.HasValue then Some maxDrawdownPercent.Value else None
            RecentOrderRate = None
            PortfolioExposure = None
        }

    static member EvaluatePositionLimit(ctx: RiskContext) : RiskDecisionDto =
        RiskRules.positionLimit ctx |> RiskInterop.ToDto

    static member EvaluateDrawdownCircuitBreaker(ctx: RiskContext) : RiskDecisionDto =
        RiskRules.drawdownCircuitBreaker ctx |> RiskInterop.ToDto

    static member Aggregate(decisions: seq<RiskDecisionDto>) : RiskDecisionDto =
        let unionDecisions =
            decisions
            |> Seq.map (fun decision ->
                if decision.Approved then Approve
                elif StringComparer.OrdinalIgnoreCase.Equals(decision.DecisionKind, "escalate") then
                    Escalate (decision.Reasons |> Array.tryHead |> Option.defaultValue "Escalated for manual review.")
                else
                    Reject (decision.Reasons |> Array.tryHead |> Option.defaultValue "Rejected by policy."))

        RiskEvaluation.aggregate unionDecisions |> RiskInterop.ToDto

[<Sealed>]
type PromotionInterop private () =

    static member private ToDto(decision: PromotionTypes.PromotionDecision) =
        let outcomeText, requiredOverrideKind =
            match decision.Outcome with
            | PromotionTypes.Approved -> "approved", String.Empty
            | PromotionTypes.RequiresHumanReview -> "requires_human_review", String.Empty
            | PromotionTypes.RequiresManualOverride kind -> "requires_manual_override", kind
            | PromotionTypes.Blocked -> "blocked", String.Empty

        { Eligible = decision.IsEligible
          Outcome = outcomeText
          RequiredManualOverrideKind = requiredOverrideKind
          Reasons = decision.BlockingReasons |> List.toArray }

    static member EvaluateBacktestPromotion(
        result: BacktestResult,
        minSharpeRatio: double,
        maxAllowedDrawdownPercent: decimal,
        minTotalReturn: decimal) : PromotionDecisionDto =
        let policyInput : PromotionPolicy.PromotionPolicyInput =
            {
                IsRunCompleted = true
                HasMetrics = true
                SharpeRatio = result.Metrics.SharpeRatio
                MaxDrawdownPercent = result.Metrics.MaxDrawdownPercent
                TotalReturn = result.Metrics.TotalReturn
                MinSharpeRatio = minSharpeRatio
                MaxAllowedDrawdownPercent = maxAllowedDrawdownPercent
                MinTotalReturn = minTotalReturn
                IsLiveTarget = false
                HasCompleteTrustEvidence = true
                HasFreshTrustEvidence = true
                IsLiveExecutionEnabled = true
                IsCircuitBreakerOpen = false
                HasConflictingOverride = false
                HasActiveLivePromotionOverride = true
                RequiredManualOverrideKind = String.Empty
            }

        PromotionPolicy.evaluatePolicy policyInput
        |> PromotionInterop.ToDto

    static member EvaluatePromotionPolicy(input: PromotionPolicy.PromotionPolicyInput) : PromotionDecisionDto =
        PromotionPolicy.evaluatePolicy input
        |> PromotionInterop.ToDto
