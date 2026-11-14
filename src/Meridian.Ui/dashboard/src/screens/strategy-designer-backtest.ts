import type {
  StrategyDesignDocument,
  StrategyDesignRunBacktestRequest
} from "@/types/workstation-8";
import type { StrategyBuilderDocument } from "./strategy-designer-screen.view-model";

/**
 * Governed evidence the backtest route requires. Each list must hold at least one reference;
 * the backend refuses the run otherwise, and this screen cannot invent references on the
 * operator's behalf.
 */
export interface StrategyBacktestEvidence {
  operatorAcceptanceCriteria: string[];
  retainedEvidenceReferences: string[];
  accountingRecordReferences: string[];
  approvalReferences: string[];
  paperValidationReferences: string[];
  governedReportReferences: string[];
}

export const BACKTEST_EVIDENCE_LABELS: Record<keyof StrategyBacktestEvidence, string> = {
  operatorAcceptanceCriteria: "Operator acceptance criteria",
  retainedEvidenceReferences: "Retained evidence references",
  accountingRecordReferences: "Accounting record references",
  approvalReferences: "Approval references",
  paperValidationReferences: "Paper validation references",
  governedReportReferences: "Governed report references"
};

export type BacktestRequestBuildResult =
  | { ok: true; request: StrategyDesignRunBacktestRequest }
  | { ok: false; missing: Array<keyof StrategyBacktestEvidence> };

function cleanReferences(values: readonly string[]): string[] {
  return values.map((value) => value.trim()).filter((value) => value.length > 0);
}

/**
 * Maps the canvas document onto the API design document. The canvas and the API share field
 * names and string-typed kinds, so the mapping is structural; nothing is dropped or defaulted.
 */
export function toStrategyDesignDocument(document: StrategyBuilderDocument, now: string): StrategyDesignDocument {
  return {
    documentId: document.documentId,
    name: document.name,
    description: document.description,
    version: document.version,
    datasetReference: document.datasetReference,
    universe: [...document.universe],
    cells: document.cells.map((cell) => ({
      cellId: cell.cellId,
      label: cell.label,
      kind: cell.kind,
      purpose: cell.purpose,
      source: cell.source,
      fieldRefs: [...cell.fieldRefs],
      parameters: cell.parameters ? { ...cell.parameters } : null,
      disabledReason: null
    })),
    transitions: document.transitions.map((transition) => ({
      transitionId: transition.transitionId,
      fromCellId: transition.fromCellId,
      toCellId: transition.toCellId,
      kind: transition.kind,
      condition: transition.condition,
      maxIterations: transition.maxIterations ?? null,
      rationale: transition.rationale ?? null
    })),
    metadata: null,
    createdAt: document.updatedAt,
    updatedAt: now
  };
}

/**
 * Builds the backtest request only when every governed evidence list has at least one
 * non-blank reference. Otherwise reports which lists are missing so the screen can say why.
 */
export function buildBacktestRunRequest(
  document: StrategyBuilderDocument,
  evidence: StrategyBacktestEvidence,
  now: string
): BacktestRequestBuildResult {
  const cleaned: StrategyBacktestEvidence = {
    operatorAcceptanceCriteria: cleanReferences(evidence.operatorAcceptanceCriteria),
    retainedEvidenceReferences: cleanReferences(evidence.retainedEvidenceReferences),
    accountingRecordReferences: cleanReferences(evidence.accountingRecordReferences),
    approvalReferences: cleanReferences(evidence.approvalReferences),
    paperValidationReferences: cleanReferences(evidence.paperValidationReferences),
    governedReportReferences: cleanReferences(evidence.governedReportReferences)
  };

  const missing = (Object.keys(cleaned) as Array<keyof StrategyBacktestEvidence>)
    .filter((key) => cleaned[key].length === 0);
  if (missing.length > 0) {
    return { ok: false, missing };
  }

  return {
    ok: true,
    request: {
      document: toStrategyDesignDocument(document, now),
      parameters: null,
      ...cleaned
    }
  };
}
