import { useCallback, useMemo, useState } from "react";
import { describeApiError, isAbortError } from "@/lib/api-errors";
import {
  CAPITAL_CALL_ALLOCATION_BASIS,
  CAPITAL_CALL_INTAKE_READINESS,
  COMMITMENT_STATUS_ACTIVE,
  runCapitalCallIssuanceIntake,
  type AutomatedJournalEvidenceAssessment,
  type CapitalCallAllocationBasisCode,
  type CapitalCallIssuanceIntakeRunResult,
  type RunCapitalCallIssuanceIntakeRequest
} from "@/lib/api/capital-call-issuance.api";
import type { ManualJournalEntryDraft } from "@/types";

export type CapitalCallAllocationBasisChoice = "pro-rata-uncalled" | "pro-rata-total-commitment";

export interface CapitalCallCommitmentFormRow {
  key: string;
  commitmentId: string;
  capitalAccountId: string;
  investorId: string;
  /** Kept as the operator's raw text; parsed at validation time. Never defaulted. */
  totalCommitment: string;
  /** ISO date, "yyyy-MM-dd". */
  commitmentDate: string;
  evidenceLink: string;
}

export interface CapitalCallIssuanceFormState {
  fundProfileId: string;
  ledgerBookId: string;
  currency: string;
  callId: string;
  /** Kept as the operator's raw text; parsed at validation time. Never defaulted. */
  amountToCall: string;
  noticeDate: string;
  dueDate: string;
  periodId: string;
  entityId: string;
  purpose: string;
  allocationBasis: CapitalCallAllocationBasisChoice;
  commitments: CapitalCallCommitmentFormRow[];
}

let commitmentRowCounter = 0;

export function createCommitmentFormRow(): CapitalCallCommitmentFormRow {
  commitmentRowCounter += 1;
  return {
    key: `commitment-row-${commitmentRowCounter}`,
    commitmentId: "",
    capitalAccountId: "",
    investorId: "",
    totalCommitment: "",
    commitmentDate: "",
    evidenceLink: ""
  };
}

export function createInitialCapitalCallIssuanceForm(): CapitalCallIssuanceFormState {
  return {
    fundProfileId: "",
    ledgerBookId: "",
    currency: "",
    callId: "",
    amountToCall: "",
    noticeDate: "",
    dueDate: "",
    periodId: "",
    entityId: "",
    purpose: "",
    allocationBasis: "pro-rata-uncalled",
    commitments: [createCommitmentFormRow()]
  };
}

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const CURRENCY_PATTERN = /^[A-Za-z]{3}$/;

function parsePositiveAmount(raw: string): number | null {
  const trimmed = raw.trim();
  if (!trimmed) {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

/**
 * Client-side mirror of the server's ArgumentException rules
 * (`CapitalCallIssuanceDraftProducer.Validate` + `InvestorCommitment` invariants), plus the
 * two conditions that deterministically block or degrade a run: a missing ledger book
 * (drafts land book-missing) and a commitment line without retained register evidence
 * (the server blocks the whole run).
 */
export function validateCapitalCallIssuanceForm(form: CapitalCallIssuanceFormState): string[] {
  const issues: string[] = [];

  if (!form.fundProfileId.trim()) {
    issues.push("Fund profile identifier is required.");
  }

  const ledgerBookId = form.ledgerBookId.trim();
  if (!ledgerBookId) {
    issues.push("Ledger book is required — without it the drafts land in the queue as book-missing.");
  } else if (!GUID_PATTERN.test(ledgerBookId)) {
    issues.push("Ledger book identifier must be a GUID (find it in the Ledger explorer).");
  }

  if (!CURRENCY_PATTERN.test(form.currency.trim())) {
    issues.push("Capital-call accounting currency must be a three-letter ISO code.");
  }

  if (!form.callId.trim()) {
    issues.push("Capital-call identifier is required.");
  }

  if (parsePositiveAmount(form.amountToCall) === null) {
    issues.push("Amount to call must be a positive number.");
  }

  const noticeDateValid = ISO_DATE_PATTERN.test(form.noticeDate);
  const dueDateValid = ISO_DATE_PATTERN.test(form.dueDate);
  if (!noticeDateValid) {
    issues.push("Notice date is required.");
  }

  if (!dueDateValid) {
    issues.push("Due date is required.");
  }

  if (noticeDateValid && dueDateValid && form.dueDate < form.noticeDate) {
    issues.push("Capital-call due date cannot precede the notice date.");
  }

  if (form.commitments.length === 0) {
    issues.push("At least one commitment-register line is required.");
  }

  form.commitments.forEach((row, index) => {
    const line = `Commitment line ${index + 1}`;
    if (!row.commitmentId.trim()) {
      issues.push(`${line}: commitment identifier is required.`);
    }

    if (!row.capitalAccountId.trim()) {
      issues.push(`${line}: capital account identifier is required.`);
    }

    if (!row.investorId.trim()) {
      issues.push(`${line}: investor identifier is required.`);
    }

    if (parsePositiveAmount(row.totalCommitment) === null) {
      issues.push(`${line}: total commitment must be a positive number.`);
    }

    if (!ISO_DATE_PATTERN.test(row.commitmentDate)) {
      issues.push(`${line}: commitment date is required.`);
    }

    if (!row.evidenceLink.trim()) {
      issues.push(`${line}: a retained commitment-register evidence link is required; without it the server blocks the run.`);
    }
  });

  return issues;
}

const ALLOCATION_BASIS_CODES: Record<CapitalCallAllocationBasisChoice, CapitalCallAllocationBasisCode> = {
  "pro-rata-uncalled": CAPITAL_CALL_ALLOCATION_BASIS.proRataByUncalled,
  "pro-rata-total-commitment": CAPITAL_CALL_ALLOCATION_BASIS.proRataByTotalCommitment
};

/**
 * Map a validated form to the wire request. Enums travel as their numeric codes
 * (`allocationBasis`, commitment `status`), dates as "yyyy-MM-dd" strings, and no actor
 * field is sent — the server resolves the operator from the authenticated session.
 */
export function buildCapitalCallIssuanceRequest(
  form: CapitalCallIssuanceFormState
): RunCapitalCallIssuanceIntakeRequest {
  const periodId = form.periodId.trim();
  const entityId = form.entityId.trim();
  const purpose = form.purpose.trim();

  return {
    fundProfileId: form.fundProfileId.trim(),
    currency: form.currency.trim().toUpperCase(),
    callId: form.callId.trim(),
    amountToCall: parsePositiveAmount(form.amountToCall) ?? 0,
    noticeDate: form.noticeDate,
    dueDate: form.dueDate,
    allocationBasis: ALLOCATION_BASIS_CODES[form.allocationBasis],
    ledgerBookId: form.ledgerBookId.trim(),
    purpose: purpose ? purpose : null,
    periodId: periodId ? periodId : null,
    entityId: entityId ? entityId : null,
    commitments: form.commitments.map((row) => ({
      commitmentId: row.commitmentId.trim(),
      capitalAccountId: row.capitalAccountId.trim(),
      investorId: row.investorId.trim(),
      totalCommitment: parsePositiveAmount(row.totalCommitment) ?? 0,
      commitmentDate: row.commitmentDate,
      status: COMMITMENT_STATUS_ACTIVE,
      evidenceLinks: [row.evidenceLink.trim()]
    }))
  };
}

export type CapitalCallRunTone = "success" | "warning" | "danger";

export interface CapitalCallRunOutcomeView {
  tone: CapitalCallRunTone;
  title: string;
  detail: string;
  blockers: string[];
}

export interface CapitalCallCreatedDraftView {
  journalEntryId: string;
  memo: string;
  amountLabel: string;
  status: string;
  investorId: string | null;
  capitalAccountId: string | null;
  assessment: CapitalCallAssessmentView | null;
}

export interface CapitalCallAssessmentView {
  quality: "Low" | "Medium" | "High";
  confidenceLabel: string;
  summary: string;
  /** Verbatim assessment reasons, including the Medium first-call sole-basis warning. */
  reasons: string[];
  requiresInvestigation: boolean;
}

export interface CapitalCallSkipView {
  subject: string;
  reason: string;
}

function formatAmount(value: number, currency: string): string {
  return `${value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;
}

function toAssessmentView(assessment: AutomatedJournalEvidenceAssessment): CapitalCallAssessmentView {
  return {
    quality: assessment.quality,
    confidenceLabel: `${Math.round(assessment.confidenceScore * 100)}%`,
    summary: assessment.summary,
    reasons: assessment.reasons ?? [],
    requiresInvestigation: assessment.requiresInvestigation
  };
}

/**
 * Honest run outcome: a blocked run is presented as blocked with every server reason
 * verbatim, never with success framing; a ready run that created nothing says so.
 */
export function presentCapitalCallRunOutcome(
  result: CapitalCallIssuanceIntakeRunResult
): CapitalCallRunOutcomeView {
  if (result.readiness === CAPITAL_CALL_INTAKE_READINESS.blocked) {
    return {
      tone: "danger",
      title: "Run blocked — no drafts entered the approval queue",
      detail: "The server refused to draft this capital call. Every reason is listed below, verbatim.",
      blockers: result.readinessBlockers
    };
  }

  if (result.readiness === CAPITAL_CALL_INTAKE_READINESS.needsInvestigation) {
    return {
      tone: "warning",
      title: "Run needs investigation before approval",
      detail: "Drafts cannot enter the human approval lifecycle until the flagged evidence is resolved.",
      blockers: result.readinessBlockers
    };
  }

  const createdCount = result.intake.created.length;
  if (createdCount === 0) {
    return {
      tone: "warning",
      title: "Run completed but created no new drafts",
      detail: "Existing drafts or skips covered every commitment; see the skip reasons below.",
      blockers: result.readinessBlockers
    };
  }

  const needsFix = result.intake.needsFixCount;
  return {
    tone: "success",
    title: `${createdCount} issuance draft${createdCount === 1 ? "" : "s"} queued for approval`,
    detail: needsFix > 0
      ? `Drafts are in the manual-journal approval queue — nothing has been posted. ${needsFix} draft${needsFix === 1 ? " needs" : "s need"} account-mapping fixes before submission.`
      : "Drafts are in the manual-journal approval queue — nothing has been posted.",
    blockers: result.readinessBlockers
  };
}

export function presentCreatedDrafts(
  result: CapitalCallIssuanceIntakeRunResult
): CapitalCallCreatedDraftView[] {
  return result.intake.created.map((draft: ManualJournalEntryDraft) => {
    const idempotencyKey = draft.treasuryContext?.idempotencyKey ?? null;
    const assessment = idempotencyKey ? result.evidenceAssessments[idempotencyKey] ?? null : null;
    return {
      journalEntryId: draft.journalEntryId,
      memo: draft.memo,
      amountLabel: formatAmount(draft.totalDebits, draft.currency),
      status: draft.status,
      investorId: draft.treasuryContext?.investorId ?? null,
      capitalAccountId: draft.treasuryContext?.capitalAccountId ?? null,
      assessment: assessment ? toAssessmentView(assessment) : null
    };
  });
}

/** Run-level assessments that no created draft claimed (e.g. the blocked-run assessment). */
export function presentRunLevelAssessments(
  result: CapitalCallIssuanceIntakeRunResult
): CapitalCallAssessmentView[] {
  const claimedKeys = new Set(
    result.intake.created
      .map((draft) => draft.treasuryContext?.idempotencyKey)
      .filter((key): key is string => Boolean(key))
  );

  return Object.entries(result.evidenceAssessments)
    .filter(([key]) => !claimedKeys.has(key))
    .map(([, assessment]) => toAssessmentView(assessment));
}

export function presentSkips(result: CapitalCallIssuanceIntakeRunResult): CapitalCallSkipView[] {
  return [
    ...result.producerSkips.map((skip) => ({ subject: skip.subject, reason: skip.reason })),
    ...result.intake.skipped.map((skip) => ({ subject: skip.idempotencyKey, reason: skip.reason }))
  ];
}

export interface CapitalCallIssuanceServices {
  runIntake: typeof runCapitalCallIssuanceIntake;
}

const defaultServices: CapitalCallIssuanceServices = {
  runIntake: runCapitalCallIssuanceIntake
};

export interface CapitalCallIssuanceViewModel {
  form: CapitalCallIssuanceFormState;
  validationIssues: string[];
  armed: boolean;
  busy: boolean;
  result: CapitalCallIssuanceIntakeRunResult | null;
  submitError: { summary: string; details: string[] } | null;
  submitLabel: string;
  armedNotice: string | null;
  updateField: <K extends keyof CapitalCallIssuanceFormState>(
    field: K,
    value: CapitalCallIssuanceFormState[K]
  ) => void;
  updateCommitment: (
    key: string,
    field: keyof Omit<CapitalCallCommitmentFormRow, "key">,
    value: string
  ) => void;
  addCommitmentRow: () => void;
  removeCommitmentRow: (key: string) => void;
  disarm: () => void;
  submit: () => Promise<void>;
}

/**
 * State machine for the governed issuance form. Submission uses the armed-confirm idiom:
 * the first activation validates and arms; only the explicit confirm activation posts.
 * Any edit disarms, so the operator always confirms exactly what will be sent.
 */
export function useCapitalCallIssuanceViewModel(
  services: CapitalCallIssuanceServices = defaultServices
): CapitalCallIssuanceViewModel {
  const [form, setForm] = useState<CapitalCallIssuanceFormState>(createInitialCapitalCallIssuanceForm);
  const [validationIssues, setValidationIssues] = useState<string[]>([]);
  const [armed, setArmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CapitalCallIssuanceIntakeRunResult | null>(null);
  const [submitError, setSubmitError] = useState<{ summary: string; details: string[] } | null>(null);

  const updateField = useCallback(<K extends keyof CapitalCallIssuanceFormState>(
    field: K,
    value: CapitalCallIssuanceFormState[K]
  ) => {
    setForm((current) => ({ ...current, [field]: value }));
    setArmed(false);
  }, []);

  const updateCommitment = useCallback((
    key: string,
    field: keyof Omit<CapitalCallCommitmentFormRow, "key">,
    value: string
  ) => {
    setForm((current) => ({
      ...current,
      commitments: current.commitments.map((row) => (
        row.key === key ? { ...row, [field]: value } : row
      ))
    }));
    setArmed(false);
  }, []);

  const addCommitmentRow = useCallback(() => {
    setForm((current) => ({
      ...current,
      commitments: [...current.commitments, createCommitmentFormRow()]
    }));
    setArmed(false);
  }, []);

  const removeCommitmentRow = useCallback((key: string) => {
    setForm((current) => ({
      ...current,
      commitments: current.commitments.filter((row) => row.key !== key)
    }));
    setArmed(false);
  }, []);

  const disarm = useCallback(() => {
    setArmed(false);
  }, []);

  const submit = useCallback(async () => {
    if (busy) {
      return;
    }

    const issues = validateCapitalCallIssuanceForm(form);
    setValidationIssues(issues);
    if (issues.length > 0) {
      setArmed(false);
      return;
    }

    if (!armed) {
      setArmed(true);
      return;
    }

    setArmed(false);
    setBusy(true);
    setSubmitError(null);
    setResult(null);
    try {
      const response = await services.runIntake(buildCapitalCallIssuanceRequest(form));
      setResult(response);
    } catch (error) {
      if (!isAbortError(error)) {
        setSubmitError(describeApiError(error, "Capital-call issuance intake failed."));
      }
    } finally {
      setBusy(false);
    }
  }, [armed, busy, form, services]);

  const submitLabel = armed ? "Confirm — queue issuance drafts" : "Queue issuance drafts";
  const armedNotice = armed
    ? "This drafts one governed journal entry per allocated commitment into the manual-journal approval queue. Nothing is posted. Select Confirm to proceed."
    : null;

  return useMemo(() => ({
    form,
    validationIssues,
    armed,
    busy,
    result,
    submitError,
    submitLabel,
    armedNotice,
    updateField,
    updateCommitment,
    addCommitmentRow,
    removeCommitmentRow,
    disarm,
    submit
  }), [
    addCommitmentRow,
    armed,
    armedNotice,
    busy,
    disarm,
    form,
    removeCommitmentRow,
    result,
    submit,
    submitError,
    submitLabel,
    updateCommitment,
    updateField,
    validationIssues
  ]);
}
