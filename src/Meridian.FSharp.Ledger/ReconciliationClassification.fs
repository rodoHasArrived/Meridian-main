namespace Meridian.FSharp.Ledger

open System

/// Versioned taxonomy identifier for canonical reconciliation break classes.
type BreakTaxonomyVersion =
    | V1

[<RequireQualifiedAccess>]
module BreakTaxonomyVersion =
    let asString = function
        | V1 -> "reconciliation-break-taxonomy/v1"

/// Closed canonical break classes used for governance-facing outputs.
type CanonicalBreakClass =
    | Timing
    | Quantity
    | Price
    | Instrument
    | CashFlow
    | CorporateAction
    | MappingError

[<RequireQualifiedAccess>]
module CanonicalBreakClass =
    let asString = function
        | Timing -> "Timing"
        | Quantity -> "Quantity"
        | Price -> "Price"
        | Instrument -> "Instrument"
        | CashFlow -> "CashFlow"
        | CorporateAction -> "CorporateAction"
        | MappingError -> "MappingError"

/// Closed reason-code set for canonical reconciliation classification.
type BreakReasonCode =
    | TimingOutsideTolerance
    | SettlementDateMissing
    | QuantityMismatch
    | QuantitySignMismatch
    | PriceMismatch
    | PriceMissing
    | InstrumentIdentifierMismatch
    | InstrumentMissing
    | CashAmountMismatch
    | CashCurrencyMismatch
    | CorporateActionTypeMismatch
    | CorporateActionFactorMismatch
    | MappingKeyNotFound
    | MappingConflict
    | UnsupportedBreakTypeFallback
    | NoDeterministicSignalFallback

[<RequireQualifiedAccess>]
module BreakReasonCode =
    let asString = function
        | TimingOutsideTolerance -> "TimingOutsideTolerance"
        | SettlementDateMissing -> "SettlementDateMissing"
        | QuantityMismatch -> "QuantityMismatch"
        | QuantitySignMismatch -> "QuantitySignMismatch"
        | PriceMismatch -> "PriceMismatch"
        | PriceMissing -> "PriceMissing"
        | InstrumentIdentifierMismatch -> "InstrumentIdentifierMismatch"
        | InstrumentMissing -> "InstrumentMissing"
        | CashAmountMismatch -> "CashAmountMismatch"
        | CashCurrencyMismatch -> "CashCurrencyMismatch"
        | CorporateActionTypeMismatch -> "CorporateActionTypeMismatch"
        | CorporateActionFactorMismatch -> "CorporateActionFactorMismatch"
        | MappingKeyNotFound -> "MappingKeyNotFound"
        | MappingConflict -> "MappingConflict"
        | UnsupportedBreakTypeFallback -> "UnsupportedBreakTypeFallback"
        | NoDeterministicSignalFallback -> "NoDeterministicSignalFallback"

/// Raw incoming break facts accepted by the F# kernel.
type RawBreakFacts = {
    BreakType: string option
    ExpectedQuantity: decimal option
    ActualQuantity: decimal option
    ExpectedPrice: decimal option
    ActualPrice: decimal option
    ExpectedInstrumentId: string option
    ActualInstrumentId: string option
    ExpectedCashAmount: decimal option
    ActualCashAmount: decimal option
    ExpectedCurrency: string option
    ActualCurrency: string option
    ExpectedSettlementDate: DateTimeOffset option
    ActualSettlementDate: DateTimeOffset option
    TimingToleranceDays: int
    ExpectedCorporateActionType: string option
    ActualCorporateActionType: string option
    ExpectedCorporateActionFactor: decimal option
    ActualCorporateActionFactor: decimal option
    MappingKey: string option
    MappingResolved: bool option
}

/// Stable classification output for UI/API governance surfaces.
type CanonicalBreakClassification = {
    TaxonomyVersion: BreakTaxonomyVersion
    BreakClass: CanonicalBreakClass
    PrimaryReasonCode: BreakReasonCode
    ReasonCodes: BreakReasonCode list
    Severity: BreakSeverity
    IsFallback: bool
}

type private RawBreakType =
    | TimingType
    | QuantityType
    | PriceType
    | InstrumentType
    | CashFlowType
    | CorporateActionType
    | MappingErrorType
    | UnknownType

[<RequireQualifiedAccess>]
module ReconciliationClassification =
    let private equalsIgnoreCase (left: string) (right: string) =
        String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

    let private hasValue (value: string option) =
        value
        |> Option.exists (fun text -> not (String.IsNullOrWhiteSpace text))

    let private normalize = function
        | Some value when not (String.IsNullOrWhiteSpace value) -> Some (value.Trim())
        | _ -> None

    let private parseBreakType breakType =
        match normalize breakType with
        | Some t when equalsIgnoreCase t "timing" -> TimingType
        | Some t when equalsIgnoreCase t "quantity" -> QuantityType
        | Some t when equalsIgnoreCase t "price" -> PriceType
        | Some t when equalsIgnoreCase t "instrument" -> InstrumentType
        | Some t when equalsIgnoreCase t "cash-flow" || equalsIgnoreCase t "cashflow" -> CashFlowType
        | Some t when equalsIgnoreCase t "corporate-action" || equalsIgnoreCase t "corporateaction" -> CorporateActionType
        | Some t when equalsIgnoreCase t "mapping-error" || equalsIgnoreCase t "mapping" -> MappingErrorType
        | Some _ -> UnknownType
        | None -> UnknownType

    let private reasonToClass = function
        | TimingOutsideTolerance
        | SettlementDateMissing -> Timing
        | QuantityMismatch
        | QuantitySignMismatch -> Quantity
        | PriceMismatch
        | PriceMissing -> Price
        | InstrumentIdentifierMismatch
        | InstrumentMissing -> Instrument
        | CashAmountMismatch
        | CashCurrencyMismatch -> CashFlow
        | CorporateActionTypeMismatch
        | CorporateActionFactorMismatch -> CorporateAction
        | MappingKeyNotFound
        | MappingConflict
        | UnsupportedBreakTypeFallback
        | NoDeterministicSignalFallback -> MappingError

    let private reasonPriority reason =
        match reasonToClass reason with
        | MappingError -> 0
        | CorporateAction -> 1
        | Instrument -> 2
        | CashFlow -> 3
        | Price -> 4
        | Quantity -> 5
        | Timing -> 6

    let private classifyByType = function
        | TimingType -> Some Timing
        | QuantityType -> Some Quantity
        | PriceType -> Some Price
        | InstrumentType -> Some Instrument
        | CashFlowType -> Some CashFlow
        | CorporateActionType -> Some CorporateAction
        | MappingErrorType -> Some MappingError
        | UnknownType -> None

    let private maxObservedAmount (facts: RawBreakFacts) =
        [
            facts.ExpectedCashAmount
            facts.ActualCashAmount
            facts.ExpectedQuantity
            facts.ActualQuantity
            facts.ExpectedPrice
            facts.ActualPrice
            facts.ExpectedCorporateActionFactor
            facts.ActualCorporateActionFactor
        ]
        |> List.choose id
        |> List.map abs
        |> function
            | [] -> 0m
            | values -> values |> List.max

    let private severityFromCashAmounts expectedAmount actualAmount =
        match expectedAmount, actualAmount with
        | Some expected, Some actual when expected <> 0m ->
            let variancePct = abs ((actual - expected) / expected)
            if variancePct > 0.05m then Critical
            elif variancePct > 0.01m then High
            else Medium
        | Some _, Some _ -> Medium
        | _ -> Medium

    let private severityFromTiming (facts: RawBreakFacts) =
        match facts.ExpectedSettlementDate, facts.ActualSettlementDate with
        | Some expectedDate, Some actualDate ->
            let daysLate = abs (int (actualDate - expectedDate).TotalDays)
            if daysLate > 30 then High
            elif daysLate > 5 then Medium
            else Low
        | _ -> Medium

    let private severityFromMapping facts =
        if maxObservedAmount facts > 10_000m then High else Medium

    let private classifySeverity (facts: RawBreakFacts) (reasons: BreakReasonCode list) =
        if reasons |> List.exists (fun reason -> reason = CashCurrencyMismatch) then
            Critical
        elif reasons |> List.exists (fun reason -> reason = CashAmountMismatch) then
            severityFromCashAmounts facts.ExpectedCashAmount facts.ActualCashAmount
        elif reasons |> List.exists (fun reason -> reason = TimingOutsideTolerance || reason = SettlementDateMissing) then
            severityFromTiming facts
        elif reasons |> List.exists (fun reason ->
            reason = QuantityMismatch
            || reason = QuantitySignMismatch
            || reason = PriceMismatch
            || reason = PriceMissing
            || reason = InstrumentIdentifierMismatch
            || reason = InstrumentMissing
            || reason = CorporateActionTypeMismatch
            || reason = CorporateActionFactorMismatch) then
            High
        elif reasons |> List.exists (fun reason ->
            reason = MappingKeyNotFound
            || reason = MappingConflict
            || reason = UnsupportedBreakTypeFallback
            || reason = NoDeterministicSignalFallback) then
            severityFromMapping facts
        else
            Medium

    let private detectReasons (parsedType: RawBreakType) (facts: RawBreakFacts) : BreakReasonCode list =
        let mutable reasons = []
        let add reason = reasons <- reason :: reasons

        match facts.ExpectedSettlementDate, facts.ActualSettlementDate with
        | Some expectedDate, Some actualDate ->
            let daysLate = abs (int (actualDate - expectedDate).TotalDays)
            if daysLate > max 0 facts.TimingToleranceDays then
                add TimingOutsideTolerance
        | Some _, None
        | None, Some _ -> add SettlementDateMissing
        | None, None -> ()

        match facts.ExpectedQuantity, facts.ActualQuantity with
        | Some expectedQty, Some actualQty ->
            if expectedQty <> actualQty then
                add QuantityMismatch
            if Math.Sign(expectedQty) <> Math.Sign(actualQty) then
                add QuantitySignMismatch
        | _ -> ()

        match facts.ExpectedPrice, facts.ActualPrice with
        | Some expectedPrice, Some actualPrice when expectedPrice <> actualPrice -> add PriceMismatch
        | Some _, None
        | None, Some _ -> add PriceMissing
        | _ -> ()

        if hasValue facts.ExpectedInstrumentId || hasValue facts.ActualInstrumentId then
            if facts.ExpectedInstrumentId <> facts.ActualInstrumentId then
                add InstrumentIdentifierMismatch
        elif parsedType = InstrumentType then
            add InstrumentMissing

        match facts.ExpectedCashAmount, facts.ActualCashAmount with
        | Some expectedCash, Some actualCash when expectedCash <> actualCash -> add CashAmountMismatch
        | _ -> ()

        match facts.ExpectedCurrency, facts.ActualCurrency with
        | Some expectedCcy, Some actualCcy when not (equalsIgnoreCase expectedCcy actualCcy) -> add CashCurrencyMismatch
        | _ -> ()

        match facts.ExpectedCorporateActionType, facts.ActualCorporateActionType with
        | Some expectedType, Some actualType when not (equalsIgnoreCase expectedType actualType) ->
            add CorporateActionTypeMismatch
        | _ -> ()

        match facts.ExpectedCorporateActionFactor, facts.ActualCorporateActionFactor with
        | Some expectedFactor, Some actualFactor when expectedFactor <> actualFactor -> add CorporateActionFactorMismatch
        | _ -> ()

        match facts.MappingResolved with
        | Some false ->
            if hasValue facts.MappingKey then
                add MappingKeyNotFound
            else
                add MappingConflict
        | _ -> ()

        reasons |> List.rev

    let classify (facts: RawBreakFacts) : CanonicalBreakClassification =
        let parsedType = parseBreakType facts.BreakType
        let reasonsFromFacts = detectReasons parsedType facts
        let reasonsWithFallback =
            match parsedType, reasonsFromFacts with
            | UnknownType, [] -> [ UnsupportedBreakTypeFallback; NoDeterministicSignalFallback ]
            | UnknownType, reasons -> reasons @ [ UnsupportedBreakTypeFallback ]
            | _, [] -> [ NoDeterministicSignalFallback ]
            | _, reasons -> reasons

        let sortedReasons = reasonsWithFallback |> List.distinct |> List.sortBy reasonPriority
        let primaryReason = sortedReasons |> List.head
        let classFromFacts = sortedReasons |> List.head |> reasonToClass
        let breakClass = classifyByType parsedType |> Option.defaultValue classFromFacts
        let isFallback =
            List.contains UnsupportedBreakTypeFallback sortedReasons
            || List.contains NoDeterministicSignalFallback sortedReasons
        let severity = classifySeverity facts sortedReasons

        {
            TaxonomyVersion = V1
            BreakClass = breakClass
            PrimaryReasonCode = primaryReason
            ReasonCodes = sortedReasons
            Severity = severity
            IsFallback = isFallback
        }

    /// Migration helper to safely map legacy break discriminators into the new canonical model.
    let classifyLegacy (nominalAmount: decimal) (legacy: LedgerBreakClassification) : CanonicalBreakClassification =
        let baseline: RawBreakFacts = {
            BreakType = None
            ExpectedQuantity = None
            ActualQuantity = None
            ExpectedPrice = None
            ActualPrice = None
            ExpectedInstrumentId = None
            ActualInstrumentId = None
            ExpectedCashAmount = None
            ActualCashAmount = None
            ExpectedCurrency = None
            ActualCurrency = None
            ExpectedSettlementDate = None
            ActualSettlementDate = None
            TimingToleranceDays = 0
            ExpectedCorporateActionType = None
            ActualCorporateActionType = None
            ExpectedCorporateActionFactor = None
            ActualCorporateActionFactor = None
            MappingKey = None
            MappingResolved = None
        }

        let facts =
            match legacy with
            | AmountBreak(expectedAmount, actualAmount) ->
                { baseline with BreakType = Some "cash-flow"; ExpectedCashAmount = Some expectedAmount; ActualCashAmount = Some actualAmount }
            | CurrencyBreak(expectedCurrency, actualCurrency) ->
                { baseline with BreakType = Some "cash-flow"; ExpectedCurrency = Some expectedCurrency; ActualCurrency = Some actualCurrency }
            | TimingBreak daysLate ->
                { baseline with
                    BreakType = Some "timing"
                    ExpectedSettlementDate = Some DateTimeOffset.UnixEpoch
                    ActualSettlementDate = Some (DateTimeOffset.UnixEpoch.AddDays(float daysLate))
                    TimingToleranceDays = 0 }
            | MissingEntry ->
                { baseline with BreakType = Some "mapping-error"; MappingResolved = Some false }
            | DuplicateEntry ->
                { baseline with BreakType = Some "mapping-error"; MappingResolved = Some false; MappingKey = Some "duplicate-entry" }
            | ClassificationBreak reason ->
                { baseline with BreakType = Some "mapping-error"; MappingResolved = Some false; MappingKey = Some reason }
            | OtherBreak reason ->
                { baseline with BreakType = Some reason }

        let classification = classify facts

        {
            classification with
                Severity = LedgerBreakClassification.severity nominalAmount legacy
        }
