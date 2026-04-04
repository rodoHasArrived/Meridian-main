namespace Meridian.FSharp.Domain

open System

type FundId = FundId of Guid
type SleeveId = SleeveId of Guid
type VehicleId = VehicleId of Guid
type EntityId = EntityId of Guid
type AccountId = AccountId of Guid
type StructureNodeId = StructureNodeId of Guid
type OwnershipLinkId = OwnershipLinkId of Guid
type AssignmentId = AssignmentId of Guid

[<RequireQualifiedAccess>]
type StructureNodeKind =
    | Fund
    | Sleeve
    | Vehicle
    | Entity
    | Account

[<RequireQualifiedAccess>]
type LegalEntityType =
    | Fund
    | ManagementCompany
    | GeneralPartner
    | LimitedPartner
    | Vehicle
    | Custodian
    | Broker
    | Counterparty
    | Other

[<RequireQualifiedAccess>]
type AccountType =
    | Brokerage
    | Custody
    | Bank
    | Margin
    | PrimeBroker
    | LedgerControl
    | Other

[<RequireQualifiedAccess>]
type OwnershipRelationshipType =
    | Owns
    | Advises
    | Operates
    | ClearsFor
    | CustodiesFor
    | AllocatesTo
    | BanksFor

type StructureNode = {
    NodeId: StructureNodeId
    Kind: StructureNodeKind
    Code: string
    Name: string
    Description: string option
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsActive: bool
}

type FundDefinition = {
    FundId: FundId
    Code: string
    Name: string
    BaseCurrency: string
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsActive: bool
}

type SleeveDefinition = {
    SleeveId: SleeveId
    FundId: FundId
    Code: string
    Name: string
    Mandate: string option
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsActive: bool
}

type VehicleDefinition = {
    VehicleId: VehicleId
    FundId: FundId
    EntityId: EntityId
    Code: string
    Name: string
    BaseCurrency: string
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsActive: bool
}

type LegalEntityDefinition = {
    EntityId: EntityId
    EntityType: LegalEntityType
    Code: string
    Name: string
    Jurisdiction: string
    BaseCurrency: string
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsActive: bool
}

/// Custody-specific settlement details attached to an AccountDefinition where AccountType = Custody.
type CustodianAccountDetails = {
    SubAccountNumber: string option
    DtcParticipantCode: string option
    CrestMemberCode: string option
    EuroclearAccountNumber: string option
    ClearstreamAccountNumber: string option
    PrimebrokerGiveupCode: string option
    SafekeepingLocation: string option          // e.g. "DTC" | "CREST" | "EUROCLEAR"
    ServiceAgreementReference: string option
}

/// Banking-specific settlement details attached to an AccountDefinition where AccountType = Bank.
type BankAccountDetails = {
    AccountNumber: string
    BankName: string
    BranchName: string option
    Iban: string option
    BicSwift: string option
    RoutingNumber: string option                // US ABA routing
    SortCode: string option                     // UK sort code
    IntermediaryBankBic: string option
    IntermediaryBankName: string option
    BeneficiaryName: string option
    BeneficiaryAddress: string option
}

/// Extended account details discriminated by settlement network type.
[<RequireQualifiedAccess>]
type FundAccountDetails =
    | Custodian of CustodianAccountDetails
    | Bank of BankAccountDetails

type AccountDefinition = {
    AccountId: AccountId
    AccountType: AccountType
    EntityId: EntityId option
    FundId: FundId option
    SleeveId: SleeveId option
    VehicleId: VehicleId option
    AccountCode: string
    DisplayName: string
    BaseCurrency: string
    Institution: string option
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsActive: bool
    PortfolioId: string option
    LedgerReference: string option
    StrategyId: string option
    RunId: string option
    // Populated for Custody and Bank account types; None for Brokerage/Margin/LedgerControl
    AccountDetails: FundAccountDetails option
}

type OwnershipLink = {
    OwnershipLinkId: OwnershipLinkId
    ParentNodeId: StructureNodeId
    ChildNodeId: StructureNodeId
    RelationshipType: OwnershipRelationshipType
    OwnershipPercent: decimal option
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsPrimary: bool
    Notes: string option
}

type StructureAssignment = {
    AssignmentId: AssignmentId
    NodeId: StructureNodeId
    AssignmentType: string
    AssignmentReference: string
    EffectiveFrom: DateTimeOffset
    EffectiveTo: DateTimeOffset option
    IsPrimary: bool
}

[<RequireQualifiedAccess>]
module FundStructure =
    let isActiveAt asOf effectiveFrom effectiveTo isActive =
        isActive
        && effectiveFrom <= asOf
        && effectiveTo |> Option.forall (fun validTo -> validTo > asOf)

    let nodeIsActiveAt asOf (node: StructureNode) =
        isActiveAt asOf node.EffectiveFrom node.EffectiveTo node.IsActive

    let ownershipIsActiveAt asOf (link: OwnershipLink) =
        isActiveAt asOf link.EffectiveFrom link.EffectiveTo true

    let assignmentIsActiveAt asOf (assignment: StructureAssignment) =
        isActiveAt asOf assignment.EffectiveFrom assignment.EffectiveTo true

    /// Returns ownership links where the fund is the parent and the relationship is CustodiesFor.
    let custodianAccountsForFund (fundNodeId: StructureNodeId) (links: OwnershipLink seq) =
        links
        |> Seq.filter (fun l ->
            l.ParentNodeId = fundNodeId
            && l.RelationshipType = OwnershipRelationshipType.CustodiesFor)

    /// Returns ownership links where the fund is the parent and the relationship is BanksFor.
    let bankAccountsForFund (fundNodeId: StructureNodeId) (links: OwnershipLink seq) =
        links
        |> Seq.filter (fun l ->
            l.ParentNodeId = fundNodeId
            && l.RelationshipType = OwnershipRelationshipType.BanksFor)
