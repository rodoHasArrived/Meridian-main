namespace Meridian.FSharp.Domain

open System

[<RequireQualifiedAccess>]
type SecurityMasterEvent =
    | SecurityCreated of SecurityMasterRecord
    | TermsAmended of beforeVersion:int64 * afterRecord:SecurityMasterRecord
    | SecurityDeactivated of securityId:SecurityId * version:int64 * effectiveTo:DateTimeOffset * provenance:Provenance

[<RequireQualifiedAccess>]
module SecurityMasterEvent =
    let securityId event =
        match event with
        | SecurityMasterEvent.SecurityCreated record -> record.SecurityId
        | SecurityMasterEvent.TermsAmended (_, record) -> record.SecurityId
        | SecurityMasterEvent.SecurityDeactivated (securityId, _, _, _) -> securityId

    let version event =
        match event with
        | SecurityMasterEvent.SecurityCreated record -> record.Version
        | SecurityMasterEvent.TermsAmended (_, record) -> record.Version
        | SecurityMasterEvent.SecurityDeactivated (_, version, _, _) -> version

    let eventType event =
        match event with
        | SecurityMasterEvent.SecurityCreated _ -> "SecurityCreated"
        | SecurityMasterEvent.TermsAmended _ -> "TermsAmended"
        | SecurityMasterEvent.SecurityDeactivated _ -> "SecurityDeactivated"

    let record event =
        match event with
        | SecurityMasterEvent.SecurityCreated record -> Some record
        | SecurityMasterEvent.TermsAmended (_, record) -> Some record
        | SecurityMasterEvent.SecurityDeactivated _ -> None

    let beforeVersion event =
        match event with
        | SecurityMasterEvent.SecurityCreated _ -> None
        | SecurityMasterEvent.TermsAmended (beforeVersion, _) -> Some beforeVersion
        | SecurityMasterEvent.SecurityDeactivated (_, version, _, _) -> Some (version - 1L)

    let affectsActiveProjection event =
        match event with
        | SecurityMasterEvent.SecurityCreated _ -> true
        | SecurityMasterEvent.TermsAmended _ -> true
        | SecurityMasterEvent.SecurityDeactivated _ -> true

    let evolve (state: SecurityMasterRecord option) (event: SecurityMasterEvent) =
        match state, event with
        | None, SecurityMasterEvent.SecurityCreated record ->
            Some (SecurityMasterRecord.normalize record)
        | Some _, SecurityMasterEvent.SecurityCreated _ ->
            state
        | Some _, SecurityMasterEvent.TermsAmended (_, record) ->
            Some (SecurityMasterRecord.normalize record)
        | Some current, SecurityMasterEvent.SecurityDeactivated (_, version, effectiveTo, provenance) ->
            current
            |> SecurityMasterRecord.deactivate effectiveTo provenance
            |> SecurityMasterRecord.withVersion version
            |> SecurityMasterRecord.normalize
            |> Some
        | None, _ ->
            None
