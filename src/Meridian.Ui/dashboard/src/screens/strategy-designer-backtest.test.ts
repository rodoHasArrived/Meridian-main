import { describe, expect, it } from "vitest";
import {
  buildBacktestRunRequest,
  toStrategyDesignDocument,
  type StrategyBacktestEvidence
} from "./strategy-designer-backtest";
import type { StrategyBuilderDocument } from "./strategy-designer-screen.view-model";

const NOW = "2026-11-02T10:00:00.000Z";

const DOCUMENT: StrategyBuilderDocument = {
  documentId: "doc-1",
  name: "Momentum",
  description: "Rank and hold",
  version: "1",
  datasetReference: "ds-equities-daily",
  universe: ["AAPL", "MSFT"],
  updatedAt: "2026-11-01T09:00:00.000Z",
  cells: [
    {
      cellId: "c1",
      label: "Universe",
      kind: "universe-builder",
      purpose: "universe",
      source: "security-master",
      fieldRefs: ["close", "volume"],
      parameters: { liquidity: "high" }
    },
    {
      cellId: "c2",
      label: "Rank",
      kind: "formula",
      purpose: "rank",
      source: "canonical",
      fieldRefs: ["close"]
    }
  ],
  transitions: [
    {
      transitionId: "t1",
      fromCellId: "c1",
      toCellId: "c2",
      kind: "next",
      condition: "always",
      maxIterations: null,
      rationale: null
    }
  ]
};

const COMPLETE_EVIDENCE: StrategyBacktestEvidence = {
  operatorAcceptanceCriteria: ["Sharpe above 1"],
  retainedEvidenceReferences: ["evidence-1"],
  accountingRecordReferences: ["acct-1"],
  approvalReferences: ["approval-1"],
  paperValidationReferences: ["paper-1"],
  governedReportReferences: ["report-1"]
};

describe("toStrategyDesignDocument", () => {
  it("carries every cell and transition through without dropping fields", () => {
    const mapped = toStrategyDesignDocument(DOCUMENT, NOW);

    expect(mapped.documentId).toBe("doc-1");
    expect(mapped.universe).toEqual(["AAPL", "MSFT"]);
    expect(mapped.cells).toHaveLength(2);
    expect(mapped.cells[0]).toMatchObject({
      cellId: "c1",
      kind: "universe-builder",
      purpose: "universe",
      fieldRefs: ["close", "volume"],
      parameters: { liquidity: "high" }
    });
    expect(mapped.transitions[0]).toMatchObject({ transitionId: "t1", kind: "next", maxIterations: null });
    expect(mapped.createdAt).toBe(DOCUMENT.updatedAt);
    expect(mapped.updatedAt).toBe(NOW);
  });

  it("copies arrays so later edits to the canvas do not mutate the request", () => {
    const mapped = toStrategyDesignDocument(DOCUMENT, NOW);
    mapped.universe.push("NVDA");
    mapped.cells[0].fieldRefs.push("extra");

    expect(DOCUMENT.universe).toEqual(["AAPL", "MSFT"]);
    expect(DOCUMENT.cells[0].fieldRefs).toEqual(["close", "volume"]);
  });
});

describe("buildBacktestRunRequest", () => {
  it("builds a request when every evidence list has a reference", () => {
    const result = buildBacktestRunRequest(DOCUMENT, COMPLETE_EVIDENCE, NOW);

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.request.document.documentId).toBe("doc-1");
      expect(result.request.approvalReferences).toEqual(["approval-1"]);
      expect(result.request.governedReportReferences).toEqual(["report-1"]);
    }
  });

  it("names each evidence list that is empty", () => {
    const result = buildBacktestRunRequest(
      DOCUMENT,
      { ...COMPLETE_EVIDENCE, approvalReferences: [], paperValidationReferences: [] },
      NOW
    );

    expect(result).toEqual({ ok: false, missing: ["approvalReferences", "paperValidationReferences"] });
  });

  it("treats blank and whitespace-only references as missing", () => {
    const result = buildBacktestRunRequest(
      DOCUMENT,
      { ...COMPLETE_EVIDENCE, retainedEvidenceReferences: ["   ", ""] },
      NOW
    );

    expect(result).toEqual({ ok: false, missing: ["retainedEvidenceReferences"] });
  });

  it("trims references before sending them", () => {
    const result = buildBacktestRunRequest(
      DOCUMENT,
      { ...COMPLETE_EVIDENCE, accountingRecordReferences: ["  acct-9 "] },
      NOW
    );

    expect(result.ok && result.request.accountingRecordReferences).toEqual(["acct-9"]);
  });
});
