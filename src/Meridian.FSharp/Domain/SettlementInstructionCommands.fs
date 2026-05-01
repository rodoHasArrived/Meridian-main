namespace Meridian.FSharp.Domain

open System

type SettlementDetailType =
    | BankWire
    | Custody
    | Other of string

type SettlementCounterpartyScope =
    | Global
    | Counterparty of string

type SettlementInstruction = {
    InstructionId: Guid
    AccountId: AccountId
    DetailType: SettlementDetailType
    Currency: string
    CounterpartyScope: SettlementCounterpartyScope
    EffectiveFromUtc: DateTimeOffset
    EffectiveToUtc: DateTimeOffset option
    IsDefault: bool
    IsClosed: bool
    SupersedesInstructionId: Guid option
}

type SettlementCommandError = {
    Code: string
    Message: string
}

module SettlementInstructionCommands =
    let private err code message = Error { Code = code; Message = message }

    let private effectiveWindowValid (instruction: SettlementInstruction) =
        instruction.EffectiveToUtc |> Option.forall (fun x -> x > instruction.EffectiveFromUtc)

    let private overlaps (a: SettlementInstruction) (b: SettlementInstruction) =
        let aEnd = a.EffectiveToUtc |> Option.defaultValue DateTimeOffset.MaxValue
        let bEnd = b.EffectiveToUtc |> Option.defaultValue DateTimeOffset.MaxValue
        a.EffectiveFromUtc < bEnd && b.EffectiveFromUtc < aEnd

    let private allowsSupersede existing candidate =
        candidate.SupersedesInstructionId = Some existing.InstructionId

    let validateAddInstruction (existing: SettlementInstruction list) (candidate: SettlementInstruction) =
        if not (effectiveWindowValid candidate) then
            err "SETTLEMENT_EFFECTIVE_WINDOW_INVALID" "EffectiveToUtc must be greater than EffectiveFromUtc when present."
        elif candidate.IsClosed then
            err "SETTLEMENT_ACCOUNT_STATUS_CLOSED" "Cannot add settlement instruction for a closed account detail state."
        else
            let conflicting =
                existing
                |> List.filter (fun x ->
                    x.AccountId = candidate.AccountId
                    && x.DetailType = candidate.DetailType
                    && not x.IsClosed
                    && overlaps x candidate
                    && not (allowsSupersede x candidate))

            if not conflicting.IsEmpty then
                err "SETTLEMENT_OVERLAP_CONFLICT" "Overlapping active windows for the same account/detail type require an explicit supersede reference."
            else
                Ok candidate

    let enforceSingleDefault (existing: SettlementInstruction list) (candidate: SettlementInstruction) =
        if not candidate.IsDefault then
            Ok existing
        else
            let sameScope x =
                x.AccountId = candidate.AccountId
                && x.Currency.Equals(candidate.Currency, StringComparison.OrdinalIgnoreCase)
                && x.CounterpartyScope = candidate.CounterpartyScope
                && not x.IsClosed
                && overlaps x candidate

            if existing |> List.exists (fun x -> sameScope x && x.IsDefault && x.InstructionId <> candidate.InstructionId) then
                err "SETTLEMENT_DEFAULT_COLLISION" "At most one default settlement instruction may exist per account/currency/counterparty scope/effective window."
            else
                Ok (existing |> List.map (fun x -> if sameScope x && x.InstructionId <> candidate.InstructionId then { x with IsDefault = false } else x))

    let addSettlementInstructionCommand existing candidate =
        match validateAddInstruction existing candidate with
        | Error e -> Error e
        | Ok validated ->
            match enforceSingleDefault existing validated with
            | Error e -> Error e
            | Ok normalized -> Ok (validated :: normalized)

    let setDefaultSettlementInstructionCommand existing instructionId =
        match existing |> List.tryFind (fun x -> x.InstructionId = instructionId) with
        | None -> err "SETTLEMENT_ACCOUNT_NOT_FOUND" "Settlement instruction was not found for the specified account."
        | Some target when target.IsClosed -> err "SETTLEMENT_ACCOUNT_STATUS_CLOSED" "Cannot set default on a closed settlement instruction."
        | Some target ->
            let defaulted = { target with IsDefault = true }
            match enforceSingleDefault existing defaulted with
            | Error e -> Error e
            | Ok normalized ->
                Ok (normalized |> List.map (fun x -> if x.InstructionId = target.InstructionId then defaulted else x))

    let upsertAccountDetailsCommand existing candidate =
        if candidate.IsClosed then
            err "SETTLEMENT_ACCOUNT_STATUS_CLOSED" "Cannot upsert details for a closed account status."
        else
            let withoutCurrent = existing |> List.filter (fun x -> x.InstructionId <> candidate.InstructionId)
            addSettlementInstructionCommand withoutCurrent candidate
