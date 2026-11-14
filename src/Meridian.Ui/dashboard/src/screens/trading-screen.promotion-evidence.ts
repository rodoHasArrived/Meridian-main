import type { ApprovePromotionRequest, RejectPromotionRequest } from "@/lib/api";
import type { PromotionEvaluationResult } from "@/types";
import { formatDecimal, formatPercent } from "@/lib/format";

/** Pure promotion form normalization and keyed evidence checks; command transport stays in the view model. */
export type PromotionGateField =
  | "runId"
  | "approvedBy"
  | "approvalReason"
  | "rejectionReason"
  | "reviewNotes"
  | "manualOverrideId"
  | "evidenceReferences";

export interface PromotionGateForm {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  rejectionReason: string;
  reviewNotes: string;
  manualOverrideId: string;
  evidenceReferences: string;
  approvalChecklist: string[];
}

export const emptyPromotionGateForm: PromotionGateForm = {
  runId: "",
  approvedBy: "",
  approvalReason: "",
  rejectionReason: "",
  reviewNotes: "",
  manualOverrideId: "",
  evidenceReferences: "",
  approvalChecklist: []
};

export const paperPromotionApprovalChecklist: string[] = [
  "DK1_TRUST_PACKET_REVIEWED",
  "RUN_LINEAGE_REVIEWED",
  "PORTFOLIO_LEDGER_CONTINUITY_REVIEWED",
  "RISK_CONTROLS_REVIEWED"
];

export const livePromotionApprovalChecklist: string[] = [
  ...paperPromotionApprovalChecklist,
  "PAPER_VALIDATION_REVIEWED",
  "RECONCILIATION_EVIDENCE_REVIEWED",
  "BROKER_EXECUTION_RECONCILIATION_REVIEWED",
  "ACCOUNTING_RECORDS_REVIEWED",
  "GOVERNED_REPORTING_REVIEWED",
  "GOVERNANCE_SIGNOFF_REVIEWED",
  "EXCEPTION_HANDLING_REVIEWED",
  "ROLLBACK_KILL_SWITCH_REVIEWED",
  "AUDIT_RETENTION_REVIEWED",
  "LIVE_OVERRIDE_REVIEWED"
];

export function validatePromotionApproval(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!trimmedForm.runId) {
    return "Run id, operator, and approval reason are required.";
  }

  if (!evaluation) {
    return "Evaluate gate checks before confirming promotion.";
  }

  if (!isPromotionEvaluationForRun(evaluation, trimmedForm.runId)) {
    return `Evaluate gate checks for ${trimmedForm.runId} before confirming promotion.`;
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Promotion gate is blocked.";
  }

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Run id, operator, and approval reason are required.";
  }

  const liveTarget = isLivePromotionTarget(evaluation);
  const requiredChecklist = liveTarget
    ? livePromotionApprovalChecklist
    : paperPromotionApprovalChecklist;
  const evidenceReferences = parsePromotionEvidenceReferences(trimmedForm.evidenceReferences);

  if (liveTarget) {
    if (!trimmedForm.manualOverrideId) {
      return "Live promotion approval requires an active AllowLivePromotion override id.";
    }
  }

  const missingEvidence = getMissingPromotionEvidenceReferences(requiredChecklist, evidenceReferences);
  if (missingEvidence.length > 0) {
    return `${liveTarget ? "Live" : "Paper"} promotion evidence references are incomplete: ${missingEvidence.join(", ")}.`;
  }

  const invalidEvidence = getInvalidPromotionEvidenceReferences(
    requiredChecklist,
    evidenceReferences,
    trimmedForm.manualOverrideId);
  if (invalidEvidence.length > 0) {
    return `${liveTarget ? "Live" : "Paper"} promotion evidence references are invalid: ${invalidEvidence.join(", ")}.`;
  }

  return null;
}

export function validatePromotionRejection(form: PromotionGateForm): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Run id, operator, and rejection reason are required.";
  }

  return null;
}

export function buildPromotionApprovalRequest(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null = null
): ApprovePromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  const evidenceReferences = parsePromotionEvidenceReferences(trimmedForm.evidenceReferences);
  const approvalChecklist = evaluation
    ? buildPromotionApprovalChecklistTokens(evaluation)
    : trimmedForm.approvalChecklist;
  return {
    runId: trimmedForm.runId,
    approvedBy: trimmedForm.approvedBy,
    approvalReason: trimmedForm.approvalReason,
    approvalChecklist: approvalChecklist.length > 0 ? approvalChecklist : undefined,
    evidenceReferences: evidenceReferences.length > 0 ? evidenceReferences : undefined,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

export function buildPromotionRejectionRequest(form: PromotionGateForm): RejectPromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    reason: trimmedForm.rejectionReason,
    rejectedBy: trimmedForm.approvedBy,
    reviewNotes: trimmedForm.reviewNotes || undefined,
    manualOverrideId: trimmedForm.manualOverrideId || undefined
  };
}

export function trimPromotionGateForm(form: PromotionGateForm): PromotionGateForm {
  return {
    runId: form.runId.trim(),
    approvedBy: form.approvedBy.trim(),
    approvalReason: form.approvalReason.trim(),
    rejectionReason: form.rejectionReason.trim(),
    reviewNotes: form.reviewNotes.trim(),
    manualOverrideId: form.manualOverrideId.trim(),
    evidenceReferences: form.evidenceReferences.trim(),
    approvalChecklist: form.approvalChecklist
  };
}

export function selectPromotionEvaluationForRun(
  evaluation: PromotionEvaluationResult | null,
  runId: string
): PromotionEvaluationResult | null {
  if (!evaluation || !isPromotionEvaluationForRun(evaluation, runId)) {
    return null;
  }

  return evaluation;
}

export function isPromotionEvaluationForRun(
  evaluation: PromotionEvaluationResult,
  runId: string
): boolean {
  return Boolean(runId) && evaluation.runId === runId;
}

function buildPromotionApprovalChecklistTokens(evaluation: PromotionEvaluationResult | null): string[] {
  return isLivePromotionTarget(evaluation)
    ? livePromotionApprovalChecklist
    : paperPromotionApprovalChecklist;
}

export function parsePromotionEvidenceReferences(value: string): string[] {
  return value
    .split(/[\n,;]+/u)
    .map((item) => item.trim())
    .filter((item) => item.length > 0)
    .filter((item, index, items) => items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function getMissingPromotionEvidenceReferences(requiredTokens: string[], evidenceReferences: string[]): string[] {
  const evidenceKeys = new Set(evidenceReferences.map((reference) => getPromotionEvidenceReferenceKey(reference)));
  return requiredTokens.filter((token) => !evidenceKeys.has(token));
}

function getInvalidPromotionEvidenceReferences(
  requiredTokens: string[],
  evidenceReferences: string[],
  manualOverrideId: string
): string[] {
  return requiredTokens.flatMap((token) => {
    const reference = evidenceReferences.find((item) => getPromotionEvidenceReferenceKey(item) === token);
    if (!reference) {
      return [];
    }

    const retainedReference = getPromotionEvidenceReferenceValue(reference);
    if (!retainedReference) {
      return [`${token} must include retained evidence after ':'`];
    }

    if (token === "LIVE_OVERRIDE_REVIEWED" && manualOverrideId && !containsPromotionEvidenceReferenceToken(retainedReference, manualOverrideId)) {
      return [`${token} must reference active manual override ${manualOverrideId}`];
    }

    return [];
  });
}

export function getPromotionEvidenceReferenceKey(reference: string): string {
  const [key] = reference.split(":", 1);
  return key.trim().replace(/[ -]/gu, "_").toUpperCase();
}

export function getPromotionEvidenceReferenceValue(reference: string): string {
  const separatorIndex = reference.indexOf(":");
  return separatorIndex < 0 ? "" : reference.slice(separatorIndex + 1).trim();
}

function containsPromotionEvidenceReferenceToken(referenceValue: string, token: string): boolean {
  if (referenceValue.toLowerCase() === token.toLowerCase()) {
    return true;
  }

  return referenceValue
    .split(/[/\\#?&=,;\s]+/u)
    .filter((segment) => segment.length > 0)
    .some((segment) => segment.toLowerCase() === token.toLowerCase());
}

export function isLivePromotionTarget(evaluation: PromotionEvaluationResult | null): boolean {
  return typeof evaluation?.targetMode === "string" &&
    evaluation.targetMode.trim().toLowerCase() === "live";
}

export interface PromotionApprovalChecklistItem {
  id: string;
  label: string;
  status: "ready" | "review" | "blocked";
  description: string | null;
  ariaLabel: string;
}

export function buildPromotionApprovalChecklist(
  evaluation: PromotionEvaluationResult | null,
  evidenceReferenceText = ""
): PromotionApprovalChecklistItem[] {
  const evidenceReferences = parsePromotionEvidenceReferences(evidenceReferenceText);
  const evidenceStatus = (checklistId: string): PromotionApprovalChecklistItem["status"] => {
    const reference = evidenceReferences.find((item) => getPromotionEvidenceReferenceKey(item) === checklistId);
    return reference && getPromotionEvidenceReferenceValue(reference) ? "ready" : "review";
  };
  const items: Array<Omit<PromotionApprovalChecklistItem, "ariaLabel">> = [
    {
      id: "dk1-data-trust",
      label: "DK1 data trust",
      status: evidenceStatus("DK1_TRUST_PACKET_REVIEWED"),
      description: "Requires operator-entered keyed evidence for the reviewed DK1 trust packet"
    },
    {
      id: "run-lineage",
      label: "Run lineage",
      status: evidenceStatus("RUN_LINEAGE_REVIEWED"),
      description: evaluation?.found
        ? `Run found for ${evaluation.strategyName ?? evaluation.strategyId}; operator-entered lineage evidence is still required`
        : "Run must be found and its retained lineage evidence reviewed"
    },
    {
      id: "risk-metrics",
      label: "Risk metrics",
      status: evaluation && !evaluation.isEligible ? "blocked" : evidenceStatus("RISK_CONTROLS_REVIEWED"),
      description: evaluation
        ? `Eligibility metrics: Sharpe ${formatDecimal(evaluation.sharpeRatio, 2)} · Max DD ${formatPercent(evaluation.maxDrawdownPercent, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} · Return ${formatPercent(evaluation.totalReturn, { minimumFractionDigits: 1, maximumFractionDigits: 1 })}; keyed review evidence remains authoritative`
        : "Metrics and retained risk-control evidence require review"
    },
    {
      id: "portfolio-ledger-continuity",
      label: "Portfolio/Ledger continuity",
      status: evidenceStatus("PORTFOLIO_LEDGER_CONTINUITY_REVIEWED"),
      description: evaluation?.ready
        ? "Run state is eligible; operator-entered continuity evidence is still required"
        : "Awaiting run state and retained continuity evidence review"
    }
  ];

  if (isLivePromotionTarget(evaluation)) {
    items.push(
      {
        id: "paper-validation",
        label: "Paper validation",
        status: evidenceStatus("PAPER_VALIDATION_REVIEWED"),
        description: "Retained paper-run validation evidence reviewed"
      },
      {
        id: "reconciliation-evidence",
        label: "Reconciliation evidence",
        status: evidenceStatus("RECONCILIATION_EVIDENCE_REVIEWED"),
        description: "Portfolio, ledger, and operations reconciliation evidence reviewed"
      },
      {
        id: "broker-execution-reconciliation",
        label: "Broker order parity",
        status: evidenceStatus("BROKER_EXECUTION_RECONCILIATION_REVIEWED"),
        description: "Broker and OMS open-order reconciliation evidence reviewed"
      },
      {
        id: "accounting-records",
        label: "Accounting records",
        status: evidenceStatus("ACCOUNTING_RECORDS_REVIEWED"),
        description: "Accounting-record evidence is retained for the live readiness scope"
      },
      {
        id: "governed-reporting",
        label: "Governed reporting",
        status: evidenceStatus("GOVERNED_REPORTING_REVIEWED"),
        description: "Governed report-pack evidence supports the live readiness decision"
      },
      {
        id: "governance-signoff",
        label: "Governance sign-off",
        status: evidenceStatus("GOVERNANCE_SIGNOFF_REVIEWED"),
        description: "Required governance sign-off is retained"
      },
      {
        id: "exception-handling",
        label: "Exception handling",
        status: evidenceStatus("EXCEPTION_HANDLING_REVIEWED"),
        description: "Open exceptions and escalation posture have been reviewed"
      },
      {
        id: "rollback-kill-switch",
        label: "Rollback/kill-switch",
        status: evidenceStatus("ROLLBACK_KILL_SWITCH_REVIEWED"),
        description: "Rollback and kill-switch posture are ready before live operation"
      },
      {
        id: "audit-retention",
        label: "Audit retention",
        status: evidenceStatus("AUDIT_RETENTION_REVIEWED"),
        description: "Audit-retention evidence is retained for the approval"
      },
      {
        id: "live-override",
        label: "Live override",
        status: evidenceStatus("LIVE_OVERRIDE_REVIEWED"),
        description: "Active AllowLivePromotion override evidence is attached"
      }
    );
  }

  return items.map((item) => ({
    ...item,
    ariaLabel: `${item.label}: ${item.status}${item.description ? `. ${item.description}` : ""}`
  }));
}
