namespace Meridian.FSharp.Domain

open System

type SecurityEconomicDefinition = {
    SecurityId: SecurityId
    Classification: SecurityClassification
    Common: CommonTerms
    Terms: SecurityTermModules
    Identifiers: Identifier list
    Status: SecurityStatus
    Version: int64
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    Provenance: Provenance
}

[<RequireQualifiedAccess>]
module SecurityEconomicDefinition =
    let assetClass (definition: SecurityEconomicDefinition) =
        definition.Classification.AssetClass

    let subType (definition: SecurityEconomicDefinition) =
        definition.Classification.SubType

    let normalize (definition: SecurityEconomicDefinition) =
        {
            definition with
                Common = definition.Common |> CommonTerms.withNormalizedCoreFields
                Provenance = definition.Provenance |> Provenance.normalize
        }
