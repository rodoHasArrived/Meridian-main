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
