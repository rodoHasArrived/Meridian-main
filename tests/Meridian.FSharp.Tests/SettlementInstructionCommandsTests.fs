namespace Meridian.FSharp.Tests

open System
open FsUnit.Xunit
open Xunit
open Meridian.FSharp.Domain

module SettlementInstructionFixtures =
    let mk account detailType fromUtc toUtc isDefault supersedes =
        { InstructionId = Guid.NewGuid()
          AccountId = account
          DetailType = detailType
          Currency = "USD"
          CounterpartyScope = SettlementCounterpartyScope.Global
          EffectiveFromUtc = fromUtc
          EffectiveToUtc = toUtc
          IsDefault = isDefault
          IsClosed = false
          SupersedesInstructionId = supersedes }

open SettlementInstructionFixtures
open Meridian.FSharp.Domain.SettlementInstructionCommands

[<Fact>]
let ``rejects invalid effective window`` () =
    let account = AccountId(Guid.NewGuid())
    let fromUtc = DateTimeOffset.Parse("2026-01-10T00:00:00Z")
    let candidate = mk account SettlementDetailType.BankWire fromUtc (Some fromUtc) false None
    match addSettlementInstructionCommand [] candidate with
    | Error e -> e.Code |> should equal "SETTLEMENT_EFFECTIVE_WINDOW_INVALID"
    | Ok _ -> failwith "Expected validation error"

[<Fact>]
let ``rejects overlap without supersede`` () =
    let account = AccountId(Guid.NewGuid())
    let existing = mk account SettlementDetailType.Custody (DateTimeOffset.Parse("2026-01-01T00:00:00Z")) None false None
    let candidate = mk account SettlementDetailType.Custody (DateTimeOffset.Parse("2026-02-01T00:00:00Z")) None false None
    match addSettlementInstructionCommand [ existing ] candidate with
    | Error e -> e.Code |> should equal "SETTLEMENT_OVERLAP_CONFLICT"
    | Ok _ -> failwith "Expected overlap conflict"

[<Fact>]
let ``allows overlap when explicitly superseding`` () =
    let account = AccountId(Guid.NewGuid())
    let existing = mk account SettlementDetailType.Custody (DateTimeOffset.Parse("2026-01-01T00:00:00Z")) None false None
    let candidate = mk account SettlementDetailType.Custody (DateTimeOffset.Parse("2026-02-01T00:00:00Z")) None false (Some existing.InstructionId)
    match addSettlementInstructionCommand [ existing ] candidate with
    | Ok xs -> xs.Length |> should equal 2
    | Error e -> failwithf "Unexpected error: %s" e.Code

[<Fact>]
let ``rejects default collision for same scope and overlapping window`` () =
    let account = AccountId(Guid.NewGuid())
    let existing = mk account SettlementDetailType.BankWire (DateTimeOffset.Parse("2026-01-01T00:00:00Z")) None true None
    let candidate = mk account SettlementDetailType.Custody (DateTimeOffset.Parse("2026-02-01T00:00:00Z")) None true None
    match addSettlementInstructionCommand [ existing ] candidate with
    | Error e -> e.Code |> should equal "SETTLEMENT_DEFAULT_COLLISION"
    | Ok _ -> failwith "Expected default collision"
