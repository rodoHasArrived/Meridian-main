import type { CorporateActionCaseScope, CorporateActionProcessingCaseDto } from "./workstation-7";

/** Native server projection. Displayed amounts never become request-side eligibility operands. */
export interface CashDividendAccountingProjection {
  projectionId: string;
  caseId: string;
  boundCaseVersion: number;
  accountingEventId: string;
  accountingEventVersion: number;
  spineVersion: number;
  projectionInputHash: string;
  postingIntentHash: string;
  postingIdempotencyKey: string;
  draftedCandidateFingerprint: string;
  policyDecisionId: string;
  policyDecisionVersion: number;
  rulePackId: string;
  rulePackVersion: string;
  selectedRuleId: string;
  selectedRuleVersion: string;
  ledgerBookId: string;
  periodId: string;
  expectedPeriodVersion: number;
  accountingBasis: string;
  fundProfileId: string;
  currency: string;
  effectiveDate: string;
  totalDebits: number;
  totalCredits: number;
  lotSnapshotId: string;
  lotSnapshotVersion: number;
  hasAuthoritativeLotResolution: boolean;
  preparedBy: string;
  preparedAtUtc: string;
  isCurrent: boolean;
  supersededAtUtc?: string | null;
  isBalanced: boolean;
}

export interface CashDividendEligibility {
  securityId: string;
  exDate: string;
  relatedEventKey: string;
  providerId: string;
  sourceEventId: string;
  sourceEventVersion: string;
  sourceEvidenceHash: string;
  dividendPerShare: number;
  grossImpactUsd: number;
  wholeEventGrossUsd: number;
  budgetMonth: string;
  entitlementSnapshotHash: string;
  entitlementSnapshotJson: string;
  accountRoleBinding?: CashDividendAccountRoleBinding | null;
}

/** Exact existing chart accounts and promoted rule supplied by the native preview. */
export interface CashDividendAccountRoleBinding {
  ledgerBookId: string;
  dividendReceivableAccount: string;
  dividendIncomeAccount: string;
  rulePackId: string;
  rulePackVersion: string;
  selectedRuleId: string;
  selectedRuleVersion: string;
  schemaVersion: number;
  dividendReceivableAccountName?: string | null;
  dividendIncomeAccountName?: string | null;
}

export interface CashDividendAttestationPreview {
  case: CorporateActionProcessingCaseDto;
  projection: CashDividendAccountingProjection;
  eligibility: CashDividendEligibility;
  disclosure: string;
  confirmation: string;
}

export interface CashDividendAttestation {
  attestationId: string;
  caseId: string;
  projectionId: string;
  boundCaseVersion: number;
  declarationId: string;
  declarationRevision: number;
  policyVersion: string;
  policyHash: string;
  budgetReservationId: string;
  relatedEventKey: string;
  exDate: string;
  providerId: string;
  sourceEventId: string;
  sourceEventVersion: string;
  sourceEvidenceHash: string;
  entitlementSnapshotHash: string;
  entitlementSnapshotJson: string;
  grossImpactUsd: number;
  wholeEventGrossUsd: number;
  budgetMonth: string;
  attestedBy: string;
  attestedAtUtc: string;
  reason: string;
  evidenceReference: string;
  evidenceHash: string;
  confirmation: string;
  disclosure: string;
  voidedAtUtc?: string | null;
  voidedBy?: string | null;
  isActive: boolean;
  decisionKind: string;
  accountRoleBinding?: CashDividendAccountRoleBinding | null;
}

export interface CashDividendAttestedPosting {
  postingId: string;
  caseId: string;
  projectionId: string;
  attestationId: string;
  journalEntryId: string;
  ledgerBookId: string;
  periodId: string;
  accountingBasis: string;
  currency: string;
  totalDebits: number;
  totalCredits: number;
  postingStatus: string;
  taxLotMutationBatchId?: string | null;
  postedBy: string;
  postedAtUtc: string;
}

/** HTTP adapter supplies actor, tenant, company and authority; UI sends only reviewed bindings. */
export interface AttestCashDividendRequest {
  expectedVersion: number;
  idempotencyKey: string;
  projectionId: string;
  expectedDeclarationRevision: number;
  expectedEligibilityHash: string;
  reason: string;
  evidenceReference: string;
  evidenceHash: string;
  confirmation: string;
  scopeAssertion?: CorporateActionCaseScope | null;
}

export interface PostAttestedCashDividendRequest {
  expectedVersion: number;
  idempotencyKey: string;
  projectionId: string;
  attestationId: string;
  reason: string;
  scopeAssertion?: CorporateActionCaseScope | null;
}

export interface CashDividendAttestationResult {
  case: CorporateActionProcessingCaseDto;
  attestation: CashDividendAttestation;
  transition: { resultingVersion: number; toState: string; actor: string; reason: string };
  replayed: boolean;
}

export interface CashDividendPostingResult {
  case: CorporateActionProcessingCaseDto;
  posting: CashDividendAttestedPosting;
  transition: { resultingVersion: number; toState: string; actor: string; reason: string };
  replayed: boolean;
}

export interface CashDividendBookDeclaration {
  declarationId: string;
  revision: number;
  tenantId: string;
  companyId: string;
  ledgerBookId: string;
  enabled: boolean;
  principalId: string;
  policyVersion: string;
  policyHash: string;
  changedBy: string;
  recordedAtUtc: string;
  reason: string;
  accountRoleBinding?: CashDividendAccountRoleBinding | null;
}

export interface ChangeCashDividendBookDeclarationRequest {
  commandId: string;
  expectedRevision: number;
  ledgerBookId: string;
  enabled: boolean;
  policyVersion: string;
  policyHash: string;
  reason: string;
  accountRoleBinding?: CashDividendAccountRoleBinding | null;
}

export interface CashDividendDeclarationResult {
  declaration: CashDividendBookDeclaration;
  replayed: boolean;
}

/** All capabilities and eligibility come from the authenticated native accounting adapter. */
export interface CashDividendAttestationWorkspace {
  schemaVersion: number;
  availability: string;
  policyVersion: string;
  policyHash: string;
  policyEnabled: boolean;
  actor: string;
  scope: CorporateActionCaseScope;
  case: CorporateActionProcessingCaseDto;
  canAdministerDeclaration: boolean;
  canBindAccountingRoles?: boolean;
  canAttestAccounting: boolean;
  canPostAttestedAccounting: boolean;
  declaration: CashDividendBookDeclaration | null;
  declarationHistory?: CashDividendBookDeclaration[];
  preview: CashDividendAttestationPreview | null;
  attestation: CashDividendAttestation | null;
  posting: CashDividendAttestedPosting | null;
  disclosure: string;
  confirmation: string;
  relatedEventGrossLimitUsd?: number;
  bookCalendarMonthGrossLimitUsd?: number;
  evidenceReference: string | null;
  evidenceHash: string | null;
  blockers: string[];
}
