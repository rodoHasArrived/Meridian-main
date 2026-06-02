namespace Meridian.FSharp.Operations

open System
open Meridian.FSharp.Domain

type SecurityTermValidationIssue = {
    Code: string
    Message: string
}

type SecurityTermValidationConfig = {
    RequireCreditRatingForPrivate: bool
    RequireCovenantsForPrivate: bool
    RequireVenueForPrivate: bool
    RequireStructuredModuleForCustom: bool
    RequireDepositaryReceiptForAdr: bool
}

[<CLIMutable>]
type SecurityTermValidationResult = {
    IsValid: bool
    Errors: SecurityTermValidationIssue array
}

[<RequireQualifiedAccess>]
module SecurityTermValidationConfig =

    let defaultConfig =
        {
            RequireCreditRatingForPrivate = true
            RequireCovenantsForPrivate = true
            RequireVenueForPrivate = true
            RequireStructuredModuleForCustom = true
            RequireDepositaryReceiptForAdr = true
        }

[<RequireQualifiedAccess>]
module private SecurityTermValidationRules =

    let private hasText (value: string option) =
        value |> Option.exists (fun text -> not (String.IsNullOrWhiteSpace text))

    let private containsText (source: string) (fragment: string) =
        if String.IsNullOrWhiteSpace source || String.IsNullOrWhiteSpace fragment then
            false
        else
            source.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0

    let private privateOrCustom definition =
        definition.Classification.AssetClass = AssetClass.PrivateCredit
        || definition.Classification.SubType = SecuritySubType.DirectLoan
        || containsText definition.Classification.TypeName "private"
        || containsText definition.Classification.TypeName "custom"

    let private adrKind definition =
        definition.Classification.SubType = SecuritySubType.Adr

    let private require (predicate: bool) code message acc =
        if predicate then acc else { Code = code; Message = message } :: acc

    let validate (config: SecurityTermValidationConfig) (definition: SecurityEconomicDefinition) =
        let mutable issues = []
        let isPrivateOrCustom = privateOrCustom definition

        if isPrivateOrCustom && config.RequireCreditRatingForPrivate then
            issues <- require (definition.Terms.CreditRating.IsSome) "credit-rating-required-for-private" "Private/custom assets require credit-rating terms." issues

        if isPrivateOrCustom && config.RequireCovenantsForPrivate then
            issues <- require (definition.Terms.Covenants.IsSome) "covenants-required-for-private" "Private/custom assets require covenant terms." issues

        if isPrivateOrCustom && config.RequireVenueForPrivate then
            issues <- require (definition.Terms.Venue.IsSome) "venue-required-for-private" "Private/custom assets require venue terms." issues

        if isPrivateOrCustom && config.RequireStructuredModuleForCustom then
            issues <- require (definition.Terms.StructuredProduct.IsSome) "structured-product-module-needed-for-custom" "Custom/private assets should include structured-product terms." issues

        if adrKind definition && config.RequireDepositaryReceiptForAdr then
            issues <- require (definition.Terms.DepositaryReceipt.IsSome) "adr-depositary-receipt-required" "ADR assets require a depositary-receipt term module." issues

        let hasFundFlags =
            definition.Terms.Fund |> Option.map (fun t -> hasText t.FundFamily) |> Option.defaultValue false
        if definition.Classification.AssetClass = AssetClass.Fund && not hasFundFlags then
            issues <- [
                {
                    Code = "fund-module-needs-family"
                    Message = "Fund assets should include a fund term module with fund family."
                }
            ] @ issues

        {
            IsValid = issues.Length = 0
            Errors = issues |> List.rev |> List.toArray
        }

[<RequireQualifiedAccess>]
module SecurityTermValidation =

    let validate (definition: SecurityEconomicDefinition) =
        SecurityTermValidationRules.validate SecurityTermValidationConfig.defaultConfig definition

    let validateWithConfig (config: SecurityTermValidationConfig) (definition: SecurityEconomicDefinition) =
        SecurityTermValidationRules.validate config definition

    let validateTermModules (definition: SecurityEconomicDefinition) =
        validate definition |> fun result -> result.Errors |> Array.map _.Message
