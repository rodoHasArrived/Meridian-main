import type { LedgerDimensionSet } from "./workstation-2";
import type { AccountingBasisKind } from "./workstation-5";

export const INSTRUMENT_ROLE_KINDS = {
  Holder: "Holder",
  Issuer: "Issuer",
  Lender: "Lender",
  Borrower: "Borrower",
  Payer: "Payer",
  Receiver: "Receiver",
} as const;

export const BOOK_POSITION_SIDES = {
  Long: "Long",
  Short: "Short",
  Asset: "Asset",
  Liability: "Liability",
} as const;

export const INSTRUMENT_ACCOUNTING_SIDES = {
  Debit: "Debit",
  Credit: "Credit",
} as const;

export const INSTRUMENT_ECONOMIC_SIDES = {
  Asset: "Asset",
  Liability: "Liability",
  Inflow: "Inflow",
  Outflow: "Outflow",
} as const;

export interface AccountingBookContext {
  ledgerBookId: string;
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
  periodId?: string | null;
  dimensions?: LedgerDimensionSet | null;
}

export interface AccountingRulePackReference {
  rulePackId: string;
  rulePackVersion: string;
  selectedRuleId?: string | null;
  selectedRuleVersion?: string | null;
}

export interface EconomicEventReference {
  eventId: string;
  eventType: string;
  eventVersion: number;
  effectiveDate: string;
  occurredAtUtc: string;
  sourceDomain: string;
  sourceEntityId?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
  sourceContentHash?: string | null;
  evidenceLinks: string[];
  securityId?: string | null;
  bookPositionId?: string | null;
}

export interface ProjectionLineage {
  projectionRunId: string;
  projectionEventId?: string | null;
  modelKey: string;
  modelVersion: string;
  engineVersion: string;
  scenario: string;
  projectionAsOfDate: string;
  generatedAtUtc: string;
  sourceDomain: string;
  sourceEntityId?: string | null;
  triggerEvent: EconomicEventReference;
  termsVersion?: string | null;
  termsHash?: string | null;
  supersededRunId?: string | null;
  evidenceLinks: string[];
  bookPositionId?: string | null;
}

export interface PositionEconomicState {
  economicStateId: string;
  positionId: string;
  asOfDate: string;
  currency: string;
  version: number;
  quantity?: number | null;
  parAmount?: number | null;
  notionalAmount?: number | null;
  originalFaceAmount?: number | null;
  currentFaceAmount?: number | null;
  unitCost?: number | null;
  carryingAmount?: number | null;
  purchasePrice?: number | null;
  tradeDate?: string | null;
  settlementDate?: string | null;
  rate?: number | null;
  priorFactor?: number | null;
  currentFactor?: number | null;
  sourceEvent?: EconomicEventReference | null;
  evidenceLinks: string[];
  extensionPayload?: Record<string, unknown> | null;
}

export interface InstrumentRole {
  roleId: string;
  securityId: string;
  ownerScopeId: string;
  ownerScopeKind: string;
  roleKind: string;
  accountingSide: string;
  economicSide: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
  counterpartyId?: string | null;
  defaultAccountId?: string | null;
  version: number;
  originEvent?: EconomicEventReference | null;
  evidenceLinks: string[];
  extensionPayload?: Record<string, unknown> | null;
}

export interface BookPosition {
  positionId: string;
  securityId: string;
  roleId: string;
  bookContext: AccountingBookContext;
  positionSide: string;
  status: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
  version: number;
  primaryAccountId?: string | null;
  currentEconomicState?: PositionEconomicState | null;
  originEvent?: EconomicEventReference | null;
  projectionLineage?: ProjectionLineage | null;
  evidenceLinks: string[];
  extensionPayload?: Record<string, unknown> | null;
}
