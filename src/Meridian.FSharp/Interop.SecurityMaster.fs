namespace Meridian.FSharp.SecurityMasterInterop

open System
open System.Text.Json
open Meridian.Contracts.SecurityMaster
open Meridian.FSharp.Domain

/// Private helpers for F# option → C# nullable conversion, mirroring the helpers in Interop.fs.
[<AutoOpen>]
module private NullableHelpers =
    let inline toNullable (opt: 'T option) : Nullable<'T> =
        match opt with
        | Some v -> Nullable v
        | None -> Nullable()

    let inline toNullableRef (opt: 'T option) : 'T =
        match opt with
        | Some v -> v
        | None -> Unchecked.defaultof<'T>

/// Exact typed tranche serialization; the flat codec keeps its existing field spellings.
module TrancheContractCodec =
    let toJson (terms: TrancheContractTerms) =
        JsonSerializer.Serialize(
            {| structuredCreditKind = terms.Kind |> Option.map StructuredCreditKind.asString
               seniority = terms.Seniority
               attachmentPoint = terms.AttachmentPoint
               detachmentPoint = terms.DetachmentPoint
               reinvestmentPeriodEnd = terms.ReinvestmentPeriodEnd
               nonCallPeriodEnd = terms.NonCallPeriodEnd |})

type SecurityIdentifierSnapshot(identifier: Identifier) =
    do
        if not (SecurityIdentifier.providerMetadataMatchesKind identifier) then
            invalidArg
                (nameof identifier.Provider)
                "ProviderSymbol metadata must match the authoritative provider namespace carried by Identifier.Kind."

    let provider =
        match identifier.Kind with
        | IdentifierKind.ProviderSymbol authoritativeProvider -> authoritativeProvider
        | _ -> identifier.Provider |> toNullableRef

    let kind = SecurityIdentifier.kindName identifier

    member _.Kind = kind
    member _.Value = identifier.Value
    member _.IsPrimary = identifier.IsPrimary
    member _.ValidFrom = identifier.ValidFrom
    member _.ValidTo = toNullable identifier.ValidTo
    member _.Provider = provider

[<AllowNullLiteral>]
[<Sealed>]
type SecurityMasterSnapshotWrapper(record: SecurityMasterRecord) =
    let schemaVersion = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms

    // Idea 2: delegate to the domain function instead of duplicating the match.
    let assetClass = SecurityKind.assetClass record.Kind

    // The version this record's flat payload is written at, and which versioned keys it carries:
    // from a term's version upward its key is always present (the block null when nothing is
    // declared, the schedules [] when none is declared: ADR-024's frozen written form); below it the
    // key is absent.
    let termsVersion = SecurityTermsWriteVersion.effective record.Kind

    let writesScheduleConventions () =
        termsVersion >= SecurityTermsWriteVersion.blockSince record.Kind

    // Called only for the classes that declare the schedules.
    let writesCallPutSchedules () =
        termsVersion >= SecurityTermsWriteVersion.schedulesSince record.Kind

    let unstampedAssetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.Equity terms -> GeneratedFlatWriter.writeEquity schemaVersion terms
        | SecurityKind.Option terms -> GeneratedFlatWriter.writeOption schemaVersion terms
        | SecurityKind.Future terms -> GeneratedFlatWriter.writeFuture schemaVersion terms
        | SecurityKind.Bond terms -> GeneratedFlatWriter.writeBond schemaVersion (writesScheduleConventions ()) (writesCallPutSchedules ()) terms
        | SecurityKind.FxSpot terms -> GeneratedFlatWriter.writeFxSpot schemaVersion terms
        | SecurityKind.Deposit terms -> GeneratedFlatWriter.writeDeposit schemaVersion terms
        | SecurityKind.MoneyMarketFund terms -> GeneratedFlatWriter.writeMoneyMarketFund schemaVersion terms
        | SecurityKind.CertificateOfDeposit terms -> GeneratedFlatWriter.writeCertificateOfDeposit schemaVersion (writesScheduleConventions ()) (writesCallPutSchedules ()) terms
        | SecurityKind.CommercialPaper terms -> GeneratedFlatWriter.writeCommercialPaper schemaVersion terms
        | SecurityKind.TreasuryBill terms -> GeneratedFlatWriter.writeTreasuryBill schemaVersion terms
        | SecurityKind.Repo terms -> GeneratedFlatWriter.writeRepo schemaVersion terms
        | SecurityKind.CashSweep terms -> GeneratedFlatWriter.writeCashSweep schemaVersion terms
        | SecurityKind.OtherSecurity terms -> GeneratedFlatWriter.writeOtherSecurity schemaVersion terms
        | SecurityKind.Swap terms -> GeneratedFlatWriter.writeSwap schemaVersion (writesScheduleConventions ()) terms
        | SecurityKind.DirectLoan terms -> GeneratedFlatWriter.writeDirectLoan schemaVersion terms
        | SecurityKind.StructuredCredit terms -> GeneratedFlatWriter.writeStructuredCredit schemaVersion (writesScheduleConventions ()) (writesCallPutSchedules ()) terms
        | SecurityKind.PrivateFundInterest terms -> GeneratedFlatWriter.writePrivateFundInterest schemaVersion terms
        | SecurityKind.PrivateCompanyEquity terms -> GeneratedFlatWriter.writePrivateCompanyEquity schemaVersion terms
        | SecurityKind.RealEstateHolding terms -> GeneratedFlatWriter.writeRealEstateHolding schemaVersion terms
        | SecurityKind.CommitmentGuarantee terms -> GeneratedFlatWriter.writeCommitmentGuarantee schemaVersion terms
        | SecurityKind.Commodity terms -> GeneratedFlatWriter.writeCommodity schemaVersion terms
        | SecurityKind.CryptoCurrency terms -> GeneratedFlatWriter.writeCryptoCurrency schemaVersion terms
        | SecurityKind.Cfd terms -> GeneratedFlatWriter.writeCfd schemaVersion terms
        | SecurityKind.Warrant terms -> GeneratedFlatWriter.writeWarrant schemaVersion terms
        | SecurityKind.FxForward terms -> GeneratedFlatWriter.writeFxForward schemaVersion terms
        | SecurityKind.NonDeliverableForward terms -> GeneratedFlatWriter.writeNonDeliverableForward schemaVersion terms
        | SecurityKind.CreditDefaultSwap terms -> GeneratedFlatWriter.writeCreditDefaultSwap schemaVersion terms
        | SecurityKind.Swaption terms -> GeneratedFlatWriter.writeSwaption schemaVersion terms
        | SecurityKind.CapFloor terms -> GeneratedFlatWriter.writeCapFloor schemaVersion terms
        | SecurityKind.StructuredNote terms -> GeneratedFlatWriter.writeStructuredNote schemaVersion terms
        | SecurityKind.InvestmentFund terms -> GeneratedFlatWriter.writeInvestmentFund schemaVersion terms
        | SecurityKind.CustomAsset terms ->
            // The custom-asset document is emitted verbatim: it already carries its own
            // schemaVersion (the CustomAssetProfile family) and its dynamic, profile-governed
            // keys must survive serialize → deserialize → serialize without loss.
            terms.TermsJson

    // Flat payloads are stamped with their format and asset-class terms version so readers
    // dispatch on what the document declares rather than on the shared schemaVersion integer.
    // The custom-asset document keeps every member verbatim and only declares its family
    // (termsFormat; ADR-024 amendment 2026-11-13): customProfile for a profile envelope, flat
    // otherwise. A document that already declares one is returned unchanged, so the round trip
    // stays lossless.
    let baseAssetSpecificTermsJson =
        match record.Kind with
        | SecurityKind.CustomAsset _ -> SecurityTermsEnvelope.StampDeclaredFormat(unstampedAssetSpecificTermsJson)
        | _ -> SecurityTermsEnvelope.StampFlat(unstampedAssetSpecificTermsJson, assetClass, termsVersion)

    let assetSpecificTermsJson =
        let unit =
            match record.Kind with
            | SecurityKind.Swap terms -> terms.RateUnit
            | SecurityKind.Deposit terms -> terms.RateUnit
            | SecurityKind.CertificateOfDeposit terms -> terms.RateUnit
            | SecurityKind.CommercialPaper terms -> terms.RateUnit
            | SecurityKind.TreasuryBill terms -> terms.RateUnit
            | SecurityKind.Repo terms -> terms.RateUnit
            | SecurityKind.CashSweep terms -> terms.RateUnit
            | SecurityKind.DirectLoan terms -> terms.RateUnit
            | SecurityKind.StructuredCredit terms -> terms.RateUnit
            | SecurityKind.Swaption terms -> terms.RateUnit
            | SecurityKind.CapFloor terms -> terms.RateUnit
            | SecurityKind.StructuredNote terms -> terms.RateUnit
            | _ -> None
        let withRate =
            match unit with
            | None -> baseAssetSpecificTermsJson
            | Some token ->
                let document = System.Text.Json.Nodes.JsonNode.Parse(baseAssetSpecificTermsJson).AsObject()
                document["rateUnit"] <- System.Text.Json.Nodes.JsonValue.Create(token)
                let version = SecurityAssetTermsSchema.Field(assetClass, "rateUnit").SinceTermsVersion
                SecurityTermsEnvelope.StampFlat(document.ToJsonString(), assetClass, max version termsVersion)
        match record.Kind with
        | SecurityKind.Bond terms when terms.Workflow.IsSome ->
            let document = System.Text.Json.Nodes.JsonNode.Parse(withRate).AsObject()
            document["bondWorkflow"] <- System.Text.Json.Nodes.JsonNode.Parse(BondWorkflowTermsJson.write(terms.Workflow.Value).GetRawText())
            SecurityTermsEnvelope.StampFlat(document.ToJsonString(), assetClass, termsVersion)
        | _ -> withRate

    let commonTermsJson =
        JsonSerializer.Serialize(
            {| displayName = record.Common.DisplayName
               currency = record.Common.Currency
               countryOfRisk = record.Common.CountryOfRisk
               issuerName = record.Common.IssuerName
               exchange = record.Common.Exchange
               lotSize = record.Common.LotSize
               tickSize = record.Common.TickSize
               primaryListingMic = record.Common.PrimaryListingMic
               countryOfIncorporation = record.Common.CountryOfIncorporation
               settlementCycleDays = record.Common.SettlementCycleDays
               holidayCalendarId = record.Common.HolidayCalendarId |})

    let provenanceJson =
        JsonSerializer.Serialize(
            {| sourceSystem = record.Provenance.SourceSystem
               sourceRecordId = record.Provenance.SourceRecordId
               asOf = record.Provenance.AsOf
               updatedBy = record.Provenance.UpdatedBy
               reason = record.Provenance.Reason |})

    // Idea 2: delegate to SecurityMasterRecord.primaryIdentifier instead of duplicating List.tryFind.
    let primaryIdentifier = SecurityMasterRecord.primaryIdentifier record

    member _.Record = record
    member _.SecurityId = let (SecurityId id) = record.SecurityId in id
    member _.AssetClass = assetClass
    // Idea 2: delegate to SecurityStatus.asString instead of inline if/else.
    member _.Status = SecurityStatus.asString record.Status
    member _.DisplayName = record.Common.DisplayName
    member _.Currency = record.Common.Currency
    member _.CommonTermsJson = commonTermsJson
    member _.AssetSpecificTermsJson = assetSpecificTermsJson
    member _.ProvenanceJson = provenanceJson
    member _.Version = record.Version
    member _.EffectiveFrom = record.EffectiveFrom
    // Idea 8: use NullableHelpers.toNullable instead of inline match.
    member _.EffectiveTo = toNullable record.EffectiveTo
    member _.PrimaryIdentifierKind =
        primaryIdentifier
        |> Option.map (fun identifier -> SecurityIdentifierSnapshot(identifier).Kind)
        |> Option.defaultValue String.Empty
    member _.PrimaryIdentifierValue =
        primaryIdentifier
        |> Option.map (fun identifier -> identifier.Value)
        |> Option.defaultValue String.Empty
    member _.Identifiers =
        record.Identifiers
        |> List.map SecurityIdentifierSnapshot
        |> List.toArray

/// C#-friendly DTO that carries both the error code and human-readable message
/// from a SecurityMaster validation failure.
[<Sealed>]
type SecurityValidationErrorDto(code: string, message: string) =
    member _.Code = code
    member _.Message = message

[<AllowNullLiteral>]
[<Sealed>]
type SecurityMasterCommandResultWrapper(result: Result<SecurityMasterRecord, SecurityValidationError list>) =
    member _.IsSuccess =
        match result with
        | Ok _ -> true
        | Error _ -> false

    member _.Snapshot =
        match result with
        | Ok record -> SecurityMasterSnapshotWrapper(record)
        | Error _ -> null

    /// Error messages only (kept for backward compatibility).
    member _.Errors =
        match result with
        | Ok _ -> Array.empty
        | Error errors -> errors |> List.map (fun error -> error.Message) |> List.toArray

    // Idea 3: expose both code and message so callers can distinguish error kinds.
    member _.ErrorDetails =
        match result with
        | Ok _ -> Array.empty
        | Error errors ->
            errors
            |> List.map (fun e -> SecurityValidationErrorDto(e.Code, e.Message))
            |> List.toArray

// Idea 6: renamed from SecurityMasterAggregate to SecurityMasterCommandFacade to
// reflect that this is a stateless facade over the aggregate command functions,
// not a stateful aggregate object.
[<Sealed>]
type SecurityMasterCommandFacade private () =
    static member private ToResult(initialState: SecurityMasterRecord option, events: Result<SecurityMasterEvent list, SecurityValidationError list>) =
        match events with
        | Error errors -> SecurityMasterCommandResultWrapper(Error errors)
        | Ok eventList ->
            let finalState = eventList |> List.fold SecurityMaster.evolve initialState
            match finalState with
            | Some record -> SecurityMasterCommandResultWrapper(Ok record)
            | None ->
                SecurityMasterCommandResultWrapper(
                    Error [ {
                        Code = "missing_state"
                        Message = "Security master command produced no aggregate state."
                    } ])

    static member Create(command: CreateSecurity) =
        SecurityMasterCommandFacade.ToResult(None, SecurityMaster.create command)

    static member Amend(current: SecurityMasterRecord, command: AmendTerms) =
        SecurityMasterCommandFacade.ToResult(Some current, SecurityMaster.amend current command)

    static member Deactivate(current: SecurityMasterRecord, command: DeactivateSecurity) =
        SecurityMasterCommandFacade.ToResult(Some current, SecurityMaster.deactivate current command)

    /// Standalone kind-invariant validation for the C# write path: a profile-backed record
    /// reclassified to its resolved first-class kind must also satisfy that kind's rule set,
    /// which can be stricter than the pinned profile's.
    static member ValidateKindInvariants(kind: SecurityKind) =
        SecurityMaster.validateKindInvariants kind |> List.toArray

    /// ADR-027 stage 2 (S2-B1): the module-expressible subset of the kind rule set, evaluated
    /// over the economic definition's modules and gated by the classification's canonical
    /// TypeName. The stage-2 equivalence tests pin, per class, that this agrees code-for-code
    /// with ValidateKindInvariants on the codes DefinitionExpressibleCodes lists; from S2-B2 the
    /// command write path validates through it.
    static member ValidateDefinitionInvariants(typeName: string, modules: SecurityTermModules) =
        SecurityMaster.validateDefinitionInvariants typeName modules |> List.toArray

    /// Per-class ledger of the term-rule error codes, each of which the definition surface
    /// expresses (complete since the stage-3 contraction: no code is codec-only).
    static member DefinitionExpressibleCodes =
        SecurityMaster.definitionExpressibleCodes
        |> Map.toSeq
        |> Seq.map (fun (typeName, codes) -> System.Collections.Generic.KeyValuePair(typeName, codes |> List.toArray))
        |> fun pairs -> System.Collections.Generic.Dictionary<string, string[]>(pairs) :> System.Collections.Generic.IReadOnlyDictionary<string, string[]>
