/// Unit tests for FundAccountDetails discriminated union and FundStructure account query helpers.
module Meridian.FSharp.Tests.AccountDetailsTests

open System
open Xunit
open FsUnit.Xunit
open Meridian.FSharp.Domain

// ── FundAccountDetailsOps helpers ─────────────────────────────────────────────

[<Fact>]
let ``FundAccountDetailsOps.tryGetCustodian returns Some for Custodian case`` () =
    let details = FundAccountDetails.Custodian {
        SubAccountNumber          = Some "SUB-001"
        DtcParticipantCode        = Some "0352"
        CrestMemberCode           = None
        EuroclearAccountNumber    = None
        ClearstreamAccountNumber  = None
        PrimebrokerGiveupCode     = None
        SafekeepingLocation       = Some "DTC"
        ServiceAgreementReference = None
    }
    let result = FundAccountDetailsOps.tryGetCustodian details
    result |> should not' (equal None)
    result.Value.SubAccountNumber |> should equal (Some "SUB-001")

[<Fact>]
let ``FundAccountDetailsOps.tryGetCustodian returns None for Bank case`` () =
    let details = FundAccountDetails.Bank {
        AccountNumber        = "00112233"
        BankName             = "JPMorgan"
        BranchName           = None
        Iban                 = Some "GB29NWBK60161331926819"
        BicSwift             = Some "CHASUS33"
        RoutingNumber        = None
        SortCode             = None
        IntermediaryBankBic  = None
        IntermediaryBankName = None
        BeneficiaryName      = None
        BeneficiaryAddress   = None
    }
    FundAccountDetailsOps.tryGetCustodian details |> should equal None

[<Fact>]
let ``FundAccountDetailsOps.tryGetBank returns Some for Bank case`` () =
    let details = FundAccountDetails.Bank {
        AccountNumber        = "99887766"
        BankName             = "Barclays"
        BranchName           = None
        Iban                 = None
        BicSwift             = Some "BARCGB22"
        RoutingNumber        = None
        SortCode             = Some "20-32-53"
        IntermediaryBankBic  = None
        IntermediaryBankName = None
        BeneficiaryName      = Some "Meridian Fund I LP"
        BeneficiaryAddress   = None
    }
    let result = FundAccountDetailsOps.tryGetBank details
    result |> should not' (equal None)
    result.Value.SortCode |> should equal (Some "20-32-53")

[<Fact>]
let ``FundAccountDetailsOps.tryGetBank returns None for Custodian case`` () =
    let details = FundAccountDetails.Custodian {
        SubAccountNumber          = None
        DtcParticipantCode        = None
        CrestMemberCode           = None
        EuroclearAccountNumber    = None
        ClearstreamAccountNumber  = None
        PrimebrokerGiveupCode     = None
        SafekeepingLocation       = None
        ServiceAgreementReference = None
    }
    FundAccountDetailsOps.tryGetBank details |> should equal None

[<Fact>]
let ``FundAccountDetailsOps.isCustodian returns true for Custodian case`` () =
    let details = FundAccountDetails.Custodian {
        SubAccountNumber          = None
        DtcParticipantCode        = None
        CrestMemberCode           = None
        EuroclearAccountNumber    = None
        ClearstreamAccountNumber  = None
        PrimebrokerGiveupCode     = None
        SafekeepingLocation       = None
        ServiceAgreementReference = None
    }
    FundAccountDetailsOps.isCustodian details |> should equal true

[<Fact>]
let ``FundAccountDetailsOps.isBank returns true for Bank case`` () =
    let details = FundAccountDetails.Bank {
        AccountNumber        = "123"
        BankName             = "Test Bank"
        BranchName           = None; Iban = None; BicSwift = None
        RoutingNumber        = None; SortCode = None
        IntermediaryBankBic  = None; IntermediaryBankName = None
        BeneficiaryName      = None; BeneficiaryAddress = None
    }
    FundAccountDetailsOps.isBank details |> should equal true

// ── FundStructure account query helpers ───────────────────────────────────────

let private makeLink parentId childId rel =
    {
        OwnershipLinkId  = OwnershipLinkId (Guid.NewGuid())
        ParentNodeId     = parentId
        ChildNodeId      = childId
        RelationshipType = rel
        OwnershipPercent = None
        EffectiveFrom    = DateTimeOffset.UtcNow.AddDays(-1.0)
        EffectiveTo      = None
        IsPrimary        = true
        Notes            = None
    }

[<Fact>]
let ``FundStructure.custodianAccountsForFund filters by CustodiesFor`` () =
    let fundNodeId = StructureNodeId (Guid.NewGuid())
    let custNode1  = StructureNodeId (Guid.NewGuid())
    let custNode2  = StructureNodeId (Guid.NewGuid())
    let bankNode   = StructureNodeId (Guid.NewGuid())

    let links = [
        makeLink fundNodeId custNode1 OwnershipRelationshipType.CustodiesFor
        makeLink fundNodeId custNode2 OwnershipRelationshipType.CustodiesFor
        makeLink fundNodeId bankNode  OwnershipRelationshipType.BanksFor
    ]

    let result = FundStructure.custodianAccountsForFund fundNodeId links |> Seq.toList
    result |> List.length |> should equal 2
    result |> List.forall (fun l -> l.RelationshipType = OwnershipRelationshipType.CustodiesFor)
           |> should equal true

[<Fact>]
let ``FundStructure.bankAccountsForFund filters by BanksFor`` () =
    let fundNodeId = StructureNodeId (Guid.NewGuid())
    let bankNode1  = StructureNodeId (Guid.NewGuid())
    let bankNode2  = StructureNodeId (Guid.NewGuid())
    let bankNode3  = StructureNodeId (Guid.NewGuid())
    let custNode   = StructureNodeId (Guid.NewGuid())

    let links = [
        makeLink fundNodeId bankNode1 OwnershipRelationshipType.BanksFor
        makeLink fundNodeId bankNode2 OwnershipRelationshipType.BanksFor
        makeLink fundNodeId bankNode3 OwnershipRelationshipType.BanksFor
        makeLink fundNodeId custNode  OwnershipRelationshipType.CustodiesFor
    ]

    let result = FundStructure.bankAccountsForFund fundNodeId links |> Seq.toList
    result |> List.length |> should equal 3

[<Fact>]
let ``FundStructure.custodianAccountsForFund excludes links for other funds`` () =
    let fundANodeId = StructureNodeId (Guid.NewGuid())
    let fundBNodeId = StructureNodeId (Guid.NewGuid())
    let custNode    = StructureNodeId (Guid.NewGuid())

    let links = [
        makeLink fundANodeId custNode OwnershipRelationshipType.CustodiesFor
        makeLink fundBNodeId custNode OwnershipRelationshipType.CustodiesFor
    ]

    let result = FundStructure.custodianAccountsForFund fundANodeId links |> Seq.toList
    result |> List.length |> should equal 1
    result.[0].ParentNodeId |> should equal fundANodeId

// ── OwnershipRelationshipType DU coverage ─────────────────────────────────────

[<Fact>]
let ``OwnershipRelationshipType BanksFor is a valid case`` () =
    let rel = OwnershipRelationshipType.BanksFor
    // Pattern match should compile and cover the BanksFor case without warning
    let label =
        match rel with
        | OwnershipRelationshipType.Owns        -> "Owns"
        | OwnershipRelationshipType.Advises     -> "Advises"
        | OwnershipRelationshipType.Operates    -> "Operates"
        | OwnershipRelationshipType.ClearsFor   -> "ClearsFor"
        | OwnershipRelationshipType.CustodiesFor -> "CustodiesFor"
        | OwnershipRelationshipType.AllocatesTo -> "AllocatesTo"
        | OwnershipRelationshipType.BanksFor    -> "BanksFor"
    label |> should equal "BanksFor"
