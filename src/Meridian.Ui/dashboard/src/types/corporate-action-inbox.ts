/** Durable corporate-action inbox, source proposals, case evidence and retained election cutoffs. */
import type { CorporateAction } from "./workstation-7";

export type CorporateActionCaseStatus =
  | "Detected"
  | "NeedsTerms"
  | "Disputed"
  | "TermsConfirmed"
  | "ElectionPending"
  | "ElectionSubmitted"
  | "AllocationPending"
  | "AccountingReview"
  | "ReadyForApproval"
  | "Approved"
  | "Scheduled"
  | "Posted"
  | "Reconciled"
  | "Reported"
  | "Closed"
  | "Blocked"
  | "Cancelled"
  | "Superseded"
  | "RestatementRequired";

export type CorporateActionConflictState = "None" | "Open" | "Resolved" | "Waived";
export type CorporateActionPermissionState = "Allowed" | "Denied" | "ServerChecked";

export interface CorporateActionCaseScope {
  tenantId: string;
  companyId: string;
  structureNodeId?: string | null;
  fundProfileId?: string | null;
  financialAccountId?: string | null;
  portfolioId?: string | null;
  custodyAccountId?: string | null;
  ledgerBookId?: string | null;
  periodId?: string | null;
  accountingBasis?: string | null;
  functionalCurrency?: string | null;
  jurisdiction?: string | null;
}

export type CorporateActionSourceProposalState =
  | "Observed"
  | "ReviewRequired"
  | "Accepted"
  | "Rejected"
  | "Superseded";

export interface CorporateActionSourceProposalActionAvailability {
  canAccept: boolean;
  canReject: boolean;
  canCompareEvidence: boolean;
  blockers: string[];
}

/**
 * Exact compact action posture returned with CorporateActionProcessingCaseDto. This is not the
 * richer case-workspace command contract: it only describes the generic durable-case operations
 * currently exposed by the API.
 */
export interface CorporateActionProcessingCaseActionAvailabilityDto {
  canAddEvidence: boolean;
  canRecordConflict: boolean;
  canResolveConflict: boolean;
  canManageOptions: boolean;
  canTransition: boolean;
  canApproveAccounting: boolean;
  allowedTransitionTargets: string[];
  blockers: string[];
}

export interface CorporateActionCaseSourceSnapshotDto {
  proposedAction: CorporateAction;
  providerIdentity: {
    providerId: string;
    sourceEventId: string;
    sourceEventVersion: string;
    observedAtUtc: string;
    evidenceHash?: string | null;
    evidenceReference?: string | null;
    releaseStatus: "ReviewOnly" | "AcceptanceEligible" | string;
  };
  displayMetadata?: {
    ticker: string;
    winningSource: string;
    agreeingSources: string[];
    dissentingSources: string[];
    dissentingFields?: Array<{
      field: string;
      candidates: Array<{
        source: string;
        value: unknown;
        evidenceReference?: string | null;
      }>;
    }> | null;
  } | null;
}

/** Wire contract for the top-level `cases` collection in CorporateActionDurableInboxDto. */
export interface CorporateActionProcessingCaseDto {
  caseId: string;
  proposalId: string;
  corporateActionId: string;
  securityId: string;
  scope: CorporateActionCaseScope;
  state: string;
  version: number;
  methodologyProfileId?: string | null;
  assignedTo?: string | null;
  blockedReason?: string | null;
  createdBy: string;
  createdAtUtc: string;
  updatedBy: string;
  updatedAtUtc: string;
  actionAvailability?: CorporateActionProcessingCaseActionAvailabilityDto | null;
  sourceSnapshot?: CorporateActionCaseSourceSnapshotDto | null;
  electionCalendar?: CorporateActionElectionCalendar | null;
}

/** Server projection of actual retained source cutoffs; never inferred from corporate dates. */
export interface CorporateActionElectionCalendar {
  evidenceState: "Available" | "MissingEvidence" | "InvalidEvidence";
  cutoffs: Array<{
    id: string;
    label: string;
    /** ISO timestamp including its retained offset. */
    deadlineAt: string;
    source: string;
    evidenceReference: string;
  }>;
  messages: string[];
}

export interface CorporateActionCaseActionAvailability {
  canAcceptCanonicalFact: boolean;
  acceptCanonicalFactDisabledReason?: string | null;
  canSubmitElection?: boolean;
  submitElectionDisabledReason?: string | null;
  canApproveTreatment?: boolean;
  approveTreatmentDisabledReason?: string | null;
  canPost?: boolean;
  postDisabledReason?: string | null;
}

export interface CorporateActionSourceFact {
  field: string;
  value: string;
  source: string;
  agreesWithWinner: boolean;
  observedAt?: string | null;
  evidenceId?: string | null;
}

export interface CorporateActionEntitlementPreview {
  scopeLabel: string;
  recordDatePosition: string;
  entitledQuantity: string;
  expectedCash: string;
  evidenceVersion: string;
}

export interface CorporateActionElectionPreview {
  electionId?: string | null;
  optionLabel: string;
  quantity: string;
  deadline?: string | null;
  status: string;
  evidenceId?: string | null;
}

export interface CorporateActionBasisComparison {
  basis: string;
  treatment: string;
  taxability: string;
  bookValueEffect: string;
  gainLossRecognition: string;
  holdingPeriodTreatment: string;
  policyVersion?: string | null;
  status: string;
}

export interface CorporateActionLotPreview {
  lotId: string;
  operation: string;
  quantityBefore: string;
  quantityAfter: string;
  bookValueBefore: string;
  bookValueAfter: string;
  basis: string;
}

export interface CorporateActionJournalPreview {
  lineId: string;
  basis: string;
  account: string;
  debit: string;
  credit: string;
  currency: string;
}

export interface CorporateActionReconciliationPreview {
  control: string;
  expected: string;
  actual: string;
  variance: string;
  status: string;
}

export interface CorporateActionCaseHistoryEntry {
  eventId: string;
  atUtc: string;
  actor: string;
  action: string;
  fromStatus?: string | null;
  toStatus?: string | null;
  evidenceId?: string | null;
}

/**
 * Additive durable-case projection. The current inbox endpoint may omit this object; the browser
 * then shows the missing server-owned evidence and policy states explicitly instead of deriving
 * accounting treatment or permission locally.
 */
export interface CorporateActionCaseProjection {
  caseId: string;
  proposalId: string;
  version: number;
  status: CorporateActionCaseStatus;
  assignedTo?: string | null;
  /** Null when the compact inbox case contract does not project conflict status. */
  conflictState?: CorporateActionConflictState | null;
  /** Null when no exact workspace-command authorization projection was returned. */
  permissionState?: CorporateActionPermissionState | null;
  scope: CorporateActionCaseScope;
  receivedAt?: string | null;
  dueAt?: string | null;
  sourceFacts: CorporateActionSourceFact[];
  entitlement?: CorporateActionEntitlementPreview | null;
  elections: CorporateActionElectionPreview[];
  basisComparisons: CorporateActionBasisComparison[];
  lotPreview: CorporateActionLotPreview[];
  journalPreview: CorporateActionJournalPreview[];
  reconciliation: CorporateActionReconciliationPreview[];
  history: CorporateActionCaseHistoryEntry[];
  proofReferences: string[];
  actionAvailability: CorporateActionCaseActionAvailability;
}

export interface CorporateActionProposalEntry {
  electionCalendar?: CorporateActionElectionCalendar | null;
  securityId: string;
  ticker: string;
  actionType: string;
  exDate: string;
  recordDate: string | null;
  payableDate: string | null;
  amount: number | null;
  currency: string | null;
  splitFromFactor: number | null;
  splitToFactor: number | null;
  winningSource: string;
  agreeingSources: string[];
  dissentingSources: string[];
  autoApplied: boolean;
  /** Durable source-proposal command identity and optimistic-concurrency token. */
  proposalId?: string | null;
  version?: number | null;
  proposalState?: CorporateActionSourceProposalState | null;
  /** Exact scope selected or supplied by the server for case creation. */
  acceptanceScope?: CorporateActionCaseScope | null;
  /** Server-owned source-proposal command posture. */
  actionAvailability?: CorporateActionSourceProposalActionAvailability | null;
}

export interface CorporateActionInboxResponse {
  lastIngestAt: string | null;
  stagedCount: number;
  appliedLastRun: number;
  duplicatesSkippedLastRun: number;
  staged: CorporateActionProposalEntry[];
  errors: string[];
  /** Durable cases remain here after their accepted source proposal leaves `staged`. */
  cases: CorporateActionProcessingCaseDto[];
}
