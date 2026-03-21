/// Trade event validation using Railway-Oriented Programming.
/// Validates all fields of a trade event and accumulates errors.
module Meridian.FSharp.Validation.TradeValidator

open System
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Sides
open Meridian.FSharp.Validation.ValidationTypes

/// Configuration for trade validation
type TradeValidationConfig = {
    /// Maximum allowed price
    MaxPrice: decimal
    /// Maximum allowed quantity
    MaxQuantity: int64
    /// Maximum allowed symbol length
    MaxSymbolLength: int
    /// Maximum age for timestamp
    MaxTimestampAge: TimeSpan
    /// Last seen sequence number (for sequence validation)
    LastSequenceNumber: int64 option
}

/// Default validation configuration
module TradeValidationConfig =

    /// Create default configuration
    [<CompiledName("CreateDefault")>]
    let createDefault () = {
        MaxPrice = 1_000_000m
        MaxQuantity = 100_000_000L
        MaxSymbolLength = 20
        MaxTimestampAge = TimeSpan.FromMinutes(5.0)
        LastSequenceNumber = None
    }

    /// Create configuration for real-time validation (stricter)
    [<CompiledName("CreateRealTime")>]
    let createRealTime lastSeq = {
        MaxPrice = 1_000_000m
        MaxQuantity = 10_000_000L
        MaxSymbolLength = 10
        MaxTimestampAge = TimeSpan.FromSeconds(5.0)
        LastSequenceNumber = Some lastSeq
    }

    /// Create configuration for historical validation (more lenient)
    [<CompiledName("CreateHistorical")>]
    let createHistorical () = {
        MaxPrice = 10_000_000m
        MaxQuantity = 1_000_000_000L
        MaxSymbolLength = 20
        MaxTimestampAge = TimeSpan.FromDays(365.0 * 50.0) // 50 years
        LastSequenceNumber = None
    }

/// Validate a trade event with the given configuration.
/// Uses applicative style to accumulate all validation errors.
[<CompiledName("ValidateTrade")>]
let validateTrade (config: TradeValidationConfig) (trade: TradeEvent) : ValidationResult<TradeEvent> =

    // Validate individual fields
    let symbolResult = Validate.symbol config.MaxSymbolLength trade.Symbol
    let priceResult = Validate.price config.MaxPrice trade.Price
    let quantityResult = Validate.quantity config.MaxQuantity trade.Quantity

    // Sequence validation (optional based on config)
    let sequenceResult =
        match config.LastSequenceNumber with
        | Some lastSeq -> Validate.sequence lastSeq trade.SequenceNumber
        | None -> Ok trade.SequenceNumber

    // Combine all validations using applicative style
    let createValidatedTrade sym price qty seq =
        { trade with
            Symbol = sym
            Price = price
            Quantity = qty
            SequenceNumber = seq }

    createValidatedTrade
    <!> symbolResult
    <*> priceResult
    <*> quantityResult
    <*> sequenceResult

/// Validate a trade event with default configuration.
[<CompiledName("ValidateTradeDefault")>]
let validateTradeDefault (trade: TradeEvent) : ValidationResult<TradeEvent> =
    validateTrade (TradeValidationConfig.createDefault()) trade

/// Validate just the price of a trade.
[<CompiledName("ValidateTradePrice")>]
let validateTradePrice (trade: TradeEvent) : ValidationResult<decimal> =
    Validate.priceDefault trade.Price

/// Validate just the quantity of a trade.
[<CompiledName("ValidateTradeQuantity")>]
let validateTradeQuantity (trade: TradeEvent) : ValidationResult<int64> =
    Validate.quantityDefault trade.Quantity

/// Quick validation that just checks if a trade is valid.
/// Returns true if valid, false otherwise.
[<CompiledName("IsValidTrade")>]
let isValidTrade (trade: TradeEvent) : bool =
    match validateTradeDefault trade with
    | Ok _ -> true
    | Error _ -> false

/// Validate a batch of trades, returning all valid trades and accumulated errors.
[<CompiledName("ValidateTrades")>]
let validateTrades (config: TradeValidationConfig) (trades: TradeEvent list) : TradeEvent list * ValidationError list =
    let mutable allErrors = []
    let validTrades =
        trades
        |> List.choose (fun trade ->
            match validateTrade config trade with
            | Ok t -> Some t
            | Error errors ->
                allErrors <- allErrors @ errors
                None)
    (validTrades, allErrors)

/// Validate a trade and transform it on success.
[<CompiledName("ValidateAndTransform")>]
let validateAndTransform (transform: TradeEvent -> 'T) (trade: TradeEvent) : ValidationResult<'T> =
    validateTradeDefault trade
    |> ValidationResult.map transform

/// Create a validated trade from raw values.
/// This is a smart constructor that ensures only valid trades are created.
[<CompiledName("CreateValidatedTrade")>]
let createValidatedTrade
    (symbol: string)
    (price: decimal)
    (quantity: int64)
    (side: AggressorSide)
    (sequenceNumber: int64)
    (timestamp: DateTimeOffset)
    : ValidationResult<TradeEvent> =

    let trade = {
        Symbol = symbol
        Price = price
        Quantity = quantity
        Side = side
        SequenceNumber = sequenceNumber
        Timestamp = timestamp
        ExchangeTimestamp = None
        StreamId = None
        Venue = None
    }

    validateTradeDefault trade
