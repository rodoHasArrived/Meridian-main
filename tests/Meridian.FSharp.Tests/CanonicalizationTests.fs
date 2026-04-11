module Meridian.FSharp.Tests.CanonicalizationTests

open System
open System.Collections.Generic
open System.IO
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

let private configPath relative =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "config", relative))

let private conditionMappingTable =
    lazy (ConditionCodeRules.LoadMappingTableFromFile(configPath "condition-codes.json"))

let private venueMappingTable =
    lazy (VenueMappingRules.LoadMappingTableFromFile(configPath "venue-mapping.json"))

let private rng seed = Random(seed)

let private randomizeCase (random: Random) (value: string) =
    value
    |> Seq.map (fun ch -> if random.NextDouble() < 0.5 then Char.ToLowerInvariant ch else Char.ToUpperInvariant ch)
    |> Array.ofSeq
    |> String

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

[<Fact>]
let ``TryParseCanonicalCondition handles all enum names with arbitrary casing`` () =
    let random = rng 42
    for name in Enum.GetNames(typeof<CanonicalTradeCondition>) do
        let mutable parsed = CanonicalTradeCondition.Unknown
        let variedCase = randomizeCase random name
        ConditionCodeRules.TryParseCanonicalCondition(variedCase, &parsed) |> should equal true
        parsed |> should equal (Enum.Parse<CanonicalTradeCondition>(name))

[<Fact>]
let ``NormalizeProvider is idempotent and trims whitespace to empty`` () =
    let random = rng 99
    for _ in 1 .. 50 do
        let sample = new string(Array.init 6 (fun _ -> char (random.Next(97, 123))))
        let normalized = ConditionCodeRules.NormalizeProvider(sample)
        ConditionCodeRules.NormalizeProvider(normalized) |> should equal normalized
    ConditionCodeRules.NormalizeProvider("   ") |> should equal String.Empty

[<Fact>]
let ``MapSingle respects provider casing from config mapping table`` () =
    let mappings = conditionMappingTable.Value.Map
    let random = rng 7

    for KeyValue(struct (provider, rawCode), expected) in mappings do
        let providerVariant = randomizeCase random provider
        ConditionCodeRules.MapSingle(mappings, providerVariant, rawCode) |> should equal expected

[<Fact>]
let ``MapConditions aligns with MapSingle for every known mapping`` () =
    let mappings = conditionMappingTable.Value.Map
    let random = rng 21

    for KeyValue(struct (provider, rawCode), expected) in mappings do
        let providerVariant = randomizeCase random provider
        ConditionCodeRules.MapConditions(mappings, providerVariant, [| rawCode |])
        |> should equal [| expected |]

[<Fact>]
let ``Venue mapping accepts mixed casing for providers and venues`` () =
    let mappings = venueMappingTable.Value.Map
    let random = rng 121

    for KeyValue(struct (provider, rawVenue), expectedMic) in mappings do
        let providerVariant = randomizeCase random provider
        let venueVariant = randomizeCase random rawVenue
        VenueMappingRules.TryMapVenue(mappings, venueVariant, providerVariant)
        |> should equal expectedMic

[<Fact>]
let ``Venue mapping returns null for empty or missing venues`` () =
    let mappings = venueMappingTable.Value.Map
    VenueMappingRules.TryMapVenue(mappings, null, "ALPACA") |> should equal null
    VenueMappingRules.TryMapVenue(mappings, "", "ALPACA") |> should equal null
