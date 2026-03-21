/// Validation pipeline for composing multiple validators.
/// Provides a fluent API for building validation workflows.
module Meridian.FSharp.Validation.ValidationPipeline

open System
open Meridian.FSharp.Domain.MarketEvents
open Meridian.FSharp.Domain.Integrity
open Meridian.FSharp.Validation.ValidationTypes
open Meridian.FSharp.Validation.TradeValidator
open Meridian.FSharp.Validation.QuoteValidator

/// Result of validating a market event.
type ValidatedEvent<'T> = {
    /// The validated event (if successful)
    Event: 'T option
    /// List of validation errors (if any)
    Errors: ValidationError list
    /// Original timestamp
    Timestamp: DateTimeOffset
    /// Validation duration
    ValidationDuration: TimeSpan
}

/// Pipeline stage for validation
type ValidationStage<'TIn, 'TOut> = 'TIn -> ValidationResult<'TOut>

/// Validation pipeline builder
type ValidationPipeline<'T> = {
    Stages: ('T -> ValidationResult<'T>) list
}

/// Pipeline operations
module ValidationPipeline =

    /// Create an empty pipeline
    [<CompiledName("Create")>]
    let create<'T> () : ValidationPipeline<'T> = { Stages = [] }

    /// Add a validation stage to the pipeline
    [<CompiledName("AddStage")>]
    let addStage (stage: 'T -> ValidationResult<'T>) (pipeline: ValidationPipeline<'T>) : ValidationPipeline<'T> =
        { Stages = pipeline.Stages @ [stage] }

    /// Add a predicate-based validation stage
    [<CompiledName("AddPredicate")>]
    let addPredicate (predicate: 'T -> bool) (errorFactory: 'T -> ValidationError) (pipeline: ValidationPipeline<'T>) : ValidationPipeline<'T> =
        let stage value =
            if predicate value then Ok value
            else Error [errorFactory value]
        addStage stage pipeline

    /// Run the pipeline on a value
    [<CompiledName("Run")>]
    let run (pipeline: ValidationPipeline<'T>) (value: 'T) : ValidationResult<'T> =
        pipeline.Stages
        |> List.fold (fun acc stage ->
            match acc with
            | Ok v -> stage v
            | Error e -> Error e) (Ok value)

    /// Run the pipeline and collect all errors (doesn't short-circuit)
    [<CompiledName("RunCollectErrors")>]
    let runCollectErrors (pipeline: ValidationPipeline<'T>) (value: 'T) : ValidationResult<'T> =
        let results = pipeline.Stages |> List.map (fun stage -> stage value)
        let errors = results |> List.collect (function Error e -> e | Ok _ -> [])
        if List.isEmpty errors then Ok value
        else Error errors

/// Market event validation pipeline
module MarketEventValidation =

    /// Validate a trade event and wrap result
    [<CompiledName("ValidateTradeEvent")>]
    let validateTradeEvent (config: TradeValidationConfig) (trade: TradeEvent) : ValidatedEvent<TradeEvent> =
        let startTime = DateTimeOffset.UtcNow
        let result = validateTrade config trade
        let endTime = DateTimeOffset.UtcNow

        match result with
        | Ok t ->
            { Event = Some t
              Errors = []
              Timestamp = trade.Timestamp
              ValidationDuration = endTime - startTime }
        | Error errors ->
            { Event = None
              Errors = errors
              Timestamp = trade.Timestamp
              ValidationDuration = endTime - startTime }

    /// Validate a quote event and wrap result
    [<CompiledName("ValidateQuoteEvent")>]
    let validateQuoteEvent (config: QuoteValidationConfig) (quote: QuoteEvent) : ValidatedEvent<QuoteEvent> =
        let startTime = DateTimeOffset.UtcNow
        let result = validateQuote config quote
        let endTime = DateTimeOffset.UtcNow

        match result with
        | Ok q ->
            { Event = Some q
              Errors = []
              Timestamp = quote.Timestamp
              ValidationDuration = endTime - startTime }
        | Error errors ->
            { Event = None
              Errors = errors
              Timestamp = quote.Timestamp
              ValidationDuration = endTime - startTime }

    /// Validate any market event
    [<CompiledName("ValidateMarketEvent")>]
    let validateMarketEvent (event: MarketEvent) : ValidationResult<MarketEvent> =
        match event with
        | MarketEvent.Trade trade ->
            validateTradeDefault trade
            |> ValidationResult.map MarketEvent.Trade
        | MarketEvent.Quote quote ->
            validateQuoteDefault quote
            |> ValidationResult.map MarketEvent.Quote
        | other -> Ok other // Other events pass through without validation

    /// Filter a sequence of events, keeping only valid ones
    [<CompiledName("FilterValid")>]
    let filterValid (events: MarketEvent seq) : MarketEvent seq =
        events
        |> Seq.choose (fun event ->
            match validateMarketEvent event with
            | Ok e -> Some e
            | Error _ -> None)

    /// Partition events into valid and invalid
    [<CompiledName("PartitionByValidity")>]
    let partitionByValidity (events: MarketEvent list) : MarketEvent list * (MarketEvent * ValidationError list) list =
        let valid, invalid =
            events
            |> List.fold (fun (valid, invalid) event ->
                match validateMarketEvent event with
                | Ok e -> (e :: valid, invalid)
                | Error errors -> (valid, (event, errors) :: invalid)
            ) ([], [])
        (List.rev valid, List.rev invalid)

    /// Convert validation errors to integrity events
    [<CompiledName("ErrorsToIntegrityEvents")>]
    let errorsToIntegrityEvents (symbol: string) (timestamp: DateTimeOffset) (seqNum: int64) (errors: ValidationError list) : IntegrityEvent list =
        errors
        |> List.map (fun error ->
            { Symbol = symbol
              Timestamp = timestamp
              Severity = IntegritySeverity.Warning
              EventType = IntegrityEventType.DataQualityIssue(error.Description)
              SequenceNumber = seqNum
              StreamId = None
              Venue = None })

/// Fluent API for building validation pipelines
type TradeValidationPipelineBuilder() =
    let mutable stages: (TradeEvent -> ValidationResult<TradeEvent>) list = []

    /// Add price validation
    member this.WithPriceValidation(maxPrice: decimal) =
        stages <- stages @ [fun t ->
            Validate.price maxPrice t.Price
            |> ValidationResult.map (fun _ -> t)]
        this

    /// Add quantity validation
    member this.WithQuantityValidation(maxQty: int64) =
        stages <- stages @ [fun t ->
            Validate.quantity maxQty t.Quantity
            |> ValidationResult.map (fun _ -> t)]
        this

    /// Add symbol validation
    member this.WithSymbolValidation(maxLen: int) =
        stages <- stages @ [fun t ->
            Validate.symbol maxLen t.Symbol
            |> ValidationResult.map (fun _ -> t)]
        this

    /// Add custom validation
    member this.WithCustomValidation(validator: TradeEvent -> ValidationResult<TradeEvent>) =
        stages <- stages @ [validator]
        this

    /// Build and run the pipeline
    member this.Validate(trade: TradeEvent) : ValidationResult<TradeEvent> =
        stages
        |> List.fold (fun acc stage ->
            match acc with
            | Ok t -> stage t
            | Error e -> Error e) (Ok trade)

/// Create a new trade validation pipeline builder
[<CompiledName("CreateTradeValidator")>]
let createTradeValidator () = TradeValidationPipelineBuilder()
