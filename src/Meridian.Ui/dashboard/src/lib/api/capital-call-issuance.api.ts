/**
 * Client for the governed capital-call issuance intake endpoint
 * (`POST /api/ledger/journal-automation/capital-call-issuance-intake`).
 *
 * Wire-shape notes, mirrored from `RunCapitalCallIssuanceDraftIntakeRequest` /
 * `AutomatedJournalIntakeRunResult` in `src/Meridian.Ui.Shared`:
 *
 * - The endpoint binds with ASP.NET Core web defaults: enums WITHOUT an attribute
 *   converter travel as numbers. `allocationBasis` (CapitalCallAllocationBasis) and
 *   commitment `status` (CommitmentStatus) are therefore numeric codes on the wire,
 *   and the response `readiness` (AutomatedJournalIntakeReadiness) and skip
 *   `disposition` come back as numbers too.
 * - Enums WITH `JsonStringEnumConverter` attributes stay strings: draft `status`
 *   (ManualJournalEntryStatusDto) and evidence-assessment `quality`
 *   (AutomatedJournalEvidenceQualityDto → "Low" | "Medium" | "High").
 * - `DateOnly` values travel as ISO "yyyy-MM-dd" strings; `ledgerBookId` is a GUID string.
 * - The request carries NO actor field: the server always resolves the acting operator
 *   from the authenticated session (`ResolveMutationActor`), the same as the
 *   reconciliation break-review client. Sending one would be ignored.
 */

import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { ManualJournalEntryDraft, ManualJournalEntryStatus } from "@/types";

/** Numeric wire codes for `CapitalCallAllocationBasis`. */
export const CAPITAL_CALL_ALLOCATION_BASIS = {
  proRataByUncalled: 0,
  proRataByTotalCommitment: 1
} as const;

export type CapitalCallAllocationBasisCode =
  (typeof CAPITAL_CALL_ALLOCATION_BASIS)[keyof typeof CAPITAL_CALL_ALLOCATION_BASIS];

/** Numeric wire code for `CommitmentStatus.Active` — the only status this surface submits. */
export const COMMITMENT_STATUS_ACTIVE = 0;

/** Numeric wire codes for `AutomatedJournalIntakeReadiness`. */
export const CAPITAL_CALL_INTAKE_READINESS = {
  ready: 0,
  needsInvestigation: 1,
  blocked: 2
} as const;

export type CapitalCallIntakeReadinessCode =
  (typeof CAPITAL_CALL_INTAKE_READINESS)[keyof typeof CAPITAL_CALL_INTAKE_READINESS];

/** One operator-attested commitment-register line backing the call (wire shape). */
export interface CapitalCallCommitmentInputWire {
  commitmentId: string;
  capitalAccountId: string;
  investorId: string;
  totalCommitment: number;
  /** ISO date, "yyyy-MM-dd". */
  commitmentDate: string;
  /** Numeric `CommitmentStatus` code (0 = Active). */
  status: number;
  evidenceLinks: string[];
}

/** Wire shape of `RunCapitalCallIssuanceDraftIntakeRequest` (actor/tenant are server-resolved). */
export interface RunCapitalCallIssuanceIntakeRequest {
  fundProfileId: string;
  currency: string;
  callId: string;
  amountToCall: number;
  /** ISO date, "yyyy-MM-dd". */
  noticeDate: string;
  /** ISO date, "yyyy-MM-dd". */
  dueDate: string;
  commitments: CapitalCallCommitmentInputWire[];
  /** Numeric `CapitalCallAllocationBasis` code. */
  allocationBasis: CapitalCallAllocationBasisCode;
  /** GUID of the ledger book; required for drafts to land clean (book-missing otherwise). */
  ledgerBookId: string;
  purpose?: string | null;
  periodId?: string | null;
  entityId?: string | null;
}

export type AutomatedJournalEvidenceQuality = "Low" | "Medium" | "High";

/** Wire shape of `AutomatedJournalEvidenceAssessmentDto`. */
export interface AutomatedJournalEvidenceAssessment {
  assessmentCode: string;
  confidenceScore: number;
  quality: AutomatedJournalEvidenceQuality;
  requiresInvestigation: boolean;
  summary: string;
  reasons: string[];
  evidenceLinks: string[];
}

/** Wire shape of `AutomatedJournalEventProductionSkip`. */
export interface AutomatedJournalProducerSkip {
  subject: string;
  reason: string;
}

/** Wire shape of `AutomatedJournalDraftIntakeSkip` (`disposition` is a numeric code). */
export interface CapitalCallIssuanceIntakeSkip {
  journalEntryId: string;
  idempotencyKey: string;
  reason: string;
  disposition: number;
  existingStatus?: ManualJournalEntryStatus | null;
  existingEvidenceAssessment?: AutomatedJournalEvidenceAssessment | null;
}

/** Wire shape of `AutomatedJournalDraftIntakeResult`. */
export interface CapitalCallIssuanceIntakeResult {
  created: ManualJournalEntryDraft[];
  skipped: CapitalCallIssuanceIntakeSkip[];
  needsFixCount: number;
}

/**
 * Wire shape of `AutomatedJournalIntakeRunResult`. `evidenceAssessments` is keyed by draft
 * idempotency key (joinable via each created draft's `treasuryContext.idempotencyKey`); a
 * blocked run instead carries one run-level assessment keyed by the run key.
 */
export interface CapitalCallIssuanceIntakeRunResult {
  producerSkips: AutomatedJournalProducerSkip[];
  intake: CapitalCallIssuanceIntakeResult;
  evidenceAssessments: Record<string, AutomatedJournalEvidenceAssessment>;
  readiness: number;
  readinessBlockers: string[];
}

/**
 * Plan a fund-level capital call into governed per-LP issuance drafts. Drafts land in the
 * manual-journal approval queue and are never posted by this call; blocked runs return
 * explicit `readinessBlockers` instead of drafts.
 */
export function runCapitalCallIssuanceIntake(
  request: RunCapitalCallIssuanceIntakeRequest,
  options: ApiRequestOptions = {}
): Promise<CapitalCallIssuanceIntakeRunResult> {
  return apiPostJson<CapitalCallIssuanceIntakeRunResult>(
    UI_API_ROUTES.LedgerJournalAutomationCapitalCallIssuanceIntake,
    request,
    options
  );
}
