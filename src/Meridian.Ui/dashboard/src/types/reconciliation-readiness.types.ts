/**
 * Reconciliation queue readiness types.
 *
 * Mirrors `Meridian.Ui.Shared.Contracts.Reconciliation.StatementImportContracts`
 * and `Meridian.Contracts.Workstation.ReconciliationDtos`, the contracts behind:
 *
 * - `GET /api/workstation/reconciliation/queue-status`
 * - `GET /api/workstation/reconciliation/cases`
 * - `GET /api/workstation/reconciliation/break-queue/taxonomy`
 *
 * These three carry no enums: queue state, SLA state, and casework codes are all
 * plain strings on the wire, so nothing here needs ordinal resolution.
 */

/** `ReconciliationQueueAccountStatusDto`. */
export interface ReconciliationQueueAccountStatus {
  accountId: string;
  accountCode: string;
  queueState: string;
  unresolvedBreakCount: number;
  signOffReady: boolean;
  /** Server-decided next step; the browser presents it, it does not derive one. */
  nextBestAction: string;
  blockerReason: string;
  evidenceLinks: string[];
}

/** `ReconciliationCaseSummaryDto`. */
export interface ReconciliationCaseSummary {
  caseId: string;
  importId: string;
  status: string;
  reason: string;
  confidence: number;
  rationale: string;
  createdAtUtc: string;
  assignee?: string | null;
  priority: string;
  slaPolicyId?: string | null;
  slaDueAtUtc?: string | null;
  slaWarningAtUtc?: string | null;
  slaBreachedAtUtc?: string | null;
  slaState: string;
  ageBand?: string | null;
  businessAgeHours: number;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  resolutionNote?: string | null;
  signedOffBy?: string | null;
  signedOffAtUtc?: string | null;
  reopenedBy?: string | null;
  reopenedAtUtc?: string | null;
  reopenReason?: string | null;
  version: number;
}

/** `ReconciliationTaxonomyValue`. */
export interface ReconciliationTaxonomyValue {
  code: string;
  displayName: string;
  version: number;
  isActive: boolean;
  requiredEvidencePrefixes?: string[] | null;
}

/** `ReconciliationTaxonomySnapshot`. */
export interface ReconciliationTaxonomySnapshot {
  version: number;
  rootCauses: ReconciliationTaxonomyValue[];
  resolutionCodes: ReconciliationTaxonomyValue[];
}
