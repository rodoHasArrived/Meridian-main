namespace Meridian.FSharp.Canonicalization

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open Meridian.Contracts.Domain.Enums

[<AutoOpen>]
module private Normalization =
    let normalizeProvider (provider: string) =
        if String.IsNullOrWhiteSpace provider then String.Empty
        else provider.ToUpperInvariant()

[<AbstractClass; Sealed>]
type ConditionCodeRules private () =
    static member NormalizeProvider(provider: string) =
        normalizeProvider provider

    static member TryParseCanonicalCondition(name: string, result: outref<CanonicalTradeCondition>) =
        if String.IsNullOrWhiteSpace name then
            result <- CanonicalTradeCondition.Unknown
            false
        else
            if Enum.TryParse<CanonicalTradeCondition>(name, true, &result) then
                true
            else
                result <- CanonicalTradeCondition.Unknown
                false

    static member MapSingle(
        mappings: IReadOnlyDictionary<ValueTuple<string, string>, CanonicalTradeCondition>,
        provider: string,
        rawCode: string)
        : CanonicalTradeCondition =
        if isNull rawCode then
            CanonicalTradeCondition.Unknown
        else
            let key = struct (normalizeProvider provider, rawCode)
            match mappings.TryGetValue key with
            | true, mapped -> mapped
            | false, _ -> CanonicalTradeCondition.Unknown

    static member MapConditions(
        mappings: IReadOnlyDictionary<ValueTuple<string, string>, CanonicalTradeCondition>,
        provider: string,
        rawConditions: string array)
        : CanonicalTradeCondition array =
        if isNull rawConditions || rawConditions.Length = 0 then
            Array.empty
        else
            let normalizedProvider = normalizeProvider provider
            rawConditions
            |> Array.map (fun rawCode ->
                if isNull rawCode then
                    CanonicalTradeCondition.Unknown
                else
                    let key = struct (normalizedProvider, rawCode)
                    match mappings.TryGetValue key with
                    | true, mapped -> mapped
                    | false, _ -> CanonicalTradeCondition.Unknown)

    static member IsHaltCondition(condition: CanonicalTradeCondition) =
        match condition with
        | CanonicalTradeCondition.Halted
        | CanonicalTradeCondition.CircuitBreakerLevel1
        | CanonicalTradeCondition.CircuitBreakerLevel2
        | CanonicalTradeCondition.CircuitBreakerLevel3
        | CanonicalTradeCondition.LuldPause
        | CanonicalTradeCondition.RegulatoryHalt
        | CanonicalTradeCondition.IpoHalt -> true
        | _ -> false

    static member ContainsHaltCondition(conditions: CanonicalTradeCondition array) =
        conditions |> Array.exists ConditionCodeRules.IsHaltCondition

    static member IsResumedCondition(condition: CanonicalTradeCondition) =
        condition = CanonicalTradeCondition.TradingResumed

[<AbstractClass; Sealed>]
type VenueMappingRules private () =
    static member NormalizeProvider(provider: string) =
        normalizeProvider provider

    static member NormalizeVenue(rawVenue: string) =
        if String.IsNullOrWhiteSpace rawVenue then String.Empty
        else rawVenue.ToUpperInvariant()

    [<return: MaybeNull>]
    static member TryMapVenue(
        mappings: IReadOnlyDictionary<ValueTuple<string, string>, string>,
        [<AllowNull>] rawVenue: string,
        provider: string)
        : string =
        if String.IsNullOrEmpty rawVenue then
            null
        else
            let normalizedProvider = normalizeProvider provider
            let directKey = struct (normalizedProvider, rawVenue)
            match mappings.TryGetValue directKey with
            | true, mic -> mic
            | false, _ ->
                let upperVenue = rawVenue.ToUpperInvariant()
                if String.Equals(upperVenue, rawVenue, StringComparison.Ordinal) then
                    null
                else
                    let fallbackKey = struct (normalizedProvider, upperVenue)
                    match mappings.TryGetValue fallbackKey with
                    | true, mic -> mic
                    | false, _ -> null
