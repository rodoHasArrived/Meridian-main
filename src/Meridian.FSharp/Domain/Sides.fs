/// Side and aggressor types for market data events.
/// These discriminated unions provide exhaustive pattern matching
/// and eliminate null reference exceptions.
module Meridian.FSharp.Domain.Sides

open System

/// Represents the side of an order in the order book.
[<RequireQualifiedAccess>]
type Side =
    | Buy
    | Sell

    /// Convert to integer for C# interop (0 = Buy, 1 = Sell)
    member this.ToInt() =
        match this with
        | Buy -> 0
        | Sell -> 1

    /// Create from integer for C# interop
    static member FromInt(value: int) =
        match value with
        | 0 -> Buy
        | 1 -> Sell
        | _ -> invalidArg "value" $"Invalid Side value: {value}"

/// Represents the aggressor side of a trade.
/// The aggressor is the party that initiated the trade by
/// crossing the spread (hitting the bid or lifting the offer).
[<RequireQualifiedAccess>]
type AggressorSide =
    /// Trade aggressor could not be determined
    | Unknown
    /// Buyer was the aggressor (lifted the offer)
    | Buyer
    /// Seller was the aggressor (hit the bid)
    | Seller

    /// Convert to integer for C# interop (0 = Unknown, 1 = Buyer, 2 = Seller)
    member this.ToInt() =
        match this with
        | Unknown -> 0
        | Buyer -> 1
        | Seller -> 2

    /// Create from integer for C# interop
    static member FromInt(value: int) =
        match value with
        | 0 -> Unknown
        | 1 -> Buyer
        | 2 -> Seller
        | _ -> Unknown // Default to Unknown for invalid values

/// Infer the aggressor side from trade price relative to BBO (Best Bid/Offer).
/// - If trade price >= ask price, buyer is aggressor (lifted the offer)
/// - If trade price <= bid price, seller is aggressor (hit the bid)
/// - Otherwise, aggressor is unknown (trade inside spread or no BBO data)
[<CompiledName("InferAggressorFromBBO")>]
let inferAggressor (tradePrice: decimal) (bidPrice: decimal option) (askPrice: decimal option) : AggressorSide =
    match bidPrice, askPrice with
    | Some bid, Some ask when tradePrice >= ask -> AggressorSide.Buyer
    | Some bid, Some ask when tradePrice <= bid -> AggressorSide.Seller
    | _ -> AggressorSide.Unknown
