import { describe, expect, it } from "vitest";
import { buildNumberPassportItems } from "@/lib/financial-record-passport";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

describe("financial record number passport", () => {
  it("builds the shared eight-facet passport from selected record evidence", () => {
    const items = buildNumberPassportItems(selectedRecord(), row(), explorer());

    expect(items.map((item) => item.label)).toEqual([
      "Source",
      "Freshness",
      "Reconciliation",
      "Approvals",
      "Report Usage",
      "Blockers",
      "Evidence Packet",
      "Audit Trail"
    ]);
    expect(items.find((item) => item.id === "source")).toMatchObject({
      value: "Security Master",
      href: "/api/workstation/security-master/securities/aapl"
    });
    expect(items.find((item) => item.id === "freshness")).toMatchObject({
      value: "Ready",
      detail: "Source-backed Security Master references from run run-1."
    });
    expect(items.find((item) => item.id === "reconciliation")).toMatchObject({
      value: "AssetOperations reconciliation",
      href: "/accounting/reconciliation/aapl"
    });
    expect(items.find((item) => item.id === "approvals")).toMatchObject({
      value: "Approved by Ops",
      detail: "/approvals/aapl"
    });
    expect(items.find((item) => item.id === "report-usage")).toMatchObject({
      value: "Board pack holdings.aapl.market-value",
      href: "/reporting/provenance/aapl"
    });
    expect(items.find((item) => item.id === "blockers")).toMatchObject({
      value: "None disclosed",
      tone: "Success"
    });
    expect(items.find((item) => item.id === "evidence-packet")).toMatchObject({
      value: "Instrument passport",
      href: "/api/workstation/security-master/securities/aapl/passport"
    });
    expect(items.find((item) => item.id === "audit-trail")).toMatchObject({
      value: "5 graph nodes",
      detail: "1 retained relationships."
    });
  });

  it("surfaces blocked source state and disabled proof actions without inventing proof links", () => {
    const record = selectedRecord({
      proofActions: [
        {
          actionId: "evidence-packet",
          label: "Evidence packet",
          description: "Evidence packet cannot open until source registration completes.",
          href: "/evidence/aapl",
          isEnabled: false,
          disabledReason: "Source registration is missing.",
          tone: "Danger"
        }
      ],
      usedIn: [],
      impacts: [],
      fields: [],
      fullRecordHref: ""
    });
    const blockedExplorer = explorer({
      isBlocked: true,
      blockedReason: "Security Master read service is not registered.",
      proofActions: [],
      recordGraph: { nodes: [], edges: [] }
    });

    const items = buildNumberPassportItems(record, null, blockedExplorer);

    expect(items.find((item) => item.id === "blockers")).toMatchObject({
      value: "Blocked",
      detail: "Security Master read service is not registered.",
      tone: "Danger",
      href: ""
    });
    expect(items.find((item) => item.id === "evidence-packet")).toMatchObject({
      value: "Evidence packet",
      detail: "Source registration is missing.",
      tone: "Danger",
      href: ""
    });
  });
});

function selectedRecord(overrides: Partial<FinancialRecordExplorerSelectedRecordDto> = {}): FinancialRecordExplorerSelectedRecordDto {
  return {
    recordId: "security:aapl",
    recordType: "Security",
    title: "AAPL - Apple Inc.",
    subtitle: "Security Master",
    description: "Security Master identity with provider, operations, ledger, and report proof.",
    tone: "Success",
    fields: [
      { label: "Instrument Identity", value: "AAPL / US0378331005", detail: "/api/workstation/security-master/securities/aapl", tone: "Success" },
      { label: "Provider Evidence", value: "Instrument passport", detail: "/api/workstation/security-master/securities/aapl/passport", tone: "Success" },
      { label: "Approvals", value: "Approved by Ops", detail: "/approvals/aapl", tone: "Success" }
    ],
    proofActions: [],
    usedIn: [
      {
        relationshipId: "report-usage",
        label: "Board pack holdings.aapl.market-value",
        description: "Report usage for June holdings.",
        href: "/reporting/provenance/aapl",
        tone: "Info"
      }
    ],
    impacts: [
      {
        relationshipId: "reconciliation",
        label: "AssetOperations reconciliation",
        description: "Reconciliation ready.",
        href: "/accounting/reconciliation/aapl",
        tone: "Success"
      }
    ],
    fullRecordHref: "/api/workstation/security-master/securities/aapl",
    ...overrides
  };
}

function row(overrides: Partial<FinancialRecordExplorerRowDto> = {}): FinancialRecordExplorerRowDto {
  return {
    recordId: "security:aapl",
    recordType: "Security",
    label: "AAPL - Apple Inc.",
    source: "Security Master",
    status: "Ready",
    tone: "Success",
    cells: [
      { columnId: "security", displayValue: "AAPL - Apple Inc.", rawValue: "AAPL", tone: "Success", linkHref: "/api/workstation/security-master/securities/aapl" },
      { columnId: "status", displayValue: "Ready", rawValue: "Ready", tone: "Success", linkHref: "" }
    ],
    detail: selectedRecord(),
    ...overrides
  };
}

function explorer(overrides: Partial<FinancialRecordExplorerDto> = {}): FinancialRecordExplorerDto {
  return {
    explorerId: "security-instrument",
    title: "Security & Instrument Explorer",
    description: "Shared security evidence explorer.",
    sourceState: "Source-backed Security Master references from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [],
    savedViews: [],
    summaryItems: [],
    filters: [],
    columns: [],
    rows: [],
    selectedRecord: selectedRecord(),
    proofActions: [],
    recordGraph: {
      nodes: [
        { nodeId: "security", label: "Security Master", nodeType: "source", tone: "Success", href: "/api/workstation/security-master/securities/aapl" },
        { nodeId: "provider", label: "Provider evidence", nodeType: "evidence", tone: "Success", href: "" },
        { nodeId: "operations", label: "AssetOperations ready", nodeType: "readiness", tone: "Success", href: "" },
        { nodeId: "ledger", label: "Ledger projection", nodeType: "ledger", tone: "Info", href: "" },
        { nodeId: "report", label: "Board report", nodeType: "report", tone: "Info", href: "" }
      ],
      edges: [{ sourceNodeId: "security", targetNodeId: "report", label: "used in", tone: "Info" }]
    },
    ...overrides
  };
}
