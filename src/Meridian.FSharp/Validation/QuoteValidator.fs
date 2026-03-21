/// Quote event validation using Railway-Oriented Programming.
/// Validates bid/ask prices, sizes, and spread integrity.
module Meridian.FSharp.Validation.QuoteValidator

open System
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Validation.ValidationTypes

/// Configuration for quote validation
type QuoteValidationConfig = {
    /// Maximum allowed price
    MaxPrice: decimal
    /// Maximum allowed size
    MaxSize: int64
    /// Maximum allowed symbol length
    MaxSymbolLength: int
    /// Maximum age for timestamp
    MaxTimestampAge: TimeSpan
    /// Allow zero sizes (some feeds send zero to indicate no liquidity)
    AllowZeroSize: bool
    /// Allow crossed quotes (bid >= ask)
    AllowCrossedQuotes: bool
}

/// Default validation configuration
module QuoteValidationConfig =

    /// Create default configuration
    [<CompiledName("CreateDefault")>]
    let createDefault () = {
        MaxPrice = 1_000_000m
        MaxSize = 100_000_000L
        MaxSymbolLength = 20
        MaxTimestampAge = TimeSpan.FromMinutes(5.0)
        AllowZeroSize = true
        AllowCrossedQuotes = false
    }

    /// Create strict configuration for real-time validation
    [<CompiledName("CreateStrict")>]
    let createStrict () = {
        MaxPrice = 1_000_000m
        MaxSize = 10_000_000L
        MaxSymbolLength = 10
        MaxTimestampAge = TimeSpan.FromSeconds(5.0)
        AllowZeroSize = false
        AllowCrossedQuotes = false
    }

    /// Create lenient configuration for historical data
    [<CompiledName("CreateHistorical")>]
    let createHistorical () = {
        MaxPrice = 10_000_000m
        MaxSize = 1_000_000_000L
        MaxSymbolLength = 20
        MaxTimestampAge = TimeSpan.FromDays(365.0 * 50.0)
        AllowZeroSize = true
        AllowCrossedQuotes = true // Historical data may have stale crossed quotes
    }

/// Validate bid size
let private validateBidSize (config: QuoteValidationConfig) (size: int64) : ValidationResult<int64> =
    if size < 0L then
        Error [ValidationError.InvalidQuantity(size, "Bid size cannot be negative")]
    elif not config.AllowZeroSize && size = 0L then
        Error [ValidationError.InvalidQuantity(size, "Bid size cannot be zero")]
    elif size > config.MaxSize then
        Error [ValidationError.InvalidQuantity(size, $"Bid size exceeds maximum of {config.MaxSize}")]
    else
        Ok size

/// Validate ask size
let private validateAskSize (config: QuoteValidationConfig) (size: int64) : ValidationResult<int64> =
    if size < 0L then
        Error [ValidationError.InvalidQuantity(size, "Ask size cannot be negative")]
    elif not config.AllowZeroSize && size = 0L then
        Error [ValidationError.InvalidQuantity(size, "Ask size cannot be zero")]
    elif size > config.MaxSize then
        Error [ValidationError.InvalidQuantity(size, $"Ask size exceeds maximum of {config.MaxSize}")]
    else
        Ok size

/// Validate a quote event with the given configuration.
[<CompiledName("ValidateQuote")>]
let validateQuote (config: QuoteValidationConfig) (quote: QuoteEvent) : ValidationResult<QuoteEvent> =

    // Validate individual fields
    let symbolResult = Validate.symbol config.MaxSymbolLength quote.Symbol
    let bidPriceResult = Validate.price config.MaxPrice quote.BidPrice
    let askPriceResult = Validate.price config.MaxPrice quote.AskPrice
    let bidSizeResult = validateBidSize config quote.BidSize
    let askSizeResult = validateAskSize config quote.AskSize

    // Validate spread (bid < ask)
    let spreadResult =
        if config.AllowCrossedQuotes then
            Ok (quote.BidPrice, quote.AskPrice)
        else
            Validate.spread quote.BidPrice quote.AskPrice

    // Combine all validations using applicative style
    let createValidatedQuote sym bidP askP bidS askS _ =
        { quote with
            Symbol = sym
            BidPrice = bidP
            AskPrice = askP
            BidSize = bidS
            AskSize = askS }

    createValidatedQuote
    <!> symbolResult
    <*> bidPriceResult
    <*> askPriceResult
    <*> bidSizeResult
    <*> askSizeResult
    <*> spreadResult

/// Validate a quote event with default configuration.
[<CompiledName("ValidateQuoteDefault")>]
let validateQuoteDefault (quote: QuoteEvent) : ValidationResult<QuoteEvent> =
    validateQuote (QuoteValidationConfig.createDefault()) quote

/// Quick validation that just checks if a quote is valid.
[<CompiledName("IsValidQuote")>]
let isValidQuote (quote: QuoteEvent) : bool =
    match validateQuoteDefault quote with
    | Ok _ -> true
    | Error _ -> false

/// Check if a quote has a valid (positive) spread.
[<CompiledName("HasValidSpread")>]
let hasValidSpread (quote: QuoteEvent) : bool =
    quote.BidPrice < quote.AskPrice && quote.BidPrice > 0m && quote.AskPrice > 0m

/// Validate a batch of quotes, returning all valid quotes and accumulated errors.
[<CompiledName("ValidateQuotes")>]
let validateQuotes (config: QuoteValidationConfig) (quotes: QuoteEvent list) : QuoteEvent list * ValidationError list =
    let mutable allErrors = []
    let validQuotes =
        quotes
        |> List.choose (fun quote ->
            match validateQuote config quote with
            | Ok q -> Some q
            | Error errors ->
                allErrors <- allErrors @ errors
                None)
    (validQuotes, allErrors)

/// Create a validated quote from raw values.
[<CompiledName("CreateValidatedQuote")>]
let createValidatedQuote
    (symbol: string)
    (bidPrice: decimal)
    (bidSize: int64)
    (askPrice: decimal)
    (askSize: int64)
    (sequenceNumber: int64)
    (timestamp: DateTimeOffset)
    : ValidationResult<QuoteEvent> =

    let quote = {
        Symbol = symbol
        BidPrice = bidPrice
        BidSize = bidSize
        AskPrice = askPrice
        AskSize = askSize
        SequenceNumber = sequenceNumber
        Timestamp = timestamp
        ExchangeTimestamp = None
        StreamId = None
    }

    validateQuoteDefault quote

/// Calculate the spread from a validated quote.
[<CompiledName("GetSpread")>]
let getSpread (quote: QuoteEvent) : decimal option =
    if quote.BidPrice > 0m && quote.AskPrice > 0m && quote.AskPrice > quote.BidPrice then
        Some (quote.AskPrice - quote.BidPrice)
    else
        None

/// Calculate the mid-price from a validated quote.
[<CompiledName("GetMidPrice")>]
let getMidPrice (quote: QuoteEvent) : decimal option =
    if quote.BidPrice > 0m && quote.AskPrice > 0m then
        Some ((quote.BidPrice + quote.AskPrice) / 2m)
    else
        None
