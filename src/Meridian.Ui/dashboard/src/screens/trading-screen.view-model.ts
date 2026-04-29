import { useCallback, useEffect, useMemo, useState } from "react";
import * as workstationApi from "@/lib/api";
import type { ApprovePromotionRequest, RejectPromotionRequest } from "@/lib/api";
import type { PromotionDecisionResult, PromotionEvaluationResult, PromotionRecord } from "@/types";

export type PromotionGateField =
  | "runId"
  | "approvedBy"
  | "approvalReason"
  | "rejectionReason"
  | "reviewNotes"
  | "manualOverrideId";

export interface PromotionGateForm {
  runId: string;
  approvedBy: string;
  approvalReason: string;
  rejectionReason: string;
  reviewNotes: string;
  manualOverrideId: string;
}

export type PromotionGatePhase = "idle" | "evaluating" | "approving" | "rejecting";
export type PromotionOutcomeLevel = "success" | "warning" | "error";

export interface PromotionOutcome {
  level: PromotionOutcomeLevel;
  message: string;
}

export interface PromotionGateServices {
  evaluatePromotion: (runId: string) => Promise<PromotionEvaluationResult>;
  approvePromotion: (request: ApprovePromotionRequest) => Promise<PromotionDecisionResult>;
  rejectPromotion: (request: RejectPromotionRequest) => Promise<PromotionDecisionResult>;
  getPromotionHistory: () => Promise<PromotionRecord[]>;
}

export interface PromotionGateState {
  form: PromotionGateForm;
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  canEvaluate: boolean;
  canPromote: boolean;
  canReject: boolean;
  evaluateButtonLabel: string;
  promoteButtonLabel: string;
  rejectButtonLabel: string;
  nextActionText: string;
  approvalRequirementText: string;
  rejectionRequirementText: string;
  historyEmptyText: string;
  statusAnnouncement: string;
}

export interface BuildPromotionGateStateOptions {
  form: PromotionGateForm;
  busy: boolean;
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}

export const emptyPromotionGateForm: PromotionGateForm = {
  runId: "",
  approvedBy: "",
  approvalReason: "",
  rejectionReason: "",
  reviewNotes: "",
  manualOverrideId: ""
};

const defaultPromotionServices: PromotionGateServices = {
  evaluatePromotion: (runId) => workstationApi.evaluatePromotion(runId),
  approvePromotion: (request) => workstationApi.approvePromotion(request),
  rejectPromotion: (request) => workstationApi.rejectPromotion(request),
  getPromotionHistory: () => workstationApi.getPromotionHistory()
};

export function usePromotionGateViewModel(
  services: PromotionGateServices = defaultPromotionServices
) {
  const [form, setForm] = useState<PromotionGateForm>(emptyPromotionGateForm);
  const [evaluation, setEvaluation] = useState<PromotionEvaluationResult | null>(null);
  const [history, setHistory] = useState<PromotionRecord[]>([]);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<PromotionGatePhase>("idle");
  const [outcome, setOutcome] = useState<PromotionOutcome | null>(null);

  useEffect(() => {
    let cancelled = false;

    services.getPromotionHistory()
      .then((rows) => {
        if (!cancelled) {
          setHistory(rows);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setHistory([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [services]);

  const state = useMemo(
    () => buildPromotionGateState({ form, busy, phase, errorText, outcome, evaluation, history }),
    [busy, errorText, evaluation, form, history, outcome, phase]
  );

  const refreshHistory = useCallback(async () => {
    const rows = await services.getPromotionHistory();
    setHistory(rows);
  }, [services]);

  const updateField = useCallback((field: PromotionGateField, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    setErrorText(null);
    setOutcome(null);

    if (field === "runId") {
      setEvaluation(null);
    }
  }, []);

  const evaluateGateChecks = useCallback(async () => {
    const runId = form.runId.trim();
    if (!runId) {
      setErrorText("Run id is required before evaluating gate checks.");
      return;
    }

    setBusy(true);
    setPhase("evaluating");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.evaluatePromotion(runId);
      setEvaluation(result);
    } catch (err) {
      setErrorText(toErrorMessage(err, "Evaluation failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form.runId, services]);

  const promoteToPaper = useCallback(async () => {
    const validationError = validatePromotionApproval(form, evaluation);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    setBusy(true);
    setPhase("approving");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.approvePromotion(buildPromotionApprovalRequest(form));
      setOutcome(buildApprovalOutcome(result));
      await refreshHistory();
    } catch (err) {
      setErrorText(toErrorMessage(err, "Promotion approval failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [evaluation, form, refreshHistory, services]);

  const rejectPromotionDecision = useCallback(async () => {
    const validationError = validatePromotionRejection(form);
    if (validationError) {
      setErrorText(validationError);
      setOutcome(null);
      return;
    }

    setBusy(true);
    setPhase("rejecting");
    setErrorText(null);
    setOutcome(null);

    try {
      const result = await services.rejectPromotion(buildPromotionRejectionRequest(form));
      setOutcome(buildRejectionOutcome(result));
      await refreshHistory();

      if (result.success) {
        setForm((current) => ({ ...current, rejectionReason: "" }));
      }
    } catch (err) {
      setErrorText(toErrorMessage(err, "Promotion rejection failed."));
    } finally {
      setBusy(false);
      setPhase("idle");
    }
  }, [form, refreshHistory, services]);

  return {
    ...state,
    updateField,
    evaluateGateChecks,
    promoteToPaper,
    rejectPromotion: rejectPromotionDecision
  };
}

export function buildPromotionGateState({
  form,
  busy,
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: BuildPromotionGateStateOptions): PromotionGateState {
  const trimmedForm = trimPromotionGateForm(form);
  const canEvaluate = !busy && Boolean(trimmedForm.runId);
  const canPromote = !busy && validatePromotionApproval(trimmedForm, evaluation) === null;
  const canReject = !busy && validatePromotionRejection(trimmedForm) === null;

  return {
    form,
    trimmedForm,
    evaluation,
    history,
    busy,
    phase,
    errorText,
    outcome,
    canEvaluate,
    canPromote,
    canReject,
    evaluateButtonLabel: phase === "evaluating" ? "Evaluating..." : "Evaluate gate checks",
    promoteButtonLabel: phase === "approving" ? "Promoting..." : "Confirm promote",
    rejectButtonLabel: phase === "rejecting" ? "Rejecting..." : "Reject promotion",
    nextActionText: buildNextActionText({ trimmedForm, evaluation, busy, phase }),
    approvalRequirementText: buildApprovalRequirementText(trimmedForm, evaluation),
    rejectionRequirementText: buildRejectionRequirementText(trimmedForm),
    historyEmptyText: "No promotion decisions recorded.",
    statusAnnouncement: buildPromotionStatusAnnouncement({ phase, errorText, outcome, evaluation, history })
  };
}

export function validatePromotionApproval(
  form: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string | null {
  const trimmedForm = trimPromotionGateForm(form);

  if (!evaluation) {
    return "Evaluate gate checks before confirming promotion.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Promotion gate is blocked.";
  }

  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Run id, operator, and approval reason are required.";
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

export function buildPromotionApprovalRequest(form: PromotionGateForm): ApprovePromotionRequest {
  const trimmedForm = trimPromotionGateForm(form);
  return {
    runId: trimmedForm.runId,
    approvedBy: trimmedForm.approvedBy,
    approvalReason: trimmedForm.approvalReason,
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

function trimPromotionGateForm(form: PromotionGateForm): PromotionGateForm {
  return {
    runId: form.runId.trim(),
    approvedBy: form.approvedBy.trim(),
    approvalReason: form.approvalReason.trim(),
    rejectionReason: form.rejectionReason.trim(),
    reviewNotes: form.reviewNotes.trim(),
    manualOverrideId: form.manualOverrideId.trim()
  };
}

function buildApprovalOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "success" : "error",
    message: result.success
      ? `Promoted. Promotion ID: ${result.promotionId ?? "n/a"}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
      : result.reason
  };
}

function buildRejectionOutcome(result: PromotionDecisionResult): PromotionOutcome {
  return {
    level: result.success ? "warning" : "error",
    message: `${result.reason}${result.auditReference ? ` · Audit reference ${result.auditReference}` : ""}`
  };
}

function buildNextActionText({
  trimmedForm,
  evaluation,
  busy,
  phase
}: {
  trimmedForm: PromotionGateForm;
  evaluation: PromotionEvaluationResult | null;
  busy: boolean;
  phase: PromotionGatePhase;
}): string {
  if (busy) {
    if (phase === "evaluating") {
      return "Evaluating promotion gate checks.";
    }

    if (phase === "approving") {
      return "Writing promotion decision and refreshing audit trail.";
    }

    if (phase === "rejecting") {
      return "Writing rejection decision and refreshing audit trail.";
    }
  }

  if (!trimmedForm.runId) {
    return "Enter a backtest run id to evaluate promotion readiness.";
  }

  if (!evaluation) {
    return "Evaluate gate checks before approving or rejecting this run.";
  }

  if (!evaluation.isEligible) {
    return "Gate is blocked. Reject with rationale or review the blocking reason.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Add operator and approval reason before confirming promotion.";
  }

  return "Promotion trace is ready for confirmation.";
}

function buildApprovalRequirementText(
  trimmedForm: PromotionGateForm,
  evaluation: PromotionEvaluationResult | null
): string {
  if (!evaluation) {
    return "Approval remains disabled until gate checks return an eligible result.";
  }

  if (!evaluation.isEligible) {
    return evaluation.blockingReasons?.[0] ?? evaluation.reason ?? "Gate checks did not return eligible.";
  }

  if (!trimmedForm.approvedBy || !trimmedForm.approvalReason) {
    return "Approval requires an operator id and approval reason.";
  }

  return "Approval request includes run id, operator, rationale, and optional audit notes.";
}

function buildRejectionRequirementText(trimmedForm: PromotionGateForm): string {
  if (!trimmedForm.runId || !trimmedForm.approvedBy || !trimmedForm.rejectionReason) {
    return "Rejection requires run id, operator id, and rejection reason.";
  }

  return "Rejection request is ready to write an audit-linked decision.";
}

function buildPromotionStatusAnnouncement({
  phase,
  errorText,
  outcome,
  evaluation,
  history
}: {
  phase: PromotionGatePhase;
  errorText: string | null;
  outcome: PromotionOutcome | null;
  evaluation: PromotionEvaluationResult | null;
  history: PromotionRecord[];
}): string {
  if (phase === "evaluating") {
    return "Evaluating promotion gate checks.";
  }

  if (phase === "approving") {
    return "Writing promotion approval.";
  }

  if (phase === "rejecting") {
    return "Writing promotion rejection.";
  }

  if (errorText) {
    return `Promotion gate failed: ${errorText}`;
  }

  if (outcome) {
    return "Promotion gate command completed.";
  }

  if (evaluation) {
    return evaluation.isEligible
      ? "Promotion gate checks returned eligible."
      : "Promotion gate checks returned blocked.";
  }

  if (history.length > 0) {
    return `${history.length} promotion decision ${history.length === 1 ? "record" : "records"} loaded.`;
  }

  return "";
}

function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}
