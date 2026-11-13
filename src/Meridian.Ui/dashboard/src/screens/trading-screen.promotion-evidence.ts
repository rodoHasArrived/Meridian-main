import type { PromotionEvaluationResult } from "@/types";

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
        ? `Eligibility metrics: Sharpe ${evaluation.sharpeRatio.toFixed(2)} · Max DD ${evaluation.maxDrawdownPercent.toFixed(1)}% · Return ${evaluation.totalReturn.toFixed(1)}%; keyed review evidence remains authoritative`
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

export function buildPromotionApprovalChecklistTokens(evaluation: PromotionEvaluationResult | null): string[] {
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

export function getMissingPromotionEvidenceReferences(requiredTokens: string[], evidenceReferences: string[]): string[] {
  const evidenceKeys = new Set(evidenceReferences.map((reference) => getPromotionEvidenceReferenceKey(reference)));
  return requiredTokens.filter((token) => !evidenceKeys.has(token));
}

export function getInvalidPromotionEvidenceReferences(
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

function getPromotionEvidenceReferenceKey(reference: string): string {
  const [key] = reference.split(":", 1);
  return key.trim().replace(/[ -]/gu, "_").toUpperCase();
}

function getPromotionEvidenceReferenceValue(reference: string): string {
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
