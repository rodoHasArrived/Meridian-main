/// Core validation types for Railway-Oriented Programming.
/// Provides Result-based validation with composable error handling.
module Meridian.FSharp.Validation.ValidationTypes

open System

/// Validation error types with associated data.
/// Each error carries context about what went wrong.
[<RequireQualifiedAccess>]
type ValidationError =
    /// Price validation failed
    | InvalidPrice of value: decimal * reason: string
    /// Quantity validation failed
    | InvalidQuantity of value: int64 * reason: string
    /// Symbol validation failed
    | InvalidSymbol of value: string * reason: string
    /// Timestamp is stale
    | StaleTimestamp of age: TimeSpan * maxAge: TimeSpan
    /// Timestamp is in the future
    | FutureTimestamp of timestamp: DateTimeOffset * now: DateTimeOffset
    /// Sequence number validation failed
    | InvalidSequence of current: int64 * last: int64
    /// Bid-ask spread is invalid
    | InvalidSpread of bid: decimal * ask: decimal
    /// Generic validation error
    | Custom of field: string * message: string

    /// Get a human-readable description of the error
    member this.Description =
        match this with
        | InvalidPrice (v, r) -> $"Invalid price {v}: {r}"
        | InvalidQuantity (v, r) -> $"Invalid quantity {v}: {r}"
        | InvalidSymbol (v, r) -> $"Invalid symbol '{v}': {r}"
        | StaleTimestamp (age, max) -> $"Stale timestamp: age {age.TotalSeconds:F1}s exceeds max {max.TotalSeconds:F1}s"
        | FutureTimestamp (ts, now) -> $"Future timestamp: {ts} is after {now}"
        | InvalidSequence (curr, last) -> $"Invalid sequence: {curr} should be after {last}"
        | InvalidSpread (bid, ask) -> $"Invalid spread: bid {bid} >= ask {ask}"
        | Custom (field, msg) -> $"Validation error in {field}: {msg}"

/// Result type alias for validation operations.
/// Ok contains the validated value, Error contains a list of validation errors.
type ValidationResult<'T> = Result<'T, ValidationError list>

/// Applicative functor operators for combining validations.
module ValidationResult =

    /// Map a function over a successful result
    let map f result =
        match result with
        | Ok x -> Ok (f x)
        | Error e -> Error e

    /// Apply a function wrapped in a Result to a value wrapped in a Result
    /// Accumulates errors from both sides
    let apply fResult xResult =
        match fResult, xResult with
        | Ok f, Ok x -> Ok (f x)
        | Error e1, Error e2 -> Error (e1 @ e2)
        | Error e, _ -> Error e
        | _, Error e -> Error e

    /// Bind for monadic composition (fails fast, doesn't accumulate)
    let bind f result =
        match result with
        | Ok x -> f x
        | Error e -> Error e

    /// Lift a value into a successful result
    let ok x = Ok x

    /// Lift an error into a failed result
    let error e = Error [e]

    /// Lift multiple errors into a failed result
    let errors es = Error es

    /// Combine two results, accumulating errors
    let combine r1 r2 =
        match r1, r2 with
        | Ok x1, Ok x2 -> Ok (x1, x2)
        | Error e1, Error e2 -> Error (e1 @ e2)
        | Error e, _ -> Error e
        | _, Error e -> Error e

    /// Traverse a list with a validation function, accumulating all errors
    let traverse f xs =
        let folder acc x =
            match acc, f x with
            | Ok xs, Ok x -> Ok (x :: xs)
            | Error e1, Error e2 -> Error (e1 @ e2)
            | Error e, _ -> Error e
            | _, Error e -> Error e
        xs |> List.fold folder (Ok []) |> map List.rev

    /// Sequence a list of results into a result of list
    let sequence xs = traverse id xs

/// Infix operators for validation composition
[<AutoOpen>]
module ValidationOperators =

    /// Map operator (functor)
    let (<!>) = ValidationResult.map

    /// Apply operator (applicative)
    let (<*>) = ValidationResult.apply

    /// Bind operator (monad, fails fast)
    let (>>=) result f = ValidationResult.bind f result

/// Core validation functions for common field types.
module Validate =

    /// Validate that a price is positive and within reasonable bounds
    [<CompiledName("ValidatePrice")>]
    let price (maxPrice: decimal) (p: decimal) : ValidationResult<decimal> =
        if p <= 0m then
            Error [ValidationError.InvalidPrice(p, "Price must be positive")]
        elif p > maxPrice then
            Error [ValidationError.InvalidPrice(p, $"Price exceeds maximum of {maxPrice}")]
        else
            Ok p

    /// Validate price with default max of 1,000,000
    [<CompiledName("ValidatePriceDefault")>]
    let priceDefault (p: decimal) : ValidationResult<decimal> =
        price 1_000_000m p

    /// Validate that a quantity is non-negative and within bounds
    [<CompiledName("ValidateQuantity")>]
    let quantity (maxQuantity: int64) (q: int64) : ValidationResult<int64> =
        if q < 0L then
            Error [ValidationError.InvalidQuantity(q, "Quantity cannot be negative")]
        elif q > maxQuantity then
            Error [ValidationError.InvalidQuantity(q, $"Quantity exceeds maximum of {maxQuantity}")]
        else
            Ok q

    /// Validate quantity with default max of 10,000,000
    [<CompiledName("ValidateQuantityDefault")>]
    let quantityDefault (q: int64) : ValidationResult<int64> =
        quantity 10_000_000L q

    /// Validate that a symbol is non-empty and within length limits
    [<CompiledName("ValidateSymbol")>]
    let symbol (maxLength: int) (s: string) : ValidationResult<string> =
        if String.IsNullOrWhiteSpace s then
            Error [ValidationError.InvalidSymbol(s, "Symbol cannot be empty")]
        elif s.Length > maxLength then
            Error [ValidationError.InvalidSymbol(s, $"Symbol exceeds maximum length of {maxLength}")]
        else
            Ok s

    /// Validate symbol with default max length of 20
    [<CompiledName("ValidateSymbolDefault")>]
    let symbolDefault (s: string) : ValidationResult<string> =
        symbol 20 s

    /// Validate that a timestamp is not stale
    [<CompiledName("ValidateTimestamp")>]
    let timestamp (maxAge: TimeSpan) (ts: DateTimeOffset) : ValidationResult<DateTimeOffset> =
        let now = DateTimeOffset.UtcNow
        let age = now - ts
        if age > maxAge then
            Error [ValidationError.StaleTimestamp(age, maxAge)]
        elif ts > now.AddSeconds(1.0) then // Allow 1 second clock skew
            Error [ValidationError.FutureTimestamp(ts, now)]
        else
            Ok ts

    /// Validate timestamp with default max age of 5 seconds
    [<CompiledName("ValidateTimestampDefault")>]
    let timestampDefault (ts: DateTimeOffset) : ValidationResult<DateTimeOffset> =
        timestamp (TimeSpan.FromSeconds(5.0)) ts

    /// Validate that a sequence number is greater than the last seen
    [<CompiledName("ValidateSequence")>]
    let sequence (lastSeq: int64) (currentSeq: int64) : ValidationResult<int64> =
        if currentSeq <= lastSeq then
            Error [ValidationError.InvalidSequence(currentSeq, lastSeq)]
        else
            Ok currentSeq

    /// Validate that bid < ask (positive spread)
    [<CompiledName("ValidateSpread")>]
    let spread (bid: decimal) (ask: decimal) : ValidationResult<decimal * decimal> =
        if bid >= ask then
            Error [ValidationError.InvalidSpread(bid, ask)]
        else
            Ok (bid, ask)

    /// Validate with a custom predicate
    [<CompiledName("ValidateCustom")>]
    let custom (field: string) (predicate: 'T -> bool) (message: string) (value: 'T) : ValidationResult<'T> =
        if predicate value then
            Ok value
        else
            Error [ValidationError.Custom(field, message)]
