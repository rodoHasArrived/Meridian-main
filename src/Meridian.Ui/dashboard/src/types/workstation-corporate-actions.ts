import type {
  CorporateAction,
  CorporateActionCaseScope,
  CorporateActionCaseStatus,
  CorporateActionSourceProposalState,
} from "./workstation-7";

export interface CorporateActionInboxAcceptRequest {
  proposalId: string;
  expectedVersion: number;
  idempotencyKey: string;
  scope: CorporateActionCaseScope;
}

export interface CorporateActionAudit {
  auditId: string;
  securityId: string;
  corporateActionId: string;
  eventType: string;
  sourceSystem: string;
  actor: string;
  recordedAtUtc: string;
  sourceRecordId: string | null;
  reason: string | null;
  correlationId: string | null;
}

export interface CorporateActionRestatementEvidenceLink {
  evidenceId: string;
  label: string;
  route: string | null;
  source: string;
  capturedAtUtc?: string | null;
}

export interface CorporateActionRestatementChangedLine {
  lineKey: string;
  previousValue: string;
  currentValue: string;
  evidenceLinks?: CorporateActionRestatementEvidenceLink[] | null;
}

export interface CorporateActionRestatementCandidate {
  reportId: string;
  priorVersionReportId: string;
  periodLabel: string;
  summary: string;
  changedLines: CorporateActionRestatementChangedLine[];
}

export interface CorporateActionRestatementResult {
  restatementRequired: boolean;
  candidates: CorporateActionRestatementCandidate[];
  evaluationStatus?: "Evaluated" | "PendingPeriodValidation" | string;
}

export interface CorporateActionConflictCandidate {
  source: string;
  value: unknown;
  evidenceReference?: string | null;
}

export interface CorporateActionConflict {
  conflictId: string;
  caseId: string;
  field: string;
  description: string;
  candidates: CorporateActionConflictCandidate[];
  state: "Open" | "Resolved" | "Waived" | string;
  resolution?: string | null;
  caseVersion: number;
  recordedBy: string;
  recordedAtUtc: string;
  resolvedBy?: string | null;
  resolvedAtUtc?: string | null;
  resolutionEvidenceReference?: string | null;
  resolutionEvidenceHash?: string | null;
}

export interface CorporateActionInboxAcceptResult {
  corporateAction: CorporateAction;
  audit: CorporateActionAudit;
  restatement?: CorporateActionRestatementResult | null;
  proposal?: {
    proposalId: string;
    state: CorporateActionSourceProposalState;
    version: number;
  } | null;
  case?: {
    caseId: string;
    proposalId: string;
    corporateActionId: string;
    securityId: string;
    scope: CorporateActionCaseScope;
    state: CorporateActionCaseStatus;
    version: number;
    assignedTo?: string | null;
  } | null;
  initialTransition?: {
    transitionId: string;
    caseId: string;
    fromState?: string | null;
    toState: CorporateActionCaseStatus;
    expectedVersion: number;
    resultingVersion: number;
    actor: string;
    reason: string;
    idempotencyKey: string;
    occurredAtUtc: string;
    correlationId?: string | null;
  } | null;
  sourceConflict?: CorporateActionConflict | null;
  replayed?: boolean;
}
