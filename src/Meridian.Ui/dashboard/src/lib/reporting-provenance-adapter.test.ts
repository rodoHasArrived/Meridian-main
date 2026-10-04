import { describe, expect, it } from "vitest";
import {
  buildTraceFromRecordGraph,
  listTraceableRecordIds
} from "@/lib/reporting-provenance-adapter";
import type {
  FinancialRecordExplorerGraphNodeDto,
  FinancialRecordExplorerRecordGraphDto
} from "@/types";

function node(
  overrides: Partial<FinancialRecordExplorerGraphNodeDto> & Pick<FinancialRecordExplorerGraphNodeDto, "nodeId">
): FinancialRecordExplorerGraphNodeDto {
  return {
    label: "Node",
    nodeType: "record",
    tone: "Info",
    href: "/accounting/records/1",
    ...overrides
  };
}

/** A relationship node as the read service names them: `rel:{recordId}:{relationshipId}`. */
function relationship(
  recordId: string,
  relationshipId: string,
  overrides: Partial<FinancialRecordExplorerGraphNodeDto> = {}
): FinancialRecordExplorerGraphNodeDto {
  return node({
    nodeId: `rel:${recordId}:${relationshipId}`,
    nodeType: relationshipId,
    label: `${relationshipId} detail`,
    href: `/accounting/${relationshipId}`,
    ...overrides
  });
}

/** A placeholder node: the read service found no relationship for the slot. */
function placeholder(recordId: string, relationshipId: string): FinancialRecordExplorerGraphNodeDto {
  return node({
    nodeId: `rel:${recordId}:${relationshipId}`,
    nodeType: relationshipId,
    label: relationshipId,
    tone: "Warning",
    href: ""
  });
}

function graph(nodes: FinancialRecordExplorerGraphNodeDto[]): FinancialRecordExplorerRecordGraphDto {
  return { nodes, edges: [] };
}

const RECORD_ID = "rec-1";

describe("buildTraceFromRecordGraph", () => {
  it("orders the relationship chain from source to publication", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        relationship(RECORD_ID, "evidence"),
        relationship(RECORD_ID, "report-line"),
        relationship(RECORD_ID, "reconciliation"),
        relationship(RECORD_ID, "journal"),
        relationship(RECORD_ID, "terms-obligations")
      ]),
      RECORD_ID
    );

    expect(trace.steps.map((step) => step.stage)).toEqual([
      "Source",
      "Normalization",
      "Calculation",
      "Reconciliation",
      "ReportBlock",
      "Publication"
    ]);
    expect(trace.isComplete).toBe(true);
  });

  it("treats the row node itself as the source record", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "journal-entry", label: "JE 8812" }),
        relationship(RECORD_ID, "report-line")
      ]),
      RECORD_ID
    );

    expect(trace.steps[0]).toMatchObject({ stage: "Source", label: "JE 8812" });
    expect(trace.gaps).toEqual([]);
  });

  it("reports a placeholder relationship as a gap, not as a completed stage", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        placeholder(RECORD_ID, "reconciliation"),
        relationship(RECORD_ID, "report-line")
      ]),
      RECORD_ID,
      { requiredStages: ["Reconciliation"] }
    );

    expect(trace.steps.map((step) => step.stage)).toEqual(["Source", "ReportBlock"]);
    expect(trace.gaps.map((gap) => gap.stage)).toEqual(["Reconciliation"]);
    expect(trace.isComplete).toBe(false);
  });

  it("keeps a real relationship whose href is empty", () => {
    // Empty href alone is not a placeholder signal - the label still describes a
    // genuine relationship, it is simply not navigable.
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        relationship(RECORD_ID, "reconciliation", { href: "", label: "Cash reconciliation" }),
        relationship(RECORD_ID, "report-line")
      ]),
      RECORD_ID
    );

    expect(trace.steps.map((step) => step.label)).toContain("Cash reconciliation");
  });

  it("scopes the trace to one record's chain", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        relationship(RECORD_ID, "report-line"),
        node({ nodeId: "rec-2", nodeType: "trade", label: "Trade 9902" }),
        relationship("rec-2", "reconciliation")
      ]),
      RECORD_ID
    );

    expect(trace.steps.map((step) => step.label)).toEqual(["Trade 4471", "report-line detail"]);
  });

  it("does not let a prefix collision pull in another record's chain", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        relationship("rec-10", "reconciliation")
      ]),
      RECORD_ID
    );

    expect(trace.steps.map((step) => step.label)).toEqual(["Trade 4471"]);
  });

  it("retains an unmapped relationship slot instead of dropping it", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        relationship(RECORD_ID, "analyst-overlay"),
        relationship(RECORD_ID, "report-line")
      ]),
      RECORD_ID
    );

    expect(trace.unresolvedSteps.map((step) => step.label)).toEqual(["analyst-overlay detail"]);
    expect(trace.isComplete).toBe(false);
  });

  it("reports a missing source as an action-level gap", () => {
    const trace = buildTraceFromRecordGraph(
      graph([relationship(RECORD_ID, "report-line")]),
      RECORD_ID
    );

    expect(trace.gaps.map((gap) => gap.stage)).toEqual(["Source"]);
    expect(trace.severity).toBe("action");
  });

  it("handles an absent graph without throwing", () => {
    expect(buildTraceFromRecordGraph(null, RECORD_ID).steps).toEqual([]);
    expect(buildTraceFromRecordGraph(undefined, RECORD_ID).summary).toBe(
      "No lineage recorded for this value."
    );
  });

  it("names the relationship slot as the system when it differs from the stage", () => {
    const trace = buildTraceFromRecordGraph(
      graph([
        node({ nodeId: RECORD_ID, nodeType: "trade", label: "Trade 4471" }),
        relationship(RECORD_ID, "report-line")
      ]),
      RECORD_ID
    );

    expect(trace.steps[1]?.system).toBe("Report Line");
  });
});

describe("listTraceableRecordIds", () => {
  it("returns the row nodes, excluding relationship nodes", () => {
    const ids = listTraceableRecordIds(
      graph([
        node({ nodeId: RECORD_ID }),
        relationship(RECORD_ID, "report-line"),
        node({ nodeId: "rec-2" })
      ])
    );

    expect(ids).toEqual([RECORD_ID, "rec-2"]);
  });

  it("returns nothing for an absent graph", () => {
    expect(listTraceableRecordIds(null)).toEqual([]);
  });
});
