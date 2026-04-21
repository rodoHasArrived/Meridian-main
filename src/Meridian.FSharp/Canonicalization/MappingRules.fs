namespace Meridian.FSharp.Canonicalization

open System
open System.Collections.Generic
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Text.Json
open Meridian.Contracts.Domain.Enums

[<CLIMutable>]
type CanonicalMappingTable<'T> =
    { Version: int
      Map: IReadOnlyDictionary<ValueTuple<string, string>, 'T> }

[<AutoOpen>]
module private Normalization =
    let normalizeProvider (provider: string) =
        if String.IsNullOrWhiteSpace provider then String.Empty
        else provider.ToUpperInvariant()

    let normalizeKey provider raw =
        struct (normalizeProvider provider, raw)

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

    static member NormalizeKey(provider: string, rawCode: string) =
        normalizeKey provider rawCode

    static member LoadMappingTableFromJson(json: string) : CanonicalMappingTable<CanonicalTradeCondition> =
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        let mutable vProp = Unchecked.defaultof<JsonElement>
        let version =
            if root.TryGetProperty("version", &vProp) then
                vProp.GetInt32()
            else
                0

        let dict = Dictionary<ValueTuple<string, string>, CanonicalTradeCondition>()

        let mutable mappingsProp = Unchecked.defaultof<JsonElement>
        if root.TryGetProperty("mappings", &mappingsProp) then
            for providerProp in mappingsProp.EnumerateObject() do
                let provider = normalizeProvider providerProp.Name
                for codeProp in providerProp.Value.EnumerateObject() do
                    match codeProp.Value.GetString() with
                    | null -> ()
                    | canonicalName ->
                        let mutable parsed = CanonicalTradeCondition.Unknown
                        if ConditionCodeRules.TryParseCanonicalCondition(canonicalName, &parsed) then
                            dict[struct (provider, codeProp.Name)] <- parsed

        { Version = version
          Map = dict :> IReadOnlyDictionary<_, _> }

    static member LoadMappingTableFromFile(path: string) : CanonicalMappingTable<CanonicalTradeCondition> =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "Mapping file path cannot be empty."

        let json = File.ReadAllText(path)
        ConditionCodeRules.LoadMappingTableFromJson(json)

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

    static member NormalizeKey(provider: string, rawVenue: string) =
        normalizeKey provider rawVenue

    static member LoadMappingTableFromJson(json: string) : CanonicalMappingTable<string> =
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        let mutable vProp = Unchecked.defaultof<JsonElement>
        let version =
            if root.TryGetProperty("version", &vProp) then
                vProp.GetInt32()
            else
                0

        let dict = Dictionary<ValueTuple<string, string>, string>()

        let mutable mappingsProp = Unchecked.defaultof<JsonElement>
        if root.TryGetProperty("mappings", &mappingsProp) then
            for providerProp in mappingsProp.EnumerateObject() do
                let provider = normalizeProvider providerProp.Name
                for venueProp in providerProp.Value.EnumerateObject() do
                    match venueProp.Value.ValueKind with
                    | JsonValueKind.Null -> dict[struct (provider, venueProp.Name)] <- null
                    | _ ->
                        match venueProp.Value.GetString() with
                        | null -> ()
                        | mic -> dict[struct (provider, venueProp.Name)] <- mic

        { Version = version
          Map = dict :> IReadOnlyDictionary<_, _> }

    static member LoadMappingTableFromFile(path: string) : CanonicalMappingTable<string> =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "Mapping file path cannot be empty."

        let json = File.ReadAllText(path)
        VenueMappingRules.LoadMappingTableFromJson(json)
