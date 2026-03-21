module Meridian.FSharp.Tests.CanonicalizationTests

open System
open System.Collections.Generic
open FsUnit.Xunit
open Xunit
open Meridian.Contracts.Domain.Enums
open Meridian.FSharp.Canonicalization

let createConditionMappings () =
    readOnlyDict [
        struct ("ALPACA", "@"), CanonicalTradeCondition.Regular
        struct ("ALPACA", "T"), CanonicalTradeCondition.FormT_ExtendedHours
        struct ("POLYGON", "29"), CanonicalTradeCondition.SellerInitiated
    ]

let createVenueMappings () =
    readOnlyDict [
        struct ("IB", "ISLAND"), "XNAS"
        struct ("IB", "SMART"), null
        struct ("ALPACA", "V"), "XNAS"
    ]

[<Fact>]
let ``NormalizeProvider uppercases provider names`` () =
    ConditionCodeRules.NormalizeProvider "alpaca" |> should equal "ALPACA"
    VenueMappingRules.NormalizeProvider "Ib" |> should equal "IB"

[<Fact>]
let ``TryParseCanonicalCondition accepts canonical enum names case-insensitively`` () =
    let mutable result = CanonicalTradeCondition.Unknown
    let parsed = ConditionCodeRules.TryParseCanonicalCondition("formt_extendedhours", &result)

    parsed |> should equal true
    result |> should equal CanonicalTradeCondition.FormT_ExtendedHours

[<Fact>]
let ``TryParseCanonicalCondition rejects blank input`` () =
    let mutable result = CanonicalTradeCondition.Regular
    let parsed = ConditionCodeRules.TryParseCanonicalCondition(null, &result)

    parsed |> should equal false
    result |> should equal CanonicalTradeCondition.Unknown

[<Fact>]
let ``MapSingle returns mapped value for known code`` () =
    let mapped = ConditionCodeRules.MapSingle(createConditionMappings(), "alpaca", "@")
    mapped |> should equal CanonicalTradeCondition.Regular

[<Fact>]
let ``MapConditions returns unknown for unmapped codes`` () =
    let mapped = ConditionCodeRules.MapConditions(createConditionMappings(), "alpaca", [| "@"; "ZZZ" |])
    mapped |> should equal [| CanonicalTradeCondition.Regular; CanonicalTradeCondition.Unknown |]

[<Fact>]
let ``ContainsHaltCondition detects halt-like conditions`` () =
    ConditionCodeRules.ContainsHaltCondition([| CanonicalTradeCondition.Regular; CanonicalTradeCondition.LuldPause |]) |> should equal true
    ConditionCodeRules.ContainsHaltCondition([| CanonicalTradeCondition.Regular; CanonicalTradeCondition.OddLot |]) |> should equal false

[<Fact>]
let ``IsResumedCondition only matches resumed state`` () =
    ConditionCodeRules.IsResumedCondition(CanonicalTradeCondition.TradingResumed) |> should equal true
    ConditionCodeRules.IsResumedCondition(CanonicalTradeCondition.Regular) |> should equal false

[<Fact>]
let ``TryMapVenue supports uppercase fallback without altering direct hits`` () =
    let mappings = createVenueMappings()

    VenueMappingRules.TryMapVenue(mappings, "island", "ib") |> should equal "XNAS"
    VenueMappingRules.TryMapVenue(mappings, "SMART", "IB") |> should equal null
    VenueMappingRules.TryMapVenue(mappings, "missing", "IB") |> should equal null
